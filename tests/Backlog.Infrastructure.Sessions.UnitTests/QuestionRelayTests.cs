using Backlog.Infrastructure.Sessions;
using Backlog.Modules.Notifications.Abstractions;
using Backlog.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Sessions.UnitTests;

/// <summary>
/// The question relay (local ADR 0041): routing by presence, publishing one Question per
/// question, escalation once the notify delay passes, answers routed to the hook or into a
/// delivery through the Sessions answer handler, and every close reason.
/// </summary>
public sealed class QuestionRelayTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan NotifyAfter = TimeSpan.FromMinutes(15);

    private static readonly RelayQuestion Pick = new(
        "Which database should the cache use?",
        "Database",
        [new RelayOption("SQLite", "A local file"), new RelayOption("Postgres", "A server")]);

    private static readonly RelayQuestion Tags = new(
        "Which tags apply?",
        "Tags",
        [new RelayOption("ui"), new RelayOption("api"), new RelayOption("docs")],
        MultiSelect: true);

    private readonly FakeTimeProvider _clock = new(Noon);
    private readonly RecordingNotifications _notifications = new();
    private readonly InMemoryWaitingQuestionStore _store = new();
    private readonly FakePresence _presence = new();
    private readonly QuestionRelay _relay;

    public QuestionRelayTests()
    {
        _relay = new QuestionRelay(_store, _presence, () => NotifyAfter, () => _notifications, _clock);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => GC.SuppressFinalize(this);

    [Fact]
    public async Task Asked_while_away_it_is_routed_to_backlog_and_published_at_once()
    {
        _presence.IsAway = true;

        var asked = (await _relay.AskAsync("session-1", "toolu_1", [Pick], Ct)).Value;

        Assert.Equal(WaitingQuestionRoute.Backlog, asked.Route);
        Assert.Equal(WaitingQuestionState.Notified, asked.State);
        Assert.Single(_notifications.Published);
        Assert.Equal(_notifications.Published[0].Id, asked.NotificationIds[0]);
    }

    [Fact]
    public async Task Asked_at_the_keyboard_it_is_routed_to_the_session_and_held()
    {
        var asked = (await _relay.AskAsync("session-1", "toolu_1", [Pick], Ct)).Value;

        Assert.Equal(WaitingQuestionRoute.Session, asked.Route);
        Assert.Equal(WaitingQuestionState.InSession, asked.State);
        Assert.Equal(Noon + NotifyAfter, asked.EscalateAt);
        Assert.Empty(_notifications.Published);
    }

    [Fact]
    public async Task A_held_question_is_published_by_the_escalation_timer_once_the_delay_passes()
    {
        using var escalation = new QuestionRelayEscalation(_relay, _clock);
        var asked = (await _relay.AskAsync("session-1", "toolu_1", [Pick], Ct)).Value;

        _clock.Advance(NotifyAfter - TimeSpan.FromSeconds(30));
        await escalation.Idle;
        Assert.Empty(_notifications.Published);

        _clock.Advance(QuestionRelayEscalation.Interval);
        await escalation.Idle;

        Assert.Single(_notifications.Published);
        Assert.Equal(WaitingQuestionState.Notified, (await _relay.GetAsync(asked.Id, Ct))!.State);

        _clock.Advance(QuestionRelayEscalation.Interval);
        await escalation.Idle;
        Assert.Single(_notifications.Published);
    }

    [Fact]
    public async Task A_question_answered_in_the_session_before_the_delay_is_never_published()
    {
        var asked = (await _relay.AskAsync("session-1", "toolu_1", [Pick], Ct)).Value;
        await _relay.CloseAsync(asked.Id, WaitingQuestionCloseReason.AnsweredInSession, [new RelayAnswer(["SQLite"])], Ct);

        _clock.Advance(NotifyAfter);

        Assert.Equal(0, await _relay.EscalateDueAsync(Ct));
        Assert.Empty(_notifications.Published);
        Assert.Empty(_notifications.ClosedById);
    }

    [Fact]
    public async Task Registering_the_same_session_and_tool_use_twice_returns_the_existing_question()
    {
        _presence.IsAway = true;

        var first = (await _relay.AskAsync("session-1", "toolu_1", [Pick], Ct)).Value;
        var second = (await _relay.AskAsync("session-1", "toolu_1", [Pick], Ct)).Value;

        Assert.Equal(first.Id, second.Id);
        Assert.Single(_notifications.Published);
        Assert.Single(await _store.ListAsync(cancellationToken: Ct));
    }

    [Fact]
    public async Task Each_question_is_published_as_a_question_about_the_session_keyed_by_the_waiting_question()
    {
        _presence.IsAway = true;

        var asked = (await _relay.AskAsync("session-1", "toolu_1", [Pick, Tags], Ct)).Value;

        Assert.Equal(2, _notifications.Published.Count);

        foreach (var (_, draft) in _notifications.Published)
        {
            Assert.Equal(NotificationKind.Question, draft.Kind);
            Assert.Equal(new NotificationSource("sessions", asked.Id.ToString("D")), draft.Source);
            Assert.Equal(new NotificationSubject("sessions", "session", "session-1"), draft.Subject);
        }

        var single = _notifications.Published[0].Draft;
        Assert.Equal("Database", single.Title);
        Assert.Equal(["SQLite", "Postgres"], single.Answer!.Choices.Select(choice => choice.Label));
        Assert.False(single.Answer.AllowMultiple);
        Assert.NotNull(single.Answer.Open);

        var multiple = _notifications.Published[1].Draft.Answer!;
        Assert.True(multiple.AllowMultiple);
        Assert.Equal(3, multiple.Choices.Count);
    }

    [Fact]
    public async Task A_prose_question_is_an_open_answer_only()
    {
        _presence.IsAway = true;

        var asked = (await _relay.AskAsync("session-1", null, [RelayQuestion.Prose("Shall I merge it?")], Ct)).Value;

        Assert.Equal(WaitingQuestionKind.Prose, asked.Kind);
        var spec = Assert.Single(_notifications.Published).Draft.Answer!;
        Assert.Empty(spec.Choices);
        Assert.NotNull(spec.Open);
    }

    [Fact]
    public async Task Questions_that_are_not_a_dialog_or_one_prose_question_are_refused()
    {
        var refused = await _relay.AskAsync("session-1", null, [Pick], Ct);

        Assert.True(refused.IsFailure);
        Assert.Empty(await _store.ListAsync(cancellationToken: Ct));
    }

    [Fact]
    public async Task A_dialog_is_answered_once_every_one_of_its_notifications_is()
    {
        _presence.IsAway = true;
        var handler = new SessionValidationAnswers(new LocalDeliverySurfaceLifecycle(Path.GetTempPath(), "m", "PC"), relay: _relay);
        var asked = (await _relay.AskAsync("session-1", "toolu_1", [Pick, Tags], Ct)).Value;

        await handler.HandleAnswerAsync(Answer(asked, 1, ["option-1", "option-3"]), Ct);
        Assert.Equal(WaitingQuestionState.Notified, (await _relay.GetAsync(asked.Id, Ct))!.State);

        await handler.HandleAnswerAsync(Answer(asked, 0, [], "MySQL"), Ct);
        var answered = (await _relay.GetAsync(asked.Id, Ct))!;

        Assert.Equal(WaitingQuestionState.Answered, answered.State);
        Assert.Equal([RelayAnswer.Open("MySQL"), new RelayAnswer(["ui", "docs"])], answered.Answers);
    }

    [Fact]
    public async Task A_replayed_answer_changes_nothing()
    {
        _presence.IsAway = true;
        var asked = (await _relay.AskAsync("session-1", "toolu_1", [Pick], Ct)).Value;
        var answer = Answer(asked, 0, ["option-2"]);

        Assert.True(await _relay.HandleAnswerAsync(answer, Ct));
        var once = (await _relay.GetAsync(asked.Id, Ct))!;

        Assert.True(await _relay.HandleAnswerAsync(answer, Ct));
        Assert.Equal(once, await _relay.GetAsync(asked.Id, Ct));
    }

    [Fact]
    public async Task An_answer_goes_to_the_hook_that_is_still_polling()
    {
        _presence.IsAway = true;
        var asked = (await _relay.AskAsync("session-1", "toolu_1", [Pick], Ct)).Value;

        _clock.Advance(TimeSpan.FromMinutes(30));
        await _relay.RecordPollAsync(asked.Id, Ct);
        _clock.Advance(TimeSpan.FromSeconds(55));

        await _relay.HandleAnswerAsync(Answer(asked, 0, ["option-1"]), Ct);
        var answered = (await _relay.GetAsync(asked.Id, Ct))!;

        Assert.Equal(WaitingQuestionState.Answered, answered.State);
        Assert.Null(answered.Delivery);
    }

    [Fact]
    public async Task An_answer_nobody_polls_for_becomes_a_pending_delivery()
    {
        _presence.IsAway = true;
        var asked = (await _relay.AskAsync("session-1", "toolu_1", [Pick], Ct)).Value;
        await _relay.RecordPollAsync(asked.Id, Ct);

        _clock.Advance(TimeSpan.FromMinutes(3));
        await _relay.HandleAnswerAsync(Answer(asked, 0, ["option-1"]), Ct);

        Assert.Equal(AnswerDeliveryState.Pending, (await _relay.GetAsync(asked.Id, Ct))!.Delivery);
    }

    [Fact]
    public async Task An_answer_to_an_escalated_dialog_becomes_a_pending_delivery()
    {
        var asked = (await _relay.AskAsync("session-1", "toolu_1", [Pick], Ct)).Value;
        _clock.Advance(NotifyAfter);
        await _relay.EscalateDueAsync(Ct);

        await _relay.HandleAnswerAsync(Answer((await _relay.GetAsync(asked.Id, Ct))!, 0, ["option-1"]), Ct);

        Assert.Equal(AnswerDeliveryState.Pending, (await _relay.GetAsync(asked.Id, Ct))!.Delivery);
    }

    [Fact]
    public async Task Answered_in_the_session_closes_its_notifications_with_the_answers()
    {
        _presence.IsAway = true;
        var asked = (await _relay.AskAsync("session-1", "toolu_1", [Pick, Tags], Ct)).Value;

        var closed = await _relay.CloseAsync(
            asked.Id,
            WaitingQuestionCloseReason.AnsweredInSession,
            [new RelayAnswer(["SQLite"]), new RelayAnswer(["ui", "api"])],
            Ct);

        Assert.Equal(WaitingQuestionState.Answered, closed.Value.State);
        Assert.Null(closed.Value.Delivery);
        Assert.Equal(
            [(asked.NotificationIds[0], "Answered in the session: SQLite"), (asked.NotificationIds[1], "Answered in the session: ui, api")],
            _notifications.ClosedById);
    }

    [Theory]
    [InlineData(WaitingQuestionCloseReason.TimedOut, "The question timed out in the session")]
    [InlineData(WaitingQuestionCloseReason.SessionEnded, "Session archived")]
    public async Task Timed_out_or_session_ended_closes_its_notifications(WaitingQuestionCloseReason reason, string because)
    {
        _presence.IsAway = true;
        var asked = (await _relay.AskAsync("session-1", "toolu_1", [Pick], Ct)).Value;

        var closed = await _relay.CloseAsync(asked.Id, reason, null, Ct);

        Assert.Equal(WaitingQuestionState.Closed, closed.Value.State);
        Assert.Equal(reason, closed.Value.CloseReason);
        Assert.Equal([(asked.NotificationIds[0], because)], _notifications.ClosedById);
        Assert.True((await _relay.CloseAsync(asked.Id, reason, null, Ct)).IsFailure);
    }

    [Fact]
    public async Task An_open_question_makes_a_live_session_waiting()
    {
        await _relay.AskAsync("session-1", "toolu_1", [Pick], Ct);
        var running = Session("session-1", AgentSessionState.Running);
        var other = Session("session-2", AgentSessionState.Running);
        var source = new WaitingSessionSource(new FixedSource(running, other), () => _relay);

        var catalog = await source.GetSessionsAsync(Ct);

        Assert.Equal(AgentSessionState.Waiting, catalog.Sessions.Single(s => s.Id == "session-1").State);
        Assert.Equal(AgentSessionState.Running, catalog.Sessions.Single(s => s.Id == "session-2").State);
    }

    [Fact]
    public async Task A_finished_idle_session_keeps_its_question_open()
    {
        var asked = (await _relay.AskAsync("session-1", null, [RelayQuestion.Prose("Merge it?")], Ct)).Value;
        var idle = new WaitingSessionSource(new FixedSource(Session("session-1", AgentSessionState.Finished)), () => _relay);

        Assert.Equal(AgentSessionState.Finished, (await idle.GetSessionsAsync(Ct)).Sessions[0].State);
        Assert.True((await _relay.GetAsync(asked.Id, Ct))!.IsOpen);
    }

    [Fact]
    public async Task Archiving_the_session_closes_its_questions_as_session_ended()
    {
        _presence.IsAway = true;
        var asked = (await _relay.AskAsync("session-1", "toolu_1", [Pick], Ct)).Value;
        var open = Session("session-1", AgentSessionState.Running) with { Archived = false };
        var watching = new FixedSource(open);
        var source = new SessionEndWatchingSource(watching, () => _notifications, relay: () => _relay);

        await source.GetSessionsAsync(Ct);
        watching.Sessions = [open with { Archived = true }];
        await source.GetSessionsAsync(Ct);

        var closed = (await _relay.GetAsync(asked.Id, Ct))!;
        Assert.Equal(WaitingQuestionState.Closed, closed.State);
        Assert.Equal(WaitingQuestionCloseReason.SessionEnded, closed.CloseReason);
        Assert.Equal([(asked.NotificationIds[0], "Session archived")], _notifications.ClosedById);
    }

    [Fact]
    public async Task With_notifications_unavailable_nothing_is_registered()
    {
        var off = new QuestionRelay(_store, _presence, () => NotifyAfter, () => _notifications, _clock, available: () => false);

        var asked = await off.AskAsync("session-1", "toolu_1", [Pick], Ct);

        Assert.True(asked.IsFailure);
        Assert.Equal("notifications.not_available", asked.Error.Code);
        Assert.Empty(await _store.ListAsync(cancellationToken: Ct));
        Assert.Equal(0, await off.EscalateDueAsync(Ct));
    }

    [Fact]
    public async Task A_publish_notifications_refuses_is_held_and_tried_again_after_the_delay()
    {
        _presence.IsAway = true;
        var refusing = new RefusingNotifications();
        var relay = new QuestionRelay(_store, _presence, () => NotifyAfter, () => refusing, _clock);

        var asked = await relay.AskAsync("session-1", "toolu_1", [Pick], Ct);

        Assert.True(asked.IsFailure);
        var held = Assert.Single(await _store.ListAsync(cancellationToken: Ct));
        Assert.Equal(WaitingQuestionState.InSession, held.State);
        Assert.Equal(Noon + NotifyAfter, held.EscalateAt);

        _clock.Advance(QuestionRelayEscalation.Interval);
        Assert.Equal(0, await relay.EscalateDueAsync(Ct));
        Assert.Equal(1, refusing.Attempts);

        _clock.Advance(NotifyAfter);
        Assert.Equal(1, await _relay.EscalateDueAsync(Ct));
    }

    [Fact]
    public async Task An_answer_that_outruns_its_publish_is_left_for_the_replay()
    {
        var asked = (await _relay.AskAsync("session-1", "toolu_1", [Pick], Ct)).Value;
        var early = new NotificationAnswer(Guid.NewGuid(), new NotificationSource("sessions", asked.Id.ToString("D")), Guid.NewGuid(), Guid.NewGuid(), ["option-1"], null, Noon);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _relay.HandleAnswerAsync(early, Ct));
    }

    [Fact]
    public async Task A_personal_validation_answer_is_not_the_relays()
    {
        var validation = new NotificationAnswer(Guid.NewGuid(), new NotificationSource("sessions", "run-1@D:\\w"), Guid.NewGuid(), Guid.NewGuid(), ["approve"], null, Noon);

        Assert.False(await _relay.HandleAnswerAsync(validation, Ct));
    }

    private NotificationAnswer Answer(WaitingQuestion question, int index, IReadOnlyList<string> choices, string? text = null) =>
        new(question.NotificationIds[index], new NotificationSource("sessions", question.Id.ToString("D")), Guid.NewGuid(), Guid.NewGuid(), choices, text, _clock.GetUtcNow());

    private static AgentSession Session(string id, AgentSessionState state) =>
        new(id, AgentSessionKind.Claude, "machine", "PC", id, @"D:\work", null, null, Noon, Noon, state, null, AgentSessionOrigin.Local);

    private sealed class FakePresence : IPresence
    {
        public bool IsAway { get; set; }

        public DateTimeOffset? LastInputAt => null;

        public DateTimeOffset? LastPromptAt(string sessionId) => null;
    }

    private sealed class FixedSource(params AgentSession[] sessions) : IAgentSessionSource
    {
        public AgentSession[] Sessions { get; set; } = sessions;

        public Task<AgentSessionCatalog> GetSessionsAsync(CancellationToken cancellationToken = default) =>
            GetSessionsAsync(AgentSessionQuery.Newest, cancellationToken);

        public Task<AgentSessionCatalog> GetSessionsAsync(AgentSessionQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AgentSessionCatalog(Sessions, [], Sessions.Length));
    }

    private sealed class RefusingNotifications : INotifications
    {
        public int Attempts { get; private set; }

        public Task<Backlog.SharedKernel.Results.Result<Guid>> PublishAsync(NotificationDraft draft, CancellationToken cancellationToken = default)
        {
            Attempts++;
            return Task.FromResult(Backlog.SharedKernel.Results.Result.Failure<Guid>(
                Backlog.SharedKernel.Results.Error.Unexpected("notifications.not_available", "off")));
        }

        public Task<Backlog.SharedKernel.Results.Result> CloseAsync(Guid notificationId, string? reason = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(Backlog.SharedKernel.Results.Result.Success());

        public Task<Backlog.SharedKernel.Results.Result<int>> CloseForSubjectAsync(NotificationSubject subject, string reason, CancellationToken cancellationToken = default) =>
            Task.FromResult(Backlog.SharedKernel.Results.Result.Success(0));

        public Task<Backlog.SharedKernel.Results.Result<NotificationAnswer?>> GetAnswerAsync(Guid notificationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Backlog.SharedKernel.Results.Result.Success<NotificationAnswer?>(null));
    }
}

