using System.Net;
using System.Text.Json;

using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks;
using Backlog.Modules.Tasks.DomainModels;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// The background loop: when it runs, when it refuses to, and what it does with
/// a cycle that fails.
///
/// <para>Every failure this covers is silent in the product. A loop that never
/// started, a loop that stopped after the first offline cycle, and two cycles
/// overlapping all look exactly like a device that simply has nothing to sync -
/// there is no screen and no log line that tells them apart, so this file is the
/// only place they are told apart at all.</para>
///
/// <para>The worker drives a real <see cref="TaskSyncSession"/> over a scripted
/// wire rather than a stand-in for one. The session is sealed, and its behaviour
/// is half of what these assert - that resetting the watermark really does
/// re-send a tombstone, that a null cursor really does read the feed from the
/// beginning - so a seam between the two would have tested neither.</para>
/// </summary>
public sealed class TaskSyncWorkerTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    private static readonly DeviceCredential Paired = new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        "Workshop PC",
        "a-registration-credential");

    // --- The two gates --------------------------------------------------------

    /// <summary>A person who has switched task sync off has asked for their tasks
    /// to stay on the machine, and a loop that went on exchanging would be the
    /// one thing the switch exists to stop.</summary>
    [Fact]
    public void No_cycle_runs_while_the_task_sync_feature_is_off()
    {
        using var fixture = Fixture.Create(featureOn: false, paired: true);

        fixture.Clock.Advance(Fixture.WellPastSeveralCycles);

        Assert.Equal(0, fixture.SessionsResolved);
        Assert.Empty(fixture.Handler.Requests);
    }

    /// <summary>Switching it on starts the loop where it stands, because the
    /// person who has just switched it on is looking at the screen to see whether
    /// it works. Switching it off stops it the same way, without a
    /// restart.</summary>
    [Fact]
    public async Task Switching_the_feature_on_starts_the_loop_and_switching_it_off_stops_it()
    {
        using var fixture = Fixture.Create(featureOn: false, paired: true);

        var first = fixture.NextCycle();
        _ = fixture.Features.SetEnabled(SyncFeatures.TaskSync, true);
        fixture.Clock.Advance(TaskSyncWorker.FirstCycleDelay);
        await first.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);

        Assert.Equal(1, fixture.SessionsResolved);

        _ = fixture.Features.SetEnabled(SyncFeatures.TaskSync, false);
        fixture.Clock.Advance(Fixture.WellPastSeveralCycles);

        Assert.Equal(1, fixture.SessionsResolved);
    }

    /// <summary>An unpaired device has no owner to replicate under, so there is
    /// nothing a cycle could ask for. Pairing one starts the loop without a
    /// restart, for the reason switching the feature on does.</summary>
    [Fact]
    public async Task No_cycle_runs_until_a_device_credential_is_stored()
    {
        using var fixture = Fixture.Create(featureOn: true, paired: false);

        fixture.Clock.Advance(Fixture.WellPastSeveralCycles);

        Assert.Equal(0, fixture.SessionsResolved);

        var first = fixture.NextCycle();
        fixture.Credentials.Save(Paired);
        fixture.Clock.Advance(TaskSyncWorker.FirstCycleDelay);
        await first.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);

        Assert.Equal(1, fixture.SessionsResolved);
    }

    // --- One cycle at a time --------------------------------------------------

    /// <summary>
    /// A cycle slow enough to still be running when the next tick arrives.
    /// <para>
    /// Two exchanges at once would each advance the push watermark from what they
    /// read before the other saved, and the lower of the two would write last -
    /// so the device would skip whatever fell between them, permanently and with
    /// nothing failing to say so.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_tick_arriving_during_a_cycle_starts_no_second_one()
    {
        using var fixture = Fixture.Create(featureOn: true, paired: true, gated: true);

        var cycle = fixture.NextCycle();
        fixture.Clock.Advance(TaskSyncWorker.FirstCycleDelay);
        await fixture.Gate.Entered.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);

        // The second tick lands while the first cycle is still inside the
        // repository read it is being held at.
        fixture.Clock.Advance(TaskSyncWorker.CyclePeriod);

        fixture.Gate.Release();
        await cycle.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);

        Assert.Equal(1, fixture.SessionsResolved);
    }

    /// <summary>The same guard from the other side: the button, pressed while the
    /// loop is already exchanging. Pressing it twice is one exchange.</summary>
    [Fact]
    public async Task A_requested_sync_during_a_cycle_starts_no_second_one()
    {
        using var fixture = Fixture.Create(featureOn: true, paired: true, gated: true);

        var cycle = fixture.NextCycle();
        fixture.Clock.Advance(TaskSyncWorker.FirstCycleDelay);
        await fixture.Gate.Entered.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);

        // Awaited rather than fired and forgotten, because what is asserted is
        // that this request declined to start anything - and the moment it
        // declined is the moment its dispatch completes. A fire-and-forget press
        // would leave the assertion racing the thread pool.
        await fixture.Worker.RequestSyncAsync().WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);

        Assert.Equal(1, fixture.SessionsResolved);

        fixture.Gate.Release();
        await cycle.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);

        Assert.Equal(1, fixture.SessionsResolved);
    }

    /// <summary>
    /// The button hands the exchange to the thread pool and comes straight back.
    /// <para>
    /// Asserted by pressing it while the cycle it starts is held open: a
    /// <c>RequestSync</c> that awaited the network could not return until the
    /// gate is released, and the gate is not released until after it has
    /// returned. The wait is a bound rather than a measurement - it turns what
    /// would otherwise be a hang into a failure.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Requesting_a_sync_returns_without_waiting_for_the_network()
    {
        using var fixture = Fixture.Create(featureOn: true, paired: true, gated: true);

        var cycle = fixture.NextCycle();

        var pressed = Task.Run(fixture.Worker.RequestSync, TestContext.Current.CancellationToken);
        await pressed.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);

        // And it really did start one: the cycle is sitting in the repository
        // read while the press it came from has already returned.
        await fixture.Gate.Entered.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);
        Assert.True(fixture.Worker.IsSyncing);

        fixture.Gate.Release();
        await cycle.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);

        Assert.False(fixture.Worker.IsSyncing);
    }

    /// <summary>
    /// The busy state is only readable if something re-renders while it is true,
    /// and the press that starts a cycle returns before the cycle has taken the
    /// guard - so the worker says so at both ends. Without the opening one, a
    /// button bound to <c>IsSyncing</c> would never once be drawn busy.
    /// </summary>
    [Fact]
    public async Task A_cycle_says_so_when_it_starts_as_well_as_when_it_ends()
    {
        using var fixture = Fixture.Create(featureOn: true, paired: true, gated: true);

        var wentBusy = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Worker.Changed += () =>
        {
            if (fixture.Worker.IsSyncing) wentBusy.TrySetResult();
        };

        var cycle = fixture.NextCycle();
        fixture.Clock.Advance(TaskSyncWorker.FirstCycleDelay);

        await wentBusy.Task.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);

        fixture.Gate.Release();
        await cycle.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);

        Assert.False(fixture.Worker.IsSyncing);
    }

    // --- Failure is ordinary --------------------------------------------------

    /// <summary>
    /// The offline case, which is the common one: a laptop on a train fails every
    /// cycle for an hour and has to go on trying. A loop that switched itself off
    /// after a failure would be a device that never syncs again until the app is
    /// restarted, with nothing on screen to say why.
    /// </summary>
    [Fact]
    public async Task A_failed_exchange_leaves_the_loop_running()
    {
        using var fixture = Fixture.Create(
            featureOn: true,
            paired: true,
            respond: (_, _) => StubHttpMessageHandler.Problem(
                HttpStatusCode.ServiceUnavailable,
                SyncErrorCodes.ReplicaUnavailable,
                "The replica is not reachable yet."));

        var first = fixture.NextCycle();
        fixture.Clock.Advance(TaskSyncWorker.FirstCycleDelay);
        await first.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);

        Assert.Null(fixture.Worker.LastSummary);
        Assert.Contains("not reachable", fixture.Worker.LastError, StringComparison.OrdinalIgnoreCase);

        var second = fixture.NextCycle();
        fixture.Clock.Advance(TaskSyncWorker.CyclePeriod);
        await second.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);

        Assert.Equal(2, fixture.SessionsResolved);
    }

    /// <summary>
    /// An exception is the one thing the session does not answer with a result,
    /// so it is the one thing that could escape a tick - and an exception
    /// escaping a thread pool callback ends the process. The next cycle still has
    /// to happen.
    /// </summary>
    [Fact]
    public async Task An_exception_out_of_the_session_neither_escapes_nor_stops_the_loop()
    {
        using var fixture = Fixture.Create(featureOn: true, paired: true, tasksThrow: true);

        var first = fixture.NextCycle();
        fixture.Clock.Advance(TaskSyncWorker.FirstCycleDelay);
        await first.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);

        Assert.Equal(TaskSyncWorker.UnexpectedFailure, fixture.Worker.LastError);

        var second = fixture.NextCycle();
        fixture.Clock.Advance(TaskSyncWorker.CyclePeriod);
        await second.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);

        Assert.Equal(2, fixture.SessionsResolved);
    }

    // --- Disposal -------------------------------------------------------------

    /// <summary>
    /// Three things at once, because they are one property: after the host lets
    /// go, nothing this worker owns can start work again. The timer is stopped,
    /// the exchange in flight is asked to stop, and both stores are let go of -
    /// the last being the one that would otherwise keep a disposed worker alive
    /// on a singleton's event and go on ticking through a dead provider.
    /// </summary>
    [Fact]
    public async Task Disposing_stops_the_timer_cancels_the_cycle_and_lets_go_of_both_stores()
    {
        using var fixture = Fixture.Create(featureOn: true, paired: true, gated: true);

        var cycle = fixture.NextCycle();
        fixture.Clock.Advance(TaskSyncWorker.FirstCycleDelay);
        await fixture.Gate.Entered.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);

        fixture.Worker.Dispose();

        // The exchange was handed the worker's own lifetime token, so the cycle
        // that was in flight has been asked to stop.
        Assert.True(fixture.Gate.LastToken.IsCancellationRequested);

        fixture.Gate.Release();
        await cycle.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);

        var resolvedBefore = fixture.SessionsResolved;

        // The timer is gone.
        fixture.Clock.Advance(Fixture.WellPastSeveralCycles);

        // And neither store can put it back: a disposed worker still subscribed
        // to either of these would build itself a new timer out of them.
        _ = fixture.Features.SetEnabled(SyncFeatures.TaskSync, false);
        _ = fixture.Features.SetEnabled(SyncFeatures.TaskSync, true);
        fixture.Credentials.Save(Paired);
        fixture.Clock.Advance(Fixture.WellPastSeveralCycles);

        Assert.Equal(resolvedBefore, fixture.SessionsResolved);
    }

    // --- Reset and rehydrate --------------------------------------------------

    /// <summary>
    /// The replica has to be disposable: deleted, recreated, and filled again
    /// from a device that still holds the data. Resetting the push watermark is
    /// what fills it, and the tombstone is the half that would quietly be missed
    /// - a deletion that never travels leaves the other device holding a task
    /// nobody can see here.
    /// <para>
    /// Safe to do at any time because the replica upserts whole documents under
    /// last-write-wins, so every document that goes a second time lands in the
    /// state it is already in.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Resetting_the_push_watermark_republishes_every_task_including_tombstones()
    {
        var deleted = Guid.NewGuid();

        using var fixture = Fixture.Create(
            featureOn: true,
            paired: true,
            seed: tasks =>
            {
                tasks.Seed(TaskChanges.Task("Still here", Noon));
                tasks.Seed(TaskChanges.Task("Also still here", Noon.AddMinutes(1)));
                tasks.Seed(TaskChanges.Task("Deleted here", Noon.AddMinutes(2), id: deleted, deletedAt: Noon.AddMinutes(2)));
            },
            // Everything on this machine has already been accepted, so an
            // ordinary cycle has nothing at all to send.
            initialState: new TaskSyncState(Noon.AddHours(1), "cursor-1"));

        var caughtUp = fixture.NextCycle();
        fixture.Clock.Advance(TaskSyncWorker.FirstCycleDelay);
        await caughtUp.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);

        Assert.Equal(0, fixture.Worker.LastSummary?.Pushed);
        Assert.Empty(fixture.PushedBodies);

        var republished = fixture.NextCycle();
        fixture.Worker.RepublishEverything();
        await republished.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);

        Assert.Equal(3, fixture.Worker.LastSummary?.Pushed);

        var sent = Pushed(Assert.Single(fixture.PushedBodies));

        Assert.Equal(3, sent.Count);
        Assert.Equal(Noon.AddMinutes(2), sent.Single(change => change.Id == deleted).DeletedAt);
    }

    /// <summary>
    /// The other direction, and the one a restored or emptied machine needs: the
    /// cursor and the replica agree perfectly while this device holds nothing at
    /// all, so nothing short of reading the feed from its beginning brings the
    /// tasks back.
    /// </summary>
    [Fact]
    public async Task Clearing_the_pull_cursor_rehydrates_an_empty_machine_from_the_feed()
    {
        var change = TaskChanges.Change("From the other machine", Noon);

        var page = JsonSerializer.Serialize(new
        {
            tasks = new[] { new { change, deviceId = Guid.NewGuid(), serverTimestamp = 100L } },
            since = "cursor-2",
            hasMore = false
        });

        using var fixture = Fixture.Create(
            featureOn: true,
            paired: true,
            initialState: new TaskSyncState(Noon.AddHours(1), "cursor-1"),
            // A pull carrying the stored cursor is caught up and gets nothing,
            // which is exactly the hole a rehydrate has to dig this machine out
            // of. Only a pull with no cursor on it replays the feed.
            respond: (request, _) => request.Method == HttpMethod.Post
                ? StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"accepted":0}""")
                : request.RequestUri!.Query.Contains("since=", StringComparison.Ordinal)
                    ? StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"tasks":[],"since":"cursor-1","hasMore":false}""")
                    : StubHttpMessageHandler.Json(HttpStatusCode.OK, page));

        var caughtUp = fixture.NextCycle();
        fixture.Clock.Advance(TaskSyncWorker.FirstCycleDelay);
        await caughtUp.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);

        Assert.Empty(fixture.Tasks.Tasks);

        var rehydrated = fixture.NextCycle();
        fixture.Worker.RehydrateFromScratch();
        await rehydrated.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);

        Assert.Equal(1, fixture.Worker.LastSummary?.Applied);
        Assert.Equal("From the other machine", fixture.Tasks.Tasks[change.Id].Title);
        Assert.Equal("cursor-2", fixture.State.Current.PullCursor);
    }

    /// <summary>
    /// The same reset, pressed while a cycle is already running - the case that
    /// used to lose it outright.
    /// <para>
    /// A cycle in flight read its watermark before the press and saves an
    /// advanced one when its batch is accepted, so a reset written straight to
    /// the store is overwritten by a cycle that started before it; and the
    /// request that came with the reset is turned away by the overlap guard, so
    /// nothing runs again until the next tick five minutes later. The person
    /// pressed the button, nothing republished, and nothing said so - which is
    /// the shape of failure this whole file exists to catch.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_republish_asked_for_during_a_cycle_is_still_carried_out()
    {
        using var fixture = Fixture.Create(
            featureOn: true,
            paired: true,
            gated: true,
            seed: tasks =>
            {
                // Already accepted, so an ordinary cycle leaves it alone and only
                // a reset watermark can put it back on the wire.
                tasks.Seed(TaskChanges.Task("Pushed long ago", Noon));
                tasks.Seed(TaskChanges.Task("Changed since", Noon.AddMinutes(1)));
            },
            initialState: new TaskSyncState(Noon, null));

        // Two, because the reset is carried out by the cycle after the one it
        // interrupts.
        var cycles = fixture.NextCycles(2);

        fixture.Clock.Advance(TaskSyncWorker.FirstCycleDelay);
        await fixture.Gate.Entered.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);

        // Pressed while the first cycle is held inside its selection read.
        fixture.Worker.RepublishEverything();

        fixture.Gate.Release();
        await cycles.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);

        Assert.Equal(2, fixture.PushedBodies.Count);

        // The interrupted cycle sent only what had changed since its watermark.
        Assert.Equal("Changed since", Assert.Single(Pushed(fixture.PushedBodies[0])).Task.Title);

        // The one the reset caused sent the whole machine, the long-accepted task
        // included.
        Assert.Equal(
            new[] { "Changed since", "Pushed long ago" },
            Pushed(fixture.PushedBodies[1]).Select(change => change.Task.Title).Order().ToArray());
    }

    /// <summary>
    /// The other reset, interrupted the same way. The cleared cursor would in
    /// fact survive - a pull re-reads the state when it saves it - but the cycle
    /// that acts on it would not run until the next tick, so a person would watch
    /// the button do nothing for five minutes.
    /// </summary>
    [Fact]
    public async Task A_rehydrate_asked_for_during_a_cycle_is_still_carried_out()
    {
        var cursors = new List<string?>();

        using var fixture = Fixture.Create(
            featureOn: true,
            paired: true,
            gated: true,
            initialState: new TaskSyncState(Noon.AddHours(1), "cursor-0"),
            respond: (request, _) =>
            {
                if (request.Method == HttpMethod.Post)
                {
                    return StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"accepted":0}""");
                }

                var query = request.RequestUri!.Query;
                cursors.Add(query.Contains("since=", StringComparison.Ordinal) ? query : null);

                return StubHttpMessageHandler.Json(
                    HttpStatusCode.OK,
                    """{"tasks":[],"since":"cursor-1","hasMore":false}""");
            });

        var cycles = fixture.NextCycles(2);

        fixture.Clock.Advance(TaskSyncWorker.FirstCycleDelay);
        await fixture.Gate.Entered.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);

        fixture.Worker.RehydrateFromScratch();

        fixture.Gate.Release();
        await cycles.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);

        // Two pulls: the interrupted one carrying the stored cursor, and the one
        // the reset caused carrying none at all.
        Assert.Equal(2, cursors.Count);
        Assert.NotNull(cursors[0]);
        Assert.Null(cursors[1]);
    }

    /// <summary>The changes a push actually put on the wire, read back through
    /// the contract rather than matched inside the JSON - a substring check
    /// cannot tell a tombstone from a live task, because the field that carries
    /// one is null on most changes.</summary>
    private static IReadOnlyList<TaskChange> Pushed(string body) =>
        JsonSerializer.Deserialize<PushTasksRequest>(
            body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Tasks;

    /// <summary>
    /// A worker with a real session behind it, a fake clock in front of it and a
    /// scripted service at the end of it.
    /// <para>
    /// The service provider is hand-rolled rather than a real container because
    /// what the worker does with it is itself under test: it must ask for a
    /// session once per cycle and never hold one, and counting the asks is how
    /// "one cycle ran, not two" is written at all.
    /// </para>
    /// </summary>
    private sealed class Fixture : IDisposable
    {
        /// <summary>How long a test waits for work it has already caused before
        /// calling it a hang. Generous, because it is a bound and not a
        /// measurement: nothing here passes or fails by being quick.</summary>
        public static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

        /// <summary>Far enough past the schedule that a running loop would
        /// certainly have ticked. Only ever used where the assertion is that
        /// nothing happened.</summary>
        public static readonly TimeSpan WellPastSeveralCycles = TimeSpan.FromMinutes(30);

        private readonly HttpClient _http;
        private readonly ITaskRepository _repository;
        private int _sessionsResolved;

        private Fixture(
            HttpClient http,
            StubHttpMessageHandler handler,
            ITaskRepository repository,
            InMemoryTaskStore tasks,
            InMemoryTaskSyncStateStore state,
            StubFeatureSettings features,
            InMemoryDeviceCredentialStore credentials,
            FakeTimeProvider clock,
            RepositoryGate gate,
            List<string> pushedBodies)
        {
            _http = http;
            _repository = repository;

            Handler = handler;
            Tasks = tasks;
            State = state;
            Features = features;
            Credentials = credentials;
            Clock = clock;
            Gate = gate;
            PushedBodies = pushedBodies;

            Worker = new TaskSyncWorker(
                new SessionProvider(BuildSession),
                features,
                credentials,
                state,
                clock);
        }

        public TaskSyncWorker Worker { get; }

        public StubHttpMessageHandler Handler { get; }

        /// <summary>The machine's own tasks, whether or not the session reaches
        /// them through a gate.</summary>
        public InMemoryTaskStore Tasks { get; }

        public InMemoryTaskSyncStateStore State { get; }

        public StubFeatureSettings Features { get; }

        public InMemoryDeviceCredentialStore Credentials { get; }

        public FakeTimeProvider Clock { get; }

        public RepositoryGate Gate { get; }

        /// <summary>Every push body, in order. A push with nothing to send makes
        /// no request at all, so this is also the count of cycles that had
        /// something to say.</summary>
        public List<string> PushedBodies { get; }

        public int SessionsResolved => Volatile.Read(ref _sessionsResolved);

        public static Fixture Create(
            bool featureOn,
            bool paired,
            bool gated = false,
            bool tasksThrow = false,
            Action<InMemoryTaskStore>? seed = null,
            TaskSyncState? initialState = null,
            Func<HttpRequestMessage, int, HttpResponseMessage>? respond = null)
        {
            var tasks = new InMemoryTaskStore();
            seed?.Invoke(tasks);

            var pushedBodies = new List<string>();
            var gate = new RepositoryGate();

            var handler = new StubHttpMessageHandler((request, index) =>
            {
                var body = request.Method == HttpMethod.Post && request.Content is not null
                    ? request.Content.ReadAsStringAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult()
                    : null;

                if (body is not null) pushedBodies.Add(body);

                if (respond is not null) return respond(request, index);

                // A replica that accepts everything it is sent and hands back an
                // empty feed. Accepting the whole batch rather than a fixed number
                // is what lets a test assert the reported count against what it
                // seeded instead of against the script.
                return body is not null
                    ? StubHttpMessageHandler.Json(
                        HttpStatusCode.OK,
                        $$"""{"accepted":{{Pushed(body).Count}}}""")
                    : StubHttpMessageHandler.Json(
                        HttpStatusCode.OK,
                        """{"tasks":[],"since":"cursor-1","hasMore":false}""");
            });

            var http = new HttpClient(handler) { BaseAddress = new Uri("https://sync.test") };

            // The repository the session reads through: the plain store, one that
            // can be held open so a cycle stays in flight, or one that throws the
            // way a local database this build cannot read would.
            var repository = tasksThrow
                ? new ThrowingTaskStore()
                : gated
                    ? gate.Around(tasks)
                    : (ITaskRepository)tasks;

            return new Fixture(
                http,
                handler,
                repository,
                tasks,
                new InMemoryTaskSyncStateStore(initialState),
                new StubFeatureSettings(featureOn),
                paired ? new InMemoryDeviceCredentialStore(Paired) : new InMemoryDeviceCredentialStore(),
                new FakeTimeProvider(Noon),
                gate,
                pushedBodies);
        }

        /// <summary>
        /// A signal that completes when the worker next finishes a cycle. Taken
        /// before the tick that causes it, because the cycle can be over before
        /// the line after the tick runs.
        /// <para>
        /// <c>Changed</c> is raised at both ends of a cycle - once so a button can
        /// go busy, once when there is something to read - so this waits for the
        /// raise that finds the worker idle. Counting to two instead would break
        /// on the tests where a cycle raises nothing at all because it declined
        /// to start.
        /// </para>
        /// </summary>
        public Task NextCycle() => NextCycles(1);

        /// <summary>
        /// The same signal, for <paramref name="count"/> cycles rather than one.
        /// <para>
        /// The reset tests need it: a reset asked for while a cycle is in flight
        /// is carried out by the cycle after it, so the one being asserted is the
        /// second. Registering a second <see cref="NextCycle"/> would not do -
        /// both handlers are subscribed by then, so the closing raise of the
        /// cycle already running completes them both.
        /// </para>
        /// </summary>
        public Task NextCycles(int count)
        {
            var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var remaining = count;

            void OnChanged()
            {
                if (Worker.IsSyncing) return;
                if (Interlocked.Decrement(ref remaining) > 0) return;

                Worker.Changed -= OnChanged;
                finished.TrySetResult();
            }

            Worker.Changed += OnChanged;

            return finished.Task;
        }

        public void Dispose()
        {
            Worker.Dispose();
            _http.Dispose();
        }

        private TaskSyncSession BuildSession()
        {
            Interlocked.Increment(ref _sessionsResolved);

            return new TaskSyncSession(
                new TaskSyncClient(_http),
                new TaskReplicaMerge(_repository),
                _repository,
                State,
                Clock);
        }
    }

    /// <summary>The one service the worker is allowed to ask for, and a count of
    /// how often it asked. Anything else answers null, which is what a container
    /// does for a service nobody registered.</summary>
    private sealed class SessionProvider(Func<TaskSyncSession> session) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(TaskSyncSession) ? session() : null;
    }

    /// <summary>
    /// A repository read that can be held open, so a test can have a cycle in
    /// flight at a moment of its choosing.
    /// <para>
    /// The gate is on the repository rather than on the wire because the token
    /// the worker hands the exchange arrives here first: the push's selection is
    /// the first thing a cycle does with it, so this is where "the cycle was
    /// asked to stop" can be read off.
    /// </para>
    /// <para>
    /// It holds the first read only. Every test using it wants one cycle stuck
    /// and the ones after it ordinary.
    /// </para>
    /// </summary>
    private sealed class RepositoryGate
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _held;

        public Task Entered => _entered.Task;

        /// <summary>The token the exchange was given, captured on the way past.</summary>
        public CancellationToken LastToken { get; private set; }

        public void Release() => _release.TrySetResult();

        public ITaskRepository Around(InMemoryTaskStore inner) => new GatedTaskStore(this, inner);

        private async Task HoldAsync(CancellationToken cancellationToken)
        {
            LastToken = cancellationToken;

            if (Interlocked.CompareExchange(ref _held, 1, 0) != 0) return;

            _entered.TrySetResult();

            // Awaited rather than blocked on: a gate that blocked its calling
            // thread would never let the tick that opened it return, and the
            // whole point is that the tick has already gone.
            await _release.Task.ConfigureAwait(false);
        }

        private sealed class GatedTaskStore(RepositoryGate gate, InMemoryTaskStore inner) : ITaskRepository
        {
            public Task SaveAsync(TaskItem task, CancellationToken cancellationToken = default) =>
                inner.SaveAsync(task, cancellationToken);

            public Task<TaskItem?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
                inner.GetAsync(id, cancellationToken);

            public Task<TaskItem?> GetIncludingDeletedAsync(Guid id, CancellationToken cancellationToken = default) =>
                inner.GetIncludingDeletedAsync(id, cancellationToken);

            public Task<IReadOnlyList<TaskItem>> ListAsync(CancellationToken cancellationToken = default) =>
                inner.ListAsync(cancellationToken);

            public async Task<IReadOnlyList<TaskItem>> ListChangedSinceAsync(
                DateTimeOffset since,
                CancellationToken cancellationToken = default)
            {
                await gate.HoldAsync(cancellationToken).ConfigureAwait(false);

                return await inner.ListChangedSinceAsync(since, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>A local task database this build cannot read. The session answers
    /// everything it planned for with a result, so a throw is the one shape that
    /// could escape a tick.</summary>
    private sealed class ThrowingTaskStore : ITaskRepository
    {
        private const string Unreadable = "The task database could not be read.";

        public Task SaveAsync(TaskItem task, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(Unreadable);

        public Task<TaskItem?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(Unreadable);

        public Task<TaskItem?> GetIncludingDeletedAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(Unreadable);

        public Task<IReadOnlyList<TaskItem>> ListAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(Unreadable);

        public Task<IReadOnlyList<TaskItem>> ListChangedSinceAsync(
            DateTimeOffset since,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(Unreadable);
    }

}
