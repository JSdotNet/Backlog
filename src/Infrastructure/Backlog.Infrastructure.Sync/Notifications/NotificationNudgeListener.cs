using System.Text.Json;

using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.SharedKernel;
using Backlog.SharedKernel.Results;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Infrastructure.Sync.Notifications;

/// <summary>What the nudge listener drives: the notification loop, told to run now and whether
/// the socket is up. <see cref="NotificationSyncWorker"/> is the one implementation; the seam is
/// there so the listener's reconnect and fallback can be proved on their own.</summary>
public interface INotificationNudgeTarget
{
    /// <summary>Run a full exchange now — the nudge said something landed.</summary>
    void RequestSync();

    /// <summary>The socket opened or closed. While it is open the scheduled tick leaves the
    /// replies to the nudge; while it is closed the two-minute poll is the fallback.</summary>
    void SetNudgeOpen(bool open);
}

/// <summary>
/// The desktop's half of the realtime nudge (local ADR 0038 §4): it asks the sync service where
/// to listen, holds the WebSocket to the Web PubSub hub, and on every "pull now" has
/// <see cref="NotificationSyncWorker"/> run an exchange at once.
/// <para>
/// It only ever wakes the worker; it never carries a document. So every way it can fail costs
/// time and nothing else: a socket that drops, a hub that refuses, a sync service that runs
/// without Web PubSub (Aspire) — in each case the worker is told the socket is down and its
/// two-minute reply poll takes over until the listener is back.
/// </para>
/// <list type="bullet">
/// <item>Negotiate answers no URL: the service has no hub. Polling only; ask again in
/// <see cref="UnavailableRetry"/>.</item>
/// <item>Negotiate fails, or the socket does not open, or it drops within
/// <see cref="StableConnection"/>: retry after <see cref="FirstRetryDelay"/>, doubling up to
/// <see cref="MaxRetryDelay"/>, and back to the first delay once a socket has stayed open.</item>
/// </list>
/// <para>
/// It runs behind the worker's three gates — the <c>sync</c> feature, the <c>notifications</c>
/// feature, and a paired device — and closes its socket the moment one closes.
/// </para>
/// </summary>
public sealed class NotificationNudgeListener : IDisposable
{
    /// <summary>The first wait after a failed attempt or a dropped socket.</summary>
    internal static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(5);

    /// <summary>The longest wait between attempts. Past this the socket is not coming back
    /// soon, and the two-minute poll is doing the work meanwhile.</summary>
    internal static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(5);

    /// <summary>How long to wait before asking again when the service runs without a hub. It
    /// does not grow one at runtime, so there is no hurry; this catches a redeploy that adds
    /// one.</summary>
    internal static readonly TimeSpan UnavailableRetry = TimeSpan.FromMinutes(30);

    /// <summary>How long a socket has to stay open to count as having worked. One the hub
    /// closes sooner — a token it refuses after the upgrade, say — is retried on the growing
    /// wait, so a desktop does not renegotiate, and wake the sync service, every few seconds.</summary>
    internal static readonly TimeSpan StableConnection = TimeSpan.FromSeconds(30);

    private readonly Lock _gate = new();

    private readonly IServiceProvider _services;
    private readonly INotificationNudgeTarget _target;
    private readonly IAppFeatureSettings _features;
    private readonly IDeviceCredentialStore _credentials;
    private readonly INotificationNudgeSocketConnector _connector;
    private readonly TimeProvider _time;
    private readonly ILogger _log;

    private CancellationTokenSource? _running;
    private Task _loop = Task.CompletedTask;
    private int _open;
    private bool _disposed;

    public NotificationNudgeListener(
        IServiceProvider services,
        INotificationNudgeTarget target,
        IAppFeatureSettings features,
        IDeviceCredentialStore credentials,
        INotificationNudgeSocketConnector connector,
        TimeProvider time,
        ILogger<NotificationNudgeListener>? log = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(connector);
        ArgumentNullException.ThrowIfNull(time);

        _services = services;
        _target = target;
        _features = features;
        _credentials = credentials;
        _connector = connector;
        _time = time;
        _log = log ?? NullLogger<NotificationNudgeListener>.Instance;

        Delay = (delay, cancellationToken) => Task.Delay(delay, _time, cancellationToken);

        _features.Changed += OnGateChanged;
        _credentials.Changed += OnGateChanged;

        ApplyGates();
    }

    /// <summary>Raised whenever the socket opens or closes, on a thread pool thread.</summary>
    public event Action? Changed;

    /// <summary>True while the socket is open.</summary>
    public bool IsOpen => Volatile.Read(ref _open) == 1;

