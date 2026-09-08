using Backlog.Modules.Sync.Abstractions;
using Backlog.SharedKernel;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Infrastructure.Sync.Sessions;

/// <summary>
/// The schedule session replication needs: a timer that runs
/// <see cref="SessionSyncSession.SyncAsync"/> on its own, so a machine's sessions
/// reach the other devices without anybody opening Settings.
/// <para>
/// A plain object with a timer in it rather than an <c>IHostedService</c>, for the
/// whole of the reason <see cref="TaskSyncWorker"/> gives: there is nowhere for a
/// hosted service to start, because a <c>BackgroundService</c> would be started by
/// the generic host behind the browser harness and never by the MAUI desktop head,
/// which is the head this exists for. It is not started, it is constructed.
/// </para>
/// <para>
/// <strong>A sibling of <see cref="TaskSyncWorker"/> rather than a second exchange
/// inside it, and that is the decision.</strong> Folding both into one loop looked
/// cheaper — one timer, one resolve, one registration to guard — and it fails on
/// the gate. That worker starts its timer only while <c>task-sync</c> is on, so a
/// combined loop would have to run whenever <em>either</em> flag was on and then
/// re-check each flag per exchange; a person who switched task sync off would have
/// a class named for it running on their battery, and the two features would share
/// one failure surface — one <c>LastError</c>, one <c>IsSyncing</c>, one summary —
/// for two exchanges the settings screen switches independently. Separate loops
/// make "one exchange failing does not stop the other" true by construction rather
/// than by a <c>try</c> somebody has to keep in place, and the cost is one timer
/// callback every five minutes.
/// </para>
/// <para>
/// It deliberately does not hold a <see cref="SessionSyncSession"/>. The session is
/// registered transient over a typed <see cref="HttpClient"/> and
/// <c>IHttpClientFactory</c> owns handler lifetimes, so a singleton holding one
/// would pin a single handler chain for the life of the process. One is resolved
/// per cycle instead, and a host that composed none is treated as having nothing to
/// do rather than as broken.
/// </para>
/// <para>
/// <strong>The two resets <see cref="TaskSyncWorker"/> offers are not here, and
/// their absence is the honest state rather than an oversight.</strong> Nothing
/// yet asks for them: there is no Settings panel for session sync until the UI
/// slice lands. Whoever adds them must copy the mechanism and not only the method
/// — a reset written straight to the store and followed by
/// <see cref="RequestSync"/> is lost whenever a cycle is already running, because
/// the push read its watermark before the reset landed and saves an advanced one
/// per batch. <see cref="TaskSyncWorker"/>'s pending flags exist for exactly that,
/// and the failure they prevent is silent.
/// </para>
/// </summary>
public sealed class SessionSyncWorker : IDisposable
{
    /// <summary>
    /// How long after the loop starts the first cycle runs.
    /// <para>
    /// Short, because the person who has just switched session sync on is looking
    /// at the screen waiting to find out whether it works. Deliberately three
    /// seconds later than <see cref="TaskSyncWorker.FirstCycleDelay"/>: both loops
    /// start when the app does, and two exchanges firing into the same moment put
    /// four requests and two full catalog reads on top of the seconds that belong
    /// to drawing the window.
    /// </para>
    /// <para>
    /// <c>internal</c> rather than private so the tests drive the loop by this
    /// number instead of copying it. A test carrying its own copy of an interval
    /// goes on passing after the interval moves, which is the one thing a test
    /// about a schedule must not do.
    /// </para>
    /// </summary>
    internal static readonly TimeSpan FirstCycleDelay = TimeSpan.FromSeconds(8);

    /// <summary>
    /// How often a cycle runs after the first one.
    /// <para>
    /// <see cref="TaskSyncWorker.CyclePeriod"/>'s five minutes, chosen against what
    /// an exchange costs rather than against how fresh a replica could be: a cycle
    /// is two requests even when nothing moved, and this runs on a laptop whose
    /// radio and battery are the budget. A session that is still going will be
    /// pushed again next cycle with a later stamp, so the cost of being five
    /// minutes behind is that another machine's row says "a few minutes ago"
    /// rather than "now" — which is what a replicated row honestly is.
    /// </para>
    /// </summary>
    internal static readonly TimeSpan CyclePeriod = TimeSpan.FromMinutes(5);

    /// <summary>What a person is told when a cycle failed in a way nobody planned
    /// for. <see cref="TaskSyncWorker.UnexpectedFailure"/>'s sentence, referenced
    /// rather than copied: it is the same event described to the same person, and
    /// two spellings of it would read as two different problems.</summary>
    public const string UnexpectedFailure = TaskSyncWorker.UnexpectedFailure;

    /// <summary>Guards <see cref="_timer"/> and <see cref="_disposed"/> together,
    /// because the pair is what decides whether a tick may still be scheduled: a
    /// store raising <c>Changed</c> on one thread while the screen disposes the
    /// worker on another is otherwise a timer created after disposal, which
    /// nothing would ever stop.</summary>
    private readonly Lock _gate = new();

