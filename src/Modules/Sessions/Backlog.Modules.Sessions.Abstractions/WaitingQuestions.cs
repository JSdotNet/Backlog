namespace Backlog.Modules.Sessions.Abstractions;

/// <summary>How a session asked (<c>.devbook/domain/sessions/domain.md#waiting-question</c>).</summary>
public enum WaitingQuestionKind
{
    /// <summary>A question dialog (<c>AskUserQuestion</c>) of one to four questions,
    /// known by its tool use id.</summary>
    Dialog,

    /// <summary>One question written in prose at the end of a turn.</summary>
    Prose
}

/// <summary>Where a <see cref="WaitingQuestion"/> was sent when it was asked, from the
/// person's presence at that moment (local ADR 0041 §1). Set once, never changed.</summary>
public enum WaitingQuestionRoute
{
    /// <summary>The person was at the keyboard: the question stays in the session and is
    /// published only once the notify delay passes.</summary>
    Session,

    /// <summary>The person was away: the question is published at once.</summary>
    Backlog
}

/// <summary>Where a <see cref="WaitingQuestion"/> stands
/// (<c>.devbook/domain/sessions/domain.md#waiting-question</c>).</summary>
public enum WaitingQuestionState
{
    /// <summary>Nothing published yet; it waits for its escalate at.</summary>
    InSession,

    /// <summary>Published, one notification per question.</summary>
    Notified,

    /// <summary>Answered in the session, or every one of its notifications has an
    /// accepted answer. Final.</summary>
    Answered,

    /// <summary>The session ended, or a dialog's hook stopped waiting. Final.</summary>
    Closed
}

/// <summary>Where the answer to a <see cref="WaitingQuestion"/> stands on its way to the
/// session through the Manager. A lapsed claim reads as Pending again.</summary>
public enum AnswerDeliveryState
{
    /// <summary>Waiting for the Manager to claim it.</summary>
    Pending,

    /// <summary>The Manager holds it under a two-minute lease.</summary>
    Claimed,

    /// <summary>The Manager sent the answer to the session.</summary>
    Delivered
}

/// <summary>Why a <see cref="WaitingQuestion"/> was closed (local ADR 0041, Amends).</summary>
public enum WaitingQuestionCloseReason
{
    /// <summary>The person answered in the session; the answers are kept and shown on
    /// every viewer of its notifications.</summary>
    AnsweredInSession,

    /// <summary>The dialog's hook stopped waiting.</summary>
    TimedOut,

    /// <summary>The session ended.</summary>
    SessionEnded
}

/// <summary>One option a dialog question offers, as <c>AskUserQuestion</c> shapes it.</summary>
/// <param name="Label">What the person picks.</param>
/// <param name="Description">What picking it means, or null.</param>
public sealed record RelayOption(string Label, string? Description = null);

/// <summary>
/// One question a session asked, in the dialog's shape: a short header, the question, and
/// its options. A question without options — every prose question — takes an open answer
/// only.
/// </summary>
/// <param name="Question">The question as the session wrote it.</param>
/// <param name="Header">The dialog's short header, or null.</param>
/// <param name="Options">The options, empty for an open question.</param>
/// <param name="MultiSelect">Whether several options may be picked.</param>
public sealed record RelayQuestion(
    string Question,
    string? Header = null,
    IReadOnlyList<RelayOption>? Options = null,
    bool MultiSelect = false)
{
    /// <summary>The options, never null.</summary>
    public IReadOnlyList<RelayOption> Choices => Options ?? [];

    /// <summary>Whether the question takes an open answer only.</summary>
    public bool IsOpenOnly => Choices.Count == 0;

    /// <summary>A prose question: one free-text question with no options.</summary>
    public static RelayQuestion Prose(string question) => new(question);

    public bool Equals(RelayQuestion? other) =>
        other is not null
        && string.Equals(Question, other.Question, StringComparison.Ordinal)
        && string.Equals(Header, other.Header, StringComparison.Ordinal)
        && MultiSelect == other.MultiSelect
        && Choices.SequenceEqual(other.Choices);

    public override int GetHashCode() => HashCode.Combine(Question, Header, MultiSelect, Choices.Count);
}

/// <summary>
/// The answer one question got: the labels of the options picked, or the open text — the
/// dialog's "Other". Wherever it was given, it reads the same.
/// </summary>
/// <param name="Choices">The labels of the options picked; empty for an open answer.</param>
/// <param name="Text">The open answer, or null.</param>
public sealed record RelayAnswer(IReadOnlyList<string> Choices, string? Text = null)
{
    /// <summary>An open answer.</summary>
    public static RelayAnswer Open(string text) => new([], text);

