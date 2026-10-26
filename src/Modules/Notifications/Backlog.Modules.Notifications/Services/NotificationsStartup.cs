using Backlog.Modules.Notifications.Abstractions;
using Backlog.SharedKernel;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Modules.Notifications.Services;

/// <summary>
/// What the owner instance does once at startup: catch every subject end it missed while it was
/// not running, then hand over every stored answer no handler has taken yet.
/// <para>
/// For each open notification this instance owns, the <see cref="INotificationSubjectProbe"/>
/// registered for its subject's context is asked whether the subject still lives, and the
/// notifications about a subject that does not are closed. A subject with no probe, or whose
/// probe throws, is left open — Notifications never reads another context's data to decide for
/// it.
/// </para>
/// <para>
/// A plain singleton the host resolves and starts, rather than an <c>IHostedService</c>: the MAUI
/// head has no generic host to start one (see <c>TaskSyncWorker</c>).
/// </para>
/// </summary>
public sealed class NotificationsStartup
{
    /// <summary>The reason a notification whose subject a probe reports ended is closed with.</summary>
    public const string SubjectEndedReason = "Subject ended";

    private readonly INotificationRepository _repository;
    private readonly INotifications _notifications;
    private readonly IDeviceIdentitySource _device;
    private readonly AnswerDelivery _delivery;
    private readonly IReadOnlyList<INotificationSubjectProbe> _probes;
    private readonly ILogger _logger;
    private readonly Lock _startLock = new();
    private Task? _run;

    internal NotificationsStartup(
        INotificationRepository repository,
        INotifications notifications,
        IDeviceIdentitySource device,
        AnswerDelivery delivery,
        IEnumerable<INotificationSubjectProbe> probes,
        ILogger<NotificationsStartup>? logger = null)
    {
        _repository = repository;
        _notifications = notifications;
        _device = device;
        _delivery = delivery;
        _probes = [.. probes];
        _logger = logger ?? NullLogger<NotificationsStartup>.Instance;
    }

    /// <summary>Starts the startup pass once, on the thread pool, and answers it; a second call
    /// answers the same pass.</summary>
    public Task Start()
    {
        lock (_startLock)
        {
            return _run ??= Task.Run(() => RunAsync(CancellationToken.None));
        }
    }

    /// <summary>The probe pass, then the replay.</summary>
    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await CloseEndedSubjectsAsync(cancellationToken).ConfigureAwait(false);
            await _delivery.ReplayAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "The notifications startup pass did not finish; it runs again on the next start.");
        }
    }

    /// <summary>Closes every open notification this instance owns whose subject its context's
    /// probe reports ended, and answers how many it closed.</summary>
    internal async Task<int> CloseEndedSubjectsAsync(CancellationToken cancellationToken)
    {
        var me = _device.Current.Id;
        var open = await _repository.ListOpenAsync(cancellationToken).ConfigureAwait(false);

        var subjects = open
            .Where(notification => notification.OwnerInstanceId == me && notification.Subject is not null)
            .Select(notification => notification.Subject!)
            .Distinct()
            .ToList();

        var closed = 0;
        foreach (var subject in subjects)
        {
            var probe = _probes.FirstOrDefault(candidate => string.Equals(candidate.Context, subject.Context, StringComparison.Ordinal));
            if (probe is null) continue;

            bool live;
            try
            {
                live = await probe.IsLiveAsync(subject, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(exception, "The {Context} subject probe failed; its notifications stay open.", subject.Context);
                continue;
            }

            if (live) continue;

            var result = await _notifications.CloseForSubjectAsync(subject, SubjectEndedReason, cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess) closed += result.Value;
        }

        return closed;
    }
}
