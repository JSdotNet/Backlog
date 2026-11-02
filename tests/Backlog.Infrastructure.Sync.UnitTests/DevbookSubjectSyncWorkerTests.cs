using System.Net;

using Backlog.Infrastructure.Sync.Annotations;
using Backlog.Infrastructure.Sync.DevbookSubjects;
using Backlog.Infrastructure.Sync.Notifications;
using Backlog.Infrastructure.Sync.Sessions;
using Backlog.Modules.Sync.Abstractions;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// The subject loop's gates and schedule — the questions
/// <see cref="AnnotationSyncWorkerTests"/> asks of the loop this one copies — and
/// what it shows when the service is older than the client: nothing wrong.
/// </summary>
public sealed class DevbookSubjectSyncWorkerTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    private static readonly DeviceCredential Paired = new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        "Workshop PC",
        "a-registration-credential");

    [Fact]
    public void No_request_is_made_while_the_sync_feature_is_off()
    {
        using var fixture = Fixture.Create(featureOn: false, paired: true);

        fixture.Clock.Advance(Fixture.WellPastSeveralCycles);

        Assert.Equal(0, fixture.SessionsResolved);
        Assert.Empty(fixture.Handler.Requests);
    }

    [Fact]
    public async Task No_cycle_runs_until_a_device_credential_is_stored()
    {
        using var fixture = Fixture.Create(featureOn: true, paired: false);

        fixture.Clock.Advance(Fixture.WellPastSeveralCycles);
        Assert.Equal(0, fixture.SessionsResolved);

        var first = fixture.NextCycle();
        fixture.Credentials.Save(Paired);
        fixture.Clock.Advance(DevbookSubjectSyncWorker.FirstCycleDelay);
        await first.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);

        Assert.Equal(1, fixture.SessionsResolved);
        Assert.NotNull(fixture.Worker.LastSummary);
        Assert.Null(fixture.Worker.LastError);
    }

    [Fact]
    public async Task A_failed_cycle_is_shown_and_does_not_stop_the_loop()
    {
        using var fixture = Fixture.Create(
            featureOn: true,
            paired: true,
            respond: (_, _) => StubHttpMessageHandler.Problem(HttpStatusCode.ServiceUnavailable, SyncErrorCodes.ReplicaUnavailable, "The replica is not reachable yet."));

        var first = fixture.NextCycle();
        fixture.Clock.Advance(DevbookSubjectSyncWorker.FirstCycleDelay);
        await first.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);

        Assert.Contains("not reachable", fixture.Worker.LastError, StringComparison.OrdinalIgnoreCase);

        var second = fixture.NextCycle();
        fixture.Clock.Advance(DevbookSubjectSyncWorker.CyclePeriod);
        await second.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);

        Assert.Equal(2, fixture.SessionsResolved);
    }

    /// <summary>A service that predates subject state answers 404 every cycle
    /// until somebody deploys it. That is no error to show, cycle after cycle:
    /// the loop records an exchange that found nothing to do and keeps
    /// asking.</summary>
    [Fact]
    public async Task A_service_without_the_route_shows_no_error_and_the_loop_keeps_asking()
    {
        using var fixture = Fixture.Create(featureOn: true, paired: true, respond: (_, _) => new HttpResponseMessage(HttpStatusCode.NotFound));

        var first = fixture.NextCycle();
        fixture.Clock.Advance(DevbookSubjectSyncWorker.FirstCycleDelay);
        await first.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);

        Assert.Null(fixture.Worker.LastError);
        Assert.True(fixture.Worker.LastSummary?.NotYetServed);

        var second = fixture.NextCycle();
        fixture.Clock.Advance(DevbookSubjectSyncWorker.CyclePeriod);
        await second.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);

        Assert.Equal(2, fixture.SessionsResolved);
        Assert.Null(fixture.Worker.LastError);
    }

    [Fact]
    public async Task A_host_that_composed_no_session_is_not_an_error()
    {
        using var fixture = Fixture.Create(featureOn: true, paired: true, compose: false);

        var first = fixture.NextCycle();
        fixture.Clock.Advance(DevbookSubjectSyncWorker.FirstCycleDelay);
        await first.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);

        Assert.Null(fixture.Worker.LastError);
        Assert.Empty(fixture.Handler.Requests);
    }

    /// <summary>Five loops start when the app does; none fires its first cycle
    /// into the same moment as another.</summary>
    [Fact]
    public void The_loops_do_not_fire_their_first_cycle_together()
    {
        TimeSpan[] others =
        [
            TaskSyncWorker.FirstCycleDelay,
            SessionSyncWorker.FirstCycleDelay,
            AnnotationSyncWorker.FirstCycleDelay,
            NotificationSyncWorker.FirstCycleDelay,
        ];

        Assert.DoesNotContain(DevbookSubjectSyncWorker.FirstCycleDelay, others);
    }

    private sealed class Fixture : IDisposable
    {
        public static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

        public static readonly TimeSpan WellPastSeveralCycles = TimeSpan.FromMinutes(30);

        private readonly HttpClient _http;
        private readonly bool _compose;
        private int _sessionsResolved;

        private Fixture(
            HttpClient http,
            StubHttpMessageHandler handler,
            StubFeatureSettings features,
            InMemoryDeviceCredentialStore credentials,
            FakeTimeProvider clock,
            bool compose)
        {
            _http = http;
            _compose = compose;

            Handler = handler;
            Features = features;
            Credentials = credentials;
            Clock = clock;

            Worker = new DevbookSubjectSyncWorker(
                new SessionProvider(BuildSession),
                features,
                credentials,
                State,
                clock);
        }

        public DevbookSubjectSyncWorker Worker { get; }

        public StubHttpMessageHandler Handler { get; }

        public InMemoryDevbookSubjectSyncStateStore State { get; } = new();

        public StubFeatureSettings Features { get; }

        public InMemoryDeviceCredentialStore Credentials { get; }

        public FakeTimeProvider Clock { get; }

        public int SessionsResolved => Volatile.Read(ref _sessionsResolved);

        public static HttpResponseMessage Ordinary(HttpRequestMessage request) =>
            request.Method == HttpMethod.Post
                ? StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"accepted":0}""")
                : StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"subjects":[],"since":"cursor-1","hasMore":false}""");

        public static Fixture Create(
            bool featureOn,
            bool paired,
            bool compose = true,
            Func<HttpRequestMessage, int, HttpResponseMessage>? respond = null)
        {
            var handler = new StubHttpMessageHandler((request, index) => respond is not null ? respond(request, index) : Ordinary(request));

            return new Fixture(
                new HttpClient(handler) { BaseAddress = new Uri("https://sync.test") },
                handler,
                new StubFeatureSettings(featureOn),
                paired ? new InMemoryDeviceCredentialStore(Paired) : new InMemoryDeviceCredentialStore(),
                new FakeTimeProvider(Noon),
                compose);
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

        private DevbookSubjectSyncSession? BuildSession()
        {
            Interlocked.Increment(ref _sessionsResolved);

            if (!_compose) return null;

            var store = new InMemoryDevbookSubjectStateStore();

            return new DevbookSubjectSyncSession(
                new DevbookSubjectSyncClient(_http),
                new DevbookSubjectReplicaMerge(store),
                store,
                State,
                Credentials,
                Clock);
        }
    }

    private sealed class SessionProvider(Func<DevbookSubjectSyncSession?> session) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(DevbookSubjectSyncSession) ? session() : null;
    }
}