    private readonly IServiceProvider _services;
    private readonly IAppFeatureSettings _features;
    private readonly IDeviceCredentialStore _credentials;
    private readonly ISessionSyncStateStore _state;
    private readonly TimeProvider _time;
    private readonly ILogger _log;

    /// <summary>Cancelled on disposal and handed to every exchange, so a cycle that
    /// is half way through a page when the window closes is asked to stop rather
    /// than left running against a provider that is going away.</summary>
    private readonly CancellationTokenSource _lifetime = new();

    /// <summary>Zero or one, moved only by
    /// <see cref="Interlocked.CompareExchange(ref int, int, int)"/>. Also what
    /// <see cref="IsSyncing"/> reports.</summary>
    private int _cycleInFlight;

    private ITimer? _timer;
    private bool _disposed;

    public SessionSyncWorker(
        IServiceProvider services,
        IAppFeatureSettings features,
        IDeviceCredentialStore credentials,
        ISessionSyncStateStore state,
        TimeProvider time,
        ILogger<SessionSyncWorker>? log = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(time);

        _services = services;
        _features = features;
        _credentials = credentials;
        _state = state;
        _time = time;
        _log = log ?? NullLogger<SessionSyncWorker>.Instance;

        // Both gates can move while the app is running, and each of them moving is
        // somebody watching to see whether it worked.
        _features.Changed += OnGateChanged;
        _credentials.Changed += OnGateChanged;

        ApplyGates();
    }

    /// <summary>Raised at both ends of a cycle, whether it succeeded or failed, so
    /// a screen showing <see cref="LastSummary"/> or <see cref="LastError"/> can
    /// redraw. Both ends rather than only the finish, because
    /// <see cref="IsSyncing"/> is otherwise unreadable — see
    /// <see cref="TaskSyncWorker.Changed"/> for the whole of that argument. It
    /// arrives on a thread pool thread: a renderer subscribing to it has to
    /// marshal.</summary>
    public event Action? Changed;

    /// <summary>What the last exchange that succeeded did, or null when none has
    /// yet. Left alone by a failure, so a device that has been offline since lunch
    /// still shows what it did at lunch.</summary>
    public SessionSyncSummary? LastSummary { get; private set; }

    /// <summary>Why the last cycle failed, or null when the last one worked.
    /// Cleared by a success, so the sentence on screen is never older than the
    /// summary beside it.</summary>
    public string? LastError { get; private set; }

    /// <summary>True while a cycle is in flight, for a button to show busy.</summary>
    public bool IsSyncing => Volatile.Read(ref _cycleInFlight) == 1;

    /// <summary>Where this device's session progress is kept, for a settings screen
    /// to show.</summary>
    public string StorePath => _state.StorePath;

    /// <summary>
    /// Runs a cycle now, and returns before it has done anything.
    /// <para>
    /// This is what a button calls, so it never blocks, never awaits and never
    /// throws: the first thing a cycle does is read both agents' folders in the
    /// user profile, which on a developer's machine is hundreds of files and is
    /// long enough to be felt on the thread that draws the window. Its result is
    /// not returned anywhere — what happened arrives through <see cref="Changed"/>,
    /// the same way a tick's does, so the screen has one path to render rather than
    /// two.
    /// </para>
    /// <para>
    /// A request that lands while a cycle is already running does nothing, by the
    /// same guard a tick meeting a slow cycle hits.
    /// </para>
    /// </summary>
    public void RequestSync() => _ = RequestSyncAsync();

