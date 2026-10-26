using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Notifications.Abstractions;

/// <summary>
/// Every way publishing, closing or answering a notification can be refused, named once. The
/// codes are the ones <c>.devbook/domain/notifications/domain.invariants.md</c> gives each rule,
/// so a screen or a viewer can match on them rather than on a message.
/// </summary>
public static class NotificationsErrors
{
    // --- Notification (.devbook/domain/notifications/domain.invariants.md#notification) ---

    public static Error NotOwner() =>
        Error.Conflict("notification.not_owner", "Only the instance that published a notification changes it.");

    public static Error QuestionIncomplete() =>
        Error.Validation("notification.question_incomplete", "A question needs an answer spec and a subject.");

    public static Error AlertWithAnswerSpec() =>
        Error.Validation("notification.alert_with_answer_spec", "An alert carries no answer spec.");

    public static Error UnknownKind() =>
        Error.Validation("notification.unknown_kind", "A notification is an alert or a question.");

    public static Error UnknownSeverity() =>
        Error.Validation("notification.unknown_severity", "A notification's severity is info, warning or critical.");

    public static Error AlreadySettled(NotificationState state) =>
        Error.Conflict("notification.already_settled", $"The notification is already {state.ToString().ToLowerInvariant()}.");

    public static Error WrongKind(NotificationKind kind) =>
        Error.Conflict(
            "notification.wrong_kind",
            kind == NotificationKind.Alert ? "An alert is acknowledged, not answered." : "A question is answered, not acknowledged.");

    public static Error AlreadyAnswered() =>
        Error.Conflict("notification.already_answered", "The question is already answered.");

    public static Error ContentTooLong(string field, int limit, string unit) =>
        Error.Validation("notification.content_too_long", $"The {field} holds at most {limit} {unit}.");

    public static Error TitleRequired() =>
        Error.Validation("notification.title_required", "A notification needs a one-line title.");

    public static Error SourceRequired() =>
        Error.Validation("notification.source_required", "A notification names its source context and key.");

    public static Error SubjectIncomplete() =>
        Error.Validation("notification.subject_incomplete", "A subject is named by its context, its type and its id.");

    public static Error OriginInvalid(string reason) =>
        Error.Validation("notification.origin_invalid", reason);

    public static Error OtherSubject() =>
        Error.Validation("notification.other_subject", "The notification is about another subject.");

    public static Error InvalidAnswerSpec(string reason) =>
        Error.Validation("notification.invalid_answer_spec", reason);

    public static Error NotFound(Guid notificationId) =>
        Error.NotFound("notification.not_found", $"There is no notification {notificationId}.");

    // --- Reply (.devbook/domain/notifications/domain.invariants.md#reply) -----------------

    public static Error ReplyInvalid(string reason) =>
        Error.Validation("reply.invalid", reason);

    public static Error ReplyClosed() =>
        Error.Conflict("reply.closed", "The question was closed, so it takes no answer.");

    public static Error ReplyOwnerOnly() =>
        Error.Conflict("reply.owner_only", "This question is answered only on the desktop that asked it.");

    /// <summary>A reply from a device under another account. Checked by the sync service and
    /// again by the owner once the replica carries the account (ADR 0039 §2).</summary>
    public static Error ReplyForeignOwner() =>
        Error.Conflict("reply.foreign_owner", "The reply comes from a device under another owner.");

    public static Error ReplyWrongInstance() =>
        Error.Conflict("reply.wrong_instance", "The reply is addressed to another owner instance.");

    // --- The module ----------------------------------------------------------------------

    public static Error ViewerOnly() =>
        Error.Conflict("notifications.viewer_only", "This device shows notifications and never publishes or changes one.");

    public static Error NotAvailable() =>
        Error.Conflict("notifications.not_available", "Notifications are switched off on this device.");
}
