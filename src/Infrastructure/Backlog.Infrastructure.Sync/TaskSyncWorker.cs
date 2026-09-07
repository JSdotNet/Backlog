using Backlog.Modules.Sync.Abstractions;
using Backlog.SharedKernel;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Infrastructure.Sync;

/// <summary>
/// The schedule task replication did not have: a timer that runs
/// <see cref="TaskSyncSession.SyncAsync"/> on its own, so a machine catches up
/// without anybody opening Settings and pressing a button.
/// <para>
/// A plain object with a timer in it rather than an <c>IHostedService</c>, and
/// that is the decision rather than an omission. There is no hosted service
/// anywhere in this repository because there is nowhere for one to start: a
/// <c>BackgroundService</c> would be started by the generic host behind the
/// browser harness and never by the MAUI desktop head, which is the head this
/// exists for. Something both heads can resolve after <c>Build()</c> starts in
/// both. That is also why nothing here is asynchronous at the edges - it is not
/// started, it is constructed.
/// </para>
/// <para>
/// It deliberately does not hold a <see cref="TaskSyncSession"/>. The session is
/// registered transient over a typed <see cref="HttpClient"/>, and
/// <c>AddTaskSyncClient</c> says why in as many words: <c>IHttpClientFactory</c>
/// owns handler lifetimes, so a singleton holding one would pin a single handler
/// chain for the life of the process. One is resolved per cycle instead, and a
/// host that composed none is treated as having nothing to do rather than as
/// broken.
/// </para>
/// <para>
/// It does hold the <see cref="ITaskSyncStateStore"/>, which is the one piece of
/// per-device state the session shares and the singleton the rest of this is
/// arranged around. Taking it here has a second effect worth naming: on a head
/// that opted into replication and chose no store, this type is registered and
/// unconstructable in exactly the way the session is, so a screen that asks for
/// it gets the same answer and hides the same panel.
/// </para>
/// </summary>
public sealed class TaskSyncWorker : IDisposable
{
    /// <summary>
    /// How long after the loop starts the first cycle runs.
    /// <para>
    /// Short, because the person who has just switched task sync on - or just
    /// paired this device - is looking at the screen waiting to find out whether
    /// it works, and a first exchange five minutes later is indistinguishable
    /// from one that never happened. Not zero, because the loop also starts when
    /// the app does, and the first seconds of a launch belong to drawing the
    /// window rather than to a network call.
    /// </para>
    /// <para>
    /// <c>internal</c> rather than private so the tests drive the loop by this
    /// number instead of copying it. A test carrying its own copy of an interval
    /// goes on passing after the interval moves, which is the one thing a test
    /// about a schedule must not do.
    /// </para>
    /// </summary>
    internal static readonly TimeSpan FirstCycleDelay = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How often a cycle runs after the first one.
    /// <para>
    /// Five minutes is chosen against what an exchange costs rather than against
    /// how fresh a replica could be: a cycle is two requests even when nothing
    /// changed on either side, and this runs on a laptop whose radio and battery
    /// are the budget. A person who wants an answer sooner than the next tick
    /// presses the button, which is what <see cref="RequestSync"/> is for.
    /// </para>
    /// </summary>
    internal static readonly TimeSpan CyclePeriod = TimeSpan.FromMinutes(5);

    /// <summary>
    /// What a person is told when a cycle failed in a way nobody planned for.
    /// <para>
    /// The same sentence the Devices panel shows for a pairing call that threw,
    /// and said again here rather than shared with it: that constant belongs to a
    /// screen in <c>Backlog.Desktop.UI</c>, which this project may not see and
    /// must not start seeing for the sake of one string. An exception's own
    /// message is written for whoever reads the log and either says nothing
    /// usable on screen or says too much - a path, a header, the shape of the
    /// service - so the detail goes to the log and the sentence goes here.
    /// </para>
    /// </summary>
    public const string UnexpectedFailure = "The sync service could not be reached. Try again in a moment.";

    /// <summary>Guards <see cref="_timer"/> and <see cref="_disposed"/> together,
    /// because the pair is what decides whether a tick may still be scheduled: a
    /// store raising <c>Changed</c> on one thread while the screen disposes the
    /// worker on another is otherwise a timer created after disposal, which
    /// nothing would ever stop.</summary>
    private readonly Lock _gate = new();

    private readonly IServiceProvider _services;
    private readonly IAppFeatureSettings _features;
    private readonly IDeviceCredentialStore _credentials;
    private readonly ITaskSyncStateStore _state;
    private readonly TimeProvider _time;
    private readonly ILogger _log;