/// <summary>An <see cref="IWaitingQuestionStore"/> in memory, applying the same
/// idempotent registering and one-write transitions the SQLite store does.</summary>
internal sealed class InMemoryWaitingQuestionStore : IWaitingQuestionStore
{
    private readonly Dictionary<Guid, WaitingQuestion> _held = [];
    private readonly Lock _gate = new();

    public Task<WaitingQuestion> RegisterAsync(WaitingQuestion candidate, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (WaitingQuestions.DuplicateOf(candidate, _held.Values) is { } registered) return Task.FromResult(registered);

            _held[candidate.Id] = candidate;
            return Task.FromResult(candidate);
        }
    }

    public Task<WaitingQuestion?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (_gate) return Task.FromResult(_held.GetValueOrDefault(id));
    }

    public Task<IReadOnlyList<WaitingQuestion>> ListAsync(bool openOnly = false, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<WaitingQuestion>>(
                [.. _held.Values.Where(question => !openOnly || question.IsOpen).OrderBy(question => question.AskedAt)]);
        }
    }

    public Task<WaitingQuestionOutcome> ChangeAsync(Guid id, Func<WaitingQuestion, WaitingQuestionOutcome> transition, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_held.TryGetValue(id, out var question)) return Task.FromResult(WaitingQuestionOutcome.Refused(null, "unknown"));

            var outcome = transition(question);
            if (outcome is { Succeeded: true, Question: { } changed }) _held[id] = changed;

            return Task.FromResult(outcome);
        }
    }
}
