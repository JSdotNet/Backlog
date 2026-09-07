using System.Net;
using System.Text.Json;

using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// One exchange, and the two pieces of bookkeeping that decide whether a device
/// ever catches up: where the push watermark lands, and when the pull cursor is
/// written down. Both failures are silent — work that never travels, and a
/// device that starts from the beginning every run — so neither shows up
/// anywhere but here.
/// </summary>
public sealed class TaskSyncSessionTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The watermark goes to the highest stamp that was accepted, and not to the
    /// clock. A task saved while the batch was in flight carries a stamp between
    /// the two: a watermark set to now would step over it, and the edit would
    /// never be offered again.
    /// </summary>
    [Fact]
    public async Task The_watermark_lands_on_the_last_task_sent_and_not_on_the_clock()
    {
        var store = new InMemoryTaskStore();
        store.Seed(TaskChanges.Task("Edited at noon", Noon));
        store.Seed(TaskChanges.Task("Edited at one", Noon.AddHours(1)));

        // Hours past the newest task, which is what "now" would have written.
        var clock = new FakeTimeProvider(Noon.AddHours(6));
        var state = new InMemoryTaskSyncStateStore();

        using var fixture = Fixture.Create(store, state, clock,
            (_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"accepted":2}"""));

        var result = await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Pushed);
        Assert.Equal(Noon.AddHours(1), state.Current.PushWatermark);
        Assert.NotEqual(clock.GetUtcNow(), state.Current.PushWatermark);
    }

    /// <summary>A task edited before the watermark has already been accepted, so
    /// it is not offered again. The whole point of keeping one.</summary>
    [Fact]
    public async Task Only_what_changed_since_the_watermark_is_sent()
    {
        var store = new InMemoryTaskStore();
        store.Seed(TaskChanges.Task("Already sent", Noon));
        store.Seed(TaskChanges.Task("Edited since", Noon.AddHours(2)));

        var state = new InMemoryTaskSyncStateStore(new TaskSyncState(Noon.AddHours(1), null));

        using var fixture = Fixture.Create(store, state, new FakeTimeProvider(Noon.AddHours(6)),
            (_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"accepted":1}"""));

        await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        Assert.Single(fixture.Handler.Requests);
        Assert.Contains("Edited since", fixture.Bodies[0], StringComparison.Ordinal);
        Assert.DoesNotContain("Already sent", fixture.Bodies[0], StringComparison.Ordinal);
    }

    /// <summary>
    /// A task deleted on this machine leaves it, carrying its tombstone.
    /// <para>
    /// The selection has to read the one list that sees a tombstone: the
    /// ordinary list hides one by design, so a push built on it offers a deleted
    /// task to nobody and the other device keeps it forever — the failure
    /// <c>.arc42/adr/0005-azure-hosted-task-replica-for-multi-device-sync.md</c>
    /// §"The sync model" rules out in as many words.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_task_deleted_here_is_pushed_as_a_tombstone()
    {
        var store = new InMemoryTaskStore();
        store.Seed(TaskChanges.Task("Deleted here", Noon.AddHours(1), deletedAt: Noon.AddHours(1)));

        var state = new InMemoryTaskSyncStateStore();

        using var fixture = Fixture.Create(store, state, new FakeTimeProvider(Noon.AddHours(6)),
            (_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"accepted":1}"""));

        var result = await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Pushed);

        var change = Assert.Single(Pushed(fixture.Bodies[0]));
        Assert.Equal(Noon.AddHours(1), change.DeletedAt);
        Assert.Equal(Noon.AddHours(1), state.Current.PushWatermark);
    }

    /// <summary>
    /// <c>UpdatedAt</c> is not unique — a task's constructor stamps it from
    /// <c>createdAt</c>, so anything building several tasks from one clock
    /// reading gives them identical stamps — and the selection is strictly
    /// greater than the watermark. So a watermark left in the middle of a shared
    /// stamp excludes every task still carrying it, permanently and silently.
    /// <para>
    /// Here the batch boundary lands inside one: the run sends 200, the next
    /// batch fails, and what must not have happened is the watermark moving onto
    /// the stamp the unsent task shares with the last one that went.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_batch_ending_inside_a_shared_stamp_does_not_advance_past_it()
    {
        var store = new InMemoryTaskStore();

        for (var i = 0; i < 199; i++)
        {
            store.Seed(TaskChanges.Task($"Task {i}", Noon.AddMinutes(i)));
        }

        // Two tasks from one clock reading, straddling the 200-task boundary.
        var shared = Noon.AddHours(5);
        store.Seed(TaskChanges.Task("First of the shared stamp", shared));
        store.Seed(TaskChanges.Task("Second of the shared stamp", shared));

        var state = new InMemoryTaskSyncStateStore();

        using var fixture = Fixture.Create(store, state, new FakeTimeProvider(Noon.AddHours(6)), (_, index) => index switch
        {
            0 => StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"accepted":200}"""),
            _ => StubHttpMessageHandler.Problem(
                HttpStatusCode.ServiceUnavailable, SyncErrorCodes.ReplicaUnavailable, "The replica is not reachable yet."),
        });

        var result = await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);

        // The last distinct stamp below the boundary, so both tasks carrying the
        // shared one are selected again next run.
        Assert.Equal(Noon.AddMinutes(198), state.Current.PushWatermark);

        var pending = await store.ListChangedSinceAsync(state.Current.PushWatermark, TestContext.Current.CancellationToken);
        Assert.Equal(2, pending.Count);
    }

    /// <summary>
    /// The other half of the same rule: once the last batch is away, everything
    /// selected has been sent, so the watermark does reach the highest stamp even
    /// when several tasks share it. Without this a backlog imported from one
    /// clock reading would be re-pushed whole on every sync, for ever.
    /// </summary>
    [Fact]
    public async Task The_last_batch_advances_the_watermark_onto_a_shared_stamp()
    {
        var store = new InMemoryTaskStore();

        var shared = Noon.AddHours(1);
        store.Seed(TaskChanges.Task("Imported together", shared));
        store.Seed(TaskChanges.Task("Imported together as well", shared));

        var state = new InMemoryTaskSyncStateStore();

        using var fixture = Fixture.Create(store, state, new FakeTimeProvider(Noon.AddHours(6)),
            (_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"accepted":2}"""));

        Assert.True((await fixture.Session.PushAsync(TestContext.Current.CancellationToken)).IsSuccess);

        Assert.Equal(shared, state.Current.PushWatermark);
        Assert.Empty(await store.ListChangedSinceAsync(state.Current.PushWatermark, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The whole journey a deletion has to survive: deleted on one device,
    /// pushed, and applied on the other, where it stops the live copy that
    /// machine still holds. The inbound half was already right, so this is what
    /// says the two halves meet.
    /// </summary>
    [Fact]
    public async Task A_deletion_made_here_tombstones_the_task_on_the_other_device()
    {
        var id = Guid.NewGuid();

        var deviceA = new InMemoryTaskStore();
        deviceA.Seed(TaskChanges.Task("Gone", Noon.AddHours(1), id, deletedAt: Noon.AddHours(1)));

        // B still holds the live copy it was given before the deletion happened.
        var deviceB = new InMemoryTaskStore();
        deviceB.Seed(TaskChanges.Task("Gone", Noon, id));

        using var fixture = Fixture.Create(deviceA, new InMemoryTaskSyncStateStore(), new FakeTimeProvider(Noon.AddHours(6)),
            (_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"accepted":1}"""));

        Assert.True((await fixture.Session.PushAsync(TestContext.Current.CancellationToken)).IsSuccess);

        // What the replica would hand B: exactly the changes A put on the wire.
        var feed = Pushed(fixture.Bodies[0])
            .Select(change => TaskChanges.Record(change, Guid.NewGuid(), serverTimestamp: 100L))
            .ToList();

        var merged = await new TaskReplicaMerge(deviceB)
            .ApplyAsync(feed, DateTimeOffset.MinValue, TestContext.Current.CancellationToken);

        Assert.Equal(1, merged.Applied);
        Assert.Null(await deviceB.GetAsync(id, TestContext.Current.CancellationToken));

        var tombstoned = await deviceB.GetIncludingDeletedAsync(id, TestContext.Current.CancellationToken);

        Assert.NotNull(tombstoned);
        Assert.Equal(Noon.AddHours(1), tombstoned.DeletedAt);
    }

    [Fact]
    public async Task A_push_that_fails_leaves_the_watermark_where_it_was()
    {
        var store = new InMemoryTaskStore();
        store.Seed(TaskChanges.Task("Never accepted", Noon));

        var state = new InMemoryTaskSyncStateStore();

        using var fixture = Fixture.Create(store, state, new FakeTimeProvider(Noon),
            (_, _) => StubHttpMessageHandler.Problem(
                HttpStatusCode.ServiceUnavailable, SyncErrorCodes.ReplicaUnavailable, "The replica is not reachable yet."));

        var result = await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SyncErrorCodes.ReplicaUnavailable, result.Error.Code);
        Assert.Equal(DateTimeOffset.MinValue, state.Current.PushWatermark);
        Assert.Empty(state.Saved);
    }

    /// <summary>
    /// The cursor is written after every page, so a pull that dies half way
    /// resumes rather than restarts. Asserted through the second call's query,
    /// because what matters is not that a string was stored but that the next
    /// request carries it.
    /// </summary>
    [Fact]
    public async Task An_interrupted_pull_resumes_from_the_page_it_reached()
    {
        var store = new InMemoryTaskStore();
        var state = new InMemoryTaskSyncStateStore();

        using var fixture = Fixture.Create(store, state, new FakeTimeProvider(Noon), (_, index) => index switch
        {
            0 => StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"tasks":[],"since":"cursor-1","hasMore":true}"""),
            1 => StubHttpMessageHandler.Problem(
                HttpStatusCode.ServiceUnavailable, SyncErrorCodes.ReplicaUnavailable, "The replica is not reachable yet."),
            _ => StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"tasks":[],"since":"cursor-2","hasMore":false}"""),
        });

        var interrupted = await fixture.Session.PullAsync(TestContext.Current.CancellationToken);

        Assert.True(interrupted.IsFailure);
        Assert.Equal("cursor-1", state.Current.PullCursor);

        var resumed = await fixture.Session.PullAsync(TestContext.Current.CancellationToken);

        Assert.True(resumed.IsSuccess);
        Assert.Contains("since=cursor-1", fixture.Queries[2], StringComparison.Ordinal);
        Assert.Equal("cursor-2", state.Current.PullCursor);
    }

    /// <summary>Each page's cursor is saved as that page lands rather than once
    /// at the end, which is the difference between resuming and restarting.</summary>
    [Fact]
    public async Task Every_page_saves_its_cursor()
    {
        var store = new InMemoryTaskStore();
        var state = new InMemoryTaskSyncStateStore();

        using var fixture = Fixture.Create(store, state, new FakeTimeProvider(Noon), (_, index) => index switch
        {
            0 => StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"tasks":[],"since":"cursor-1","hasMore":true}"""),
            1 => StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"tasks":[],"since":"cursor-2","hasMore":true}"""),
            _ => StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"tasks":[],"since":"cursor-3","hasMore":false}"""),
        });

        await fixture.Session.PullAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            ["cursor-1", "cursor-2", "cursor-3"],
            state.Saved.Select(saved => saved.PullCursor).ToArray());
    }

    // --- A cursor the service will not resume from ---------------------------

    /// <summary>
    /// The recovery three doc comments already promise. A container recreated by
    /// a redeploy — or the local emulator restarting on a fresh volume — retires
    /// every continuation it ever minted, and a device that kept sending the old
    /// one would get a 400 on every pull it ever made again. The only recovery
    /// otherwise is deleting the state file by hand.
    /// </summary>
    [Fact]
    public async Task An_expired_cursor_is_dropped_and_the_pull_starts_over()
    {
        var store = new InMemoryTaskStore();
        var state = new InMemoryTaskSyncStateStore(new TaskSyncState(Noon, "a-cursor-the-store-forgot"));

        using var fixture = Fixture.Create(store, state, new FakeTimeProvider(Noon), (_, index) => index switch
        {
            0 => StubHttpMessageHandler.Problem(
                HttpStatusCode.BadRequest, SyncErrorCodes.SyncCursorExpired, "That cursor is too old to resume from."),
            _ => StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"tasks":[],"since":"cursor-1","hasMore":false}"""),
        });

        var result = await fixture.Session.PullAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);

        // From the beginning: no cursor on the second request at all.
        Assert.DoesNotContain("since=", fixture.Queries[1], StringComparison.Ordinal);

        // And the forgotten one is off the disk before the retry, so a run that
        // died in between would not send it again either.
        Assert.Null(state.Saved[0].PullCursor);
        Assert.Equal("cursor-1", state.Current.PullCursor);
    }

    /// <summary>A cursor the service cannot even read recovers the same way. It
    /// is a different fault — a truncated or re-signed string rather than one
    /// the store has aged out — and the device has the same single answer to
    /// both: drop it and start over.</summary>
    [Fact]
    public async Task A_malformed_cursor_is_dropped_and_the_pull_starts_over()
    {
        var store = new InMemoryTaskStore();
        var state = new InMemoryTaskSyncStateStore(new TaskSyncState(Noon, "not-a-cursor"));

        using var fixture = Fixture.Create(store, state, new FakeTimeProvider(Noon), (_, index) => index switch
        {
            0 => StubHttpMessageHandler.Problem(
                HttpStatusCode.BadRequest, SyncErrorCodes.SyncCursorMalformed, "That is not a cursor this service minted."),
            _ => StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"tasks":[],"since":"cursor-1","hasMore":false}"""),
        });

        Assert.True((await fixture.Session.PullAsync(TestContext.Current.CancellationToken)).IsSuccess);
        Assert.Equal("cursor-1", state.Current.PullCursor);
    }

    /// <summary>Once, not in a loop. A service answering "expired" to a pull
    /// that carried no cursor is saying something the device cannot fix, and a
    /// retry per answer would be an unbounded one.</summary>
    [Fact]
    public async Task Starting_over_is_tried_once_and_not_in_a_loop()
    {
        var store = new InMemoryTaskStore();
        var state = new InMemoryTaskSyncStateStore(new TaskSyncState(Noon, "a-cursor-the-store-forgot"));

        using var fixture = Fixture.Create(store, state, new FakeTimeProvider(Noon), (_, _) =>
            StubHttpMessageHandler.Problem(
                HttpStatusCode.BadRequest, SyncErrorCodes.SyncCursorExpired, "That cursor is too old to resume from."));

        var result = await fixture.Session.PullAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SyncErrorCodes.SyncCursorExpired, result.Error.Code);
        Assert.Equal(2, fixture.Handler.Requests.Count);
    }

    /// <summary>
    /// The one cursor failure that is not recovered from. A correctly-signed
    /// cursor naming another owner's feed is the event .arc42/adr/0005
    /// section Consequences asks to be loud about, and quietly starting over
    /// would be exactly the quiet.
    /// </summary>
    [Fact]
    public async Task A_cursor_belonging_to_somebody_else_is_not_swallowed()
    {
        var store = new InMemoryTaskStore();
        var state = new InMemoryTaskSyncStateStore(new TaskSyncState(Noon, "somebody-elses-cursor"));

        using var fixture = Fixture.Create(store, state, new FakeTimeProvider(Noon), (_, _) =>
            StubHttpMessageHandler.Problem(
                HttpStatusCode.Forbidden, SyncErrorCodes.SyncCursorNotYours, "That cursor is not yours."));

        var result = await fixture.Session.PullAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SyncErrorCodes.SyncCursorNotYours, result.Error.Code);
        Assert.Single(fixture.Handler.Requests);
        Assert.Equal("somebody-elses-cursor", state.Current.PullCursor);
    }

    /// <summary>A page's tasks are applied as it arrives, and what came back is
    /// counted apart from what was written — a device's own echo arrives and
    /// changes nothing.</summary>
    [Fact]
    public async Task A_pulled_task_is_applied_and_counted_apart_from_what_arrived()
    {
        var store = new InMemoryTaskStore();
        var state = new InMemoryTaskSyncStateStore();
        var change = TaskChanges.Change("From the other machine", Noon);

        var body = System.Text.Json.JsonSerializer.Serialize(new
        {
            tasks = new[] { new { change, deviceId = Guid.NewGuid(), serverTimestamp = 100L } },
            since = "cursor-1",
            hasMore = false
        });

        using var fixture = Fixture.Create(store, state, new FakeTimeProvider(Noon),
            (_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, body));

        var result = await fixture.Session.PullAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Pulled);
        Assert.Equal(1, result.Value.Applied);
        Assert.Equal("From the other machine", store.Tasks[change.Id].Title);
    }

    /// <summary>
    /// A page carrying a document this build cannot read still advances the
    /// cursor. The cursor is saved after the apply, so an apply that threw would
    /// leave the cursor where it was and replay the same page forever — one
    /// unreadable document, and this device never receives another task change.
    /// </summary>
    [Fact]
    public async Task A_page_holding_an_unreadable_document_still_advances_the_cursor()
    {
        var store = new InMemoryTaskStore();
        var state = new InMemoryTaskSyncStateStore();

        var unreadable = TaskChanges.Change("From a newer build", Noon);
        unreadable = unreadable with { Task = unreadable.Task with { Status = "awaiting_review" } };

        var body = JsonSerializer.Serialize(new
        {
            tasks = new[] { new { change = unreadable, deviceId = Guid.NewGuid(), serverTimestamp = 100L } },
            since = "cursor-1",
            hasMore = false
        });

        using var fixture = Fixture.Create(store, state, new FakeTimeProvider(Noon),
            (_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, body));

        var result = await fixture.Session.PullAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Pulled);
        Assert.Equal(0, result.Value.Applied);
        Assert.Equal(1, result.Value.Skipped);
        Assert.Equal("cursor-1", state.Current.PullCursor);
        Assert.Empty(store.Tasks);
    }

    /// <summary>Push first: the pull is what tells the device it is up to date,
    /// and a pull that ran first would say so while local work was still
    /// unsent.</summary>
    [Fact]
    public async Task Syncing_pushes_before_it_pulls()
    {
        var store = new InMemoryTaskStore();
        store.Seed(TaskChanges.Task("Mine", Noon));

        var state = new InMemoryTaskSyncStateStore();

        using var fixture = Fixture.Create(store, state, new FakeTimeProvider(Noon.AddHours(6)), (request, _) =>
            request.Method == HttpMethod.Post
                ? StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"accepted":1}""")
                : StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"tasks":[],"since":"cursor-1","hasMore":false}"""));

        var result = await fixture.Session.SyncAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Pushed);
        Assert.Equal(Noon.AddHours(6), result.Value.At);
        Assert.Equal(HttpMethod.Post, fixture.Handler.Requests[0].Method);
        Assert.Equal(HttpMethod.Get, fixture.Handler.Requests[1].Method);
    }

    /// <summary>A push that fails stops the exchange. The failure is almost
    /// always the service being unreachable, and a pull that then said the same
    /// thing is a second sentence for a person to read.</summary>
    [Fact]
    public async Task A_failed_push_stops_the_exchange_before_the_pull()
    {
        var store = new InMemoryTaskStore();
        store.Seed(TaskChanges.Task("Mine", Noon));

        using var fixture = Fixture.Create(store, new InMemoryTaskSyncStateStore(), new FakeTimeProvider(Noon),
            (_, _) => throw new HttpRequestException("No route to host."));

        var result = await fixture.Session.SyncAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Single(fixture.Handler.Requests);
    }

    /// <summary>The changes a push actually put on the wire, read back through
    /// the contract rather than matched inside the JSON. A substring check cannot
    /// tell a tombstone from a live task — the field that carries one is null on
    /// most changes — and reading it through the record is also what catches a
    /// rename of it.</summary>
    private static IReadOnlyList<TaskChange> Pushed(string body) =>
        JsonSerializer.Deserialize<PushTasksRequest>(
            body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Tasks;

    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient _http;

        private Fixture(HttpClient http, StubHttpMessageHandler handler, TaskSyncSession session, List<string> bodies, List<string> queries)
        {
            _http = http;
            Handler = handler;
            Session = session;
            Bodies = bodies;
            Queries = queries;
        }

        public StubHttpMessageHandler Handler { get; }

        public TaskSyncSession Session { get; }

        public List<string> Bodies { get; }

        public List<string> Queries { get; }

        public static Fixture Create(
            InMemoryTaskStore tasks,
            ITaskSyncStateStore state,
            TimeProvider time,
            Func<HttpRequestMessage, int, HttpResponseMessage> respond)
        {
            var bodies = new List<string>();
            var queries = new List<string>();

            var handler = new StubHttpMessageHandler((request, index) =>
            {
                queries.Add(request.RequestUri?.Query ?? string.Empty);

                bodies.Add(request.Content is null
                    ? string.Empty
                    : request.Content.ReadAsStringAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult());

                return respond(request, index);
            });

            var http = new HttpClient(handler) { BaseAddress = new Uri("https://sync.test") };

            var session = new TaskSyncSession(
                new TaskSyncClient(http),
                new TaskReplicaMerge(tasks),
                tasks,
                state,
                time);

            return new Fixture(http, handler, session, bodies, queries);
        }

        public void Dispose() => _http.Dispose();
    }
}
