using System.Net;

using Backlog.Infrastructure.Sync.Annotations;
using Backlog.Infrastructure.Sync.DevbookSubjects;
using Backlog.Infrastructure.Sync.KnowledgeEvents;
using Backlog.Infrastructure.Sync.Notifications;
using Backlog.Infrastructure.Sync.Sessions;
using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// The knowledge event loop's three gates — the <c>sync</c> switch, the <c>events</c> area and
/// a paired device (local ADR 0050 §6) — its schedule, and what it shows when the service is
/// older than the client: nothing wrong.
/// </summary>
public sealed class KnowledgeEventSyncWorkerTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    private static readonly DeviceCredential Paired = new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        "Workshop PC",
        "a-registration-credential");

    [Fact]
    public void No_request_is_made_while_the_sync_feature_is_off()
    {
        using var fixture = Fixture.Create(syncOn: false, eventsOn: true, paired: true);

        fixture.Clock.Advance(Fixture.WellPastSeveralCycles);

        Assert.Equal(0, fixture.SessionsResolved);
        Assert.Empty(fixture.Handler.Requests);
    }

    [Fact]
    public async Task No_request_is_made_while_the_events_area_is_off_and_turning_it_on_starts_the_loop()
    {
        using var fixture = Fixture.Create(syncOn: true, eventsOn: false, paired: true);

        fixture.Clock.Advance(Fixture.WellPastSeveralCycles);
        Assert.Equal(0, fixture.SessionsResolved);
        Assert.Empty(fixture.Handler.Requests);

        var first = fixture.NextCycle();
        fixture.Features.SetEnabled(EventsFeatures.Events, true);
        fixture.Clock.Advance(KnowledgeEventSyncWorker.FirstCycleDelay);
        await first.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);

        Assert.Equal(1, fixture.SessionsResolved);
        Assert.Null(fixture.Worker.LastError);
    }

    [Fact]
    public async Task No_cycle_runs_until_a_device_credential_is_stored()
    {
        using var fixture = Fixture.Create(syncOn: true, eventsOn: true, paired: false);

        fixture.Clock.Advance(Fixture.WellPastSeveralCycles);
        Assert.Equal(0, fixture.SessionsResolved);

        var first = fixture.NextCycle();
        fixture.Credentials.Save(Paired);
        fixture.Clock.Advance(KnowledgeEventSyncWorker.FirstCycleDelay);
        await first.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);

        Assert.Equal(1, fixture.SessionsResolved);
        Assert.NotNull(fixture.Worker.LastSummary);
        Assert.Null(fixture.Worker.LastError);
    }

    [Fact]
    public async Task A_failed_cycle_is_shown_and_does_not_stop_the_loop()
    {
        using var fixture = Fixture.Create(
            syncOn: true,
            eventsOn: true,
            paired: true,
            respond: (_, _) => StubHttpMessageHandler.Problem(HttpStatusCode.ServiceUnavailable, SyncErrorCodes.ReplicaUnavailable, "The replica is not reachable yet."));

        var first = fixture.NextCycle();
        fixture.Clock.Advance(KnowledgeEventSyncWorker.FirstCycleDelay);
        await first.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);

        Assert.Contains("not reachable", fixture.Worker.LastError, StringComparison.OrdinalIgnoreCase);

        var second = fixture.NextCycle();
        fixture.Clock.Advance(KnowledgeEventSyncWorker.CyclePeriod);
        await second.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);

        Assert.Equal(2, fixture.SessionsResolved);
    }

    [Fact]
    public async Task A_service_without_the_route_shows_no_error_and_the_loop_keeps_asking()
    {
        using var fixture = Fixture.Create(syncOn: true, eventsOn: true, paired: true, respond: (_, _) => new HttpResponseMessage(HttpStatusCode.NotFound));

        var first = fixture.NextCycle();
        fixture.Clock.Advance(KnowledgeEventSyncWorker.FirstCycleDelay);
        await first.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);

        Assert.Null(fixture.Worker.LastError);
        Assert.True(fixture.Worker.LastSummary?.NotYetServed);

        var second = fixture.NextCycle();
        fixture.Clock.Advance(KnowledgeEventSyncWorker.CyclePeriod);
        await second.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);

        Assert.Equal(2, fixture.SessionsResolved);
        Assert.Null(fixture.Worker.LastError);
    }

    [Fact]
    public async Task A_host_that_composed_no_session_is_not_an_error()
    {
        using var fixture = Fixture.Create(syncOn: true, eventsOn: true, paired: true, compose: false);

        var first = fixture.NextCycle();
        fixture.Clock.Advance(KnowledgeEventSyncWorker.FirstCycleDelay);
        await first.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);

        Assert.Null(fixture.Worker.LastError);
        Assert.Empty(fixture.Handler.Requests);
    }

    /// <summary>Six loops start when the app does; none fires its first cycle into the same
    /// moment as another.</summary>
    [Fact]
    public void The_loops_do_not_fire_their_first_cycle_together()
    {
        TimeSpan[] others =
        [
            TaskSyncWorker.FirstCycleDelay,
            SessionSyncWorker.FirstCycleDelay,
            AnnotationSyncWorker.FirstCycleDelay,
            NotificationSyncWorker.FirstCycleDelay,
            DevbookSubjectSyncWorker.FirstCycleDelay,
        ];

        Assert.DoesNotContain(KnowledgeEventSyncWorker.FirstCycleDelay, others);
    }

    /// <summary>The withdrawal (<c>.devbook/domain/events/requirements.md#requirement-no-events-screen-while-the-area-is-off</c>):
    /// a desktop that turns the area off sends that switch alone, at once, and then nothing more
    /// while the area stays off.</summary>
    [Fact]
    public async Task Turning_the_area_off_sends_the_withdrawal_alone_and_then_stops()
    {
        using var fixture = Fixture.Create(syncOn: true, eventsOn: true, paired: true);

        var first = fixture.NextCycle();
        fixture.Clock.Advance(KnowledgeEventSyncWorker.FirstCycleDelay);
        await first.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(fixture.Handler.Requests, request => request.Method == HttpMethod.Post);

        var withdrawal = fixture.NextCycle();
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        fixture.Features.SetEnabled(EventsFeatures.Events, false);
        await withdrawal.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);

        var push = Assert.Single(fixture.Handler.Requests, request => request.Method == HttpMethod.Post);
        var change = Assert.Single(Fixture.Pushed(fixture.Bodies[^1]));
        Assert.Equal((KnowledgeEventKinds.EventsArea, Noon + KnowledgeEventSyncWorker.FirstCycleDelay + TimeSpan.FromMinutes(1)), (change.Kind, change.UpdatedAt));
        Assert.False(KnowledgeEventDocuments.ReadArea(change)!.On);
        Assert.Equal(new EventsAreaSwitch(false, change.UpdatedAt, Sent: true), fixture.State.Current.Area);
        Assert.Null(fixture.Worker.LastError);

        var requests = fixture.Handler.Requests.Count;
        fixture.Clock.Advance(Fixture.WellPastSeveralCycles);
        Assert.Equal(requests, fixture.Handler.Requests.Count);
    }

    [Fact]
    public async Task An_area_turned_off_while_sync_is_off_is_withdrawn_once_sync_comes_on()
    {
        using var fixture = Fixture.Create(syncOn: false, eventsOn: true, paired: true);

        fixture.Features.SetEnabled(EventsFeatures.Events, false);
        fixture.Clock.Advance(Fixture.WellPastSeveralCycles);
        Assert.Empty(fixture.Handler.Requests);

        var withdrawal = fixture.NextCycle();
        fixture.Features.SetEnabled(SyncFeatures.Sync, true);
        await withdrawal.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);

        var change = Assert.Single(Fixture.Pushed(Assert.Single(fixture.Bodies)));
        Assert.False(KnowledgeEventDocuments.ReadArea(change)!.On);
        Assert.True(fixture.State.Current.Area!.Sent);
    }

    /// <summary>A deployed service from before the switch refuses its kind: nothing shows wrong,
    /// and the switch stays unsent for the next cycle.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_withdrawal_the_service_cannot_take_yet_shows_nothing_wrong_and_stays_unsent(bool refusedKind)
    {
        using var fixture = Fixture.Create(
            syncOn: true,
            eventsOn: true,
            paired: true,
            respond: (request, _) => request.Method != HttpMethod.Post
                ? Fixture.Ordinary(request)
                : refusedKind
                    ? StubHttpMessageHandler.Problem(HttpStatusCode.BadRequest, SyncErrorCodes.KnowledgeEventInvalid, "Refused.")
                    : new HttpResponseMessage(HttpStatusCode.NotFound));

        var withdrawal = fixture.NextCycle();
        fixture.Features.SetEnabled(EventsFeatures.Events, false);
        await withdrawal.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);

        Assert.Null(fixture.Worker.LastError);
        Assert.True(fixture.Worker.LastSummary?.NotYetServed);
        Assert.False(fixture.State.Current.Area!.Sent);

        var retry = fixture.NextCycle();
        fixture.Clock.Advance(KnowledgeEventSyncWorker.CyclePeriod);
        await retry.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);

        Assert.Equal(2, fixture.Handler.Requests.Count(request => request.Method == HttpMethod.Post));
    }

    [Fact]
    public async Task Turning_the_area_on_sends_the_switch_with_the_first_exchange()
    {
        using var fixture = Fixture.Create(syncOn: true, eventsOn: false, paired: true);

        var first = fixture.NextCycle();
        fixture.Features.SetEnabled(EventsFeatures.Events, true);
        fixture.Clock.Advance(KnowledgeEventSyncWorker.FirstCycleDelay);
        await first.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);

        var change = Assert.Single(Fixture.Pushed(Assert.Single(fixture.Bodies)));
        Assert.True(KnowledgeEventDocuments.ReadArea(change)!.On);
        Assert.Equal(Noon, change.UpdatedAt);
        Assert.True(fixture.Worker.LastSummary?.AreaSent);
    }

    /// <summary>A desktop that upgrades sees its switch for the first time and sends nothing: a
    /// first sight is no move, so it never overrides a switch another desktop moved.</summary>
    [Fact]
    public async Task The_first_sight_of_the_switch_sends_nothing()
    {
        using var on = Fixture.Create(syncOn: true, eventsOn: true, paired: true);

        var first = on.NextCycle();
        on.Clock.Advance(KnowledgeEventSyncWorker.FirstCycleDelay);
        await first.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);

        Assert.Empty(on.Bodies);
        Assert.Equal(new EventsAreaSwitch(true, Noon, Sent: true), on.State.Current.Area);

        using var off = Fixture.Create(syncOn: true, eventsOn: false, paired: true);
        off.Clock.Advance(Fixture.WellPastSeveralCycles);

        Assert.Empty(off.Handler.Requests);
        Assert.Equal(new EventsAreaSwitch(false, Noon, Sent: true), off.State.Current.Area);
    }

    private sealed class Fixture : IDisposable
    {
        public static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

        private static readonly System.Text.Json.JsonSerializerOptions Web = new(System.Text.Json.JsonSerializerDefaults.Web);

        public static IReadOnlyList<KnowledgeEventChange> Pushed(string body) =>
            System.Text.Json.JsonSerializer.Deserialize<PushKnowledgeEventsRequest>(body, Web)!.Events;

        /// <summary>Every request body the stub was sent, in order.</summary>
        public List<string> Bodies { get; }

        public static readonly TimeSpan WellPastSeveralCycles = TimeSpan.FromMinutes(30);

        private readonly HttpClient _http;
        private readonly bool _compose;
        private int _sessionsResolved;

        private Fixture(
            HttpClient http,
            StubHttpMessageHandler handler,
            KeyedFeatureSettings features,
            InMemoryDeviceCredentialStore credentials,
            FakeTimeProvider clock,
            bool compose,
            List<string> bodies)
        {
            _http = http;
            Bodies = bodies;
            _compose = compose;

            Handler = handler;
            Features = features;
            Credentials = credentials;
            Clock = clock;

            Worker = new KnowledgeEventSyncWorker(
                new SessionProvider(BuildSession),
                features,
                credentials,
                State,
                clock);
        }

        public KnowledgeEventSyncWorker Worker { get; }

        public StubHttpMessageHandler Handler { get; }

        public InMemoryKnowledgeEventSyncStateStore State { get; } = new();

        public KeyedFeatureSettings Features { get; }

        public InMemoryDeviceCredentialStore Credentials { get; }

        public FakeTimeProvider Clock { get; }

        public int SessionsResolved => Volatile.Read(ref _sessionsResolved);

        public static HttpResponseMessage Ordinary(HttpRequestMessage request) =>
            request.Method == HttpMethod.Post
                ? StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"accepted":0}""")
                : StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"events":[],"since":"cursor-1","hasMore":false}""");

        public static Fixture Create(
            bool syncOn,
            bool eventsOn,
            bool paired,
            bool compose = true,
            Func<HttpRequestMessage, int, HttpResponseMessage>? respond = null)
        {
            var bodies = new List<string>();
            var handler = new StubHttpMessageHandler((request, index) =>
            {
                if (request.Content is not null)
                {
                    lock (bodies) bodies.Add(request.Content.ReadAsStringAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult());
                }

                return respond is not null ? respond(request, index) : Ordinary(request);
            });

            return new Fixture(
                new HttpClient(handler) { BaseAddress = new Uri("https://sync.test") },
                handler,
                new KeyedFeatureSettings(syncOn, eventsOn),
                paired ? new InMemoryDeviceCredentialStore(Paired) : new InMemoryDeviceCredentialStore(),
                new FakeTimeProvider(Noon),
                compose,
                bodies);
        }

        public Task NextCycle()
        {
            var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            void OnChanged()
            {
                if (Worker.IsSyncing) return;

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

        private KnowledgeEventSyncSession? BuildSession()
        {
            Interlocked.Increment(ref _sessionsResolved);

            if (!_compose) return null;

            var replication = new InMemoryKnowledgeEventReplication();

            return new KnowledgeEventSyncSession(
                new KnowledgeEventSyncClient(_http),
                new KnowledgeEventReplicaMerge(replication),
                replication,
                State,
                Credentials,
                Clock);
        }
    }

    private sealed class SessionProvider(Func<KnowledgeEventSyncSession?> session) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(KnowledgeEventSyncSession) ? session() : null;
    }
}