    /// <summary>The wait between attempts — <see cref="Task.Delay(TimeSpan, TimeProvider, CancellationToken)"/>
    /// on the injected clock, replaced by a test that wants to see each wait and decide when it
    /// ends. Set it while a gate is still closed: the loop reads it on every wait.</summary>
    internal Func<TimeSpan, CancellationToken, Task> Delay { get; set; }

    /// <summary>The running loop, so a test can wait for it to stop.</summary>
    internal Task Loop
    {
        get
        {
            lock (_gate) return _loop;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;

            _disposed = true;
            StopLocked();
        }

        _features.Changed -= OnGateChanged;
        _credentials.Changed -= OnGateChanged;
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
                StopLocked();
                return;
            }

            if (_running is not null) return;

            _running = new CancellationTokenSource();
            var token = _running.Token;
            _loop = Task.Run(() => RunAsync(token), CancellationToken.None);
        }
    }

    private void StopLocked()
    {
        if (_running is null) return;

        _running.Cancel();
        _running.Dispose();
        _running = null;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var retry = FirstRetryDelay;

        while (!cancellationToken.IsCancellationRequested)
        {
            TimeSpan wait;

            try
            {
                switch (await ListenOnceAsync(cancellationToken).ConfigureAwait(false))
                {
                    case Attempt.Unavailable:
                        wait = UnavailableRetry;
                        retry = FirstRetryDelay;
                        break;

                    case Attempt.Listened:
                        wait = FirstRetryDelay;
                        retry = Next(FirstRetryDelay);
                        break;

                    default:
                        wait = retry;
                        retry = Next(retry);
                        break;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Nothing here may end the loop: the next attempt is the recovery.
                _log.LogError(ex, "The notification nudge listener failed unexpectedly.");
                wait = retry;
                retry = Next(retry);
            }

            try
            {
                await Delay(wait, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private static TimeSpan Next(TimeSpan delay) =>
        delay * 2 > MaxRetryDelay ? MaxRetryDelay : delay * 2;

    private async Task<Attempt> ListenOnceAsync(CancellationToken cancellationToken)
    {
        var negotiated = await NegotiateAsync(cancellationToken).ConfigureAwait(false);

        if (negotiated.IsFailure)
        {
            _log.LogInformation(
                "The notification nudge could not be negotiated, so replies wait for the two-minute poll: {Code} {Message}",
                negotiated.Error.Code,
                negotiated.Error.Message);
            return Attempt.Failed;
        }

        if (negotiated.Value is null)
        {
            _log.LogInformation("The sync service runs without a nudge hub, so replies arrive on the two-minute poll.");
            return Attempt.Unavailable;
        }

        INotificationNudgeSocket socket;

        try
        {
            socket = await _connector.ConnectAsync(negotiated.Value, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _log.LogInformation(ex, "The notification nudge socket did not open, so replies wait for the two-minute poll.");
            return Attempt.Failed;
        }

        var openedAt = _time.GetTimestamp();

        await using (socket.ConfigureAwait(false))
        {
            SetOpen(true);

            try
            {
                while (await socket.ReceiveAsync(cancellationToken).ConfigureAwait(false) is { } message)
                {
                    if (IsPull(message)) _target.RequestSync();
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _log.LogInformation(ex, "The notification nudge socket dropped; reconnecting, and polling meanwhile.");
            }
            finally
            {
                SetOpen(false);
            }
        }

        return _time.GetElapsedTime(openedAt) >= StableConnection ? Attempt.Listened : Attempt.Failed;
    }

    private async Task<Result<Uri?>> NegotiateAsync(CancellationToken cancellationToken)
    {
        var client = _services.GetService<NotificationSyncClient>();

        if (client is null)
        {
            return Result.Failure<Uri?>(Error.Conflict(
                "sync.not_composed", "Notification sync is not composed in this host."));
        }

        var result = await RefusedTokenRerun.RunAsync(
            _services, () => client.NegotiateNudgeAsync(cancellationToken), _log, "notification nudge").ConfigureAwait(false);

        return result.IsSuccess
            ? Result.Success(result.Value.Url)
            : Result.Failure<Uri?>(result.Error);
    }

    private void SetOpen(bool open)
    {
        Volatile.Write(ref _open, open ? 1 : 0);
        _target.SetNudgeOpen(open);
        Changed?.Invoke();
    }

    /// <summary>Whether a message is the one kind the hub carries. Anything else is ignored
    /// rather than acted on: the socket only ever wakes the worker.</summary>
    internal static bool IsPull(string message)
    {
        try
        {
            var nudge = JsonSerializer.Deserialize<NotificationNudgeMessage>(message, JsonSerializerOptions.Web);
            return string.Equals(nudge?.Type, NotificationNudgeMessage.Pull, StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private enum Attempt
    {
        Failed,
        Unavailable,
        Listened,
    }
}
