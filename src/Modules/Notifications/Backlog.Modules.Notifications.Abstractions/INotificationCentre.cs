using Backlog.SharedKernel;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Notifications.Abstractions;

/// <summary>
/// What the owner instance's screens read and answer through: the notification centre in the
/// shell and the operating system's notifications (<c>.devbook/domain/notifications/context.md</c>).
/// Both answer through <see cref="AnswerAsync"/>, which hands this device's reply to
/// <see cref="INotificationReplies.AcceptReplyAsync"/> — the one path every reply takes, from
/// whichever channel it was given.
/// <para>
/// Registered on an owner instance only. A viewer reads the replica instead, which is the sync
/// exchange's work.
/// </para>
/// </summary>
public interface INotificationCentre
{
    /// <summary>The device this centre answers from, and names an answer given here by.</summary>
    DeviceIdentity ThisDevice { get; }

    /// <summary>
    /// Every open notification, and every one settled in the last <see cref="SettledWindow"/>,
    /// newest first. A notification the store holds but this build cannot read is left out.
    /// </summary>
    Task<IReadOnlyList<NotificationView>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Answers a question from this device: picks <paramref name="choiceIds"/>, or writes
    /// <paramref name="text"/>, or both where the question takes both. Rejected for the reasons
    /// <see cref="INotificationReplies.AcceptReplyAsync"/> gives, with its error codes, so a
    /// person can answer again.
    /// </summary>
    Task<Result<NotificationAnswer>> AnswerAsync(
        Guid notificationId,
        IReadOnlyList<string> choiceIds,
        string? text,
        CancellationToken cancellationToken = default);

    /// <summary>Acknowledges an open alert, through <see cref="INotificationReplies.AcknowledgeAsync"/>.</summary>
    Task<Result> AcknowledgeAsync(Guid notificationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Raised after any notification this instance owns is stored — published, answered,
    /// acknowledged or closed, from any channel. Raised on the thread that stored it; a screen
    /// marshals to its own.
    /// </summary>
    event EventHandler<NotificationChanged>? Changed;
}

/// <summary>One stored change to a notification: which one, where it now stands, and whether this
/// was the write that published it.</summary>
/// <param name="Notification">The notification as it was stored.</param>
/// <param name="IsNew">True for the first write of a notification — the one that published it.</param>
public sealed record NotificationChanged(NotificationView Notification, bool IsNew);

/// <summary>
/// A notification as a screen reads it: the draft it was published from, where it stands, and how
/// it was settled (<c>.devbook/domain/notifications/domain.md#notification</c>).
/// </summary>
public sealed record NotificationView(
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
    NotificationAnswer? Answer,
    NotificationOrigin? Origin = null)
{
    /// <summary>How long a settled notification stays in the centre.</summary>
    public static readonly TimeSpan SettledWindow = TimeSpan.FromDays(7);

    public bool IsOpen => State == NotificationState.Open;

    /// <summary>Whether it is an open question — what the shell's badge counts.</summary>
    public bool IsOpenQuestion => IsOpen && Kind == NotificationKind.Question;
}
