using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Notifications.Abstractions;

/// <summary>
/// Where one notification stands, as a publisher that polls reads it: who raised it, what it is
/// about, its state, the reason it was closed with, and its accepted answer. Never its content —
/// a publisher already holds what it wrote.
/// </summary>
/// <param name="Id">The notification's id.</param>
/// <param name="Source">Who raised it.</param>
/// <param name="Subject">What it is about, or null for an alert about nothing in particular.</param>
/// <param name="Kind">Alert or question.</param>
/// <param name="State">Open, acknowledged, answered or closed.</param>
/// <param name="CloseReason">The reason it was closed with, or null.</param>
/// <param name="Answer">The accepted answer, or null while it has none.</param>
public sealed record NotificationStatus(
    Guid Id,
    NotificationSource Source,
    NotificationSubject? Subject,
    NotificationKind Kind,
    NotificationState State,
    string? CloseReason,
    NotificationAnswer? Answer);

/// <summary>
/// The read a publisher that polls needs beside <see cref="INotifications"/>: where a
/// notification it published stands (<c>.devbook/domain/notifications/features.md#publishing-a-notification</c>).
/// The MCP <c>get_answer</c> and <c>close</c> tools read it to tell an open question from a closed
/// one, and to refuse a notification another source raised.
/// <para>
/// On a viewer instance every call is refused with <c>notifications.viewer_only</c>.
/// </para>
/// </summary>
public interface INotificationStatuses
{
    /// <summary>Where notification <paramref name="notificationId"/> stands. Refused for an
    /// unknown id (<c>notification.not_found</c>).</summary>
    Task<Result<NotificationStatus>> GetStatusAsync(Guid notificationId, CancellationToken cancellationToken = default);
}
