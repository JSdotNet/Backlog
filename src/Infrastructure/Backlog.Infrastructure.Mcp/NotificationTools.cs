using System.ComponentModel;

using Backlog.Modules.Notifications.Abstractions;
using Backlog.SharedKernel.Results;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Backlog.Infrastructure.Mcp;

/// <summary>
/// The tools a session tells the person something, or asks them something, through
/// (<c>.devbook/domain/notifications/features.md#publishing-a-notification</c>, local ADR 0039):
/// <c>notify</c> publishes an alert, <c>ask</c> a question, <c>get_answer</c> polls a question,
/// and <c>close</c> withdraws what the session published.
/// <para>
/// <b>The source is <c>mcp</c>, keyed by the call.</b> Every <c>notify</c> and <c>ask</c> is its
/// own source key, a fresh id, so no two calls share an answer. The subject is the calling Claude
/// session — <c>sessions</c> / <c>session</c> / the id — when the caller names one, so the context
/// that owns sessions can close them when the session ends; otherwise a question is about itself,
/// <c>mcp</c> / <c>ask</c> / the call's key, and an alert is about nothing in particular.
/// </para>
/// <para>
/// <b>The origin is the calling client.</b> What a person reads as where it came from is the MCP
/// client's own name — "Claude Code" for <c>claude-code</c> — asked "MCP ask"
/// (<c>.devbook/domain/notifications/domain.md#origin</c>). <see cref="Origin"/> says what it
/// falls back on where the stateless endpoint cannot name the client.
/// </para>
/// <para>
/// <b><c>ask</c> never blocks.</b> It answers the notification's id as soon as the question is
/// stored; the session polls <c>get_answer</c>. A question has no deadline and no default answer,
/// so a tool call waiting on a person would hold the session for as long as the person is away.
/// </para>
/// <para>
/// <b>Only what this server published.</b> <c>get_answer</c> and <c>close</c> answer
/// <c>notification.not_found</c> for a notification another context raised, as for one that does
/// not exist: a session reads and withdraws its own questions, never the pairing question Sync
/// asked. One published about a session answers only a caller that names that same session; for
/// one published without a session, its id is the only key, and whoever holds it may poll or
/// withdraw it.
/// </para>
/// <para>
/// <b>An instance per call</b>, for the reason <see cref="WorkTools"/> states.
/// </para>
/// </summary>
/// <param name="notifications">The Notifications port every publish and close goes through.</param>
/// <param name="statuses">Where a notification stands, for <c>get_answer</c> and <c>close</c>.</param>
[McpServerToolType]
public sealed class NotificationTools(INotifications notifications, INotificationStatuses statuses)
{
    internal const string Notify = "notify";
    internal const string Ask = "ask";
    internal const string GetAnswer = "get_answer";
    internal const string Close = "close";

    /// <summary>The source context every notification published here names.</summary>
    internal const string SourceContext = "mcp";

    /// <summary>The subject type of a question no session was named for: the question itself.</summary>
    internal const string AskSubjectType = "ask";

    /// <summary>The Sessions context and the subject type a calling session is named by.</summary>
    internal const string SessionsContext = "sessions";

    internal const string SessionSubjectType = "session";

    /// <summary>The <c>get_answer</c> status tokens.</summary>
    internal const string Pending = "pending";
    internal const string Answered = "answered";
    internal const string Acknowledged = "acknowledged";
    internal const string Closed = "closed";

    /// <summary>The severity tokens a call names, as the wire carries them.</summary>
    internal static readonly IReadOnlyDictionary<string, NotificationSeverity> Severities =
        new Dictionary<string, NotificationSeverity>(StringComparer.OrdinalIgnoreCase)
        {
            ["info"] = NotificationSeverity.Info,
            ["warning"] = NotificationSeverity.Warning,
            ["critical"] = NotificationSeverity.Critical,
        };

    [McpServerTool(Name = Notify, Title = "Tell the person something", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = false)]
    [Description(
        "Publishes an alert: something the person should know, on this desktop and on every paired device. Nothing "
        + "waits on it. Answers the notification's id, which close takes. Pass sessionId - your Claude session id - "
        + "so the alert is about your session; then pass it to close too. Calling it twice publishes two.")]
    public async Task<NotificationPublishedPayload> NotifyAsync(
        [Description("One line of plain text, at most 80 characters.")]
        string title,
        [Description("Plain text, at most 180 characters, shown under the title.")]
        string? summary = null,
        [Description("The full message as Markdown, at most 32 KB.")]
        string? body = null,
        [Description("info, warning or critical. Defaults to info.")]
        string? severity = null,
        [Description("Your Claude session id, so the alert is about this session. Optional.")]
        string? sessionId = null,
        CancellationToken cancellationToken = default,
        McpServer? server = null)
    {
        var key = NewKey();
        var draft = new NotificationDraft(
            new NotificationSource(SourceContext, key),
            SessionSubject(sessionId),
            NotificationKind.Alert,
            ReadSeverity(severity),
            title,
            summary ?? string.Empty,
            body ?? string.Empty,
            Origin: Origin(server?.ClientInfo, sessionId));

        var id = (await notifications.PublishAsync(draft, cancellationToken).ConfigureAwait(false)).ValueOrThrow();

        return new NotificationPublishedPayload(id);
    }

