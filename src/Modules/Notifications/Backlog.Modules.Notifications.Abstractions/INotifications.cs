using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Notifications.Abstractions;

/// <summary>
/// The one port every context tells the person something, or asks them something, through
/// (<c>.devbook/arc42/08-crosscutting-concepts.md#notifications</c>, local ADR 0038). The
/// desktop that publishes becomes the notification's owner instance, and only it changes the
/// notification afterwards.
/// <para>
/// On a viewer instance — the phone — every call is refused with
/// <c>notifications.viewer_only</c>: a viewer never publishes and never changes a notification.
/// </para>
/// </summary>
public interface INotifications
{
    /// <summary>
    /// Publishes an alert or a question owned by this instance, and answers its id. Refused
    /// when the draft breaks a rule of <c>.devbook/domain/notifications/domain.invariants.md</c>
    /// — a question without a subject or an answer spec (<c>notification.question_incomplete</c>),
    /// content past its bounds (<c>notification.content_too_long</c>) — or when the
    /// <see cref="NotificationsFeatures.Notifications"/> flag is off on this device
    /// (<c>notifications.not_available</c>). Nothing is stored when it is refused.
    /// </summary>
    Task<Result<Guid>> PublishAsync(NotificationDraft draft, CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes an open notification for the publisher, with an optional reason. A reply that
    /// reaches the owner afterwards is rejected as closed. Refused for one already settled
    /// (<c>notification.already_settled</c>) or unknown (<c>notification.not_found</c>).
    /// </summary>
    Task<Result> CloseAsync(Guid notificationId, string? reason = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes every open notification this instance owns about <paramref name="subject"/>,
    /// with <paramref name="reason"/>, and answers how many it closed. The context that owns
    /// the subject calls it when the subject ends; a subject that comes back later does not
    /// reopen them.
    /// </summary>
    Task<Result<int>> CloseForSubjectAsync(NotificationSubject subject, string reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// The accepted answer to a question, or null while it has none. Refused for an unknown id
    /// (<c>notification.not_found</c>). What an MCP <c>ask</c> polls.
    /// </summary>
    Task<Result<NotificationAnswer?>> GetAnswerAsync(Guid notificationId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Where a reply, or an acknowledgement, reaches the owner instance: from its own screen, and
/// later from the sync exchange that pulls viewers' replies
/// (<c>.devbook/arc42/06-runtime-view.md#notification-reply</c>). The owner checks the reply
/// against the question and accepts the first valid one; every later reply is rejected with
/// its reason, and the viewer can reply again.
/// </summary>
public interface INotificationReplies
{
    /// <summary>
    /// Accepts <paramref name="reply"/> as the question's answer, stores it, and hands it to the
    /// source's handler — or keeps it for a handler that registers later. Rejected with
    /// <c>reply.invalid</c>, <c>reply.closed</c>, <c>reply.owner_only</c>,
    /// <c>reply.wrong_instance</c> or <c>notification.already_answered</c>.
    /// </summary>
    Task<Result<NotificationAnswer>> AcceptReplyAsync(NotificationReply reply, CancellationToken cancellationToken = default);

    /// <summary>Acknowledges an open alert. Refused for a question
    /// (<c>notification.wrong_kind</c>) or one already settled.</summary>
    Task<Result> AcknowledgeAsync(Guid notificationId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Registered by each context that asks questions, for its source name
/// (<see cref="NotificationSource.Context"/>), with <c>AddNotificationAnswerHandler&lt;T&gt;()</c>.
/// An alert-only source registers none.
/// <para>
/// Delivery is at least once: the owner stores an accepted answer before it calls the handler,
/// and replays one the handler did not take — because it was not registered yet, or it threw —
/// on the next start. <see cref="HandleAnswerAsync"/> must therefore be idempotent on
/// <see cref="NotificationAnswer.NotificationId"/>.
/// </para>
/// <para>
/// Answers are handed over one at a time. A handler that wants to publish, close or answer
/// another notification in response starts that work rather than awaiting it inside
/// <see cref="HandleAnswerAsync"/>: an answer it causes cannot be handed over until it returns.
/// </para>
/// </summary>
public interface INotificationAnswerHandler
{
    /// <summary>The source context this handler answers for.</summary>
    string Source { get; }

    Task HandleAnswerAsync(NotificationAnswer answer, CancellationToken cancellationToken = default);
}

/// <summary>
/// Registered by each context that owns subjects, so the owner can catch a subject end it
/// missed: at startup it asks the probe for <see cref="Context"/> about every open
/// notification's subject, and closes the ones that are no longer live. Notifications never
/// reads the context's data itself.
/// </summary>
public interface INotificationSubjectProbe
{
    /// <summary>The subject context this probe answers for.</summary>
    string Context { get; }

    Task<bool> IsLiveAsync(NotificationSubject subject, CancellationToken cancellationToken = default);
}
