using System.ComponentModel;
using System.Text.Json.Serialization;

using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Sessions.Abstractions;
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
/// <para>
/// <b>A session's question goes through the Sessions relay</b> (local ADR 0041, amending ADR
/// 0039's tools). An <c>ask</c> that passes <c>toolUseId</c>, <c>questions</c> or <c>kind</c> —
/// the hooks' call — registers a waiting question through <see cref="IQuestionRelay"/>, which
/// picks the route from the person's presence, and answers the route, the waiting question's id
/// and the dialog wait. <c>get_answer</c> and <c>close</c> take that id too: <c>get_answer</c>
/// may hold up to <see cref="MaxWaitSeconds"/> on the completion source the relay completes when
/// the question is answered or closed, and <c>close</c> takes the relay's three reasons. A call
/// that passes none of the new arguments, about a notification id, behaves as before.
/// </para>
/// </summary>
/// <param name="notifications">The Notifications port every publish and close goes through.</param>
/// <param name="statuses">Where a notification stands, for <c>get_answer</c> and <c>close</c>.</param>
/// <param name="relay">The Sessions question relay, or null when the host composed none; a
/// relay argument is then refused as notifications not available.</param>
/// <param name="timings">The dialog wait <c>ask</c> answers; the default 8 hours when null.</param>
[McpServerToolType]
public sealed class NotificationTools(
    INotifications notifications,
    INotificationStatuses statuses,
    IQuestionRelay? relay = null,
    INotificationTimingSettings? timings = null)
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

    /// <summary>The <c>get_answer</c> status of a waiting question nobody has answered yet.</summary>
    internal const string Open = "open";

    /// <summary>The <c>ask</c> kind of a question dialog, asked with its tool use id.</summary>
    internal const string DialogKind = "dialog";

    /// <summary>The <c>ask</c> kind of one question written in prose at the end of a turn.</summary>
    internal const string ProseKind = "prose";

    /// <summary>The <c>ask</c> routes: the question stays in the session for now, or was
    /// published at once.</summary>
    internal const string SessionRoute = "session";

    internal const string BacklogRoute = "backlog";

    /// <summary>The longest one <c>get_answer</c> holds, under Claude Code's two-minute
    /// background and five-minute idle limits for an MCP call (local ADR 0041).</summary>
    internal const int MaxWaitSeconds = 55;

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
        + "Calling it twice asks twice. "
        + "A session hook relaying its own question passes sessionId with questions (and toolUseId for a question "
        + "dialog) instead of title and choices: Backlog then decides from the person's presence whether the question "
        + "stays in the session for now or is published at once, and answers route (session or backlog), the waiting "
        + "question's id and waitSeconds, the dialog wait. Asked again for the same tool use, it answers the same question.")]
    public async Task<NotificationPublishedPayload> AskAsync(
        [Description("The question, one line of plain text, at most 80 characters. Required unless questions is given.")]
        string? title = null,
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
        [Description("Your Claude session id, so the question is about this session. Optional; required with questions.")]
        string? sessionId = null,
        [Description("The question dialog's tool use id (AskUserQuestion), so asking again for it answers the same question. Omit for a prose question.")]
        string? toolUseId = null,
        [Description("The questions in the dialog's shape: one to four, each with question, header, options (2 to 4, each a label and a description) and multiSelect. A prose question is one question with no options.")]
        AskQuestion[]? questions = null,
        [Description("dialog or prose. Defaults to dialog with toolUseId, prose without.")]
        string? kind = null,
        CancellationToken cancellationToken = default,
        McpServer? server = null)
    {
        if (toolUseId is not null || questions is not null || kind is not null)
            return await RelayAskAsync(sessionId, toolUseId, questions, kind, cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(title))
            throw RepositoryScope.Failure(Error.Validation("notification.title_required", "A question needs its title."));

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
        + "alert the person saw. Poll it after ask; once it is not pending it never changes again. "
        + "For a waiting question's id it answers open, answered with answers keyed by question text (several picked "
        + "labels comma-joined), or closed with its reason; waitSeconds holds the call until then, at most 55 seconds. "
        + "Every call tells Backlog the asking hook is still waiting.")]
    public async Task<NotificationAnswerPayload> GetAnswerAsync(
        [Description("The notification's id, as ask or notify answered it, or the waiting question's id.")]
        Guid id,
        [Description("Your Claude session id, when you passed one to ask or notify; required then.")]
        string? sessionId = null,
        [Description("For a waiting question: how long to hold the call for the answer, 1 to 55 seconds; a longer wait is held 55. Omit to answer at once.")]
        int? waitSeconds = null,
        CancellationToken cancellationToken = default)
    {
        var wait = ReadWait(waitSeconds);

        if (await OwnWaitingQuestionAsync(id, sessionId, cancellationToken).ConfigureAwait(false) is { } waiting)
        {
            var held = await relay!.WaitAsync(waiting.Id, wait, cancellationToken).ConfigureAwait(false) ?? waiting;

            return NotificationAnswerPayload.From(held);
        }

        var status = await OwnStatusAsync(id, sessionId, cancellationToken).ConfigureAwait(false);

        return NotificationAnswerPayload.From(status);
    }

    [McpServerTool(Name = Close, Title = "Withdraw a notification", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = false)]
    [Description(
        "Closes an open notification you published, with an optional reason, on every device: use it when you no "
        + "longer need the answer. A reply sent afterwards is rejected as closed. Refused for one already answered, "
        + "acknowledged or closed, and for one this server did not publish. "
        + "For a waiting question's id the reason is answered-in-session (with answers), timed-out or session-ended, "
        + "and every notification it was published as closes with it.")]
    public async Task<NotificationAnswerPayload> CloseAsync(
        [Description("The notification's id, as ask or notify answered it, or the waiting question's id.")]
        Guid id,
        [Description("Why it is withdrawn, in words, e.g. \"Decided without it\". Optional. For a waiting question: answered-in-session, timed-out or session-ended.")]
        string? reason = null,
        [Description("Your Claude session id, when you passed one to ask or notify; required then.")]
        string? sessionId = null,
        [Description("With answered-in-session: the answers the person gave in the session, keyed by question text, several picked labels comma-joined - as the question dialog answers.")]
        Dictionary<string, string>? answers = null,
        CancellationToken cancellationToken = default)
    {
        if (await OwnWaitingQuestionAsync(id, sessionId, cancellationToken).ConfigureAwait(false) is { } waiting)
            return await RelayCloseAsync(waiting, reason, answers, cancellationToken).ConfigureAwait(false);

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

    /// <summary>The <c>get_answer</c> status of a waiting question.</summary>
    internal static string ToWire(WaitingQuestionState state) => state switch
    {
        WaitingQuestionState.InSession or WaitingQuestionState.Notified => Open,
        WaitingQuestionState.Answered => Answered,
        WaitingQuestionState.Closed => Closed,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "A waiting question is open, answered or closed."),
    };

    /// <summary>
    /// The answers of an answered waiting question as Claude's question dialog takes them: keyed
    /// by question text, the picked labels comma-joined, or the open text.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> DialogAnswers(WaitingQuestion question)
    {
        var answers = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var i = 0; i < question.Questions.Count; i++)
        {
            if (question.Answers.ElementAtOrDefault(i) is { } given) answers[question.Questions[i].Question] = given.Display;
        }

        return answers;
    }

    /// <summary>
    /// One answer given in the session, as <c>close</c> receives it, read against its question:
    /// the option label when the whole text names one, the labels of a multi-select when every
    /// comma-joined part names one, and otherwise the open text.
    /// </summary>
    internal static RelayAnswer ReadAnswer(RelayQuestion question, string answer)
    {
        var text = answer.Trim();
        var labels = question.Choices.Select(option => option.Label.Trim()).ToList();

        if (labels.Contains(text, StringComparer.Ordinal)) return new RelayAnswer([text]);

        var parts = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        return question.MultiSelect && parts.Length > 0 && parts.All(part => labels.Contains(part, StringComparer.Ordinal))
            ? new RelayAnswer(parts)
            : RelayAnswer.Open(text);
    }

    /// <summary>The <c>waitSeconds</c> a <c>get_answer</c> holds for: none when omitted, refused
    /// below one second, and capped at <see cref="MaxWaitSeconds"/>.</summary>
    internal static TimeSpan ReadWait(int? waitSeconds) => waitSeconds switch
    {
        null => TimeSpan.Zero,
        < 1 => throw RepositoryScope.Failure(Error.Validation(
            "relay.wait_invalid", $"waitSeconds is 1 to {MaxWaitSeconds} seconds.")),
        _ => TimeSpan.FromSeconds(Math.Min(waitSeconds.Value, MaxWaitSeconds)),
    };

    private async Task<NotificationPublishedPayload> RelayAskAsync(
        string? sessionId,
        string? toolUseId,
        AskQuestion[]? questions,
        string? kind,
        CancellationToken cancellationToken)
    {
        if (relay is null) throw RepositoryScope.Failure(NotificationsErrors.NotAvailable());

        if (string.IsNullOrWhiteSpace(sessionId))
            throw RepositoryScope.Failure(Error.Validation("relay.session_required", "A session's question needs its sessionId."));

        var dialog = !string.IsNullOrWhiteSpace(toolUseId);

        switch ((kind ?? (dialog ? DialogKind : ProseKind)).Trim().ToLowerInvariant())
        {
            case DialogKind when !dialog:
                throw RepositoryScope.Failure(Error.Validation("relay.tool_use_required", "A dialog question needs its toolUseId."));
            case ProseKind when dialog:
                throw RepositoryScope.Failure(Error.Validation("relay.kind_invalid", "A prose question has no toolUseId."));
            case DialogKind or ProseKind:
                break;
            default:
                throw RepositoryScope.Failure(Error.Validation("relay.kind_invalid", $"'{kind}' is not a kind. Use dialog or prose."));
        }

        var asked = (questions ?? []).Select(question => question?.ToRelay()!).ToList();

        var result = await relay.AskAsync(sessionId.Trim(), dialog ? toolUseId!.Trim() : null, asked, cancellationToken).ConfigureAwait(false);
        var waiting = Succeeded(result);

        return new NotificationPublishedPayload(
            waiting.Id,
            waiting.Route == WaitingQuestionRoute.Backlog ? BacklogRoute : SessionRoute,
            (int)(timings?.DialogWait ?? PresenceSettings.Default.DialogWait).TotalSeconds);
    }

    private async Task<NotificationAnswerPayload> RelayCloseAsync(
        WaitingQuestion waiting,
        string? reason,
        Dictionary<string, string>? answers,
        CancellationToken cancellationToken)
    {
        if (WaitingQuestions.ParseReason(reason) is not { } because)
        {
            throw RepositoryScope.Failure(Error.Validation(
                "relay.reason_invalid",
                $"'{reason}' is not a reason a waiting question closes with. Use answered-in-session, timed-out or session-ended."));
        }

        IReadOnlyList<RelayAnswer>? given = null;

        if (because == WaitingQuestionCloseReason.AnsweredInSession)
        {
            var read = new List<RelayAnswer>(waiting.Questions.Count);

            foreach (var question in waiting.Questions)
            {
                if (answers is not null && answers.TryGetValue(question.Question, out var answer) && !string.IsNullOrWhiteSpace(answer))
                    read.Add(ReadAnswer(question, answer));
            }

            given = read;
        }

        var result = await relay!.CloseAsync(waiting.Id, because, given, cancellationToken).ConfigureAwait(false);

        return NotificationAnswerPayload.From(Succeeded(result));
    }

    /// <summary>The waiting question with <paramref name="id"/>, when a relay is composed and holds
    /// one; null for a notification id. One the caller did not ask, naming its own session, is
    /// not found.</summary>
    private async Task<WaitingQuestion?> OwnWaitingQuestionAsync(Guid id, string? sessionId, CancellationToken cancellationToken)
    {
        if (relay is null || await relay.FindAsync(id, cancellationToken).ConfigureAwait(false) is not { } waiting) return null;

        return string.Equals(waiting.SessionId, sessionId?.Trim(), StringComparison.Ordinal)
            ? waiting
            : throw RepositoryScope.Failure(NotificationsErrors.NotFound(id));
    }

    private static WaitingQuestion Succeeded(QuestionRelayResult result) =>
        result is { Succeeded: true, Question: { } question }
            ? question
            : throw RepositoryScope.Failure(Error.Validation(result.Code ?? "relay.failed", result.Message ?? "The question relay refused the call."));

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
/// <param name="Id">The notification's id, or for a session's relayed question the waiting question's id.</param>
/// <param name="Route">For a relayed question: <c>session</c> when it stays in the session for now,
/// <c>backlog</c> when it was published at once. Absent otherwise.</param>
/// <param name="WaitSeconds">For a relayed question: the dialog wait, how long a dialog's hook keeps
/// asking get_answer. Absent otherwise.</param>
public sealed record NotificationPublishedPayload(
    Guid Id,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Route = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? WaitSeconds = null);

