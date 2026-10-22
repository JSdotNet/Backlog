namespace Backlog.Modules.Notifications.Abstractions;

/// <summary>
/// What the sync exchange reads off the owner instance to carry its notifications to the
/// other devices (local ADR 0038, <c>.devbook/domain/notifications/context.md</c>): the open
/// notifications, and any one notification as it stands. Read-only — a reply goes back in
/// through <see cref="INotificationReplies"/>, so the owner's gate stays the one every change
/// passes through.
/// <para>
/// Registered on an owner instance only. A viewer owns nothing to carry.
/// </para>
/// </summary>
public interface INotificationReplication
{
    /// <summary>Every Open notification this instance owns, oldest first.</summary>
    Task<IReadOnlyList<NotificationSnapshot>> ListOpenAsync(CancellationToken cancellationToken = default);

    /// <summary>One notification as it stands, or null when this instance holds none with
    /// that id.</summary>
    Task<NotificationSnapshot?> GetAsync(Guid notificationId, CancellationToken cancellationToken = default);
}

/// <summary>
/// One notification as the owner holds it, whole: what a viewer needs to show it and to answer
/// it, and where it stands (<c>.devbook/domain/notifications/domain.md#notification</c>).
/// </summary>
/// <param name="Id">The notification's id.</param>
/// <param name="OwnerInstanceId">The installation that published it.</param>
/// <param name="Source">Who asked.</param>
/// <param name="Subject">What it is about, or null for an alert about nothing.</param>
/// <param name="Kind">Alert or question.</param>
/// <param name="Severity">Info, warning or critical.</param>
/// <param name="Title">Plain text, at most 80 characters.</param>
/// <param name="Summary">Plain text, at most 180 characters.</param>
/// <param name="BodyMarkdown">Markdown, at most 32 KB.</param>
/// <param name="AnswerSpec">What a question accepts; null on an alert.</param>
/// <param name="DeepLink">What it opens, or null.</param>
/// <param name="IsPrivate">Whether the summary is replaced outside the app.</param>
/// <param name="CreatedAt">When it was published.</param>
/// <param name="State">Where it stands.</param>
/// <param name="SettledAt">When it left Open, or null.</param>
/// <param name="CloseReason">Why it was closed, or null.</param>
/// <param name="Answer">The accepted answer, or null.</param>
public sealed record NotificationSnapshot(
    Guid Id,
    Guid OwnerInstanceId,
    NotificationSource Source,
    NotificationSubject? Subject,
    NotificationKind Kind,
    NotificationSeverity Severity,
    string Title,
    string Summary,
    string BodyMarkdown,
    AnswerSpec? AnswerSpec,
    string? DeepLink,
    bool IsPrivate,
    DateTimeOffset CreatedAt,
    NotificationState State,
    DateTimeOffset? SettledAt,
    string? CloseReason,
    NotificationAnswer? Answer);
