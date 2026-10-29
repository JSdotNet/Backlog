using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.SharedKernel.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Infrastructure.Sessions;

/// <summary>
/// The Sessions context's question relay (<c>.devbook/domain/sessions/domain.md#question-relay</c>,
/// local ADR 0041): it registers the questions a session asks as
/// <see cref="WaitingQuestion"/>s, picks each one's route from the person's
/// <see cref="IPresence"/>, publishes it through <see cref="INotifications"/> — at once
/// when the person is away, once the notify delay passes otherwise — and routes the
/// accepted answers back: to the dialog's hook while it is still waiting, or into a
/// delivery for the Manager.
/// <para>
/// The relay keeps no question store of its own beyond the waiting question: the
/// notification, its replies and which reply became the answer stay Notifications'.
/// </para>
/// </summary>
public sealed class QuestionRelay : IQuestionRelay
{
    private readonly IWaitingQuestionStore _store;
    private readonly IPresence? _presence;
    private readonly Func<TimeSpan> _notifyAfter;
    private readonly Func<INotifications?> _notifications;
    private readonly Func<bool> _available;
    private readonly TimeProvider _clock;
    private readonly ILogger _logger;

    // One publish at a time, so the escalation timer and an away ask never publish the
    // same question twice: each re-reads the question under the gate before it publishes.
    private readonly SemaphoreSlim _publishing = new(1, 1);

    // What a held get_answer awaits: one completion source per question somebody is
    // waiting on, completed and dropped the moment the question is answered or closed.
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource> _ended = new();

