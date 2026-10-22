using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Sync.Abstractions;
using Backlog.SharedKernel;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Infrastructure.Sync.Notifications;

/// <summary>
/// The schedule the notification exchange needs on an owner desktop: a timer that runs
/// <see cref="NotificationSyncSession.SyncAsync"/> every two minutes, so a question published
/// here reaches the phone and a reply made there reaches the source that asked (local ADR 0038
/// §4).
/// <para>
/// Two minutes is ADR 0038's fallback poll while a question is open. The session keeps the
/// "stay idle otherwise" half: a tick with no open question and nothing new to push sends no
/// request at all, so what a quiet desktop pays for this loop is one read of its own
/// <c>backlog.db</c> every two minutes.
/// </para>
/// <para>
/// The realtime nudge (ADR 0038 §4, <see cref="NotificationNudgeListener"/>) calls
/// <see cref="RequestSync"/> on every "pull now" to run a full cycle at once, and says through
/// <see cref="SetNudgeOpen"/> whether its socket is up. While it is, the tick still pushes what
/// was published here but leaves the replies to the nudge; while it is down — or on a host
/// with no Web PubSub — the tick polls them, which is the two-minute fallback.
/// </para>
/// <para>
/// A sibling of <c>AnnotationSyncWorker</c>, copied rather than shared for the reasons that one
/// records: the gates, the overlap guard, the resolve-per-cycle and the catch that keeps a tick
/// from ending the process. It runs only while three gates are open — the <c>sync</c> feature,
/// the <c>notifications</c> feature, and a paired device.
/// </para>
/// </summary>
public sealed class NotificationSyncWorker : IDisposable, INotificationNudgeTarget
{
    /// <summary>How long after the loop starts the first cycle runs, staggered behind the
    /// other loops.</summary>
    internal static readonly TimeSpan FirstCycleDelay = TimeSpan.FromSeconds(13);

    /// <summary>How often a cycle runs: the reply poll ADR 0038 sets while a question is open.</summary>
    internal static readonly TimeSpan CyclePeriod = TimeSpan.FromMinutes(2);

    public const string UnexpectedFailure = TaskSyncWorker.UnexpectedFailure;

    private readonly Lock _gate = new();

    private readonly IServiceProvider _services;
    private readonly IAppFeatureSettings _features;
    private readonly IDeviceCredentialStore _credentials;
    private readonly TimeProvider _time;
    private readonly ILogger _log;

    private readonly CancellationTokenSource _lifetime = new();

    private int _cycleInFlight;

    private int _nudgeOpen;

    /// <summary>Set when a full cycle was asked for while one was already in flight. That one
    /// may have started before the reply landed, or may be a tick that leaves the replies to the
    /// nudge, so dropping the request would leave the reply waiting while the socket stays up.
    /// It runs once more instead.</summary>
    private int _fullCycleOwed;

    private ITimer? _timer;
    private bool _disposed;

    public NotificationSyncWorker(
        IServiceProvider services,
        IAppFeatureSettings features,
        IDeviceCredentialStore credentials,
        TimeProvider time,
        ILogger<NotificationSyncWorker>? log = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(time);

        _services = services;
        _features = features;
        _credentials = credentials;
        _time = time;
        _log = log ?? NullLogger<NotificationSyncWorker>.Instance;

        _features.Changed += OnGateChanged;
        _credentials.Changed += OnGateChanged;

        ApplyGates();
    }

    /// <summary>Raised at both ends of a cycle, on a thread pool thread.</summary>
    public event Action? Changed;

    /// <summary>What the last exchange that succeeded did, or null when none has.</summary>
    public NotificationSyncSummary? LastSummary { get; private set; }

    /// <summary>Why the last cycle failed, or null when the last one worked.</summary>
    public string? LastError { get; private set; }

    /// <summary>True while a cycle is in flight.</summary>
    public bool IsSyncing => Volatile.Read(ref _cycleInFlight) == 1;