    /// <summary>The answer as one line: the picked labels joined, or the text.</summary>
    public string Display =>
        Choices.Count > 0 ? string.Join(", ", Choices) : (Text ?? string.Empty).Trim();

    public bool Equals(RelayAnswer? other) =>
        other is not null
        && string.Equals(Text, other.Text, StringComparison.Ordinal)
        && Choices.SequenceEqual(other.Choices, StringComparer.Ordinal);

    public override int GetHashCode() => HashCode.Combine(Text, Choices.Count);
}

/// <summary>
/// One question a session asked the person and is waiting on — the record of the Sessions
/// context's Question Relay (<c>.devbook/domain/sessions/domain.md#waiting-question</c>,
/// local ADR 0041). The relay owns the waiting and the route; Notifications owns the
/// notifications it publishes, one per question, in <see cref="NotificationIds"/>.
/// </summary>
/// <param name="Id">The question's own id, and the source key of its notifications.</param>
/// <param name="SessionId">The session that asked.</param>
/// <param name="ToolUseId">The dialog's tool use id; null for a prose question.</param>
/// <param name="Kind">Dialog or prose.</param>
/// <param name="Route">Where it was sent when asked.</param>
/// <param name="State">Where it stands.</param>
/// <param name="Questions">The questions, one to four.</param>
/// <param name="NotificationIds">One per question, in order, once Notified; else empty.</param>
/// <param name="Answers">One per question, in order, null while that one is unanswered.</param>
/// <param name="AskedAt">When it was registered.</param>
/// <param name="EscalateAt">When it becomes a notification if still InSession.</param>
/// <param name="HookPolledAt">When the dialog's hook last asked for the answer, or null.</param>
/// <param name="Delivery">The delivery's state, once its answer must reach the session
/// through the Manager; null otherwise.</param>
/// <param name="CloseReason">Why it was closed, once closed or answered in the session.</param>
/// <param name="EndedAt">When it was answered or closed.</param>
public sealed record WaitingQuestion(
    Guid Id,
    string SessionId,
    string? ToolUseId,
    WaitingQuestionKind Kind,
    WaitingQuestionRoute Route,
    WaitingQuestionState State,
    IReadOnlyList<RelayQuestion> Questions,
    IReadOnlyList<Guid> NotificationIds,
    IReadOnlyList<RelayAnswer?> Answers,
    DateTimeOffset AskedAt,
    DateTimeOffset EscalateAt,
    DateTimeOffset? HookPolledAt,
    AnswerDeliveryState? Delivery,
    WaitingQuestionCloseReason? CloseReason,
    DateTimeOffset? EndedAt)
{
    /// <summary>Whether it is still waiting: InSession or Notified.</summary>
    public bool IsOpen => State is WaitingQuestionState.InSession or WaitingQuestionState.Notified;

    /// <summary>Whether its notify delay has passed while it is still InSession.</summary>
    public bool IsDueAt(DateTimeOffset now) => State == WaitingQuestionState.InSession && EscalateAt <= now;

    /// <summary>Whether a dialog's hook is still holding it at <paramref name="now"/>:
    /// the route is <c>backlog</c> and the hook asked within <see cref="WaitingQuestions.HookPollWindow"/>.</summary>
    public bool HookIsWaitingAt(DateTimeOffset now) =>
        Route == WaitingQuestionRoute.Backlog
        && HookPolledAt is { } polled
        && now - polled <= WaitingQuestions.HookPollWindow;

    public bool Equals(WaitingQuestion? other) =>
        other is not null
        && Id == other.Id
        && string.Equals(SessionId, other.SessionId, StringComparison.Ordinal)
        && string.Equals(ToolUseId, other.ToolUseId, StringComparison.Ordinal)
        && Kind == other.Kind
        && Route == other.Route
        && State == other.State
        && Questions.SequenceEqual(other.Questions)
        && NotificationIds.SequenceEqual(other.NotificationIds)
        && Answers.SequenceEqual(other.Answers)
        && AskedAt == other.AskedAt
        && EscalateAt == other.EscalateAt
        && HookPolledAt == other.HookPolledAt
        && Delivery == other.Delivery
        && CloseReason == other.CloseReason
        && EndedAt == other.EndedAt;

    public override int GetHashCode() => Id.GetHashCode();
}

