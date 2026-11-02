using Backlog.Infrastructure.SpecManager.Questions;
using Backlog.Modules.Notifications.Abstractions;

namespace Backlog.Infrastructure.SpecManager.UnitTests;

/// <summary>
/// A question withdrawn here that reaches the person again — reassigned back, or "Only
/// questions assigned to me" turned off — is published as a new notification, the next round
/// of its derived id; one answered, or owned by another desktop, is not.
/// </summary>
public sealed class SpecManagerQuestionComesBackTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_question_reassigned_away_and_back_is_asked_again_as_a_new_notification()
    {
        using var scenario = new QuestionScenario();
        await scenario.Sync.PullAsync(whole: false, Token);
        var first = scenario.IdOf(QuestionScenario.AssignedToMe);

        scenario.Questions(ReassignedToKees(), "[]");
        await scenario.Sync.PullAsync(whole: false, Token);
        scenario.Questions("questions-open.json", "[]");
        await scenario.Sync.PullAsync(whole: false, Token);

        var again = SpecManagerQuestionIds.For(scenario.Root, QuestionScenario.AssignedToMe, round: 1);
        Assert.Equal(NotificationState.Closed, scenario.Ledger.StateOf(first));
        Assert.Equal(SpecManagerQuestionMapper.WithdrawnReason, scenario.Ledger.ReasonOf(first));
        Assert.Equal(NotificationState.Open, scenario.Ledger.StateOf(again));
        Assert.Equal(scenario.Ledger.Draft(first).Source, scenario.Ledger.Draft(again).Source);
        Assert.Equal(NotificationSeverity.Warning, scenario.Ledger.Draft(again).Severity);

        // The next pulls hold it open, and publish nothing beside it.
        await scenario.Sync.PullAsync(whole: true, Token);
        Assert.Equal(3, scenario.Ledger.Published.Count);
        Assert.Equal(NotificationState.Open, scenario.Ledger.StateOf(again));
    }

    [Fact]
    public async Task Each_time_it_comes_back_is_a_round_of_its_own()
    {
        using var scenario = new QuestionScenario();
        await scenario.Sync.PullAsync(whole: false, Token);

        for (var round = 1; round <= 2; round++)
        {
            scenario.Questions(ReassignedToKees(), "[]");
            await scenario.Sync.PullAsync(whole: false, Token);
            scenario.Questions("questions-open.json", "[]");
            await scenario.Sync.PullAsync(whole: false, Token);
        }

        Assert.Equal(NotificationState.Closed, scenario.Ledger.StateOf(SpecManagerQuestionIds.For(scenario.Root, QuestionScenario.AssignedToMe, round: 1)));
        Assert.Equal(NotificationState.Open, scenario.Ledger.StateOf(SpecManagerQuestionIds.For(scenario.Root, QuestionScenario.AssignedToMe, round: 2)));
    }

    [Fact]
    public async Task Turning_only_questions_assigned_to_me_on_and_off_asks_the_unassigned_ones_again()
    {
        using var scenario = new QuestionScenario();
        await scenario.Sync.PullAsync(whole: false, Token);
        var first = scenario.IdOf(QuestionScenario.Unassigned);

        scenario.OnlyAssignedToMe(true);
        await scenario.Sync.PullAsync(whole: true, Token);
        scenario.OnlyAssignedToMe(false);
        await scenario.Sync.PullAsync(whole: true, Token);

        Assert.Equal(NotificationState.Closed, scenario.Ledger.StateOf(first));
        Assert.Equal(NotificationState.Open, scenario.Ledger.StateOf(SpecManagerQuestionIds.For(scenario.Root, QuestionScenario.Unassigned, round: 1)));
        Assert.Equal(NotificationState.Open, scenario.Ledger.StateOf(scenario.IdOf(QuestionScenario.AssignedToMe)));
    }

    [Fact]
    public async Task A_question_answered_here_does_not_come_back()
    {
        using var scenario = new QuestionScenario();
        await scenario.Sync.PullAsync(whole: false, Token);
        var first = scenario.IdOf(QuestionScenario.AssignedToMe);
        scenario.Ledger.Answer(first);

        scenario.Questions(ReassignedToKees(), "[]");
        await scenario.Sync.PullAsync(whole: false, Token);
        scenario.Questions("questions-open.json", "[]");
        await scenario.Sync.PullAsync(whole: false, Token);

        Assert.Equal(NotificationState.Answered, scenario.Ledger.StateOf(first));
        Assert.Equal(2, scenario.Ledger.Published.Count);
    }

    [Fact]
    public async Task A_question_closed_as_answered_in_spec_manager_does_not_come_back()
    {
        using var scenario = new QuestionScenario();
        await scenario.Sync.PullAsync(whole: false, Token);
        var first = scenario.IdOf(QuestionScenario.AssignedToMe);
        await scenario.Ledger.CloseAsync(first, "Answered in spec-manager by Kees de Vries: No", Token);

        await scenario.Sync.PullAsync(whole: true, Token);

        Assert.Equal(NotificationState.Closed, scenario.Ledger.StateOf(first));
        Assert.Equal(2, scenario.Ledger.Published.Count);
    }

    /// <summary>The desktop that lost the race for the first round closed its copy as owned
    /// elsewhere: it publishes no round of its own, and stays a viewer of the owner's.</summary>
    [Fact]
    public async Task A_desktop_that_is_a_viewer_of_the_question_publishes_no_new_round()
    {
        using var scenario = new QuestionScenario();
        await scenario.Sync.PullAsync(whole: false, Token);
        var first = scenario.IdOf(QuestionScenario.AssignedToMe);
        await scenario.Ledger.CloseAsync(first, "Owned by another desktop", Token);

        scenario.Questions(ReassignedToKees(), "[]");
        await scenario.Sync.PullAsync(whole: false, Token);
        scenario.Questions("questions-open.json", "[]");
        await scenario.Sync.PullAsync(whole: false, Token);

        Assert.Equal("Owned by another desktop", scenario.Ledger.ReasonOf(first));
        Assert.Equal(2, scenario.Ledger.Published.Count);
    }

    [Fact]
    public async Task Two_desktops_that_saw_it_withdrawn_name_the_new_notification_alike()
    {
        using var first = new QuestionScenario();
        using var second = new QuestionScenario();
        Assert.Equal(first.Root, second.Root);

        foreach (var desktop in new[] { first, second })
        {
            await desktop.Sync.PullAsync(whole: false, Token);
            desktop.Questions(ReassignedToKees(), "[]");
            await desktop.Sync.PullAsync(whole: false, Token);
            desktop.Questions("questions-open.json", "[]");
            await desktop.Sync.PullAsync(whole: false, Token);
        }

        var open = (QuestionScenario desktop) => desktop.Ledger.Published
            .Where(entry => desktop.Ledger.StateOf(entry.Id) == NotificationState.Open)
            .Select(entry => entry.Id)
            .Order()
            .ToList();

        Assert.Equal(open(first), open(second));
        Assert.Contains(SpecManagerQuestionIds.For(first.Root, QuestionScenario.AssignedToMe, round: 1), open(first));
    }

    [Fact]
    public void A_later_round_is_named_in_the_first_rounds_namespace()
    {
        const string Root = "https://spec-manager.test";
        const string Question = "40000000-0000-0000-0000-000000000001";
        var first = SpecManagerQuestionIds.For(Root, Question);

        Assert.Equal(first, SpecManagerQuestionIds.For(Root, Question, round: 0));
        Assert.Equal(SpecManagerQuestionIds.Version5(first, "round:1"), SpecManagerQuestionIds.For(Root, Question, round: 1));
        Assert.Equal(5, SpecManagerQuestionIds.For(Root, Question, round: 1).Version);
        Assert.NotEqual(SpecManagerQuestionIds.For(Root, Question, round: 1), SpecManagerQuestionIds.For(Root, Question, round: 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => SpecManagerQuestionIds.For(Root, Question, round: -1));
    }

    /// <summary>The open questions with the one assigned to the person reassigned to Kees.</summary>
    private static string ReassignedToKees() => Fixtures.Read("questions-open.json").Replace(
        "\"assignee\": { \"id\": \"30000000-0000-0000-0000-000000000001\", \"name\": \"Jip Jansen\" }",
        "\"assignee\": { \"id\": \"30000000-0000-0000-0000-000000000002\", \"name\": \"Kees de Vries\" }",
        StringComparison.Ordinal);
}