    /// <summary>Stops the loop, cancels whatever cycle is in flight, and lets go of
    /// both stores. Unsubscribing matters more than stopping the timer does: a
    /// worker that stayed on a singleton store's event would be kept alive by it and
    /// would go on starting cycles through a provider that has been disposed
    /// underneath it.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;

            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }

        _features.Changed -= OnGateChanged;
        _credentials.Changed -= OnGateChanged;

        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    /// <summary>What <see cref="RequestSync"/> discards. Internal so a test can wait
    /// for the dispatch the button deliberately does not wait for — otherwise "a
    /// request that arrives during a cycle starts no second one" can only be written
    /// as a sleep, and a sleep asserts the machine's speed rather than the
    /// guard.</summary>
    internal Task RequestSyncAsync()
    {
        lock (_gate)
        {
            if (_disposed) return Task.CompletedTask;
        }

        return Task.Run(RunGuardedAsync, CancellationToken.None);
    }

    /// <summary>Both gates, read together. A device with session sync on and no
    /// credential has no owner to replicate under, and a paired device with the
    /// feature off has asked for its session records to stay on the machine; either
    /// one is reason enough for the loop not to exist. This is what makes the
    /// feature inert rather than merely quiet — with it off no timer is created, so
    /// nothing reaches the wire at all.</summary>
    private bool ShouldRun => _features.IsEnabled(SyncFeatures.SessionSync) && _credentials.Current is not null;

    private void OnGateChanged() => ApplyGates();

    /// <summary>
    /// Brings the timer into line with the two gates: started when both are
    /// satisfied, gone when either is not, and neither of those needing a restart.
    /// <para>
    /// An already-running loop is left running rather than rescheduled, so a store
    /// that raises <c>Changed</c> for an unrelated reason does not keep pushing the
    /// next cycle eight more seconds into the future.
    /// </para>
    /// </summary>
    private void ApplyGates()
    {
        lock (_gate)
        {
            if (_disposed) return;

            if (!ShouldRun)
            {
                _timer?.Dispose();
                _timer = null;
                return;
            }

            // TimeProvider.CreateTimer rather than a System.Threading.Timer,
            // because a real timer is driven by the machine's clock and nothing in
            // a test can move it. Through the provider, FakeTimeProvider fires the
            // same callback on demand.
            _timer ??= _time.CreateTimer(_ => OnTickElapsed(), state: null, FirstCycleDelay, CyclePeriod);
        }
    }

    /// <summary>
    /// A tick, which must not throw.
    /// <para>
    /// <c>async void</c> because a timer callback returns void and there is nobody
    /// to hand the task to. A tick runs on a thread pool thread with nobody to hand
    /// a failure to either, and an escaping exception there ends the process — so
    /// the catch is not tidiness, it is the difference between a missed exchange and
    /// a closed window.
    /// </para>
    /// </summary>
    private async void OnTickElapsed()
    {
        try
        {
            await RunGuardedAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "A session sync tick failed outside the cycle it was starting.");
        }
    }

    /// <summary>The cycle with its own catch around it, so both the timer and the
    /// button call something that cannot fail.</summary>
    private async Task RunGuardedAsync()
    {
        try
        {
            await RunCycleAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "A session sync cycle failed unexpectedly.");
        }
    }

    /// <summary>
    /// One exchange.
    /// <para>
    /// The gates are read again here and not only when the timer was arranged: a
    /// request from a button arrives without passing <see cref="ApplyGates"/> at
    /// all, and a tick can be in the air when a device is unpaired or the feature
    /// is switched off.
    /// </para>
    /// <para>
    /// <b>A failed result is not an alarm.</b> The overwhelmingly common failure is
    /// a machine with no network, and a background loop that logged an error every
    /// five minutes for a laptop on a train would drown the log it is meant to make
    /// searchable. Only an exception is unexpected, because the session answers
    /// everything it planned for with a result.
    /// </para>
    /// </summary>
    private async Task RunCycleAsync()
    {
        if (!ShouldRun) return;

        // The one guard that covers both ways two cycles could overlap: a slow
        // cycle still running when the next tick arrives, and a tick arriving while
        // somebody presses the button. Two exchanges at once would each advance the
        // watermark from what they read before the other saved, and the lower of
        // the two writes last - so the device would skip whatever fell between
        // them, permanently and with nothing failing to say so.
        if (Interlocked.CompareExchange(ref _cycleInFlight, 1, 0) != 0) return;

        // After the guard, never before it: a raise from a cycle that turned out
        // not to be starting would put a screen into a busy state nothing was going
        // to bring it out of.
        Changed?.Invoke();

        try
        {
            var session = ResolveSession();

            // A head that composed no session has no sessions to replicate, which
            // is an arrangement rather than a fault. Nothing to do, and no noise.
            if (session is null) return;

            var result = await session.SyncAsync(_lifetime.Token).ConfigureAwait(false);

            if (result.IsSuccess)
            {
                LastSummary = result.Value;
                LastError = null;
            }
            else
            {
                LastError = result.Error.Message;
                _log.LogInformation(
                    "A session sync cycle did not complete: {Code} {Message}", result.Error.Code, result.Error.Message);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // The window closed mid-exchange. Nothing failed, and there is nobody
            // left to tell about it.
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "A session sync cycle threw.");
            LastError = UnexpectedFailure;
        }
        finally
        {
            Volatile.Write(ref _cycleInFlight, 0);
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// A session for this cycle, where this host composed one.
    /// <para>
    /// Not a plain <c>GetService</c>, for the reason
    /// <c>TaskSyncWorker.ResolveSession</c> is not: <c>AddSessionSyncClient</c>
    /// registers the session and a head that chose no
    /// <see cref="ISessionSyncStateStore"/> leaves it registered and
    /// unconstructable, so asking for it throws rather than answering null. A
    /// background loop is the worst possible place for that to surface — it would
    /// be an error in the log every five minutes for a head that simply does not
    /// replicate sessions.
    /// </para>
    /// </summary>
    private SessionSyncSession? ResolveSession()
    {
        try
        {
            return _services.GetService<SessionSyncSession>();
        }
        catch (InvalidOperationException ex)
        {
            _log.LogInformation(ex, "Session sync is not composed in this host, so the loop has nothing to do.");
            return null;
        }
    }
}