/// <summary>What one operation on a waiting question did: the question as it now stands,
/// or the reason it was refused and the question as it was.</summary>
public sealed record WaitingQuestionOutcome(WaitingQuestion? Question, string? Refusal)
{
    public bool Succeeded => Refusal is null;

    public static WaitingQuestionOutcome Done(WaitingQuestion question) => new(question, null);

    public static WaitingQuestionOutcome Refused(WaitingQuestion? question, string refusal) => new(question, refusal);
}

/// <summary>
/// The Waiting Question's states and transitions (local ADR 0041), as pure functions of
/// the question and the clock. The store applies them inside one write.
/// <code>
/// [new]     → InSession   asked at the keyboard, or away and not yet published
/// InSession → Notified    published: at once when away, else once escalate at passes
/// InSession → Answered    answered in the session
/// Notified  → Answered    answered in the session, or every notification answered
/// InSession → Closed      timed out, or the session ended
/// Notified  → Closed      timed out, or the session ended
/// </code>
/// Answered and Closed are final, and a Notified question never returns to InSession.
/// </summary>
public static class WaitingQuestions
{
    /// <summary>The most questions one dialog holds.</summary>
    public const int MaxQuestions = 4;

    /// <summary>The fewest options a dialog question with options offers.</summary>
    public const int MinOptions = 2;

    /// <summary>The most options a dialog question offers.</summary>
    public const int MaxOptions = 4;

    /// <summary>How recently a dialog's hook must have asked for the answer to count as
    /// still waiting on it; after that, an answer becomes a delivery instead.</summary>
    public static readonly TimeSpan HookPollWindow = TimeSpan.FromMinutes(2);

    /// <summary>
    /// A new waiting question, InSession: a dialog when <paramref name="toolUseId"/> is
    /// given, else a prose question. Its route is <c>backlog</c> when the person is away
    /// — due at once — and <c>session</c> otherwise, due <paramref name="notifyAfter"/>
    /// from now.
    /// </summary>
    /// <exception cref="ArgumentException">The questions are not one to four dialog
    /// questions, or one open question for prose.</exception>
    public static WaitingQuestion Ask(
        string sessionId,
        string? toolUseId,
        IReadOnlyList<RelayQuestion> questions,
        bool isAway,
        TimeSpan notifyAfter,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(questions);

        var session = Required(sessionId, nameof(sessionId));
        var toolUse = string.IsNullOrWhiteSpace(toolUseId) ? null : toolUseId.Trim();
        var kind = toolUse is null ? WaitingQuestionKind.Prose : WaitingQuestionKind.Dialog;

        if (Problem(kind, questions) is { } problem) throw new ArgumentException(problem, nameof(questions));
        if (notifyAfter < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(notifyAfter));

        return new WaitingQuestion(
            Guid.NewGuid(),
            session,
            toolUse,
            kind,
            isAway ? WaitingQuestionRoute.Backlog : WaitingQuestionRoute.Session,
            WaitingQuestionState.InSession,
            [.. questions],
            NotificationIds: [],
            Answers: [.. questions.Select(_ => (RelayAnswer?)null)],
            AskedAt: now,
            EscalateAt: isAway ? now : now + notifyAfter,
            HookPolledAt: null,
            Delivery: null,
            CloseReason: null,
            EndedAt: null);
    }

    /// <summary>Why <paramref name="questions"/> cannot be asked as <paramref name="kind"/>,
    /// or null when they can.</summary>
    public static string? Problem(WaitingQuestionKind kind, IReadOnlyList<RelayQuestion>? questions)
    {
        if (questions is null || questions.Count == 0) return "A waiting question holds at least one question.";

        if (questions.Any(question => question is null || string.IsNullOrWhiteSpace(question.Question)))
            return "Every question has its text.";

        if (kind == WaitingQuestionKind.Prose)
        {
            return questions.Count == 1 && questions[0].IsOpenOnly
                ? null
                : "A prose question is one free-text question, without options.";
        }

        if (questions.Count > MaxQuestions) return $"A dialog holds one to {MaxQuestions} questions.";

        foreach (var question in questions)
        {
            var options = question.Choices;

            if (options.Count == 0) continue;

            if (options.Count is < MinOptions or > MaxOptions)
                return $"A dialog question offers {MinOptions} to {MaxOptions} options.";

            if (options.Any(option => option is null || string.IsNullOrWhiteSpace(option.Label)))
                return "Every option has a label.";

            if (options.Select(option => option.Label.Trim()).Distinct(StringComparer.Ordinal).Count() != options.Count)
                return "Every option of a question has its own label.";
        }

        return null;
    }

