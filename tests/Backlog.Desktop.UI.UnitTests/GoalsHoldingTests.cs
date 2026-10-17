using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.UI.Goals;
using Backlog.UI.Components.Progress;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// What a goal holds, and so what No goal lists
/// (<c>.devbook/domain/progress/requirements.md#requirement-work-in-progress-that-no-goal-holds-is-listed-under-no-goal</c>):
/// a goal holds its tasks, the rows filed under its plans, its pull requests and the pull
/// requests its tasks link; every task in progress and every open pull request in the
/// header's scope that no goal holds is No goal.
/// </summary>
public sealed class GoalsHoldingTests
{
    private static readonly Guid Invoice = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Vat = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid Matching = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid Elsewhere = Guid.Parse("00000000-0000-0000-0000-000000000004");
    private static readonly Guid Finished = Guid.Parse("00000000-0000-0000-0000-000000000005");

    private static GoalsTaskCandidate Task(Guid id, string title, GoalTaskStatus status = GoalTaskStatus.Doing, string[]? plans = null, PullRequestRefDto[]? pulls = null, bool inScope = true) =>
        new(id, title, status, plans ?? [], pulls ?? [], inScope);

    private static GoalsPullRequestCandidate Pull(int number, string title, bool inScope = true) =>
        new("JSdotNet/Backlog", number, title, inScope);

    private static GoalDto Goal(params GoalItemDto[] items) =>
        new(Guid.NewGuid(), "Customers get paid faster", null, new DateOnly(2026, 10, 30),
            [.. items.Select(item => new GoalAttachmentDto(item, new DateOnly(2026, 10, 12)))]);

    private static readonly GoalsCandidates Candidates = new(
        [
            Task(Invoice, "Invoice PDF export", pulls: [new PullRequestRefDto("JSdotNet/Backlog", "1341")]),
            Task(Vat, "VAT rate per line item"),
            Task(Matching, "Payment matching rules", plans: ["+bank-integration"], pulls: [new PullRequestRefDto("jsdotnet/backlog", "1350")]),
            Task(Elsewhere, "Phone search", inScope: false),
            Task(Finished, "Reminder emails", GoalTaskStatus.Done)
        ],
        ["bank-integration"],
        [
            Pull(1341, "Invoice PDF export"),
            Pull(1350, "Matching rules engine"),
            Pull(1357, "Payment status webhook"),
            Pull(1360, "Other repository work", inScope: false),
            Pull(1362, "Credit notes")
        ]);

    /// <summary>Scenario "A task in progress outside every goal".</summary>
    [Fact]
    public void A_task_in_progress_that_no_goal_holds_is_no_goal()
    {
        var noGoal = GoalsHolding.NoGoal([Goal(GoalItemDto.ForTask(Invoice))], Candidates);

        Assert.Contains(noGoal, item => item.Kind == GoalsItemKind.Task && item.Title == "VAT rate per line item");
    }

    [Fact]
    public void A_goal_holds_its_task_the_rows_under_its_plan_its_pull_request_and_the_pull_requests_its_tasks_link()
    {
        var goals = new[]
        {
            Goal(GoalItemDto.ForTask(Invoice), GoalItemDto.ForPlan("bank-integration"), GoalItemDto.ForPullRequest("JSdotNet/Backlog", 1357))
        };

        var noGoal = GoalsHolding.NoGoal(goals, Candidates);

        // Held: Invoice (task), Matching (in the plan), #1357 (attached), #1341 (Invoice
        // links it) and #1350 (Matching links it, the repository spelled another way).
        Assert.Equal(
            ["VAT rate per line item", "Credit notes"],
            noGoal.Select(item => item.Title));
    }

    [Fact]
    public void Nothing_outside_the_scope_and_nothing_finished_is_no_goal()
    {
        var noGoal = GoalsHolding.NoGoal([], Candidates);

        Assert.DoesNotContain(noGoal, item => item.Title is "Phone search" or "Other repository work" or "Reminder emails");
    }

    [Fact]
    public void Tasks_come_before_pull_requests_and_each_carries_a_key_the_view_hands_back()
    {
        var noGoal = GoalsHolding.NoGoal([], Candidates);

        Assert.Equal(
            [GoalsItemKind.Task, GoalsItemKind.Task, GoalsItemKind.Task, GoalsItemKind.PullRequest, GoalsItemKind.PullRequest, GoalsItemKind.PullRequest, GoalsItemKind.PullRequest],
            noGoal.Select(item => item.Kind));

        var pull = noGoal.First(item => item.Kind == GoalsItemKind.PullRequest);
        Assert.Equal(1341, pull.Number);
        Assert.True(GoalsKeys.TryRead(pull.Key, out var read));
        Assert.Equal(GoalItemDto.ForPullRequest("JSdotNet/Backlog", 1341), read);
    }

    [Fact]
    public void A_blocked_task_in_progress_is_still_work_in_progress()
    {
        var candidates = Candidates with { Tasks = [Task(Vat, "VAT rate per line item", GoalTaskStatus.Blocked)], PullRequests = [] };

        Assert.Single(GoalsHolding.NoGoal([], candidates));
    }

    [Theory]
    [InlineData("plan:bank-integration")]
    [InlineData("task:00000000-0000-0000-0000-000000000002")]
    [InlineData("pr:JSdotNet/Backlog#1357")]
    public void A_key_reads_back_as_the_item_it_was_made_from(string key)
    {
        Assert.True(GoalsKeys.TryRead(key, out var item));
        Assert.Equal(key, GoalsKeys.For(item));
    }

    [Theory]
    [InlineData("")]
    [InlineData("task:not-a-guid")]
    [InlineData("pr:JSdotNet/Backlog")]
    [InlineData("pr:JSdotNet/Backlog#x")]
    [InlineData("plan:")]
    [InlineData("sprint:42")]
    public void A_key_that_names_nothing_reads_as_nothing(string key) => Assert.False(GoalsKeys.TryRead(key, out _));
}
