namespace Backlog.Modules.Notifications.Abstractions;

/// <summary>
/// The boundary that raised a notification and the key inside it, for example
/// <c>sessions</c> / <c>run-58</c> (<c>.devbook/domain/notifications/domain.md#source</c>).
/// The accepted answer goes back to the <see cref="INotificationAnswerHandler"/> registered
/// for <see cref="Context"/>.
/// </summary>
public sealed record NotificationSource(string Context, string Key);

/// <summary>
/// Where a notification came from, in words a person reads
/// (<c>.devbook/domain/notifications/domain.md#origin</c>): the system that raised it, where in
/// that system, who asked and how, and a link that opens it there. Optional beside
/// <see cref="NotificationSource"/>, which routes the answer and is never shown; a notification
/// without one is drawn as it always was, by its source.
/// <para>
/// All text, one line each, and bounded: <see cref="System"/> 40 characters, <see cref="Place"/>
/// 120, <see cref="AskedBy"/> 80, <see cref="Via"/> 40. <see cref="OpenLink"/> is an absolute
/// <c>http</c> or <c>https</c> address, or null. The owner refuses a draft whose origin breaks a
/// bound; a source builds its origin through <see cref="Fit"/>, which cuts each part to fit.
/// </para>
/// </summary>
/// <param name="System">The system that raised it — "Claude Code", "Backlog", "spec-manager". Required.</param>
/// <param name="Place">Where in that system — a repository and session title, a product and item.</param>
/// <param name="AskedBy">The person who asked, when known.</param>
/// <param name="Via">How it was asked — "Agent session", "MCP ask".</param>
/// <param name="OpenLink">A link that opens it at its origin, or null.</param>
public sealed record NotificationOrigin(
    string System,
    string? Place = null,
    string? AskedBy = null,
    string? Via = null,
    string? OpenLink = null)
{
    /// <summary>The most characters <see cref="System"/> holds.</summary>
    public const int MaxSystemLength = 40;

    /// <summary>The most characters <see cref="Place"/> holds.</summary>
    public const int MaxPlaceLength = 120;

    /// <summary>The most characters <see cref="AskedBy"/> holds.</summary>
    public const int MaxAskedByLength = 80;

    /// <summary>The most characters <see cref="Via"/> holds.</summary>
    public const int MaxViaLength = 40;

    /// <summary>The most characters <see cref="OpenLink"/> holds.</summary>
    public const int MaxOpenLinkLength = 2048;

    /// <summary>What sits between the system and the place in <see cref="Line"/>.</summary>
    public const string Separator = " · ";

    /// <summary>"System · Place", or the system alone — the attribution line of a Windows
    /// notification, the phone's origin line and the push's <c>origin</c>.</summary>
    public string Line => string.IsNullOrWhiteSpace(Place) ? System : System + Separator + Place;

    /// <summary><see cref="Line"/> cut to <paramref name="length"/> characters, the last one an
    /// ellipsis when it was cut.</summary>
    public string LineWithin(int length) => Cut(Line, length);

    /// <summary>
    /// An origin built to fit: every part made one line, trimmed, and cut to its bound; a blank
    /// part dropped, and a link that is not an absolute <c>http</c> or <c>https</c> address
    /// dropped. Null when <paramref name="system"/> is blank, since an origin names its system.
    /// </summary>
    public static NotificationOrigin? Fit(string? system, string? place = null, string? askedBy = null, string? via = null, string? openLink = null)
    {
        var named = Part(system, MaxSystemLength);
        if (named is null) return null;

        return new NotificationOrigin(
            named,
            Part(place, MaxPlaceLength),
            Part(askedBy, MaxAskedByLength),
            Part(via, MaxViaLength),
            IsOpenLink(openLink) ? openLink!.Trim() : null);
    }

    /// <summary>Whether <paramref name="link"/> may be an <see cref="OpenLink"/>: an absolute
    /// <c>http</c> or <c>https</c> address within <see cref="MaxOpenLinkLength"/>.</summary>
    public static bool IsOpenLink(string? link) =>
        !string.IsNullOrWhiteSpace(link)
        && link.Trim().Length <= MaxOpenLinkLength
        && Uri.TryCreate(link.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    private static string? Part(string? text, int length)
    {
        var line = string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return line.Length == 0 ? null : Cut(line, length);
    }

    private static string Cut(string text, int length) =>
        text.Length <= length ? text : text[..(length - 1)] + "…";
}

/// <summary>The words the sources inside Backlog put in a <see cref="NotificationOrigin"/>, so
/// two sources that mean the same system say it the same way.</summary>
public static class NotificationOrigins
{
    /// <summary>Backlog itself: pairing, Devbook remarks, the settings test.</summary>
    public const string Backlog = "Backlog";

    /// <summary>A Claude Code session, as a delivery run reports it.</summary>
    public const string ClaudeCode = "Claude Code";

    /// <summary>A spec-manager installation: a question parked on a backlog item or a change.</summary>
    public const string SpecManager = "spec-manager";

    /// <summary>Asked by an agent in a session.</summary>
    public const string AgentSession = "Agent session";

    /// <summary>Asked through the MCP <c>ask</c> or <c>notify</c> tool.</summary>
    public const string McpAsk = "MCP ask";
}

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
/// single choice (two to eight <see cref="Choices"/>), multiple choice
/// (<see cref="AllowMultiple"/>, within <see cref="MinChoices"/> and <see cref="MaxChoices"/>),
/// open (no choices, an <see cref="Open"/> answer), or choices with open (one choice, or
/// the open answer instead). Any shape may also take a <see cref="Note"/> beside its answer.
/// </summary>
/// <param name="Choices">The set answers; empty for an open question.</param>
/// <param name="AllowMultiple">Whether a reply may pick several choices.</param>
/// <param name="Open">The open answer, or null when only set choices are accepted.</param>
/// <param name="MinChoices">A multiple-choice question's least number of picks; one when null.</param>
/// <param name="MaxChoices">A multiple-choice question's most picks; every choice when null.</param>
/// <param name="AnswerOn">Whether any paired viewer may answer, or only the owner instance.</param>
/// <param name="Note">The optional plain-text note a reply may add beside its choice or open
/// answer, with its placeholder and length limit; null when the question takes no note. A note
/// never answers on its own: the reply's choices or open text still have to fit.</param>
public sealed record AnswerSpec(
    IReadOnlyList<AnswerChoice> Choices,
    bool AllowMultiple = false,
    OpenAnswer? Open = null,
    int? MinChoices = null,
    int? MaxChoices = null,
    AnswerOn AnswerOn = AnswerOn.AnyInstance,
    OpenAnswer? Note = null);

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
/// <param name="Origin">Where it came from, in words a person reads, or null.</param>
/// <param name="Id">The id the source names it by, or null for a fresh one. A source that asks
/// about something with an identity of its own — a question in another system — derives the id
/// from it, so publishing the same thing twice is one notification: an id this instance already
/// holds publishes nothing and answers that id, and an id another desktop already put on the
/// replica stays that desktop's (<c>.devbook/domain/notifications/domain.md#notification</c>).</param>
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
    bool IsPrivate = false,
    NotificationOrigin? Origin = null,
    Guid? Id = null);

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
/// <param name="Note">The note written beside the answer, or null; only where the answer spec
/// takes a <see cref="AnswerSpec.Note"/>.</param>
public sealed record NotificationReply(
    Guid Id,
    Guid NotificationId,
    Guid OwnerInstanceId,
    Guid FromDeviceId,
    IReadOnlyList<string> ChoiceIds,
    string? Text,
    DateTimeOffset RepliedAt,
    string? Note = null);

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
/// <param name="Note">The note written beside the answer, or null.</param>
public sealed record NotificationAnswer(
    Guid NotificationId,
    NotificationSource Source,
    Guid ReplyId,
    Guid FromDeviceId,
    IReadOnlyList<string> ChoiceIds,
    string? Text,
    DateTimeOffset AnsweredAt,
    string? Note = null);
