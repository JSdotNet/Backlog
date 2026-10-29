using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Modules.Sessions.UnitTests;

/// <summary>
/// The Waiting Question's states and transitions (local ADR 0041,
/// <c>.devbook/domain/sessions/domain.invariants.md#question-relay</c>), as pure functions of
/// the question and the clock.
/// </summary>
public sealed class WaitingQuestionsTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan NotifyAfter = TimeSpan.FromMinutes(15);

    private static readonly RelayQuestion Pick = new(
        "Which database?",
        "Database",
        [new RelayOption("SQLite", "Local file"), new RelayOption("Postgres", "Server")]);

    private static readonly RelayQuestion Tags = new(
        "Which tags?",
        "Tags",
        [new RelayOption("ui"), new RelayOption("api"), new RelayOption("docs")],
        MultiSelect: true);

    private static WaitingQuestion Dialog(bool away = false, params RelayQuestion[] questions) =>
        WaitingQuestions.Ask("session-1", "toolu_1", questions.Length == 0 ? [Pick] : questions, away, NotifyAfter, Noon);

    private static WaitingQuestion Notified(WaitingQuestion question) =>
        WaitingQuestions.Notify(question, [.. question.Questions.Select(_ => Guid.NewGuid())]).Question!;

    [Fact]
    public void Asked_at_the_keyboard_it_stays_in_the_session_until_the_notify_delay_passes()
    {
        var question = Dialog(away: false);

        Assert.Equal(WaitingQuestionRoute.Session, question.Route);
        Assert.Equal(WaitingQuestionState.InSession, question.State);
        Assert.Equal(Noon + NotifyAfter, question.EscalateAt);
        Assert.False(question.IsDueAt(Noon + NotifyAfter - TimeSpan.FromSeconds(1)));
        Assert.True(question.IsDueAt(Noon + NotifyAfter));
    }

    [Fact]
    public void Asked_while_away_it_is_routed_to_backlog_and_due_at_once()
    {
        var question = Dialog(away: true);

        Assert.Equal(WaitingQuestionRoute.Backlog, question.Route);
        Assert.True(question.IsDueAt(Noon));
    }

    [Fact]
    public void A_tool_use_id_makes_a_dialog_and_none_makes_a_prose_question()
    {
        Assert.Equal(WaitingQuestionKind.Dialog, Dialog().Kind);

        var prose = WaitingQuestions.Ask("session-1", null, [RelayQuestion.Prose("Ship it?")], false, NotifyAfter, Noon);

        Assert.Equal(WaitingQuestionKind.Prose, prose.Kind);
        Assert.Null(prose.ToolUseId);
    }

    [Fact]
    public void A_prose_question_is_one_free_text_question()
    {
        Assert.Throws<ArgumentException>(() =>
            WaitingQuestions.Ask("session-1", null, [RelayQuestion.Prose("A?"), RelayQuestion.Prose("B?")], false, NotifyAfter, Noon));
        Assert.Throws<ArgumentException>(() =>
            WaitingQuestions.Ask("session-1", " ", [Pick], false, NotifyAfter, Noon));
    }

    [Fact]
    public void A_dialog_holds_one_to_four_questions_of_two_to_four_options()
    {
        Assert.Null(WaitingQuestions.Problem(WaitingQuestionKind.Dialog, [Pick, Tags, Pick, Tags]));
        Assert.NotNull(WaitingQuestions.Problem(WaitingQuestionKind.Dialog, [Pick, Tags, Pick, Tags, Pick]));
        Assert.NotNull(WaitingQuestions.Problem(WaitingQuestionKind.Dialog, []));
        Assert.NotNull(WaitingQuestions.Problem(WaitingQuestionKind.Dialog, [new RelayQuestion("One?", null, [new RelayOption("only")])]));
    }

    [Fact]
    public void Registering_the_same_session_and_tool_use_finds_the_first_question()
    {
        var first = Dialog();
        var again = Dialog();

        Assert.Equal(first, WaitingQuestions.DuplicateOf(again, [first]));
        Assert.Null(WaitingQuestions.DuplicateOf(again with { ToolUseId = "toolu_2" }, [first]));
        Assert.Null(WaitingQuestions.DuplicateOf(again with { SessionId = "session-2" }, [first]));

        var prose = WaitingQuestions.Ask("session-1", null, [RelayQuestion.Prose("Ship it?")], false, NotifyAfter, Noon);
        Assert.Null(WaitingQuestions.DuplicateOf(prose, [prose]));
    }

    [Fact]
    public void Publishing_takes_one_notification_per_question_and_never_goes_back()
    {
        var question = Dialog(false, Pick, Tags);

        Assert.False(WaitingQuestions.Notify(question, [Guid.NewGuid()]).Succeeded);

        var notified = Notified(question);

        Assert.Equal(WaitingQuestionState.Notified, notified.State);
        Assert.Equal(2, notified.NotificationIds.Count);
        Assert.False(WaitingQuestions.Notify(notified, [Guid.NewGuid(), Guid.NewGuid()]).Succeeded);
        Assert.False(notified.IsDueAt(Noon.AddDays(1)));
    }

    [Fact]
    public void A_dialog_is_answered_only_once_every_one_of_its_notifications_is()
    {
        var notified = Notified(Dialog(false, Pick, Tags));

        var first = WaitingQuestions.Answer(notified, notified.NotificationIds[1], new RelayAnswer(["ui", "api"]), Noon);
        Assert.True(first.Succeeded);
        Assert.Equal(WaitingQuestionState.Notified, first.Question!.State);

        var replayed = WaitingQuestions.Answer(first.Question, first.Question.NotificationIds[1], new RelayAnswer(["docs"]), Noon);
        Assert.False(replayed.Succeeded);

        var second = WaitingQuestions.Answer(first.Question, first.Question.NotificationIds[0], RelayAnswer.Open("MySQL"), Noon);
        Assert.Equal(WaitingQuestionState.Answered, second.Question!.State);
        Assert.Equal([RelayAnswer.Open("MySQL"), new RelayAnswer(["ui", "api"])], second.Question.Answers);
    }

    [Fact]
    public void An_answer_goes_to_the_hook_that_polled_within_two_minutes()
    {
        var notified = WaitingQuestions.Polled(Notified(Dialog(away: true)), Noon);

        var answered = WaitingQuestions.Answer(notified, notified.NotificationIds[0], new RelayAnswer(["SQLite"]), Noon.AddMinutes(2));

        Assert.Equal(WaitingQuestionState.Answered, answered.Question!.State);
        Assert.Null(answered.Question.Delivery);
    }

    [Fact]
    public void An_answer_becomes_a_pending_delivery_once_the_hook_stopped_polling()
    {
        var notified = WaitingQuestions.Polled(Notified(Dialog(away: true)), Noon);

        var answered = WaitingQuestions.Answer(notified, notified.NotificationIds[0], new RelayAnswer(["SQLite"]), Noon.AddMinutes(2).AddSeconds(1));

        Assert.Equal(AnswerDeliveryState.Pending, answered.Question!.Delivery);
    }

    [Fact]
    public void An_answer_to_a_question_held_in_the_session_becomes_a_delivery_even_while_polled()
    {
        var notified = WaitingQuestions.Polled(Notified(Dialog(away: false)), Noon);

        var answered = WaitingQuestions.Answer(notified, notified.NotificationIds[0], new RelayAnswer(["SQLite"]), Noon);

        Assert.Equal(AnswerDeliveryState.Pending, answered.Question!.Delivery);
    }

    [Fact]
    public void Answered_in_the_session_it_is_answered_with_the_answers_and_gets_no_delivery()
    {
        var notified = Notified(Dialog(false, Pick, Tags));
        IReadOnlyList<RelayAnswer> answers = [new RelayAnswer(["SQLite"]), new RelayAnswer(["docs"])];

        Assert.False(WaitingQuestions.Close(notified, WaitingQuestionCloseReason.AnsweredInSession, [answers[0]], Noon).Succeeded);

        var closed = WaitingQuestions.Close(notified, WaitingQuestionCloseReason.AnsweredInSession, answers, Noon).Question!;

        Assert.Equal(WaitingQuestionState.Answered, closed.State);
        Assert.Equal(WaitingQuestionCloseReason.AnsweredInSession, closed.CloseReason);
        Assert.Equal(answers, closed.Answers);
        Assert.Null(closed.Delivery);
        Assert.Equal(Noon, closed.EndedAt);
    }

    [Theory]
    [InlineData(WaitingQuestionCloseReason.TimedOut)]
    [InlineData(WaitingQuestionCloseReason.SessionEnded)]
    public void Timed_out_or_session_ended_it_is_closed(WaitingQuestionCloseReason reason)
    {
        foreach (var open in new[] { Dialog(), Notified(Dialog()) })
        {
            var closed = WaitingQuestions.Close(open, reason, null, Noon).Question!;

            Assert.Equal(WaitingQuestionState.Closed, closed.State);
            Assert.Equal(reason, closed.CloseReason);
        }
    }

    [Fact]
    public void Answered_and_Closed_are_final()
    {
        var closed = WaitingQuestions.Close(Dialog(), WaitingQuestionCloseReason.TimedOut, null, Noon).Question!;

        Assert.False(WaitingQuestions.Close(closed, WaitingQuestionCloseReason.SessionEnded, null, Noon).Succeeded);
        Assert.False(WaitingQuestions.Notify(closed, [Guid.NewGuid()]).Succeeded);
    }

    [Theory]
    [InlineData(WaitingQuestionCloseReason.AnsweredInSession, "answered-in-session")]
    [InlineData(WaitingQuestionCloseReason.TimedOut, "timed-out")]
    [InlineData(WaitingQuestionCloseReason.SessionEnded, "session-ended")]
    public void Every_close_reason_has_its_wire_spelling(WaitingQuestionCloseReason reason, string spelling)
    {
        Assert.Equal(spelling, WaitingQuestions.Spelling(reason));
        Assert.Equal(reason, WaitingQuestions.ParseReason(spelling));
    }

    [Theory]
    [InlineData(AgentSessionState.Running, true, AgentSessionState.Waiting)]
    [InlineData(AgentSessionState.Stalled, true, AgentSessionState.Waiting)]
    [InlineData(AgentSessionState.Finished, true, AgentSessionState.Finished)]
    [InlineData(AgentSessionState.Running, false, AgentSessionState.Running)]
    public void An_open_waiting_question_makes_a_live_session_waiting(AgentSessionState state, bool holds, AgentSessionState expected)
    {
        Assert.Equal(expected, WaitingQuestions.StateWith(state, holds));
        Assert.True(AgentSessionViews.IsLive(AgentSessionState.Waiting));
    }
}