    /// <summary>Cancelled on disposal and handed to every exchange, so a cycle
    /// that is half way through a page when the window closes is asked to stop
    /// rather than left running against a provider that is going away.</summary>
    private readonly CancellationTokenSource _lifetime = new();

    /// <summary>Zero or one, moved only by
    /// <see cref="Interlocked.CompareExchange(ref int, int, int)"/>. Also what
    /// <see cref="IsSyncing"/> reports.</summary>
    private int _cycleInFlight;

    /// <summary>
    /// A reset that has been asked for and not yet taken effect, one flag each.
    /// <para>
    /// They exist because the obvious shape does not work. Writing the reset
    /// straight to the store and then asking for a cycle loses it whenever a
    /// cycle is already running: <see cref="TaskSyncSession.PushAsync"/> read the
    /// watermark before the reset landed and saves an advanced one per batch, so
    /// the reset is overwritten by a cycle that started before it — and the
    /// <see cref="RequestSync"/> that followed it was turned away by the overlap
    /// guard, so nothing runs again until the next tick. A person would press
    /// "Republish everything", have nothing republished, and be told nothing.
    /// </para>
    /// <para>
    /// Held here and applied inside the guard instead, so a reset is the cycle's
    /// business rather than the caller's: whichever cycle next holds the guard
    /// applies it before it reads anything, and a cycle that finishes with one
    /// still pending starts another. "Reset" therefore means "the next cycle",
    /// which is what a person pressing the button reads it as.
    /// </para>
    /// </summary>
    private int _pendingRepublish;

    /// <inheritdoc cref="_pendingRepublish"/>
    private int _pendingRehydrate;

    private ITimer? _timer;
    private bool _disposed;

    public TaskSyncWorker(
        IServiceProvider services,
        IAppFeatureSettings features,
        IDeviceCredentialStore credentials,
        ITaskSyncStateStore state,
        TimeProvider time,
        ILogger<TaskSyncWorker>? log = null)
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
        _log = log ?? NullLogger<TaskSyncWorker>.Instance;

        // Both gates can move while the app is running, and each of them moving
        // is somebody watching to see whether it worked.
        _features.Changed += OnGateChanged;
        _credentials.Changed += OnGateChanged;

