using System.Net;

using Backlog.Infrastructure.Sync.Sessions;
using Backlog.Modules.Sync.Abstractions;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// The session loop: when it runs, when it refuses to, and what it does with a
/// cycle that fails.
/// <para>
/// Every failure covered here is silent in the product. A loop that never started,
/// a loop that stopped after the first offline cycle, and a loop still running
/// after the feature was switched off all look exactly like a device that has
/// nothing to sync — there is no screen and no log line that tells them apart.
/// </para>
/// <para>
/// The worker drives a real <see cref="SessionSyncSession"/> over a scripted wire
/// rather than a stand-in, for the reason <see cref="TaskSyncWorkerTests"/> does:
/// half of what these assert is the exchange's own behaviour, and a seam between
/// the two would have tested neither.
/// </para>
/// </summary>
public sealed class SessionSyncWorkerTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    private static readonly DeviceCredential Paired = new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        "Workshop PC",
        "a-registration-credential");

    /// <summary>
    /// <strong>Off means inert, not quiet.</strong> A person who has switched
    /// session sync off has asked for records of what their agents have been doing
    /// to stay on the machine, and a loop that went on exchanging — even one that
    /// discarded what it got — would be the one thing the switch exists to stop.
    /// Asserted on the wire and not on a summary, because a request that was made
    /// and thrown away has still been made.
    /// </summary>
    [Fact]
    public void No_request_is_made_while_the_session_sync_feature_is_off()
    {
        using var fixture = Fixture.Create(featureOn: false, paired: true);

        fixture.Clock.Advance(Fixture.WellPastSeveralCycles);

        Assert.Equal(0, fixture.SessionsResolved);
        Assert.Empty(fixture.Handler.Requests);
    }

    /// <summary>Pressing the button while the feature is off does nothing either.
    /// The gate is read inside the cycle and not only when the timer was arranged,
    /// because a request from a screen arrives without passing the timer at
    /// all.</summary>
    [Fact]
    public async Task An_explicit_request_while_the_feature_is_off_makes_no_request()
    {
        using var fixture = Fixture.Create(featureOn: false, paired: true);

        await fixture.Worker.RequestSyncAsync();

        Assert.Empty(fixture.Handler.Requests);
    }

    /// <summary>Switching it on starts the loop where it stands, because the person
    /// who has just switched it on is looking at the screen to see whether it works.
    /// Switching it off stops it the same way, without a restart.</summary>
    [Fact]
    public async Task Switching_the_feature_on_starts_the_loop_and_switching_it_off_stops_it()
    {
        using var fixture = Fixture.Create(featureOn: false, paired: true);

        var first = fixture.NextCycle();
        _ = fixture.Features.SetEnabled(SyncFeatures.SessionSync, true);
        fixture.Clock.Advance(SessionSyncWorker.FirstCycleDelay);
        await first.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);

        Assert.Equal(1, fixture.SessionsResolved);

        _ = fixture.Features.SetEnabled(SyncFeatures.SessionSync, false);
        fixture.Clock.Advance(Fixture.WellPastSeveralCycles);

        Assert.Equal(1, fixture.SessionsResolved);
    }

    /// <summary>An unpaired device has no owner to replicate under, so there is
    /// nothing a cycle could ask for.</summary>
    [Fact]
    public async Task No_cycle_runs_until_a_device_credential_is_stored()
    {
        using var fixture = Fixture.Create(featureOn: true, paired: false);

        fixture.Clock.Advance(Fixture.WellPastSeveralCycles);

        Assert.Equal(0, fixture.SessionsResolved);

        var first = fixture.NextCycle();
        fixture.Credentials.Save(Paired);
        fixture.Clock.Advance(SessionSyncWorker.FirstCycleDelay);
        await first.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);

        Assert.Equal(1, fixture.SessionsResolved);
    }

    /// <summary>The loop keeps ticking after a cycle that could not reach the
    /// service. A background loop that gave up on the first offline cycle would stop
    /// syncing for the rest of the session on a laptop that briefly lost its
    /// network, and nothing would say so.</summary>
    [Fact]
    public async Task A_failed_cycle_does_not_stop_the_loop()
    {
        using var fixture = Fixture.Create(
            featureOn: true,
            paired: true,
            respond: (_, _) => throw new HttpRequestException("No route to host."));

        var first = fixture.NextCycle();
        fixture.Clock.Advance(SessionSyncWorker.FirstCycleDelay);
        await first.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);

        Assert.NotNull(fixture.Worker.LastError);

        var second = fixture.NextCycle();
        fixture.Clock.Advance(SessionSyncWorker.CyclePeriod);
        await second.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);

        Assert.Equal(2, fixture.SessionsResolved);
    }

    /// <summary>A head that composed no session — no sessions to replicate — has
    /// nothing to do rather than an error every five minutes.</summary>
    [Fact]
    public async Task A_host_that_composed_no_session_is_not_an_error()
    {
        using var fixture = Fixture.Create(featureOn: true, paired: true, compose: false);

        var first = fixture.NextCycle();
        fixture.Clock.Advance(SessionSyncWorker.FirstCycleDelay);
        await first.WaitAsync(Fixture.Patience, TestContext.Current.CancellationToken);

        Assert.Null(fixture.Worker.LastError);
        Assert.Null(fixture.Worker.LastSummary);
        Assert.Empty(fixture.Handler.Requests);
    }

    /// <summary>
    /// The first session cycle is deliberately not the first task cycle. Both loops
    /// start when the app does, and two exchanges firing into the same moment put
    /// four requests and two full catalog reads on top of the seconds that belong to
    /// drawing the window.
    /// </summary>
    [Fact]
    public void The_two_loops_do_not_fire_their_first_cycle_together()
    {
        Assert.NotEqual(TaskSyncWorker.FirstCycleDelay, SessionSyncWorker.FirstCycleDelay);
    }

    private sealed class Fixture : IDisposable
    {
        /// <summary>How long a test waits for work it has already caused before
        /// calling it a hang. Generous, because it is a bound and not a measurement:
        /// nothing here passes or fails by being quick.</summary>
        public static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

        /// <summary>Far enough past the schedule that a running loop would certainly
        /// have ticked. Only ever used where the assertion is that nothing
        /// happened.</summary>
        public static readonly TimeSpan WellPastSeveralCycles = TimeSpan.FromMinutes(30);

        private readonly HttpClient _http;
        private readonly bool _compose;
        private int _sessionsResolved;

        private Fixture(
            HttpClient http,
            StubHttpMessageHandler handler,
            InMemorySessionSyncStateStore state,
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

            Worker = new SessionSyncWorker(
                new SessionProvider(BuildSession),
                features,
                credentials,
                state,
                clock);
        }

        public SessionSyncWorker Worker { get; }

        public StubHttpMessageHandler Handler { get; }

        public InMemorySessionSyncStateStore State { get; }

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
                        """{"sessions":[],"since":"cursor-1","hasMore":false}""");
            });

            var http = new HttpClient(handler) { BaseAddress = new Uri("https://sync.test") };

            return new Fixture(
                http,
                handler,
                new InMemorySessionSyncStateStore(),
                new StubFeatureSettings(featureOn),
                paired ? new InMemoryDeviceCredentialStore(Paired) : new InMemoryDeviceCredentialStore(),
                new FakeTimeProvider(Noon),
                compose);
        }

        /// <summary>
        /// A signal that completes when the worker next finishes a cycle. Taken
        /// before the tick that causes it, because the cycle can be over before the
        /// line after the tick runs.
        /// <para>
        /// <c>Changed</c> is raised at both ends of a cycle, so this waits for the
        /// raise that finds the worker idle.
        /// </para>
        /// </summary>
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

        private SessionSyncSession? BuildSession()
        {
            Interlocked.Increment(ref _sessionsResolved);

            if (!_compose) return null;

            return new SessionSyncSession(
                new SessionSyncClient(_http),
                new StubAgentSessionSource(),
                new StubSessionRepositoryAliases(),
                State,
                new InMemoryReplicatedSessionStore(),
                Credentials,
                Clock);
        }
    }

    /// <summary>The one service the worker is allowed to ask for, and a count of how
    /// often it asked. Anything else answers null, which is what a container does for
    /// a service nobody registered.</summary>
    private sealed class SessionProvider(Func<SessionSyncSession?> session) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(SessionSyncSession) ? session() : null;
    }
}