    [McpServerTool(Name = Ask, Title = "Ask the person a question", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = false)]
    [Description(
        "Asks the person a question they can answer on this desktop or on any paired device. Offer 2 to 8 choices, "
        + "an open plain-text answer, or both (one choice, or the open answer instead). allowMultiple lets them pick "
        + "several choices, and takes no open answer. Answers the notification's id at once and never waits for the "
        + "answer: poll get_answer with the id. There is no deadline and no default answer. Pass sessionId - your "
        + "Claude session id - so the question is about your session; then pass it to get_answer and close too. "
        + "Calling it twice asks twice.")]
    public async Task<NotificationPublishedPayload> AskAsync(
        [Description("The question, one line of plain text, at most 80 characters.")]
        string title,
        [Description("The choices, as the labels the person reads, e.g. [\"Yes\", \"No\"]. Each label is also the choice's id get_answer answers with. Omit for an open question.")]
        string[]? choices = null,
        [Description("True to let the person pick several choices.")]
        bool allowMultiple = false,
        [Description("True to accept a plain-text answer, instead of a choice or on its own.")]
        bool openAnswer = false,
        [Description("The hint shown in the empty open answer, e.g. \"Name the module\". Implies openAnswer.")]
        string? placeholder = null,
        [Description("Plain text, at most 180 characters, shown under the question.")]
        string? summary = null,
        [Description("The context the person needs to answer, as Markdown, at most 32 KB.")]
        string? body = null,
        [Description("info, warning or critical. Defaults to info.")]
        string? severity = null,
        [Description("Your Claude session id, so the question is about this session. Optional.")]
        string? sessionId = null,
        CancellationToken cancellationToken = default,
        McpServer? server = null)
    {
        var key = NewKey();
        var labels = (choices ?? []).Select(label => label?.Trim() ?? string.Empty).ToList();
        if (labels.Any(string.IsNullOrEmpty)) throw InvalidChoices("Every choice has a label.");
        if (labels.Distinct(StringComparer.OrdinalIgnoreCase).Count() != labels.Count) throw InvalidChoices("Every choice has its own label.");
        var open = openAnswer || !string.IsNullOrWhiteSpace(placeholder)
            ? new OpenAnswer(string.IsNullOrWhiteSpace(placeholder) ? null : placeholder.Trim())
            : null;

        var draft = new NotificationDraft(
            new NotificationSource(SourceContext, key),
            SessionSubject(sessionId) ?? new NotificationSubject(SourceContext, AskSubjectType, key),
            NotificationKind.Question,
            ReadSeverity(severity),
            title,
            summary ?? string.Empty,
            body ?? string.Empty,
            new AnswerSpec([.. labels.Select(label => new AnswerChoice(label, label))], allowMultiple, open),
            Origin: Origin(server?.ClientInfo, sessionId));

        var id = (await notifications.PublishAsync(draft, cancellationToken).ConfigureAwait(false)).ValueOrThrow();

        return new NotificationPublishedPayload(id);
    }

    [McpServerTool(Name = GetAnswer, Title = "Read the answer to a question", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Where a notification you published stands: pending while nobody has answered, answered with the chosen "
        + "choices and the open answer's text, closed with the reason it was closed with, or acknowledged for an "
        + "alert the person saw. Poll it after ask; once it is not pending it never changes again.")]
    public async Task<NotificationAnswerPayload> GetAnswerAsync(
        [Description("The notification's id, as ask or notify answered it.")]
        Guid id,
        [Description("Your Claude session id, when you passed one to ask or notify; required then.")]
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        var status = await OwnStatusAsync(id, sessionId, cancellationToken).ConfigureAwait(false);

        return NotificationAnswerPayload.From(status);
    }

    [McpServerTool(Name = Close, Title = "Withdraw a notification", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = false)]
    [Description(
        "Closes an open notification you published, with an optional reason, on every device: use it when you no "
        + "longer need the answer. A reply sent afterwards is rejected as closed. Refused for one already answered, "
        + "acknowledged or closed, and for one this server did not publish.")]
    public async Task<NotificationAnswerPayload> CloseAsync(
        [Description("The notification's id, as ask or notify answered it.")]
        Guid id,
        [Description("Why it is withdrawn, in words, e.g. \"Decided without it\". Optional.")]
        string? reason = null,
        [Description("Your Claude session id, when you passed one to ask or notify; required then.")]
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        _ = await OwnStatusAsync(id, sessionId, cancellationToken).ConfigureAwait(false);

        var closed = await notifications.CloseAsync(id, reason, cancellationToken).ConfigureAwait(false);
        if (closed.IsFailure) throw RepositoryScope.Failure(closed.Error);

        return NotificationAnswerPayload.From(
            (await statuses.GetStatusAsync(id, cancellationToken).ConfigureAwait(false)).ValueOrThrow());
    }

