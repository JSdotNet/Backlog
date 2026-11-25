using Backlog.Infrastructure.FileSystem.Tasks;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.Tests;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The docked session queue's read of what needs the person and how full the Manager is
/// (local ADR 0062 §6): the open waiting questions, oldest first, by their first question,
/// and the Manager's latest check-in's working sessions against its session limit.
/// </summary>
public sealed class SessionQueueAttentionReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 9, 0, 0, TimeSpan.Zero);

    private static WaitingQuestion Asked(string sessionId, string question, DateTimeOffset at) =>
        WaitingQuestions.Ask(sessionId, null, [RelayQuestion.Prose(question)], isAway: true, TimeSpan.Zero, at);

    [Fact]
    public async Task The_open_questions_come_oldest_first_by_their_first_question_and_a_closed_one_is_left_out()
    {
        var questions = new HeldWaitingQuestions();
        questions.Add(Asked("session-b", "  Ship it now?  ", Now.AddMinutes(-2)));
        questions.Add(Asked("session-a", "Offer the client accounts too?", Now.AddMinutes(-9)));
        var closed = WaitingQuestions.Close(Asked("session-c", "Gone?", Now.AddMinutes(-20)), WaitingQuestionCloseReason.SessionEnded, null, Now).Question!;
        questions.Add(closed);

        var attention = await new SessionQueueAttentionReader(questions, loops: null).ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [("session-a", "Offer the client accounts too?"), ("session-b", "Ship it now?")],
            attention.Questions.Select(question => (question.SessionId, question.Text)));
        Assert.Null(attention.Slots);
    }

    [Fact]
    public async Task The_slots_are_the_Manager_s_working_sessions_against_its_session_limit()
    {
        var loops = new InMemoryLoopRegister();
        var limits = CapacityLimits.OrDefault(4, null, null, null);
        await loops.CheckInAsync(new LoopCheckIn("manager", LoopKind.Dispatcher, LoopActivity.Working, 1, new LoopHeldWork(0, 0, 3, 0), Now)
        {
            Limits = limits,
            Reading = new CapacityReading(2, null, null, null, limits),
        }, TestContext.Current.CancellationToken);
        await loops.CheckInAsync(new LoopCheckIn("brain", LoopKind.BrainTender, LoopActivity.Listening, 5, LoopHeldWork.None, Now.AddMinutes(1)), TestContext.Current.CancellationToken);

        var attention = await new SessionQueueAttentionReader(questions: null, loops).ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new(2, 4), attention.Slots);
        Assert.Empty(attention.Questions);
    }

    [Fact]
    public async Task Without_a_measured_reading_the_slots_count_the_sessions_the_Manager_says_it_holds()
    {
        var loops = new InMemoryLoopRegister();
        await loops.CheckInAsync(new LoopCheckIn("manager", LoopKind.Dispatcher, LoopActivity.Working, 1, new LoopHeldWork(0, 0, 1, 0), Now), TestContext.Current.CancellationToken);

        var attention = await new SessionQueueAttentionReader(questions: null, loops).ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new(1, CapacityLimits.Default.Sessions), attention.Slots);
    }

    [Fact]
    public async Task With_no_Manager_checked_in_and_no_stores_it_answers_nothing()
    {
        Assert.Null((await new SessionQueueAttentionReader(null, new InMemoryLoopRegister()).ReadAsync(TestContext.Current.CancellationToken)).Slots);

        var none = await new SessionQueueAttentionReader(null, null).ReadAsync(TestContext.Current.CancellationToken);
        Assert.Empty(none.Questions);
        Assert.Null(none.Slots);
    }
}
