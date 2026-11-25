using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.Abstractions.Services;
using Backlog.Modules.Sync.Abstractions;
using Backlog.SharedKernel;
using Backlog.SharedKernel.Results;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Infrastructure.Sync.KnowledgeEvents;

/// <summary>
/// The schedule knowledge event replication needs: a timer that runs
/// <see cref="KnowledgeEventSyncSession.SyncAsync"/> on its own, so an event saved on one
/// desktop reaches the others and the phone without anybody opening Settings.
/// <para>
/// A sibling of <see cref="DevbookSubjects.DevbookSubjectSyncWorker"/>, one loop per feed so
/// one exchange failing never stops another. The mechanism — the gates, the overlap guard, the
/// resolve-per-cycle, the catch that keeps a tick from ending the process — is that worker's,
/// copied rather than shared. One gate more, as the notification loop has its own: the
/// <c>events</c> area (local ADR 0050 §6). Off, this desktop sends nothing and takes nothing,
/// and the loop starts as soon as it is turned on.
/// </para>
/// <para>
/// The one thing an area turned off still sends is the move itself: the worker records every
/// move of the switch, and while one turned off is unsent, with sync on and the device paired, a
/// cycle sends that switch alone, so the phone withdraws its Events screen. A move turned on goes
/// with the next full exchange.
/// </para>
/// <para>
/// A cycle against a service that predates the feed ends as a success that says
/// <see cref="KnowledgeEventSyncSummary.NotYetServed"/>, so <see cref="LastError"/> stays empty
/// while the deployed service catches up.
/// </para>
/// <para>
/// A local write runs a cycle <see cref="ChangeSettleDelay"/> after the last one, as a task
/// write does (<see cref="TaskSyncWorker"/>): Events announces every save through
/// <see cref="IKnowledgeEventReplication.Changed"/>, a desktop's answer to a phone's attachment
/// request included, so neither waits for the tick. The footer's Sync asks through
/// <see cref="RequestSync"/>.
/// </para>
/// </summary>
public sealed partial class KnowledgeEventSyncWorker : IDisposable
{
    /// <summary>How long after the loop starts the first cycle runs — staggered behind every
    /// other loop.</summary>
    internal static readonly TimeSpan FirstCycleDelay = TimeSpan.FromSeconds(19);

    /// <summary>How often a cycle runs after the first one — the other loops' five
    /// minutes.</summary>
    internal static readonly TimeSpan CyclePeriod = TimeSpan.FromMinutes(5);

    /// <summary>How long after the last local write a cycle runs — the task loop's settle
    /// delay, so a burst of edits is one exchange.</summary>
    internal static readonly TimeSpan ChangeSettleDelay = TaskSyncWorker.ChangeSettleDelay;

    public const string UnexpectedFailure = TaskSyncWorker.UnexpectedFailure;

    private readonly Lock _gate = new();

    private readonly IServiceProvider _services;
    private readonly IAppFeatureSettings _features;
    private readonly IDeviceCredentialStore _credentials;
    private readonly IKnowledgeEventSyncStateStore _state;
    private readonly TimeProvider _time;
    private readonly ILogger _log;
    private readonly IKnowledgeEventReplication? _changes;

    private readonly CancellationTokenSource _lifetime = new();

    /// <summary>The guard: a cycle holds it from start to finish.</summary>
    private const int CycleInFlight = 1;

    /// <summary>Set when the settle timer fired, cleared by the cycle that answers it, so a
    /// write met while a cycle holds the guard runs one more cycle rather than waiting for the
    /// tick — <see cref="TaskSyncWorker"/>'s arrangement.</summary>
    private const int ChangeSettled = 2;

    /// <summary><see cref="CycleInFlight"/> and <see cref="ChangeSettled"/> in one word, so a
    /// settled-only request reads the flag and takes the guard in one step: one that finds
    /// nothing never holds the guard, and never turns a full request away.</summary>
    private int _cycleState;

    private int _pendingRequests;

    private ITimer? _timer;

