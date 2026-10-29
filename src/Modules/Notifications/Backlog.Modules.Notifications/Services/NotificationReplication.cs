using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Notifications.DomainModels;

namespace Backlog.Modules.Notifications.Services;

/// <summary>
/// The owner instance's <see cref="INotificationReplication"/>: the stored notifications, read
/// through the repository and handed out as snapshots, so the sync exchange never sees the
/// aggregate and never writes one.
/// </summary>
internal sealed class NotificationReplication(INotificationRepository repository) : INotificationReplication
{
    public async Task<IReadOnlyList<NotificationSnapshot>> ListOpenAsync(CancellationToken cancellationToken = default) =>
        [.. (await repository.ListOpenAsync(cancellationToken).ConfigureAwait(false)).Select(Snapshot)];

    public async Task<NotificationSnapshot?> GetAsync(Guid notificationId, CancellationToken cancellationToken = default) =>
        await repository.GetAsync(notificationId, cancellationToken).ConfigureAwait(false) is { } notification
            ? Snapshot(notification)
            : null;

    internal static NotificationSnapshot Snapshot(Notification notification) =>
        new(
            notification.Id,
            notification.OwnerInstanceId,
            notification.Source,
            notification.Subject,
            notification.Kind,
            notification.Severity,
            notification.Title,
            notification.Summary,
            notification.BodyMarkdown,
            notification.AnswerSpec,
            notification.DeepLink,
            notification.IsPrivate,
            notification.CreatedAt,
            notification.State,
            notification.SettledAt,
            notification.CloseReason,
            notification.Answer,
            notification.Origin);
}