    internal static string ToWire(NotificationState state) => state switch
    {
        NotificationState.Open => Pending,
        NotificationState.Answered => Answered,
        NotificationState.Acknowledged => Acknowledged,
        NotificationState.Closed => Closed,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "A notification is open, acknowledged, answered or closed."),
    };

    /// <summary>The notification, when this server published it and — when it was published about a
    /// session — the caller names that session; otherwise the failure a missing one gets.</summary>
    private async Task<NotificationStatus> OwnStatusAsync(Guid id, string? sessionId, CancellationToken cancellationToken)
    {
        var status = (await statuses.GetStatusAsync(id, cancellationToken).ConfigureAwait(false)).ValueOrThrow();

        var published = string.Equals(status.Source.Context, SourceContext, StringComparison.Ordinal);
        var aboutASession = status.Subject is { Context: SessionsContext, Type: SessionSubjectType };
        var callers = !aboutASession || status.Subject == SessionSubject(sessionId);

        return published && callers ? status : throw RepositoryScope.Failure(NotificationsErrors.NotFound(id));
    }

    /// <summary>
    /// Where a <c>notify</c> or <c>ask</c> came from: the calling MCP client as it names itself —
    /// its title, else its name, with Claude Code's <c>claude-code</c> in words — asked
    /// "MCP ask".
    /// <para>
    /// The endpoint is stateless, so a client on a revision with the <c>initialize</c> handshake
    /// is not named on a later <c>tools/call</c>: only the 2026-07-28 revision carries the
    /// client per request. Then a call that names a Claude session — the <c>sessionId</c> every
    /// one of these tools asks a Claude session for — reads as Claude Code, and any other as
    /// <see cref="UnnamedClient"/>.
    /// </para>
    /// </summary>
    internal static NotificationOrigin? Origin(Implementation? client, string? sessionId = null)
    {
        var title = client?.Title;
        var name = string.IsNullOrWhiteSpace(title) ? client?.Name : title;
        var system = string.IsNullOrWhiteSpace(name)
            ? string.IsNullOrWhiteSpace(sessionId) ? UnnamedClient : NotificationOrigins.ClaudeCode
            : KnownClients.TryGetValue(name.Trim(), out var known) ? known : name;

        return NotificationOrigin.Fit(system, via: NotificationOrigins.McpAsk);
    }

    /// <summary>The system an MCP client that sent no name is shown as.</summary>
    internal const string UnnamedClient = "MCP client";

    /// <summary>Client names that read better in words, matched case-insensitively.</summary>
    private static readonly IReadOnlyDictionary<string, string> KnownClients =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["claude-code"] = NotificationOrigins.ClaudeCode,
            ["claude-ai"] = "Claude",
        };

    private static ModelContextProtocol.McpException InvalidChoices(string reason) =>
        RepositoryScope.Failure(NotificationsErrors.InvalidAnswerSpec(reason));

    private static string NewKey() => Guid.NewGuid().ToString("D");

    private static NotificationSubject? SessionSubject(string? sessionId) =>
        string.IsNullOrWhiteSpace(sessionId) ? null : new NotificationSubject(SessionsContext, SessionSubjectType, sessionId.Trim());

    private static NotificationSeverity ReadSeverity(string? severity)
    {
        if (string.IsNullOrWhiteSpace(severity)) return NotificationSeverity.Info;
        if (Severities.TryGetValue(severity.Trim(), out var read)) return read;

        throw RepositoryScope.Failure(Error.Validation(
            "notification.unknown_severity",
            $"'{severity}' is not a severity. Use info, warning or critical."));
    }
}

/// <summary>What <c>notify</c> and <c>ask</c> answer: the id <c>get_answer</c> and <c>close</c> take.</summary>
/// <param name="Id">The notification's id.</param>
public sealed record NotificationPublishedPayload(Guid Id);

/// <summary>Where a notification stands, as <c>get_answer</c> and <c>close</c> answer it.</summary>
/// <param name="Id">The notification's id.</param>
/// <param name="Status">pending, answered, acknowledged or closed.</param>
/// <param name="Choices">The chosen choice ids — the labels <c>ask</c> was given — when answered; otherwise empty.</param>
/// <param name="Text">The open answer's text, or null.</param>
/// <param name="AnsweredAt">When the answer was accepted, or null.</param>
/// <param name="Reason">The reason a closed notification was closed with, or null.</param>
/// <param name="Note">The note the person wrote beside the answer, or null.</param>
public sealed record NotificationAnswerPayload(
    Guid Id,
    string Status,
    IReadOnlyList<string> Choices,
    string? Text,
    DateTimeOffset? AnsweredAt,
    string? Reason,
    string? Note = null)
{
    internal static NotificationAnswerPayload From(NotificationStatus status) =>
        new(
            status.Id,
            NotificationTools.ToWire(status.State),
            status.Answer?.ChoiceIds ?? [],
            status.Answer?.Text,
            status.Answer?.AnsweredAt,
            status.State == NotificationState.Closed ? status.CloseReason : null,
            status.Answer?.Note);
}