/// <summary>One question of a question dialog, in the shape <c>AskUserQuestion</c> takes it.</summary>
/// <param name="Question">The question.</param>
/// <param name="Header">The dialog's short header, or null.</param>
/// <param name="Options">The options, 2 to 4; none for an open question.</param>
/// <param name="MultiSelect">Whether several options may be picked.</param>
public sealed record AskQuestion(string Question, string? Header = null, AskOption[]? Options = null, bool MultiSelect = false)
{
    internal RelayQuestion ToRelay() =>
        new(Question ?? string.Empty, Header, [.. (Options ?? []).Select(option => new RelayOption(option?.Label ?? string.Empty, option?.Description))], MultiSelect);
}

/// <summary>One option of a dialog question.</summary>
/// <param name="Label">What the person picks.</param>
/// <param name="Description">What picking it means, or null.</param>
public sealed record AskOption(string Label, string? Description = null);

/// <summary>Where a notification stands, as <c>get_answer</c> and <c>close</c> answer it.</summary>
/// <param name="Id">The notification's id.</param>
/// <param name="Status">pending, answered, acknowledged or closed.</param>
/// <param name="Choices">The chosen choice ids — the labels <c>ask</c> was given — when answered; otherwise empty.</param>
/// <param name="Text">The open answer's text, or null.</param>
/// <param name="AnsweredAt">When the answer was accepted, or null.</param>
/// <param name="Reason">The reason a closed notification was closed with, or null. For a waiting
/// question: answered-in-session, timed-out or session-ended.</param>
/// <param name="Answers">For an answered waiting question: the answers keyed by question text, several
/// picked labels comma-joined. Absent otherwise.</param>
/// <param name="Note">The note the person wrote beside the answer, or null.</param>
public sealed record NotificationAnswerPayload(
    Guid Id,
    string Status,
    IReadOnlyList<string> Choices,
    string? Text,
    DateTimeOffset? AnsweredAt,
    string? Reason,
    string? Note = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, string>? Answers = null)
{
    internal static NotificationAnswerPayload From(WaitingQuestion question) =>
        new(
            question.Id,
            NotificationTools.ToWire(question.State),
            [],
            null,
            question.State == WaitingQuestionState.Answered ? question.EndedAt : null,
            question.CloseReason is { } reason ? WaitingQuestions.Spelling(reason) : null,
            Answers: question.State == WaitingQuestionState.Answered ? NotificationTools.DialogAnswers(question) : null);

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