        ApplyGates();
    }

    /// <summary>
    /// Raised at both ends of a cycle, whether it succeeded or failed, so a
    /// screen showing <see cref="LastSummary"/> or <see cref="LastError"/> can
    /// redraw.
    /// <para>
    /// Both ends rather than only the finish, because <see cref="IsSyncing"/> is
    /// otherwise unreadable: <see cref="RequestSync"/> hands the exchange to the
    /// thread pool and returns, so the render that follows the button press
    /// happens before the cycle has taken the guard, and a busy state nothing
    /// re-renders for is a busy state nobody sees. A cycle that declines to start
    /// - the gates are shut, or one is already in flight - raises neither.
    /// </para>
    /// <para>
    /// It arrives on a thread pool thread: a renderer subscribing to it has to
    /// marshal.
    /// </para>
    /// </summary>
    public event Action? Changed;

    /// <summary>What the last exchange that succeeded did, or null when none has
    /// yet. Left alone by a failure, so a device that has been offline since
    /// lunch still shows what it did at lunch.</summary>
    public TaskSyncSummary? LastSummary { get; private set; }

    /// <summary>Why the last cycle failed, or null when the last one worked.
    /// Cleared by a success, so the sentence on screen is never older than the
    /// summary beside it.</summary>
    public string? LastError { get; private set; }

    /// <summary>True while a cycle is in flight, for a button to show busy.</summary>
    public bool IsSyncing => Volatile.Read(ref _cycleInFlight) == 1;

    /// <summary>Where this device's progress is kept, for a settings screen to
    /// show. Passed through rather than making the screen resolve a second
    /// collaborator for one line of text.</summary>
    public string StorePath => _state.StorePath;

    /// <summary>
    /// Runs a cycle now, and returns before it has done anything.
    /// <para>
    /// This is what a button calls, so it never blocks, never awaits and never
    /// throws: the first thing a cycle does is read the task database, which on a
    /// large backlog is long enough to be felt on the thread that draws the
    /// window. It is dispatched to the thread pool for that reason, and its
    /// result is not returned anywhere - what happened arrives through
    /// <see cref="Changed"/>, the same way a tick's does, so the screen has one
    /// path to render rather than two.
    /// </para>
    /// <para>
    /// A request that lands while a cycle is already running does nothing, by the
    /// same guard a tick meeting a slow cycle hits. Pressing the button twice is
    /// one exchange.
    /// </para>
    /// </summary>
    public void RequestSync() => _ = RequestSyncAsync();

    /// <summary>
    /// Forgets how far this device has pushed, so the next cycle offers every
    /// live task and every tombstone on the machine again.
    /// <para>
    /// Safe, and that is why it can be offered at all: the replica upserts whole
    /// documents under last-write-wins, so a document sent a second time lands in
    /// the state it is already in. What it costs is bandwidth and one long cycle;
    /// what it buys is a replica that can be treated as disposable - deleted,
    /// recreated, and filled again from a device that still has the data.
    /// </para>
    /// <para>
    /// Recorded rather than written, and applied by the cycle - see
    /// <see cref="_pendingRepublish"/> for why writing it here would lose it to a
    /// cycle that was already running.
    /// </para>
    /// </summary>
    public void RepublishEverything()
    {
        Volatile.Write(ref _pendingRepublish, 1);
        RequestSync();
    }

    /// <summary>
    /// Forgets the feed position, so the next cycle reads the owner's change feed
    /// from the beginning.
    /// <para>
    /// Which is what a freshly paired device does anyway, and what this machine
    /// does by itself when the service says its cursor has been retired - see
    /// <see cref="TaskSyncSession.PullAsync"/>. Offered as a deliberate act as
    /// well because the other way to need it is local: a replica that is intact
    /// and a machine whose task database was emptied or restored have no
    /// disagreement the cursor can see, and reading the feed again is the only
    /// thing that fills the machine back up.
    /// </para>
    /// <para>
    /// Recorded rather than written, for the reason
    /// <see cref="RepublishEverything"/> is. The cleared cursor would in fact
    /// survive a cycle already in flight - a pull re-reads the state when it
    /// saves - but the cycle that would act on it would not run until the next
    /// tick, and a person watching a button do nothing for five minutes has been
    /// told the same lie either way.
    /// </para>
    /// </summary>
    public void RehydrateFromScratch()
    {
        Volatile.Write(ref _pendingRehydrate, 1);
        RequestSync();
    }

    /// <summary>Stops the loop, cancels whatever cycle is in flight, and lets go
    /// of both stores. Unsubscribing matters more than stopping the timer does: a
    /// worker that stayed on a singleton store's event would be kept alive by it
    /// and would go on starting cycles through a provider that has been disposed
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

    /// <summary>What <see cref="RequestSync"/> discards. Internal so a test can
    /// wait for the dispatch the button deliberately does not wait for -
    /// otherwise "a request that arrives during a cycle starts no second one" can
    /// only be written as a sleep, and a sleep asserts the machine's speed rather
    /// than the guard.</summary>
    internal Task RequestSyncAsync()
    {
        lock (_gate)
        {
            if (_disposed) return Task.CompletedTask;
        }

        return Task.Run(RunGuardedAsync, CancellationToken.None);
    }

    /// <summary>Both gates, read together. A device with task sync on and no
    /// credential has no owner to replicate under, and a paired device with the
    /// feature off has asked for its tasks to stay on the machine; either one is
    /// reason enough for the loop not to exist.</summary>
    private bool ShouldRun => _features.IsEnabled(SyncFeatures.TaskSync) && _credentials.Current is not null;

    private void OnGateChanged() => ApplyGates();

    /// <summary>
    /// Brings the timer into line with the two gates: started when both are
    /// satisfied, gone when either is not, and neither of those needing a
    /// restart. A person who has just switched task sync on, or just typed a
    /// pairing code, is looking at the screen to see whether it works - the same
    /// reasoning <c>TasksDesktopState.ApplyRefreshSettings</c> gives for its own
    /// poll timer, which this is deliberately shaped on.
    /// <para>
    /// An already-running loop is left running rather than rescheduled, so a
    /// store that raises <c>Changed</c> for an unrelated reason does not keep
    /// pushing the next cycle five more seconds into the future.
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
            // because a real timer is driven by the machine's clock and nothing
            // in a test can move it: the schedule would then only be assertable
            // by waiting for it. Through the provider, FakeTimeProvider fires the
            // same callback on demand.
            _timer ??= _time.CreateTimer(_ => OnTickElapsed(), state: null, FirstCycleDelay, CyclePeriod);
        }
    }

    /// <summary>
    /// A tick, which must not throw.
    /// <para>
    /// <c>async void</c> because a timer callback returns void and there is
    /// nobody to hand the task to. A tick runs on a thread pool thread with
    /// nobody to hand a failure to either, and an escaping exception there ends
    /// the process - so the catch is not tidiness, it is the difference between a
    /// missed exchange and a closed window.
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
            // RunGuardedAsync already swallows everything a cycle can raise, so
            // reaching here means the guard itself failed. Swallowed all the
            // same, and for the reason above: the next tick tries again.
            _log.LogError(ex, "A task sync tick failed outside the cycle it was starting.");
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
            _log.LogError(ex, "A task sync cycle failed unexpectedly.");
        }
    }

    /// <summary>
    /// One exchange.
    /// <para>
    /// The gates are read again here and not only when the timer was arranged: a
    /// request from the button arrives without passing <see cref="ApplyGates"/>
    /// at all, and a tick can be in the air when a device is unpaired.
    /// </para>
    /// <para>
    /// <b>A failed result is not an alarm.</b> The overwhelmingly common failure
    /// is a machine with no network, and a background loop that logged an error
    /// every five minutes for a laptop on a train would drown the log it is meant
    /// to be searchable in. Only an exception is unexpected, because the session
    /// answers everything it planned for with a result.
    /// </para>
    /// </summary>
    private async Task RunCycleAsync()
    {
        if (!ShouldRun) return;

        // The one guard that covers both ways two cycles could overlap: a slow
        // cycle still running when the next tick arrives, and a tick arriving
        // while somebody presses the button. Two exchanges at once would each
        // advance the watermark from what they read before the other saved, and
        // the lower of the two writes last - so the device would skip whatever
        // fell between them, permanently and with nothing failing to say so.
        if (Interlocked.CompareExchange(ref _cycleInFlight, 1, 0) != 0) return;

        // After the guard, never before it: a raise from a cycle that turned out
        // not to be starting would put a screen into a busy state nothing was
        // going to bring it out of.
        Changed?.Invoke();

        try
        {
            // Inside the guard and before anything is read, so a reset asked for
            // while another cycle held the guard is applied by this one rather
            // than trampled by it.
            ApplyPendingResets();

            var session = ResolveSession();

            // A head that composed no session has nothing to replicate, which is
            // an arrangement rather than a fault. Nothing to do, and no noise.
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
                    "A task sync cycle did not complete: {Code} {Message}", result.Error.Code, result.Error.Message);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // The window closed mid-exchange. Nothing failed, and there is nobody
            // left to tell about it.
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "A task sync cycle threw.");
            LastError = UnexpectedFailure;
        }
        finally
        {
            Volatile.Write(ref _cycleInFlight, 0);
            Changed?.Invoke();

            // A reset that arrived while this cycle held the guard is still
            // pending: the RequestSync it came with was turned away. Now the
            // guard is free, so it runs - which is what makes "reset" mean the
            // next cycle rather than the next tick, five minutes away. It cannot
            // spin: the cycle it starts consumes the flags before it reads
            // anything, so the second pass finds nothing pending.
            if (ResetPending) RequestSync();
        }
    }

    /// <summary>True while either reset is waiting for a cycle to carry it
    /// out.</summary>
    private bool ResetPending =>
        Volatile.Read(ref _pendingRepublish) == 1 || Volatile.Read(ref _pendingRehydrate) == 1;

    /// <summary>
    /// Takes whichever resets are pending and writes them, as one save.
    /// <para>
    /// One save rather than two because both are asked for through the same
    /// dialog and could in principle both be pending: writing them separately
    /// would put a state on disk that nobody asked for, in the window between.
    /// </para>
    /// <para>
    /// Claimed with <see cref="Interlocked.Exchange(ref int, int)"/> so a reset
    /// arriving while this runs is not swallowed by it - it stays pending and the
    /// cycle after this one carries it out.
    /// </para>
    /// </summary>
    private void ApplyPendingResets()
    {
        var republish = Interlocked.Exchange(ref _pendingRepublish, 0) == 1;
        var rehydrate = Interlocked.Exchange(ref _pendingRehydrate, 0) == 1;

        if (!republish && !rehydrate) return;

        var state = _state.Current;

        if (republish) state = state with { PushWatermark = DateTimeOffset.MinValue };
        if (rehydrate) state = state with { PullCursor = null };

        _state.Save(state);
    }

    /// <summary>
    /// A session for this cycle, where this host composed one.
    /// <para>
    /// Not a plain <c>GetService</c>, for the reason the settings screen's own
    /// lookup is not: <c>AddTaskSyncClient</c> registers the session and
    /// deliberately registers no <see cref="ITaskSyncStateStore"/>, so on a head
    /// that chose none the session is registered and unconstructable and asking
    /// for it throws rather than answering null. A background loop is the worst
    /// possible place for that to surface - it would be an error in the log every
    /// five minutes for a head that simply does not replicate.
    /// </para>
    /// </summary>
    private TaskSyncSession? ResolveSession()
    {
        try
        {
            return _services.GetService<TaskSyncSession>();
        }
        catch (InvalidOperationException ex)
        {
            _log.LogInformation(ex, "Task sync is not composed in this host, so the loop has nothing to do.");
            return null;
        }
    }
}
