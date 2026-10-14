using System.Net;
using System.Text.Json;

using Backlog.Infrastructure.Sync.Annotations;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// One annotation exchange, and the bookkeeping that decides whether a device
/// ever catches up: where the push watermark lands, when the cursor is written
/// down, and what the pull does with what it gets. The same questions
/// <see cref="TaskSyncSessionTests"/> asks, because the loop is the same loop.
/// </summary>
public sealed class AnnotationSyncSessionTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid ThisOwner = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ThisDevice = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid AnotherOwner = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static readonly DeviceCredential Paired = new(ThisOwner, ThisDevice, "Workshop PC", "a-registration-credential");

    /// <summary>Progress recorded for the fixture's own identity. A seeded state
    /// with none is started over on first use, by design.</summary>
    private static AnnotationSyncState Mine(DateTimeOffset watermark, string? cursor) =>
        new(watermark, cursor, ThisOwner, ThisDevice);

    [Fact]
    public async Task Only_what_changed_since_the_watermark_is_sent_and_the_watermark_lands_on_it()
    {
        var store = new InMemoryDevbookAnnotationStore();
        store.Seed(Annotations.Local("Already sent", Noon));
        store.Seed(Annotations.Local("Edited since", Noon.AddHours(1)));
        var state = new InMemoryAnnotationSyncStateStore(Mine(Noon, null));

        using var fixture = Fixture.Create(store, state, new FakeTimeProvider(Noon.AddHours(6)),
            (_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"accepted":1}"""));

        var result = await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Pushed);

        var request = Assert.Single(fixture.Handler.Requests);
        Assert.Equal(SyncRoutes.Absolute(SyncRoutes.Annotations), request.Path);

        var change = Assert.Single(Pushed(fixture.Bodies[0]));
        Assert.Equal("Edited since", change.Annotation.Body);
        Assert.Equal(Annotations.Repository, change.Annotation.RepositoryAlias);
        Assert.Equal(Annotations.Chapter, change.Annotation.ChapterPath);

        // Onto the stamp that was accepted, never onto the clock.
        Assert.Equal(Noon.AddHours(1), state.Current.PushWatermark);
    }

    /// <summary>The replica takes a batch in part when it already holds a later
    /// version of the rest; the watermark moves on regardless, so the shortfall
    /// is counted or the edit that lost is lost in silence.</summary>
    [Fact]
    public async Task A_batch_the_replica_took_in_part_reports_the_rest_as_refused()
    {
        var store = new InMemoryDevbookAnnotationStore();
        store.Seed(Annotations.Local("Kept", Noon.AddHours(1)));
        store.Seed(Annotations.Local("Refused as stale", Noon.AddHours(2)));
        var state = new InMemoryAnnotationSyncStateStore();

        using var fixture = Fixture.Create(store, state, new FakeTimeProvider(Noon.AddHours(6)),
            (_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"accepted":1}"""));

        var result = await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Pushed);
        Assert.Equal(1, result.Value.Refused);
        Assert.Equal(Noon.AddHours(2), state.Current.PushWatermark);
    }

    [Fact]
    public async Task A_remark_deleted_here_is_pushed_as_a_tombstone()
    {
        var store = new InMemoryDevbookAnnotationStore();
        store.Seed(Annotations.Local("Deleted here", Noon.AddHours(1), deletedAt: Noon.AddHours(1)));
        var state = new InMemoryAnnotationSyncStateStore(Mine(DateTimeOffset.MinValue, null));

        using var fixture = Fixture.Create(store, state, new FakeTimeProvider(Noon.AddHours(6)),
            (_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"accepted":1}"""));

        await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        var change = Assert.Single(Pushed(fixture.Bodies[0]));
        Assert.Equal(Noon.AddHours(1), change.DeletedAt);
    }

    [Fact]
    public async Task A_draft_never_leaves_the_machine()
    {
        var store = new InMemoryDevbookAnnotationStore();
        store.Seed(Annotations.Local(string.Empty, Noon.AddHours(1)));
        var state = new InMemoryAnnotationSyncStateStore(Mine(DateTimeOffset.MinValue, null));

        using var fixture = Fixture.Create(store, state, new FakeTimeProvider(Noon.AddHours(6)),
            (_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"accepted":0}"""));

        var result = await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Empty(fixture.Handler.Requests);
    }

    /// <summary>A remark the service will never store — here one filed under no
    /// repository — used to take the whole batch down with it, on every cycle:
    /// the service refuses a batch whole, and the watermark never moved.</summary>
    [Fact]
    public async Task A_remark_the_service_would_refuse_does_not_hold_back_the_rest()
    {
        var store = new InMemoryDevbookAnnotationStore();
        var valid = store.Seed(Annotations.Local("Placed on a repository", Noon.AddHours(1)));
        store.Seed(Annotations.Local("Filed under no repository", Noon.AddHours(2)) with { RepositoryAlias = string.Empty });
        var state = new InMemoryAnnotationSyncStateStore(Mine(Noon, null));

        using var fixture = Fixture.Create(store, state, new FakeTimeProvider(Noon.AddHours(6)), AsTheServiceAnswers);

        var result = await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(1, result.Value.Pushed);

        Assert.Single(fixture.Handler.Requests);
        var change = Assert.Single(Pushed(fixture.Bodies[0]));
        Assert.Equal(valid.Id, change.Id);

        // Past both: the one left out cannot become pushable without an edit,
        // and an edit stamps it past this watermark again.
        Assert.Equal(Noon.AddHours(2), state.Current.PushWatermark);
    }

    /// <summary>A body past the service's bound is refused the same way, so it
    /// is left out the same way.</summary>
    [Fact]
    public async Task A_remark_longer_than_the_service_stores_does_not_hold_back_the_rest()
    {
        var store = new InMemoryDevbookAnnotationStore();
        var valid = store.Seed(Annotations.Local("Short enough", Noon.AddHours(1)));
        store.Seed(Annotations.Local(new string('x', AnnotationRecordLimits.BodyLength + 1), Noon.AddHours(2)));
        var state = new InMemoryAnnotationSyncStateStore(Mine(Noon, null));

        using var fixture = Fixture.Create(store, state, new FakeTimeProvider(Noon.AddHours(6)), AsTheServiceAnswers);

        var result = await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(1, result.Value.Pushed);
        Assert.Equal(valid.Id, Assert.Single(Pushed(fixture.Bodies[0])).Id);
        Assert.Equal(Noon.AddHours(2), state.Current.PushWatermark);
    }

    /// <summary>Every rule the push endpoint refuses a remark by, with the
    /// service's own numbers: a batch of nothing else is not sent at all, and
    /// the watermark still moves past it, so it is not offered again until an
    /// edit restamps it.</summary>
    [Theory]
    [InlineData("blank repository alias")]
    [InlineData("blank chapter path")]
    [InlineData("negative block index")]
    [InlineData("repository alias over 200")]
    [InlineData("chapter path over 1024")]
    [InlineData("author over 255")]
    [InlineData("body over 8000")]
    [InlineData("block hash over 128")]
    public async Task A_batch_of_nothing_the_service_would_store_is_not_sent(string shape)
    {
        var local = Annotations.Local("Kept here", Noon.AddHours(1));
        var unpushable = shape switch
        {
            "blank repository alias" => local with { RepositoryAlias = "  " },
            "blank chapter path" => local with { ChapterPath = string.Empty },
            "negative block index" => local with { BlockIndex = -1 },
            "repository alias over 200" => local with { RepositoryAlias = new string('a', 201) },
            "chapter path over 1024" => local with { ChapterPath = new string('c', 1_025) },
            "author over 255" => local with { Author = new string('w', 256) },
            "body over 8000" => local with { Body = new string('b', 8_001) },
            "block hash over 128" => local with { BlockHash = new string('h', 129) },
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "No such shape."),
        };

        var store = new InMemoryDevbookAnnotationStore();
        store.Seed(unpushable);
        var state = new InMemoryAnnotationSyncStateStore(Mine(Noon, null));

        using var fixture = Fixture.Create(store, state, new FakeTimeProvider(Noon.AddHours(6)),
            (_, _) => StubHttpMessageHandler.Problem(HttpStatusCode.BadRequest, SyncErrorCodes.AnnotationInvalid, "Refused."));

        var result = await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Empty(fixture.Handler.Requests);
        Assert.Equal(0, result.Value.Pushed);
        Assert.Equal(Noon.AddHours(1), state.Current.PushWatermark);
    }

    /// <summary>A deletion must always reach the other devices, whatever the
    /// remark said: a remark edited past the body bound and then deleted would
    /// otherwise stay on every other device for good. A tombstone says nothing,
    /// so it carries no body.</summary>
    [Fact]
    public async Task A_deleted_remark_is_sent_as_a_tombstone_however_long_it_was()
    {
        var store = new InMemoryDevbookAnnotationStore();
        var deleted = store.Seed(Annotations.Local(new string('x', 8_001), Noon.AddHours(1), deletedAt: Noon.AddHours(1)));
        var state = new InMemoryAnnotationSyncStateStore(Mine(Noon, null));

        using var fixture = Fixture.Create(store, state, new FakeTimeProvider(Noon.AddHours(6)), AsTheServiceAnswers);

        var result = await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(1, result.Value.Pushed);
        Assert.Equal(0, result.Value.Skipped);

        var tombstone = Assert.Single(Pushed(Assert.Single(fixture.Bodies)));
        Assert.Equal(deleted.Id, tombstone.Id);
        Assert.Equal(Noon.AddHours(1), tombstone.DeletedAt);
        Assert.Equal(string.Empty, tombstone.Annotation.Body);
        Assert.Equal(Noon.AddHours(1), state.Current.PushWatermark);
    }

    /// <summary>The exchange's summary is the one the worker keeps, so a
    /// refusal the push counted has to survive into it.</summary>
    [Fact]
    public async Task Syncing_keeps_the_count_of_what_the_replica_refused()
    {
        var store = new InMemoryDevbookAnnotationStore();
        store.Seed(Annotations.Local("Older than the replica's copy", Noon.AddHours(1)));
        var state = new InMemoryAnnotationSyncStateStore(Mine(Noon, null));

        using var fixture = Fixture.Create(store, state, new FakeTimeProvider(Noon.AddHours(6)), (request, _) =>
            request.Method == HttpMethod.Post
                ? StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"accepted":0}""")
                : StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"annotations":[],"since":"cursor-1","hasMore":false}"""));

        var result = await fixture.Session.SyncAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Refused);
    }

    /// <summary>A remark left out is said, once, at Warning — by id, chapter and
    /// reason, and never by what it says: the body is the person's own
    /// text.</summary>
    [Fact]
    public async Task A_remark_left_out_is_counted_and_named_in_a_warning_that_leaves_out_its_body()
    {
        var store = new InMemoryDevbookAnnotationStore();
        store.Seed(Annotations.Local("Placed on a repository", Noon.AddHours(1)));
        var unscoped = store.Seed(Annotations.Local("A private thought", Noon.AddHours(2)) with { RepositoryAlias = string.Empty });
        var state = new InMemoryAnnotationSyncStateStore(Mine(Noon, null));
        var log = new CapturingLogger<AnnotationSyncSession>();

        using var fixture = Fixture.Create(store, state, new FakeTimeProvider(Noon.AddHours(6)), AsTheServiceAnswers, log);

        var result = await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Value.Skipped);

        var line = Assert.Single(log.Lines);
        Assert.Equal(LogLevel.Warning, line.Level);
        Assert.Contains(unscoped.Id.ToString(), line.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Annotations.Chapter, line.Message, StringComparison.Ordinal);
        Assert.Contains("alias of the repository", line.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("A private thought", line.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(line.Properties.Values, value => value?.ToString()?.Contains("A private thought", StringComparison.Ordinal) == true);
    }

    /// <summary>The refusal used to end the exchange before the pull, so a
    /// device with one such remark never heard from the others either.</summary>
    [Fact]
    public async Task A_remark_left_out_does_not_stop_the_pull()
    {
        var store = new InMemoryDevbookAnnotationStore();
        store.Seed(Annotations.Local("Placed on a repository", Noon.AddHours(1)));
        store.Seed(Annotations.Local("Filed under no repository", Noon.AddHours(2)) with { RepositoryAlias = string.Empty });
        var state = new InMemoryAnnotationSyncStateStore(Mine(Noon, null));

        using var fixture = Fixture.Create(store, state, new FakeTimeProvider(Noon.AddHours(6)), AsTheServiceAnswers);

        var result = await fixture.Session.SyncAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal([HttpMethod.Post, HttpMethod.Get], fixture.Handler.Requests.Select(request => request.Method).ToArray());
        Assert.Equal(1, result.Value.Pushed);
        Assert.Equal(1, result.Value.Skipped);
        Assert.Equal("cursor-1", state.Current.PullCursor);
    }

    [Fact]
    public async Task A_push_that_fails_leaves_the_watermark_where_it_was()
    {
        var store = new InMemoryDevbookAnnotationStore();
        store.Seed(Annotations.Local("Unsent", Noon.AddHours(1)));
        var state = new InMemoryAnnotationSyncStateStore(Mine(Noon, null));

        using var fixture = Fixture.Create(store, state, new FakeTimeProvider(Noon.AddHours(6)),
            (_, _) => StubHttpMessageHandler.Problem(HttpStatusCode.ServiceUnavailable, SyncErrorCodes.ReplicaUnavailable, "Not yet."));

        var result = await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SyncErrorCodes.ReplicaUnavailable, result.Error.Code);
        Assert.Equal(Noon, state.Current.PushWatermark);
        Assert.Empty(state.Saved);
    }

    [Fact]
    public async Task Every_page_saves_its_cursor_and_the_loop_ends_on_the_services_word()
    {
        var store = new InMemoryDevbookAnnotationStore();
        var state = new InMemoryAnnotationSyncStateStore(Mine(DateTimeOffset.MinValue, null));

        using var fixture = Fixture.Create(store, state, new FakeTimeProvider(Noon), (_, index) => index switch
        {
            0 => StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"annotations":[],"since":"cursor-1","hasMore":true}"""),
            1 => StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"annotations":[],"since":"cursor-2","hasMore":true}"""),
            _ => StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"annotations":[],"since":"cursor-3","hasMore":false}"""),
        });

        await fixture.Session.PullAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["cursor-1", "cursor-2", "cursor-3"], state.Saved.Select(saved => saved.PullCursor).ToArray());
        Assert.Contains("since=cursor-2", fixture.Queries[2], StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_expired_cursor_is_dropped_and_the_pull_starts_over_once()
    {
        var store = new InMemoryDevbookAnnotationStore();
        var state = new InMemoryAnnotationSyncStateStore(Mine(Noon, "a-cursor-the-store-forgot"));

        using var fixture = Fixture.Create(store, state, new FakeTimeProvider(Noon), (_, index) => index switch
        {
            0 => StubHttpMessageHandler.Problem(HttpStatusCode.BadRequest, SyncErrorCodes.SyncCursorExpired, "Too old."),
            _ => StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"annotations":[],"since":"cursor-1","hasMore":false}"""),
        });

        var result = await fixture.Session.PullAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("since=", fixture.Queries[1], StringComparison.Ordinal);
        Assert.Null(state.Saved[0].PullCursor);
        Assert.Equal("cursor-1", state.Current.PullCursor);
    }

    [Fact]
    public async Task A_cursor_belonging_to_somebody_else_is_not_swallowed()
    {
        var store = new InMemoryDevbookAnnotationStore();
        var state = new InMemoryAnnotationSyncStateStore(Mine(Noon, "somebody-elses-cursor"));

        using var fixture = Fixture.Create(store, state, new FakeTimeProvider(Noon),
            (_, _) => StubHttpMessageHandler.Problem(HttpStatusCode.Forbidden, SyncErrorCodes.SyncCursorNotYours, "Not yours."));

        var result = await fixture.Session.PullAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SyncErrorCodes.SyncCursorNotYours, result.Error.Code);
        Assert.Equal("somebody-elses-cursor", state.Current.PullCursor);
    }

    [Fact]
    public async Task A_pulled_remark_is_applied_and_counted_apart_from_what_arrived()
    {
        var store = new InMemoryDevbookAnnotationStore();
        var state = new InMemoryAnnotationSyncStateStore(Mine(DateTimeOffset.MinValue, null));
        var change = Annotations.Change(Guid.NewGuid(), "From the other machine", Noon);

        var body = JsonSerializer.Serialize(new
        {
            annotations = new[] { new { change, deviceId = Guid.NewGuid(), serverTimestamp = 100L } },
            since = "cursor-1",
            hasMore = false
        });

        using var fixture = Fixture.Create(store, state, new FakeTimeProvider(Noon),
            (_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, body));

        var result = await fixture.Session.PullAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Pulled);
        Assert.Equal(1, result.Value.Applied);

        var listed = Assert.Single(store.List(Annotations.Repository, Annotations.Chapter));
        Assert.Equal("From the other machine", listed.Body);
        Assert.Equal("DEV-LAPTOP", listed.Author);
    }

    [Fact]
    public async Task Syncing_pushes_before_it_pulls_and_a_failed_push_stops_the_exchange()
    {
        var store = new InMemoryDevbookAnnotationStore();
        store.Seed(Annotations.Local("Unsent", Noon.AddHours(1)));
        var state = new InMemoryAnnotationSyncStateStore(Mine(DateTimeOffset.MinValue, null));

        using var ordered = Fixture.Create(store, state, new FakeTimeProvider(Noon), (request, _) =>
            request.Method == HttpMethod.Post
                ? StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"accepted":1}""")
                : StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"annotations":[],"since":"cursor-1","hasMore":false}"""));

        var result = await ordered.Session.SyncAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal([HttpMethod.Post, HttpMethod.Get], ordered.Handler.Requests.Select(request => request.Method).ToArray());
        Assert.Equal(1, result.Value.Pushed);

        store.Seed(Annotations.Local("Another", Noon.AddHours(2)));

        using var failing = Fixture.Create(store, state, new FakeTimeProvider(Noon),
            (_, _) => StubHttpMessageHandler.Problem(HttpStatusCode.ServiceUnavailable, SyncErrorCodes.ReplicaUnavailable, "Not yet."));

        var stopped = await failing.Session.SyncAsync(TestContext.Current.CancellationToken);

        Assert.True(stopped.IsFailure);
        Assert.Single(failing.Handler.Requests);
    }

    private static IReadOnlyList<AnnotationChange> Pushed(string body) =>
        JsonSerializer.Deserialize<PushAnnotationsRequest>(
            body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Annotations;

    /// <summary>
    /// The service as it answers today: a push is refused whole, with
    /// <see cref="SyncErrorCodes.AnnotationInvalid"/>, when any one change in it
    /// names no repository or carries a body over the service's 8,000-character
    /// bound, and is otherwise taken in full. A pull reads an empty feed.
    /// </summary>
    private static HttpResponseMessage AsTheServiceAnswers(HttpRequestMessage request, int index)
    {
        if (request.Method != HttpMethod.Post)
        {
            return StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"annotations":[],"since":"cursor-1","hasMore":false}""");
        }

        var changes = Pushed(request.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult());

        if (changes.Any(change =>
                string.IsNullOrWhiteSpace(change.Annotation.RepositoryAlias)
                || change.Annotation.Body.Length > 8_000))
        {
            return StubHttpMessageHandler.Problem(
                HttpStatusCode.BadRequest,
                SyncErrorCodes.AnnotationInvalid,
                "An annotation needs the alias of the repository its chapter is in.");
        }

        return StubHttpMessageHandler.Json(HttpStatusCode.OK, $$"""{"accepted":{{changes.Count}}}""");
    }

    /// <summary>The same rule as the task and session feeds: progress recorded
    /// under another owner - or under none - is started over and stamped.</summary>
    [Fact]
    public async Task Progress_recorded_for_another_owner_is_started_over()
    {
        var state = new InMemoryAnnotationSyncStateStore(
            new AnnotationSyncState(Noon, "the-old-owners-cursor", AnotherOwner, ThisDevice));

        using var fixture = Fixture.Create(new InMemoryDevbookAnnotationStore(), state, new FakeTimeProvider(Noon),
            (_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"annotations":[],"since":"cursor-fresh","hasMore":false}"""));

        var result = await fixture.Session.PullAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("the-old-owners-cursor", fixture.Queries[0], StringComparison.Ordinal);
        Assert.Equal(ThisOwner, state.Current.OwnerId);
        Assert.Equal(ThisDevice, state.Current.DeviceId);
        Assert.Equal(DateTimeOffset.MinValue, state.Saved[0].PushWatermark);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient _http;

        private Fixture(HttpClient http, StubHttpMessageHandler handler, AnnotationSyncSession session, List<string> bodies, List<string> queries)
        {
            _http = http;
            Handler = handler;
            Session = session;
            Bodies = bodies;
            Queries = queries;
        }

        public StubHttpMessageHandler Handler { get; }

        public AnnotationSyncSession Session { get; }

        public List<string> Bodies { get; }

        public List<string> Queries { get; }

        public static Fixture Create(
            InMemoryDevbookAnnotationStore store,
            IAnnotationSyncStateStore state,
            TimeProvider time,
            Func<HttpRequestMessage, int, HttpResponseMessage> respond,
            ILogger<AnnotationSyncSession>? log = null)
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

            var session = new AnnotationSyncSession(
                new AnnotationSyncClient(http),
                new AnnotationReplicaMerge(store),
                store,
                state,
                new InMemoryDeviceCredentialStore(Paired),
                time,
                log);

            return new Fixture(http, handler, session, bodies, queries);
        }

        public void Dispose() => _http.Dispose();
    }
}
