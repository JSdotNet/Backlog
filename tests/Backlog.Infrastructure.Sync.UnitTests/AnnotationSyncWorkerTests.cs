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

    // --- A token refused mid-cycle --------------------------------------------

    /// <summary>A token refused while fresh - the service restarted with a new
    /// signing key - costs one more cycle at once, not a failure on
    /// screen.</summary>
    [Fact]
    public async Task A_cycle_whose_token_was_refused_runs_once_more_at_once()
    {
        using var fixture = Fixture.Create(
            featureOn: true,
            paired: true,
            authenticated: true,
            respond: (request, index) => index == 0
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : Fixture.Ordinary(request));

        var first = fixture.NextCycle();
        fixture.Clock.Advance(AnnotationSyncWorker.FirstCycleDelay);
        await first.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);

        Assert.Null(fixture.Worker.LastError);
        Assert.NotNull(fixture.Worker.LastSummary);
        Assert.Equal(2, fixture.Handler.Requests.Count);
        Assert.Equal(2, fixture.TokenRequests);
    }

    /// <summary>Inherited ADR 0015: a second refusal is shown, not asked
    /// again.</summary>
    [Fact]
    public async Task A_cycle_refused_twice_shows_the_failure_and_runs_no_third_time()
    {
        using var fixture = Fixture.Create(
            featureOn: true,
            paired: true,
            authenticated: true,
            respond: (_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var first = fixture.NextCycle();
        fixture.Clock.Advance(AnnotationSyncWorker.FirstCycleDelay);
        await first.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);

        Assert.NotNull(fixture.Worker.LastError);
        Assert.Equal(2, fixture.Handler.Requests.Count);
    }

    /// <summary>A failure no token had a part in is left to the schedule and
    /// the button.</summary>
    [Fact]
    public async Task A_failure_with_no_token_refused_is_not_run_again_at_once()
    {
        using var fixture = Fixture.Create(
            featureOn: true,
            paired: true,
            authenticated: true,
            respond: (_, _) => StubHttpMessageHandler.Problem(
                HttpStatusCode.ServiceUnavailable,
                SyncErrorCodes.ReplicaUnavailable,
                "The replica is not reachable yet."));

        var first = fixture.NextCycle();
        fixture.Clock.Advance(AnnotationSyncWorker.FirstCycleDelay);
        await first.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);

        Assert.Contains("not reachable", fixture.Worker.LastError, StringComparison.OrdinalIgnoreCase);
        Assert.Single(fixture.Handler.Requests);
    }

    private sealed class Fixture : IDisposable
    {
        public static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

        public static readonly TimeSpan WellPastSeveralCycles = TimeSpan.FromMinutes(30);

        private readonly HttpClient _http;
        private readonly bool _compose;
        private readonly TokenPipeline? _tokens;
        private int _sessionsResolved;

        private Fixture(
            HttpClient http,
            StubHttpMessageHandler handler,
            InMemoryAnnotationSyncStateStore state,
            StubFeatureSettings features,
            InMemoryDeviceCredentialStore credentials,
            FakeTimeProvider clock,
            bool compose,
            TokenPipeline? tokens)
        {
            _http = http;
            _compose = compose;
            _tokens = tokens;

            Handler = handler;
            State = state;
            Features = features;
            Credentials = credentials;
            Clock = clock;

            Worker = new AnnotationSyncWorker(
                new SessionProvider(BuildSession, tokens?.Provider),
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

        /// <summary>How many tokens were minted, where the fixture was composed
        /// <c>authenticated</c>.</summary>
        public int TokenRequests => _tokens?.Endpoint.Requests.Count ?? 0;

        /// <summary>What the scripted service answers when a test does not
        /// say: nothing accepted, nothing to pull.</summary>
        public static HttpResponseMessage Ordinary(HttpRequestMessage request) =>
            request.Method == HttpMethod.Post
                ? StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"accepted":0}""")
                : StubHttpMessageHandler.Json(
                    HttpStatusCode.OK,
                    """{"annotations":[],"since":"cursor-1","hasMore":false}""");

        public static Fixture Create(
            bool featureOn,
            bool paired,
            bool compose = true,
            Func<HttpRequestMessage, int, HttpResponseMessage>? respond = null,
            bool authenticated = false)
        {
            var handler = new StubHttpMessageHandler((request, index) =>
                respond is not null ? respond(request, index) : Ordinary(request));

            var credentials = paired ? new InMemoryDeviceCredentialStore(Paired) : new InMemoryDeviceCredentialStore();
            var clock = new FakeTimeProvider(Noon);

            var tokens = authenticated ? TokenPipeline.Create(credentials, clock) : null;
            var http = new HttpClient(tokens?.InFrontOf(handler) ?? handler) { BaseAddress = new Uri("https://sync.test") };

            return new Fixture(
                http,
                handler,
                new InMemoryAnnotationSyncStateStore(),
                new StubFeatureSettings(featureOn),
                credentials,
                clock,
                compose,
                tokens);
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
            _tokens?.Dispose();
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
                Credentials,
                Clock);
        }
    }

    private sealed class SessionProvider(Func<AnnotationSyncSession?> session, SyncTokenProvider? tokens) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(AnnotationSyncSession) ? session()
            : serviceType == typeof(SyncTokenProvider) ? tokens
            : null;
    }
}
