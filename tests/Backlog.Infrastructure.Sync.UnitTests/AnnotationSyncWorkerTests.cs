using System.Net;

using Backlog.Infrastructure.Sync.Annotations;
using Backlog.Infrastructure.Sync.Sessions;
using Backlog.Modules.Sync.Abstractions;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// The third loop's gates and schedule — the questions
/// <see cref="SessionSyncWorkerTests"/> asks of the second, because the
/// mechanism is a copy and a copy that drifted would drift silently.
/// </summary>
public sealed class AnnotationSyncWorkerTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

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
    public async Task Switching_the_feature_on_starts_the_loop_where_it_stands()
    {
        using var fixture = Fixture.Create(featureOn: false, paired: true);

        var first = fixture.NextCycle();
        _ = fixture.Features.SetEnabled(SyncFeatures.Sync, true);
        fixture.Clock.Advance(AnnotationSyncWorker.FirstCycleDelay);
        await first.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);

        Assert.Equal(1, fixture.SessionsResolved);
        Assert.NotNull(fixture.Worker.LastSummary);
        Assert.Null(fixture.Worker.LastError);
    }

    [Fact]
    public async Task No_cycle_runs_until_a_device_credential_is_stored()
    {
        using var fixture = Fixture.Create(featureOn: true, paired: false);

        fixture.Clock.Advance(Fixture.WellPastSeveralCycles);

        Assert.Equal(0, fixture.SessionsResolved);

        var first = fixture.NextCycle();
        fixture.Credentials.Save(Paired);
        fixture.Clock.Advance(AnnotationSyncWorker.FirstCycleDelay);
        await first.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);

        Assert.Equal(1, fixture.SessionsResolved);
    }

    [Fact]
    public async Task A_failed_cycle_does_not_stop_the_loop()
    {
        using var fixture = Fixture.Create(
            featureOn: true,
            paired: true,
            respond: (_, _) => throw new HttpRequestException("No route to host."));

        var first = fixture.NextCycle();
        fixture.Clock.Advance(AnnotationSyncWorker.FirstCycleDelay);
        await first.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);

        Assert.NotNull(fixture.Worker.LastError);

        var second = fixture.NextCycle();
        fixture.Clock.Advance(AnnotationSyncWorker.CyclePeriod);
        await second.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);

        Assert.Equal(2, fixture.SessionsResolved);
    }

    [Fact]
    public async Task A_host_that_composed_no_session_is_not_an_error()
    {
        using var fixture = Fixture.Create(featureOn: true, paired: true, compose: false);

        var first = fixture.NextCycle();
        fixture.Clock.Advance(AnnotationSyncWorker.FirstCycleDelay);
        await first.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);

        Assert.Equal(1, fixture.SessionsResolved);
        Assert.Null(fixture.Worker.LastError);
        Assert.Empty(fixture.Handler.Requests);
    }

    /// <summary>Three loops start when the app does; none of them fires its
    /// first cycle into the same moment as another.</summary>
    [Fact]
    public void The_three_loops_do_not_fire_their_first_cycle_together()
    {
        Assert.NotEqual(TaskSyncWorker.FirstCycleDelay, AnnotationSyncWorker.FirstCycleDelay);
        Assert.NotEqual(SessionSyncWorker.FirstCycleDelay, AnnotationSyncWorker.FirstCycleDelay);
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
            InMemoryAnnotationSyncStateStore state,
            StubFeatureSettings features,
            InMemoryDeviceCredentialStore credentials,
            FakeTimeProvider clock,
            bool compose)
        {
            _http = http;
            _compose = compose;

            Handler = handler;
            State = state;
            Features = features;
            Credentials = credentials;
            Clock = clock;

            Worker = new AnnotationSyncWorker(
                new SessionProvider(BuildSession),
                features,
                credentials,
                state,
                clock);
        }

        public AnnotationSyncWorker Worker { get; }

        public StubHttpMessageHandler Handler { get; }

        public InMemoryAnnotationSyncStateStore State { get; }

        public StubFeatureSettings Features { get; }

        public InMemoryDeviceCredentialStore Credentials { get; }

        public FakeTimeProvider Clock { get; }

        public int SessionsResolved => Volatile.Read(ref _sessionsResolved);

        public static Fixture Create(
            bool featureOn,
            bool paired,
            bool compose = true,
            Func<HttpRequestMessage, int, HttpResponseMessage>? respond = null)
        {
            var handler = new StubHttpMessageHandler((request, index) =>
            {
                if (respond is not null) return respond(request, index);

                return request.Method == HttpMethod.Post
                    ? StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"accepted":0}""")
                    : StubHttpMessageHandler.Json(
                        HttpStatusCode.OK,
                        """{"annotations":[],"since":"cursor-1","hasMore":false}""");
            });

            var http = new HttpClient(handler) { BaseAddress = new Uri("https://sync.test") };

            return new Fixture(
                http,
                handler,
                new InMemoryAnnotationSyncStateStore(),
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

        private AnnotationSyncSession? BuildSession()
        {
            Interlocked.Increment(ref _sessionsResolved);

            if (!_compose) return null;

            var store = new InMemoryDevbookAnnotationStore();

            return new AnnotationSyncSession(
                new AnnotationSyncClient(_http),
                new AnnotationReplicaMerge(store),
                store,
                State,
                Clock);
        }
    }

    private sealed class SessionProvider(Func<AnnotationSyncSession?> session) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(AnnotationSyncSession) ? session() : null;
    }
}
