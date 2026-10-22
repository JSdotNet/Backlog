using Backlog.Modules.Notifications.Abstractions;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Notifications.Services;

/// <summary>
/// A viewer instance's ports — the phone's. A viewer shows notifications and writes replies, and
/// never publishes or changes a notification (<c>.devbook/domain/notifications/domain.md#viewer-instance</c>),
/// so every call here is refused with <c>notifications.viewer_only</c>. What a viewer shows, and
/// how its replies travel, arrive with the sync exchange.
/// </summary>
internal sealed class ViewerNotifications : INotifications, INotificationReplies
{
    public Task<Result<Guid>> PublishAsync(NotificationDraft draft, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Failure<Guid>(NotificationsErrors.ViewerOnly()));

    public Task<Result> CloseAsync(Guid notificationId, string? reason = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Failure(NotificationsErrors.ViewerOnly()));

    public Task<Result<int>> CloseForSubjectAsync(NotificationSubject subject, string reason, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Failure<int>(NotificationsErrors.ViewerOnly()));

    public Task<Result<NotificationAnswer?>> GetAnswerAsync(Guid notificationId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Failure<NotificationAnswer?>(NotificationsErrors.ViewerOnly()));

    public Task<Result<NotificationAnswer>> AcceptReplyAsync(NotificationReply reply, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Failure<NotificationAnswer>(NotificationsErrors.ViewerOnly()));

    public Task<Result> AcknowledgeAsync(Guid notificationId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Failure(NotificationsErrors.ViewerOnly()));
}