    /// <summary>The open question among <paramref name="held"/> — or any question, open or
    /// not — registered for the same session and tool use as <paramref name="candidate"/>,
    /// or null. A prose question has no tool use, so it is never a duplicate.</summary>
    public static WaitingQuestion? DuplicateOf(WaitingQuestion candidate, IEnumerable<WaitingQuestion> held)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(held);

        if (candidate.ToolUseId is null) return null;

        return held
            .Where(question =>
                string.Equals(question.SessionId, candidate.SessionId, StringComparison.Ordinal)
                && string.Equals(question.ToolUseId, candidate.ToolUseId, StringComparison.Ordinal))
            .OrderBy(question => question.AskedAt)
            .FirstOrDefault();
    }

    /// <summary>InSession → Notified, with one published notification per question.
    /// Refused on any other state, or when the ids do not match the questions.</summary>
    public static WaitingQuestionOutcome Notify(WaitingQuestion question, IReadOnlyList<Guid> notificationIds)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(notificationIds);

        if (question.State != WaitingQuestionState.InSession)
            return WaitingQuestionOutcome.Refused(question, $"The question is {question.State}, and only an InSession question is published.");

        if (notificationIds.Count != question.Questions.Count)
            return WaitingQuestionOutcome.Refused(question, "A waiting question is published as one notification per question.");

        return WaitingQuestionOutcome.Done(question with
        {
            State = WaitingQuestionState.Notified,
            NotificationIds = [.. notificationIds]
        });
    }

    /// <summary>
    /// Records the accepted answer to the notification <paramref name="notificationId"/>.
    /// Once every question has one, the question is Answered: its answer goes to the hook
    /// when one is still waiting (<see cref="WaitingQuestion.HookIsWaitingAt"/>), otherwise
    /// it becomes a Pending delivery. A second answer to the same notification changes
    /// nothing, so a replayed answer is harmless.
    /// </summary>
    public static WaitingQuestionOutcome Answer(
        WaitingQuestion question,
        Guid notificationId,
        RelayAnswer answer,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(answer);

        var index = IndexOf(question, notificationId);

        if (index < 0) return WaitingQuestionOutcome.Refused(question, "The notification is not one of this question's.");
        if (question.State != WaitingQuestionState.Notified)
            return WaitingQuestionOutcome.Refused(question, $"The question is {question.State}, and only a Notified question takes an answer from Backlog.");
        if (question.Answers[index] is not null)
            return WaitingQuestionOutcome.Refused(question, "That question already has its answer.");

        var answers = question.Answers.ToArray();
        answers[index] = answer;

        var changed = question with { Answers = answers };

        if (answers.Any(given => given is null)) return WaitingQuestionOutcome.Done(changed);

        return WaitingQuestionOutcome.Done(changed with
        {
            State = WaitingQuestionState.Answered,
            EndedAt = now,
            Delivery = question.HookIsWaitingAt(now) ? null : AnswerDeliveryState.Pending
        });
    }

    /// <summary>
    /// Ends an open question with <paramref name="reason"/>. Answered in the session, it is
    /// Answered with <paramref name="answers"/> — one per question — and gets no delivery;
    /// timed out or session ended, it is Closed. Refused once Answered or Closed.
    /// </summary>
    public static WaitingQuestionOutcome Close(
        WaitingQuestion question,
        WaitingQuestionCloseReason reason,
        IReadOnlyList<RelayAnswer>? answers,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(question);

        if (!Enum.IsDefined(reason)) throw new ArgumentOutOfRangeException(nameof(reason));

        if (!question.IsOpen)
            return WaitingQuestionOutcome.Refused(question, $"The question is {question.State}, which is final.");

        if (reason == WaitingQuestionCloseReason.AnsweredInSession)
        {
            if (answers is null || answers.Count != question.Questions.Count || answers.Any(given => given is null))
                return WaitingQuestionOutcome.Refused(question, "An answer in the session gives one answer per question.");

            return WaitingQuestionOutcome.Done(question with
            {
                State = WaitingQuestionState.Answered,
                Answers = [.. answers],
                Delivery = null,
                CloseReason = reason,
                EndedAt = now
            });
        }

        return WaitingQuestionOutcome.Done(question with
        {
            State = WaitingQuestionState.Closed,
            Delivery = null,
            CloseReason = reason,
            EndedAt = now
        });
    }

    /// <summary>
    /// Moves an InSession question's escalate at to <paramref name="until"/>: what a
    /// publish Notifications refused does, so the question waits in the session and is
    /// tried again once per notify delay rather than on every tick. Refused on any other
    /// state; the route is never changed.
    /// </summary>
    public static WaitingQuestionOutcome Postpone(WaitingQuestion question, DateTimeOffset until)
    {
        ArgumentNullException.ThrowIfNull(question);

        return question.State == WaitingQuestionState.InSession
            ? WaitingQuestionOutcome.Done(question with { EscalateAt = until })
            : WaitingQuestionOutcome.Refused(question, $"The question is {question.State}, and only an InSession question waits to be published.");
    }

    /// <summary>Records that the dialog's hook asked for the answer at <paramref name="now"/>.</summary>
    public static WaitingQuestion Polled(WaitingQuestion question, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(question);

        return question with { HookPolledAt = now };
    }

    /// <summary>The index of the question <paramref name="notificationId"/> was published
    /// for, or -1.</summary>
    public static int IndexOf(WaitingQuestion question, Guid notificationId)
    {
        ArgumentNullException.ThrowIfNull(question);

        for (var i = 0; i < question.NotificationIds.Count; i++)
        {
            if (question.NotificationIds[i] == notificationId) return i;
        }

        return -1;
    }

    /// <summary>The wire spelling of a close reason: <c>answered-in-session</c>,
    /// <c>timed-out</c> or <c>session-ended</c>.</summary>
    public static string Spelling(WaitingQuestionCloseReason reason) => reason switch
    {
        WaitingQuestionCloseReason.AnsweredInSession => "answered-in-session",
        WaitingQuestionCloseReason.TimedOut => "timed-out",
        WaitingQuestionCloseReason.SessionEnded => "session-ended",
        _ => throw new ArgumentOutOfRangeException(nameof(reason))
    };

    /// <summary>The close reason a wire spelling names, or null.</summary>
    public static WaitingQuestionCloseReason? ParseReason(string? spelling) => (spelling ?? string.Empty).Trim() switch
    {
        "answered-in-session" => WaitingQuestionCloseReason.AnsweredInSession,
        "timed-out" => WaitingQuestionCloseReason.TimedOut,
        "session-ended" => WaitingQuestionCloseReason.SessionEnded,
        _ => null
    };

    /// <summary>A live session's state with its open waiting questions read in: Running or
    /// Stalled become Waiting while it holds one, and Finished stays Finished
    /// (<c>domain.invariants.md#invariant-an-open-waiting-question-makes-a-live-session-waiting</c>).</summary>
    public static AgentSessionState StateWith(AgentSessionState state, bool holdsOpenQuestion) =>
        holdsOpenQuestion && state is AgentSessionState.Running or AgentSessionState.Stalled
            ? AgentSessionState.Waiting
            : state;

    private static string Required(string? value, string name)
    {
        var trimmed = (value ?? string.Empty).Trim();

        return trimmed.Length == 0 ? throw new ArgumentException($"{name} is required.", name) : trimmed;
    }
}

/// <summary>
/// PORT — this machine's waiting questions (local ADR 0041). One row per question in the
/// app's local database, beside the session requests; it does not replicate.
/// <para>
/// Every write reads and writes in one transaction, so a transition checks the state and
/// moves it in the same write.
/// </para>
/// </summary>
public interface IWaitingQuestionStore
{
    /// <summary>Stores <paramref name="candidate"/>, or answers the question already
    /// registered for the same session and tool use and stores nothing.</summary>
    Task<WaitingQuestion> RegisterAsync(WaitingQuestion candidate, CancellationToken cancellationToken = default);

    /// <summary>The question with <paramref name="id"/>, or null.</summary>
    Task<WaitingQuestion?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Every question, or only the open ones, oldest first.</summary>
    Task<IReadOnlyList<WaitingQuestion>> ListAsync(bool openOnly = false, CancellationToken cancellationToken = default);

    /// <summary>Applies <paramref name="transition"/> to the question with
    /// <paramref name="id"/> in one write, keeping the result when it succeeded.</summary>
    Task<WaitingQuestionOutcome> ChangeAsync(
        Guid id,
        Func<WaitingQuestion, WaitingQuestionOutcome> transition,
        CancellationToken cancellationToken = default);
}
