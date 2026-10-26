namespace Backlog.Modules.Notifications.Abstractions;

/// <summary>
/// The boundary that raised a notification and the key inside it, for example
/// <c>sessions</c> / <c>run-58</c> (<c>.devbook/domain/notifications/domain.md#source</c>).
/// The accepted answer goes back to the <see cref="INotificationAnswerHandler"/> registered
/// for <see cref="Context"/>.
/// </summary>
public sealed record NotificationSource(string Context, string Key);

/// <summary>
/// The entity a notification is about, named by its boundary, its type and its id, for
/// example <c>sessions</c> / <c>session</c> / <c>sess-91</c>
/// (<c>.devbook/domain/notifications/domain.md#subject</c>). Two subjects are the same when
/// all three parts are equal, compared ordinally.
/// </summary>
public sealed record NotificationSubject(string Context, string Type, string Id);

/// <summary>One set answer a question offers: its id, the label a person reads, and how it
/// is drawn.</summary>
public sealed record AnswerChoice(string Id, string Label, AnswerChoiceStyle Style = AnswerChoiceStyle.Default);

/// <summary>
/// The plain-text answer a question takes, with an optional placeholder and a length
/// limit. Markdown is not allowed in an answer; the text is kept and shown as written.
/// </summary>
public sealed record OpenAnswer(string? Placeholder = null, int MaxLength = OpenAnswer.DefaultMaxLength)
{
    /// <summary>The length an open answer may run to when the question names none.</summary>
    public const int DefaultMaxLength = 2000;
}

/// <summary>
/// What makes a question a question: the answers the owner will accept
/// (<c>.devbook/domain/notifications/domain.md#answer-spec</c>). One of four shapes —
/// single choice (two to six <see cref="Choices"/>), multiple choice
/// (<see cref="AllowMultiple"/>, within <see cref="MinChoices"/> and <see cref="MaxChoices"/>),
/// open (no choices, an <see cref="Open"/> answer), or choices with open (one choice, or
/// the open answer instead).
/// </summary>
/// <param name="Choices">The set answers; empty for an open question.</param>
/// <param name="AllowMultiple">Whether a reply may pick several choices.</param>
/// <param name="Open">The open answer, or null when only set choices are accepted.</param>
/// <param name="MinChoices">A multiple-choice question's least number of picks; one when null.</param>
/// <param name="MaxChoices">A multiple-choice question's most picks; every choice when null.</param>
/// <param name="AnswerOn">Whether any paired viewer may answer, or only the owner instance.</param>
public sealed record AnswerSpec(
    IReadOnlyList<AnswerChoice> Choices,
    bool AllowMultiple = false,
    OpenAnswer? Open = null,
    int? MinChoices = null,
    int? MaxChoices = null,
    AnswerOn AnswerOn = AnswerOn.AnyInstance);

/// <summary>
/// What a source hands <see cref="INotifications.PublishAsync"/>
/// (<c>.devbook/domain/notifications/domain.md#notification</c>). There is no deadline and
/// no default answer, on purpose: time alone never settles a question.
/// </summary>
/// <param name="Source">Who asks.</param>
/// <param name="Subject">What it is about; required for a question, optional for an alert.</param>
/// <param name="Kind">Alert or question.</param>
/// <param name="Severity">Info, warning or critical.</param>
/// <param name="Title">Plain text, one line, at most 80 characters.</param>
/// <param name="Summary">Plain text, at most 180 characters.</param>
/// <param name="BodyMarkdown">Markdown, at most 32 KB.</param>
/// <param name="Answer">The answer spec; required for a question, absent on an alert.</param>
/// <param name="DeepLink">A link that opens the item that raised it, or null.</param>
/// <param name="IsPrivate">Replace the summary with a fixed text outside the app.</param>
public sealed record NotificationDraft(
    NotificationSource Source,
    NotificationSubject? Subject,
    NotificationKind Kind,
    NotificationSeverity Severity,
    string Title,
    string Summary,
    string BodyMarkdown,
    AnswerSpec? Answer = null,
    string? DeepLink = null,
    bool IsPrivate = false);

/// <summary>
/// What a person submitted to answer a question, from any instance
/// (<c>.devbook/domain/notifications/domain.md#reply</c>). A proposal until the owner
/// accepts it.
/// </summary>
/// <param name="Id">The reply's own id.</param>
/// <param name="NotificationId">The question it answers.</param>
/// <param name="OwnerInstanceId">The owner instance the reply is addressed to.</param>
/// <param name="FromDeviceId">The device the person replied on.</param>
/// <param name="ChoiceIds">The chosen choice ids; empty for an open answer.</param>
/// <param name="Text">The open answer's text, or null.</param>
/// <param name="RepliedAt">When the person replied.</param>
public sealed record NotificationReply(
    Guid Id,
    Guid NotificationId,
    Guid OwnerInstanceId,
    Guid FromDeviceId,
    IReadOnlyList<string> ChoiceIds,
    string? Text,
    DateTimeOffset RepliedAt);

/// <summary>
/// The reply the owner accepted (<c>.devbook/domain/notifications/domain.md#answer</c>). A
/// question has at most one. A handler receives it at least once and treats a second
/// delivery for the same <see cref="NotificationId"/> as the same answer.
/// </summary>
/// <param name="NotificationId">The question it answers — the idempotency key.</param>
/// <param name="Source">The source that asked, whose handler receives it.</param>
/// <param name="ReplyId">The reply the owner accepted.</param>
/// <param name="FromDeviceId">The device the person answered on.</param>
/// <param name="ChoiceIds">The chosen choice ids; empty for an open answer.</param>
/// <param name="Text">The open answer's text, or null.</param>
/// <param name="AnsweredAt">When the owner accepted it.</param>
public sealed record NotificationAnswer(
    Guid NotificationId,
    NotificationSource Source,
    Guid ReplyId,
    Guid FromDeviceId,
    IReadOnlyList<string> ChoiceIds,
    string? Text,
    DateTimeOffset AnsweredAt);
