using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.Abstractions.Services;
using Backlog.Modules.Sync.Abstractions;
using Backlog.SharedKernel;

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
public sealed class KnowledgeEventSyncWorker : IDisposable
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

    private int _cycleInFlight;

    /// <summary>Set when the settle timer fired, cleared by the cycle that answers it, so a
    /// write met while a cycle holds the guard runs one more cycle rather than waiting for the
    /// tick — <see cref="TaskSyncWorker"/>'s arrangement.</summary>
    private int _changeSettled;

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

        ApplyGates();
    }

    /// <summary>Raised at both ends of a cycle, on a thread pool thread.</summary>
    public event Action? Changed;

    /// <summary>What the last exchange that succeeded did, or null when none has.</summary>
    public KnowledgeEventSyncSummary? LastSummary { get; private set; }

    /// <summary>Why the last cycle failed, or null when the last one worked.</summary>
    public string? LastError { get; private set; }

    /// <summary>True while a cycle is in flight.</summary>
    public bool IsSyncing => Volatile.Read(ref _cycleInFlight) == 1;

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

        return Task.Run(RunGuardedAsync, CancellationToken.None);
    }

    /// <summary>All three gates: the <c>sync</c> feature every loop answers to, the
    /// <c>events</c> area this feed belongs to, and a paired device to replicate under.</summary>
    private bool ShouldRun =>
        _features.IsEnabled(SyncFeatures.Sync)
        && _features.IsEnabled(EventsFeatures.Events)
        && _credentials.Current is not null;

    private void OnGateChanged() => ApplyGates();

    /// <summary>An event or a series was saved here: arm the settle timer, or push it back if
    /// it is already armed. Gated as a tick is, so a write with a gate shut arms nothing.</summary>
    private void OnLocalChange()
    {
        lock (_gate)
        {
            if (_disposed || !ShouldRun) return;

            if (_settle is null)
            {
                _settle = _time.CreateTimer(_ => OnSettled(), state: null, ChangeSettleDelay, Timeout.InfiniteTimeSpan);
            }
            else
            {
                _settle.Change(ChangeSettleDelay, Timeout.InfiniteTimeSpan);
            }
        }
    }

    /// <summary>The writes have stopped: run a cycle now, recorded first so a cycle already
    /// holding the guard runs one more on its way out.</summary>
    private void OnSettled()
    {
        Volatile.Write(ref _changeSettled, 1);
        RequestSync();
    }

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

            _timer ??= _time.CreateTimer(_ => OnTickElapsed(), state: null, FirstCycleDelay, CyclePeriod);
        }
    }

    private async void OnTickElapsed()
    {
        try
        {
            await RunGuardedAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "A knowledge event sync tick failed outside the cycle it was starting.");
        }
    }

    private async Task RunGuardedAsync()
    {
        try
        {
            await RunCycleAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "A knowledge event sync cycle failed unexpectedly.");
        }
    }

    private async Task RunCycleAsync()
    {
        if (!ShouldRun) return;

        if (Interlocked.CompareExchange(ref _cycleInFlight, 1, 0) != 0) return;

        Changed?.Invoke();

        try
        {
            // Inside the guard and before anything is read, so this cycle's push carries every
            // write that fired the settle timer.
            Interlocked.Exchange(ref _changeSettled, 0);

            var session = ResolveSession();

            if (session is null) return;

            var result = await RefusedTokenRerun.RunAsync(
                _services, () => session.SyncAsync(_lifetime.Token), _log, "knowledge event").ConfigureAwait(false);

            if (result.IsSuccess)
            {
                LastSummary = result.Value;
                LastError = null;
            }
            else
            {
                LastError = result.Error.Message;
                _log.LogInformation(
                    "A knowledge event sync cycle did not complete: {Code} {Message}", result.Error.Code, result.Error.Message);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // The window closed mid-exchange.
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "A knowledge event sync cycle threw.");
            LastError = UnexpectedFailure;
        }
        finally
        {
            Volatile.Write(ref _cycleInFlight, 0);
            Changed?.Invoke();

            // A write that settled while this cycle held the guard: its request was turned
            // away, so it runs now rather than on the next tick.
            if (Volatile.Read(ref _changeSettled) == 1) RequestSync();
        }
    }

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
            _log.LogInformation(ex, "Knowledge event sync is not composed in this host, so the loop has nothing to do.");
            return null;
        }
    }
}