    /// <summary>True while the nudge socket is open, so the tick leaves the replies to it.</summary>
    public bool IsNudgeOpen => Volatile.Read(ref _nudgeOpen) == 1;

    /// <summary>Runs a full cycle now — replies included — and returns before it has done
    /// anything.</summary>
    public void RequestSync() => _ = RequestSyncAsync();

    /// <summary>
    /// What the nudge listener says about its socket. Opening it runs a full cycle at once,
    /// because a reply may have landed while it was down and its nudge went nowhere; closing it
    /// hands the replies back to the two-minute poll.
    /// </summary>
    public void SetNudgeOpen(bool open)
    {
        var was = Interlocked.Exchange(ref _nudgeOpen, open ? 1 : 0) == 1;

        if (open && !was) RequestSync();
    }

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

    /// <summary>What <see cref="RequestSync"/> discards, so a test can wait for it.</summary>
    internal Task RequestSyncAsync()
    {
        lock (_gate)
        {
            if (_disposed) return Task.CompletedTask;
        }

        return Task.Run(() => RunGuardedAsync(pollReplies: true), CancellationToken.None);
    }

    private bool ShouldRun =>
        _features.IsEnabled(SyncFeatures.Sync)
        && _features.IsEnabled(NotificationsFeatures.Notifications)
        && _credentials.Current is not null;

    private void OnGateChanged() => ApplyGates();

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
            await RunGuardedAsync(pollReplies: !IsNudgeOpen).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "A notification sync tick failed outside the cycle it was starting.");
        }
    }

    private async Task RunGuardedAsync(bool pollReplies)
    {
        try
        {
            await RunCycleAsync(pollReplies).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "A notification sync cycle failed unexpectedly.");
        }
    }

    private async Task RunCycleAsync(bool pollReplies)
    {
        if (!ShouldRun) return;

        if (Interlocked.CompareExchange(ref _cycleInFlight, 1, 0) != 0)
        {
            if (!pollReplies) return;

            Volatile.Write(ref _fullCycleOwed, 1);

            // The cycle in flight may have finished, and checked for an owed cycle, between the
            // exchange above and the write; if so, nobody else will collect it.
            if (Volatile.Read(ref _cycleInFlight) == 0 && Interlocked.Exchange(ref _fullCycleOwed, 0) == 1)
            {
                await RequestSyncAsync().ConfigureAwait(false);
            }

            return;
        }

        Changed?.Invoke();

        try
        {
            var session = ResolveSession();

            if (session is null) return;

            var result = await RefusedTokenRerun.RunAsync(
                _services, () => session.SyncAsync(pollReplies, _lifetime.Token), _log, "notification").ConfigureAwait(false);

            if (result.IsSuccess)
            {
                LastSummary = result.Value;
                LastError = null;
            }
            else
            {
                LastError = result.Error.Message;
                _log.LogInformation(
                    "A notification sync cycle did not complete: {Code} {Message}", result.Error.Code, result.Error.Message);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // The window closed mid-exchange.
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "A notification sync cycle threw.");
            LastError = UnexpectedFailure;
        }
        finally
        {
            Volatile.Write(ref _cycleInFlight, 0);
            Changed?.Invoke();
        }

        if (Interlocked.Exchange(ref _fullCycleOwed, 0) == 1)
        {
            await RequestSyncAsync().ConfigureAwait(false);
        }
    }

    /// <summary>A session for this cycle, where this host composed one. A viewer head, or one
    /// without the Notifications module, has nothing to carry.</summary>
    private NotificationSyncSession? ResolveSession()
    {
        try
        {
            return _services.GetService<NotificationSyncSession>();
        }
        catch (InvalidOperationException ex)
        {
            _log.LogInformation(ex, "Notification sync is not composed in this host, so the loop has nothing to do.");
            return null;
        }
    }
}