    /// <summary>The one-shot timer a local write arms, and re-arms while writes keep coming.</summary>
    private ITimer? _settle;

    private bool _disposed;

    /// <param name="changes">Events' replication port, for its local-change notice only, or
    /// null on a head that composed none — then the loop is the schedule and the button.</param>
    public KnowledgeEventSyncWorker(
        IServiceProvider services,
        IAppFeatureSettings features,
        IDeviceCredentialStore credentials,
        IKnowledgeEventSyncStateStore state,
        TimeProvider time,
        ILogger<KnowledgeEventSyncWorker>? log = null,
        IKnowledgeEventReplication? changes = null)
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
        _log = log ?? NullLogger<KnowledgeEventSyncWorker>.Instance;
        _changes = changes;

        _features.Changed += OnGateChanged;
        _credentials.Changed += OnGateChanged;
        if (_changes is not null) _changes.Changed += OnLocalChange;

        NoteSwitch();
        ApplyGates();
    }

    /// <summary>Raised at both ends of a cycle, on a thread pool thread.</summary>
    public event Action? Changed;

    /// <summary>What the last exchange that succeeded did, or null when none has.</summary>
    public KnowledgeEventSyncSummary? LastSummary { get; private set; }

    /// <summary>Why the last cycle failed, or null when the last one worked.</summary>
    public string? LastError { get; private set; }

    /// <summary>True while a cycle is in flight.</summary>
    public bool IsSyncing => (Volatile.Read(ref _cycleState) & CycleInFlight) != 0;

    /// <summary>Where this device's knowledge event progress is kept.</summary>
    public string StorePath => _state.StorePath;

    /// <summary>Runs a cycle now, and returns before it has done anything.</summary>
    public void RequestSync() => _ = RequestSyncAsync();

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;

            _disposed = true;
            _timer?.Dispose();
            _timer = null;
            _settle?.Dispose();
            _settle = null;
        }

        _features.Changed -= OnGateChanged;
        _credentials.Changed -= OnGateChanged;
        if (_changes is not null) _changes.Changed -= OnLocalChange;

        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    /// <summary>What <see cref="RequestSync"/> discards, so a test can wait for the
    /// dispatch.</summary>
    internal Task RequestSyncAsync()
    {
        lock (_gate)
        {
            if (_disposed) return Task.CompletedTask;
        }

        return DispatchAsync(settledOnly: false);
    }

    /// <summary>What <see cref="RequestSettledSync"/> discards, so a test can wait for the
    /// dispatch.</summary>
    internal Task RequestSettledSyncAsync()
    {
        lock (_gate)
        {
            if (_disposed) return Task.CompletedTask;
        }

        return DispatchAsync(settledOnly: true);
    }

    /// <summary>True while a request has been dispatched and not yet run or turned away, so a
    /// test can let a request it cannot await drain.</summary>
    internal bool HasPendingRequests => Volatile.Read(ref _pendingRequests) != 0;

    private Task DispatchAsync(bool settledOnly)
    {
        Interlocked.Increment(ref _pendingRequests);

        return Task.Run(
            async () =>
            {
                try
                {
                    await RunGuardedAsync(settledOnly).ConfigureAwait(false);
                }
                finally
                {
                    Interlocked.Decrement(ref _pendingRequests);
                }
            },
            CancellationToken.None);
    }

    /// <summary>
    /// A cycle for writes that settled, which runs only if no cycle has carried them yet.
    /// <para>
    /// A settle asks twice: the settle timer itself, and a cycle that held the guard when it
    /// fired, on its way out. Either may reach the guard first, and the other then finds
    /// nothing left to send. Without the check the later one ran a cycle of its own — an
    /// exchange that sent nothing, and a third cycle where one write had asked for one.
    /// </para>
    /// </summary>
    private void RequestSettledSync() => _ = RequestSettledSyncAsync();

    /// <summary>All three gates: the <c>sync</c> feature every loop answers to, the
    /// <c>events</c> area this feed belongs to, and a paired device to replicate under.</summary>
    private bool ShouldRun => CanReachService && _features.IsEnabled(EventsFeatures.Events);

    /// <summary>The area is off here and the phone has not been told: sync on and paired, and the
    /// switch's move not yet sent.</summary>
    private bool ShouldWithdraw =>
        CanReachService
        && !_features.IsEnabled(EventsFeatures.Events)
        && _state.Current.Area is { On: false, Sent: false };

    /// <summary>The <c>sync</c> feature on and a paired device: what either kind of cycle needs.</summary>
    private bool CanReachService => _features.IsEnabled(SyncFeatures.Sync) && _credentials.Current is not null;

    private void OnGateChanged()
    {
        NoteSwitch();
        ApplyGates();

        // The phone is told as soon as it can be, not a cycle later; the timer retries.
        if (ShouldWithdraw) RequestSync();
    }

    /// <summary>Records the <c>events</c> switch when it stands other than last recorded: a move,
    /// stamped now and unsent. The first sight is recorded as sent, so upgrading moves
    /// nothing.</summary>
    private void NoteSwitch()
    {
        lock (_gate)
        {
            if (_disposed) return;

            var on = _features.IsEnabled(EventsFeatures.Events);
            var now = _time.GetUtcNow();

            try
            {
                _state.Update(state => state.Area is { } area && area.On == on
                    ? state
                    : state with { Area = new EventsAreaSwitch(on, now, Sent: state.Area is null) });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LogSwitchNotRecorded(_log, ex);
            }
        }
    }

    /// <summary>An event or a series was saved here: arm the settle timer, or push it back if
    /// it is already armed. Gated as a tick is, so a write with a gate shut arms nothing.</summary>
    private void OnLocalChange()
    {
        lock (_gate)
        {
            if (_disposed || !ShouldRun) return;

            if (_settle is null)
            {
                // Created on the writer's call, so without this the timer would hold the
                // writer's execution context, and every cycle after would run in it for the
                // app's life: the Events pane's self-write mark, a window's scope. A writer whose
                // flow is suppressed already is left as it is: suppressing twice is documented
                // to throw, and undoing it here would hand the writer its flow back.
                if (ExecutionContext.IsFlowSuppressed())
                {
                    _settle = CreateSettleTimer();
                }
                else
                {
                    using (ExecutionContext.SuppressFlow())
                    {
                        _settle = CreateSettleTimer();
                    }
                }
            }
            else
            {
                _settle.Change(ChangeSettleDelay, Timeout.InfiniteTimeSpan);
            }
        }
    }

    private ITimer CreateSettleTimer() =>
        _time.CreateTimer(_ => OnSettled(), state: null, ChangeSettleDelay, Timeout.InfiniteTimeSpan);

    /// <summary>The writes have stopped: run a cycle now, recorded first so a cycle already
    /// holding the guard runs one more on its way out.</summary>
    private void OnSettled()
    {
        Interlocked.Or(ref _cycleState, ChangeSettled);
        RequestSettledSync();
    }

    private void ApplyGates()
    {
        lock (_gate)
        {
            if (_disposed) return;

            if (!ShouldRun && !ShouldWithdraw)
            {
                _timer?.Dispose();
                _timer = null;
                return;
            }

            _timer ??= _time.CreateTimer(_ => OnTickElapsed(), state: null, FirstCycleDelay, CyclePeriod);
        }
    }

    private async void OnTickElapsed()
    {
        try
        {
            await RunGuardedAsync(settledOnly: false).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogTickFailed(_log, ex);
        }
    }

    private async Task RunGuardedAsync(bool settledOnly)
    {
        try
        {
            await RunCycleAsync(settledOnly).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogCycleFailedUnexpectedly(_log, ex);
        }
    }

    private async Task RunCycleAsync(bool settledOnly)
    {
        var full = ShouldRun;
        if (!full && !ShouldWithdraw) return;

        if (!TryTakeGuard(settledOnly)) return;

        try
        {
            Changed?.Invoke();

            var session = ResolveSession();

            if (session is null) return;

            var result = full
                ? await RefusedTokenRerun.RunAsync(
                    _services, () => session.SyncAsync(_lifetime.Token), _log, "knowledge event").ConfigureAwait(false)
                : await WithdrawAsync(session).ConfigureAwait(false);

            // A switch expression, so CS8509 proves both cases handled (app-defaults ADR 0005).
            LastError = result switch
            {
                Ok<KnowledgeEventSyncSummary>(var summary) => Completed(summary),
                Error error => Failed(error),
            };

            string? Completed(KnowledgeEventSyncSummary summary)
            {
                LastSummary = summary;
                return null;
            }

            string Failed(Error error)
            {
                LogCycleIncomplete(_log, error.Code, error.Message);
                return error.Message;
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // The window closed mid-exchange.
        }
        catch (Exception ex)
        {
            LogCycleThrew(_log, ex);
            LastError = UnexpectedFailure;
        }
        finally
        {
            Interlocked.And(ref _cycleState, ~CycleInFlight);
            Changed?.Invoke();

            // A withdrawal sent leaves nothing for the loop to do while the area stays off.
            if (!full) ApplyGates();

            // Turned off while this exchange ran, after it had sent the switch: go again now.
            else if (ShouldWithdraw) RequestSync();

            // A write that settled while this cycle held the guard: its request was turned
            // away, so it runs now rather than on the next tick.
            if ((Volatile.Read(ref _cycleState) & ChangeSettled) != 0 && ShouldRun) RequestSettledSync();
        }
    }

    /// <summary>
    /// Takes the guard, clearing <see cref="ChangeSettled"/> in the same step — before anything
    /// is read, so the cycle's push carries every write that fired the settle timer.
    /// <para>
    /// A cycle in flight turns every request away; its way out asks again for a settle it met.
    /// A settled-only request that finds the flag clear — the write carried already, by the
    /// cycle that answered the settle's other ask — returns without ever holding the guard, so
    /// a full request (a manual sync, a withdrawal) arriving at that moment is not turned away.
    /// </para>
    /// </summary>
    private bool TryTakeGuard(bool settledOnly)
    {
        var seen = Volatile.Read(ref _cycleState);

        while (true)
        {
            if ((seen & CycleInFlight) != 0) return false;
            if (settledOnly && (seen & ChangeSettled) == 0) return false;

            var was = Interlocked.CompareExchange(ref _cycleState, CycleInFlight, seen);
            if (was == seen) return true;

            seen = was;
        }
    }

    /// <summary>The cycle of an area turned off: the switch alone.</summary>
    private Task<Result<KnowledgeEventSyncSummary>> WithdrawAsync(KnowledgeEventSyncSession session) =>
        RefusedTokenRerun.RunAsync(_services, () => session.WithdrawAsync(_lifetime.Token), _log, "events switch");

    /// <summary>A session for this cycle, where this host composed one; a head that registered
    /// the loop without Events has nothing to do rather than being broken.</summary>
    private KnowledgeEventSyncSession? ResolveSession()
    {
        try
        {
            return _services.GetService<KnowledgeEventSyncSession>();
        }
        catch (InvalidOperationException ex)
        {
            LogNotComposed(_log, ex);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The events switch could not be recorded, so the phone is not told it moved.")]
    private static partial void LogSwitchNotRecorded(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "A knowledge event sync tick failed outside the cycle it was starting.")]
    private static partial void LogTickFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "A knowledge event sync cycle failed unexpectedly.")]
    private static partial void LogCycleFailedUnexpectedly(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "A knowledge event sync cycle did not complete: {Code} {Message}")]
    private static partial void LogCycleIncomplete(ILogger logger, string code, string message);

    [LoggerMessage(Level = LogLevel.Error, Message = "A knowledge event sync cycle threw.")]
    private static partial void LogCycleThrew(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Knowledge event sync is not composed in this host, so the loop has nothing to do.")]
    private static partial void LogNotComposed(ILogger logger, Exception exception);
}