    /// <param name="store">This machine's waiting questions.</param>
    /// <param name="presence">Where the person is; null reads as at the keyboard, so a head
    /// that cannot read input still publishes a question nobody answers once the delay passes.</param>
    /// <param name="notifyAfter">The "notify after" setting, read on every question.</param>
    /// <param name="notifications">Notifications, resolved when publishing so the two
    /// singletons never wait on each other; null when the host composed none.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="logger">Where a refused publish or close is noted.</param>
    /// <param name="available">Whether the <c>notifications</c> flag is on, read on every
    /// question; always on when null. With it off nothing is registered (ADR 0041 §5).</param>
    public QuestionRelay(
        IWaitingQuestionStore store,
        IPresence? presence,
        Func<TimeSpan> notifyAfter,
        Func<INotifications?> notifications,
        TimeProvider? clock = null,
        ILogger<QuestionRelay>? logger = null,
        Func<bool>? available = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(notifyAfter);
        ArgumentNullException.ThrowIfNull(notifications);

        _store = store;
        _presence = presence;
        _notifyAfter = notifyAfter;
        _notifications = notifications;
        _available = available ?? (() => true);
        _clock = clock ?? TimeProvider.System;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    private DateTimeOffset Now => _clock.GetUtcNow();

    /// <summary>
    /// Registers a question <paramref name="sessionId"/> asked: a dialog of one to four
    /// questions when <paramref name="toolUseId"/> is given, else one free-text question.
    /// Answers the waiting question, with its route. A second call for the same session
    /// and tool use answers the question already registered and publishes nothing new.
    /// <para>
    /// With the <c>notifications</c> flag off, or no Notifications composed, nothing is
    /// registered and the call answers <c>notifications.not_available</c>, so the session
    /// asks in place (ADR 0041 §5).
    /// </para>
    /// <para>
    /// When the person is away the question is published at once. A publish Notifications
    /// refuses anyway is answered as that failure; the question stays InSession and is
    /// tried again once the notify delay passes.
    /// </para>
    /// </summary>
    public async Task<Result<WaitingQuestion>> AskAsync(
        string sessionId,
        string? toolUseId,
        IReadOnlyList<RelayQuestion> questions,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return Error.Validation("relay.session_required", "A waiting question needs the session that asked.");

        var kind = string.IsNullOrWhiteSpace(toolUseId) ? WaitingQuestionKind.Prose : WaitingQuestionKind.Dialog;

        if (WaitingQuestions.Problem(kind, questions) is { } problem)
            return Error.Validation("relay.questions_invalid", problem);

        if (!_available() || _notifications() is null) return NotAvailable();

        var candidate = WaitingQuestions.Ask(sessionId, toolUseId, questions, _presence?.IsAway ?? false, _notifyAfter(), Now);
        var registered = await _store.RegisterAsync(candidate, cancellationToken).ConfigureAwait(false);

        if (registered.Id != candidate.Id || registered.Route != WaitingQuestionRoute.Backlog)
            return registered;

        return await PublishIfDueAsync(registered.Id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The waiting question with <paramref name="id"/>, or null.</summary>
    public Task<WaitingQuestion?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        _store.GetAsync(id, cancellationToken);

    /// <summary>
    /// Records that the dialog's hook asked for the answer now, so an answer that arrives
    /// within <see cref="WaitingQuestions.HookPollWindow"/> goes to it rather than into a
    /// delivery. Answers the question as it now stands, or null for an unknown id.
    /// </summary>
    public async Task<WaitingQuestion?> RecordPollAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var now = Now;
        var outcome = await _store.ChangeAsync(
            id,
            question => question.IsOpen
                ? WaitingQuestionOutcome.Done(WaitingQuestions.Polled(question, now))
                : WaitingQuestionOutcome.Refused(question, "The question is no longer open."),
            cancellationToken).ConfigureAwait(false);

        return outcome.Question;
    }

    /// <summary>
    /// Records the hook's poll (<see cref="RecordPollAsync"/>), then holds until the question
    /// is answered or closed, or <paramref name="wait"/> passes, by awaiting the completion
    /// source an answer or a close completes — never a loop. Answers the question as it then
    /// stands, or null for an unknown id.
    /// </summary>
    public async Task<WaitingQuestion?> WaitAsync(Guid id, TimeSpan wait, CancellationToken cancellationToken = default)
    {
        if (wait <= TimeSpan.Zero) return await RecordPollAsync(id, cancellationToken).ConfigureAwait(false);

        // Taken before the state is read, so an answer that lands in between completes it.
        var ended = _ended.GetOrAdd(id, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

        var polled = await RecordPollAsync(id, cancellationToken).ConfigureAwait(false);

        if (polled is null || !polled.IsOpen)
        {
            if (polled is null) _ended.TryRemove(new KeyValuePair<Guid, TaskCompletionSource>(id, ended));
            else Release(id);

            return polled;
        }

        try
        {
            await ended.Task.WaitAsync(wait, _clock, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // The wait ran out with the question still open: the caller asks again.
        }

        return await _store.GetAsync(id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Closes an open question with <paramref name="reason"/> — answered in the session with
    /// <paramref name="answers"/>, one per question; timed out; or session ended — and closes
    /// each of its notifications through <see cref="INotifications.CloseAsync"/>, with the
    /// answer given in the session as the reason so every viewer shows where it was answered.
    /// </summary>
    public async Task<Result<WaitingQuestion>> CloseAsync(
        Guid id,
        WaitingQuestionCloseReason reason,
        IReadOnlyList<RelayAnswer>? answers = null,
        CancellationToken cancellationToken = default)
    {
        var now = Now;
        var outcome = await _store.ChangeAsync(
            id,
            question => WaitingQuestions.Close(question, reason, answers, now),
            cancellationToken).ConfigureAwait(false);

        if (outcome.Question is null)
            return Error.NotFound("relay.not_found", $"No waiting question has the id '{id:D}'.");

        if (!outcome.Succeeded)
        {
            return outcome.Question.IsOpen
                ? Error.Validation("relay.answers_invalid", outcome.Refusal!)
                : Error.Conflict("relay.already_ended", outcome.Refusal!);
        }

        Release(id);

        await CloseNotificationsAsync(outcome.Question, cancellationToken).ConfigureAwait(false);

        return outcome.Question;
    }

    /// <summary>Closes every open question <paramref name="sessionId"/> holds as session
    /// ended, and answers how many it closed.</summary>
    public async Task<int> CloseForSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var closed = 0;

        foreach (var question in await _store.ListAsync(openOnly: true, cancellationToken).ConfigureAwait(false))
        {
            if (!string.Equals(question.SessionId, sessionId, StringComparison.Ordinal)) continue;

            var result = await CloseAsync(question.Id, WaitingQuestionCloseReason.SessionEnded, null, cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess) closed++;
        }

        return closed;
    }

    /// <summary>The sessions holding an open waiting question now.</summary>
    public async Task<IReadOnlySet<string>> WaitingSessionsAsync(CancellationToken cancellationToken = default)
    {
        var open = await _store.ListAsync(openOnly: true, cancellationToken).ConfigureAwait(false);

        return open.Select(question => question.SessionId).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Publishes every InSession question whose notify delay has passed — a dialog still
    /// open in the session included — and answers how many it published. What the
    /// escalation timer runs.
    /// </summary>
    public async Task<int> EscalateDueAsync(CancellationToken cancellationToken = default)
    {
        if (!_available()) return 0;

        var now = Now;
        var published = 0;

        foreach (var question in await _store.ListAsync(openOnly: true, cancellationToken).ConfigureAwait(false))
        {
            if (!question.IsDueAt(now)) continue;

            try
            {
                var result = await PublishIfDueAsync(question.Id, cancellationToken).ConfigureAwait(false);
                if (result is { IsSuccess: true, Value.State: WaitingQuestionState.Notified }) published++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(exception, "Publishing waiting question {Question} failed; the others are still tried.", question.Id);
            }
        }

        return published;
    }

    /// <summary>
    /// Takes an accepted answer for the <c>sessions</c> source. Answers false when its
    /// source key names no waiting question — a Personal Validation answer, say — so the
    /// handler can route it elsewhere. Idempotent: an answer already recorded changes nothing.
    /// </summary>
    public async Task<bool> HandleAnswerAsync(NotificationAnswer answer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(answer);

        if (!Guid.TryParse(answer.Source.Key, out var id)) return false;

        var now = Now;

        // An answer that outruns the write recording the question's notifications is not
        // taken, so Notifications replays it on the next start rather than losing it.
        if (await _store.GetAsync(id, cancellationToken).ConfigureAwait(false) is { State: WaitingQuestionState.InSession })
        {
            throw new InvalidOperationException(
                $"Waiting question {id:D} is still being published; its answer is replayed once it is.");
        }

        var outcome = await _store.ChangeAsync(
            id,
            question => WaitingQuestions.IndexOf(question, answer.NotificationId) is var index and >= 0
                ? WaitingQuestions.Answer(question, answer.NotificationId, ToRelayAnswer(question.Questions[index], answer), now)
                : WaitingQuestionOutcome.Refused(question, "The notification is not one of this question's."),
            cancellationToken).ConfigureAwait(false);

        if (outcome.Question is null)
        {
            _logger.LogDebug("An answer names waiting question {Question}, which this desktop does not hold.", id);
            return true;
        }

        if (!outcome.Succeeded)
        {
            _logger.LogDebug("The answer to waiting question {Question} changed nothing: {Reason}", id, outcome.Refusal);
            return true;
        }

        if (outcome.Question.State == WaitingQuestionState.Answered)
        {
            Release(id);

            _logger.LogDebug(
                outcome.Question.Delivery is null
                    ? "Waiting question {Question} is answered; its hook takes the answer."
                    : "Waiting question {Question} is answered; its answer waits as a delivery.",
                id);
        }

        return true;
    }

    /// <summary>The source key of a waiting question's notifications: its id.</summary>
    public static string SourceKey(WaitingQuestion question)
    {
        ArgumentNullException.ThrowIfNull(question);

        return question.Id.ToString("D");
    }

    /// <summary>The choice id of option <paramref name="index"/>, from zero.</summary>
    public static string ChoiceId(int index) => $"option-{(index + 1).ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// The notification one question of <paramref name="question"/> is published as: a
    /// Question from the <c>sessions</c> source keyed by the waiting question's id, about
    /// the session. Options become single or multiple choice — with an open answer beside
    /// a single choice, as the dialog's "Other" — and a question without options takes an
    /// open answer only.
    /// </summary>
    public static NotificationDraft Draft(WaitingQuestion question, int index)
    {
        ArgumentNullException.ThrowIfNull(question);

        var asked = question.Questions[index];
        var title = OneLine(string.IsNullOrWhiteSpace(asked.Header) ? asked.Question : asked.Header!, Limits.Title);

        return new NotificationDraft(
            new NotificationSource(SessionNotificationSubjects.Context, SourceKey(question)),
            new NotificationSubject(SessionNotificationSubjects.Context, SessionNotificationSubjects.Session, question.SessionId),
            NotificationKind.Question,
            NotificationSeverity.Info,
            title,
            OneLine(asked.Question, Limits.Summary),
            Body(question, asked, index),
            Spec(asked));
    }

    /// <summary>The answer spec of one question; see <see cref="Draft"/>.</summary>
    public static AnswerSpec Spec(RelayQuestion question)
    {
        ArgumentNullException.ThrowIfNull(question);

        if (question.IsOpenOnly) return new AnswerSpec([], Open: new OpenAnswer("Your answer"));

        var choices = question.Choices
            .Select((option, i) => new AnswerChoice(ChoiceId(i), OneLine(option.Label, Limits.Title)))
            .ToArray();

        // Notifications refuses an open answer beside a multiple choice
        // (AnswerSpecRules), so "Other" is offered beside a single choice only.
        return question.MultiSelect
            ? new AnswerSpec(choices, AllowMultiple: true, MinChoices: 1)
            : new AnswerSpec(choices, Open: new OpenAnswer("Other"));
    }

    /// <summary>The accepted answer to one question, as option labels or open text.</summary>
    public static RelayAnswer ToRelayAnswer(RelayQuestion question, NotificationAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(answer);

        var labels = new List<string>();

        foreach (var id in answer.ChoiceIds)
        {
            for (var i = 0; i < question.Choices.Count; i++)
            {
                if (string.Equals(ChoiceId(i), id, StringComparison.Ordinal)) labels.Add(question.Choices[i].Label);
            }
        }

        return new RelayAnswer(labels, string.IsNullOrWhiteSpace(answer.Text) ? null : answer.Text.Trim());
    }

    /// <summary>The reason each notification of <paramref name="question"/> closes with.</summary>
    public static string CloseReasonFor(WaitingQuestion question, int index)
    {
        ArgumentNullException.ThrowIfNull(question);

        return question.CloseReason switch
        {
            WaitingQuestionCloseReason.AnsweredInSession when question.Answers.ElementAtOrDefault(index) is { } given =>
                $"Answered in the session: {given.Display}",
            WaitingQuestionCloseReason.AnsweredInSession => "Answered in the session",
            WaitingQuestionCloseReason.TimedOut => "The question timed out in the session",
            WaitingQuestionCloseReason.SessionEnded => SessionNotificationSubjects.SessionEndedReason,
            _ => "Not published"
        };
    }

    async Task<QuestionRelayResult> IQuestionRelay.AskAsync(
        string sessionId,
        string? toolUseId,
        IReadOnlyList<RelayQuestion> questions,
        CancellationToken cancellationToken) =>
        ToPort(await AskAsync(sessionId, toolUseId, questions, cancellationToken).ConfigureAwait(false));

    Task<WaitingQuestion?> IQuestionRelay.FindAsync(Guid id, CancellationToken cancellationToken) =>
        _store.GetAsync(id, cancellationToken);

    async Task<QuestionRelayResult> IQuestionRelay.CloseAsync(
        Guid id,
        WaitingQuestionCloseReason reason,
        IReadOnlyList<RelayAnswer>? answers,
        CancellationToken cancellationToken) =>
        ToPort(await CloseAsync(id, reason, answers, cancellationToken).ConfigureAwait(false));

    private static QuestionRelayResult ToPort(Result<WaitingQuestion> result) =>
        result.IsSuccess
            ? QuestionRelayResult.Of(result.Value)
            : QuestionRelayResult.Refused(result.Error.Code, result.Error.Message);

    /// <summary>Completes and drops the completion source a held wait on <paramref name="id"/> awaits.</summary>
    private void Release(Guid id)
    {
        if (_ended.TryRemove(id, out var ended)) ended.TrySetResult();
    }

    private static Error NotAvailable() =>
        Error.Unexpected("notifications.not_available", "Notifications are not available on this desktop.");

    private async Task<Result<WaitingQuestion>> PublishIfDueAsync(Guid id, CancellationToken cancellationToken)
    {
        await _publishing.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (await _store.GetAsync(id, cancellationToken).ConfigureAwait(false) is not { } question)
                return Error.NotFound("relay.not_found", $"No waiting question has the id '{id:D}'.");

            if (!question.IsDueAt(Now)) return question;

            if (_notifications() is not { } notifications) return NotAvailable();

            var ids = new List<Guid>(question.Questions.Count);

            for (var i = 0; i < question.Questions.Count; i++)
            {
                var published = await notifications.PublishAsync(Draft(question, i), cancellationToken).ConfigureAwait(false);

                if (published.IsFailure)
                {
                    _logger.LogDebug("Waiting question {Question} was not published: {Error}", id, published.Error.Message);
                    await CloseAllAsync(notifications, ids, "Not published", cancellationToken).ConfigureAwait(false);

                    // Held in the session and tried again once per notify delay, never on
                    // every tick: a refusal that keeps recurring must not buzz the phone.
                    var later = Now + _notifyAfter();
                    await _store.ChangeAsync(id, held => WaitingQuestions.Postpone(held, later), cancellationToken).ConfigureAwait(false);

                    return Result.Failure<WaitingQuestion>(published.Error);
                }

                ids.Add(published.Value);
            }

            var outcome = await _store.ChangeAsync(id, held => WaitingQuestions.Notify(held, ids), cancellationToken).ConfigureAwait(false);

            if (!outcome.Succeeded)
            {
                // Answered or closed in the session while publishing: take the notifications
                // back, with the reason it ended for.
                for (var i = 0; i < ids.Count; i++)
                {
                    var reason = outcome.Question is { } ended ? CloseReasonFor(ended, i) : "Not published";
                    await CloseAllAsync(notifications, [ids[i]], reason, cancellationToken).ConfigureAwait(false);
                }
            }

            return outcome.Question ?? question;
        }
        finally
        {
            _publishing.Release();
        }
    }

    private async Task CloseNotificationsAsync(WaitingQuestion question, CancellationToken cancellationToken)
    {
        if (question.NotificationIds.Count == 0 || _notifications() is not { } notifications) return;

        for (var i = 0; i < question.NotificationIds.Count; i++)
        {
            var closed = await notifications.CloseAsync(question.NotificationIds[i], CloseReasonFor(question, i), cancellationToken).ConfigureAwait(false);

            // One already answered in Backlog is settled, and stays as it was answered.
            if (closed.IsFailure)
            {
                _logger.LogDebug("Notification {Notification} of waiting question {Question} was not closed: {Error}",
                    question.NotificationIds[i], question.Id, closed.Error.Message);
            }
        }
    }

    private async Task CloseAllAsync(INotifications notifications, IEnumerable<Guid> ids, string reason, CancellationToken cancellationToken)
    {
        foreach (var notificationId in ids)
        {
            var closed = await notifications.CloseAsync(notificationId, reason, cancellationToken).ConfigureAwait(false);
            if (closed.IsFailure) _logger.LogDebug("Notification {Notification} was not closed: {Error}", notificationId, closed.Error.Message);
        }
    }

    private static string Body(WaitingQuestion question, RelayQuestion asked, int index)
    {
        var body = new StringBuilder();

        if (question.Questions.Count > 1)
        {
            body.Append("Question ").Append(index + 1).Append(" of ").Append(question.Questions.Count).AppendLine(" the session asked.").AppendLine();
        }

        body.AppendLine(asked.Question.Trim());

        if (asked.Choices.Count > 0)
        {
            body.AppendLine();

            foreach (var option in asked.Choices)
            {
                body.Append("- **").Append(option.Label.Trim()).Append("**");
                if (!string.IsNullOrWhiteSpace(option.Description)) body.Append(" — ").Append(option.Description.Trim());
                body.AppendLine();
            }
        }

        var text = body.ToString().TrimEnd();

        // Notifications measures the body in UTF-8 bytes: keep whole text elements until it fits.
        if (Encoding.UTF8.GetByteCount(text) <= Limits.Body) return text;

        var cut = new StringBuilder();
        var bytes = 0;
        var elements = StringInfo.GetTextElementEnumerator(text);

        while (elements.MoveNext())
        {
            var element = elements.GetTextElement();
            var size = Encoding.UTF8.GetByteCount(element);

            if (bytes + size > Limits.Body) break;

            cut.Append(element);
            bytes += size;
        }

        return cut.ToString();
    }

    private static string OneLine(string text, int limit)
    {
        var line = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return line.Length <= limit ? line : string.Concat(line.AsSpan(0, limit - 1), "…");
    }

    private static class Limits
    {
        public const int Title = 80;
        public const int Summary = 180;
        public const int Body = 32 * 1024;
    }
}
