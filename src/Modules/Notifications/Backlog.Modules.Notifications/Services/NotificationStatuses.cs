using Backlog.Modules.Notifications.Abstractions;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Notifications.Services;

/// <summary>
/// The owner instance's <see cref="INotificationStatuses"/>: one stored notification, read without
/// the gate every change passes through, because a read changes nothing and a poll must never
/// wait behind a reply being accepted.
/// </summary>
internal sealed class OwnerNotificationStatuses(INotificationRepository repository) : INotificationStatuses
{
    public async Task<Result<NotificationStatus>> GetStatusAsync(Guid notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await repository.GetAsync(notificationId, cancellationToken).ConfigureAwait(false);
        if (notification is null) return NotificationsErrors.NotFound(notificationId);

        return new NotificationStatus(
            notification.Id,
            notification.Source,
            notification.Subject,
            notification.Kind,
            notification.State,
            notification.CloseReason,
            notification.Answer);
    }
}

/// <summary>A viewer instance's <see cref="INotificationStatuses"/>: refused, as every port is on
/// the phone.</summary>
internal sealed class ViewerNotificationStatuses : INotificationStatuses
{
    public Task<Result<NotificationStatus>> GetStatusAsync(Guid notificationId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Failure<NotificationStatus>(NotificationsErrors.ViewerOnly()));
}
