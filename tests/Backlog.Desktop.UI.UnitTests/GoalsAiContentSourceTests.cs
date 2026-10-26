using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.UI;
using Backlog.SharedKernel.Results;
using Backlog.SharedKernel.Ai;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Goals view's Ask AI content: the stand-up as <c>IStandUp</c> reads it — the goals
/// with their progress and standing, Finished, Planned and the open impediments — in the
/// shape the assistant reads, within the shared budget.
/// </summary>
public sealed class GoalsAiContentSourceTests
{
    private static readonly Guid PaidId = Guid.Parse("8d0b8f43-3c71-4e0e-9a51-1a1e4f0e2b01");
    private static readonly Guid SearchId = Guid.Parse("8d0b8f43-3c71-4e0e-9a51-1a1e4f0e2b02");
    private static readonly Guid BankId = Guid.Parse("8d0b8f43-3c71-4e0e-9a51-1a1e4f0e2b03");
    private static readonly Guid TaskId = Guid.Parse("8d0b8f43-3c71-4e0e-9a51-1a1e4f0e2b04");

    [Fact]
    public async Task The_body_is_the_stand_up_goals_first_then_finished_planned_and_impediments()
    {
        var standUp = new GoalsHostTests.FakeStandUp { Reading = Reading() };

        var content = await new GoalsAiContentSource(standUp).ComposeAsync(new AiContentRequest("what is at risk?", 6000), TestContext.Current.CancellationToken);

        Assert.Equal("goals", content.AreaKey);
        Assert.Equal(1, standUp.Reads);
        Assert.Equal(
            "Goals (stand-up: last week 2026-10-12 to 2026-10-16, next week 2026-10-19 to 2026-10-23): 6 entries.\n"
            + "Planned effort 5 of a capacity of 18 points. 1 impediment resolved in last.\n"
            + "Goal: Customers get paid faster, due 2026-10-30, 9 of 15 done, 3 in last, 3 doing, 1 blocked, 2 to do, at risk, no landing date\n"
            + "---\n"
            + "Goal: Devbook search ships, due 2026-10-23, 5 of 6 done, 0 in last, 0 doing, 0 blocked, 1 to do, on track, lands 2026-10-21\n"
            + "---\n"
            + "Finished: pull request JSdotNet/Backlog#1357, for goal Customers get paid faster, done 2026-10-14\n"
            + "---\n"
            + "Planned: Search the devbook index, task, repository backlog, effort 5, due 2026-10-21, for goal Devbook search ships\n"
            + "---\n"
            + "Planned: Answer the bank, task, no goal\n"
            + "---\n"
            + "Impediment: Bank sample files missing, escalated, with Account manager, 3 worked days open, check again 2026-10-20, holds back goal Customers get paid faster\n"
            + "Chased on Monday; promised by Friday.",
            content.Body);
        Assert.Equal(6, content.Shown);
        Assert.Equal(6, content.Total);
        Assert.False(content.Trimmed);
    }

    /// <summary>The goals are what the view is about, so they go whatever the question
    /// says and whatever the budget leaves; the items compete for the rest, and the first
    /// line says the list was cut down.</summary>
    [Fact]
    public async Task A_tight_budget_keeps_every_goal_and_selects_the_items_by_relevance()
    {
        var items = Enumerable.Range(1, 60)
            .Select(index => new StandUpItemDto(GoalAttachmentKind.Task, Guid.NewGuid(), $"Routine chore number {index}", "backlog", null, null, null, new DateOnly(2026, 10, 14)))
            .Append(new StandUpItemDto(GoalAttachmentKind.Task, TaskId, "Wire the webhook retries", "backlog", null, 3, null, new DateOnly(2026, 10, 15)))
            .ToList();
        var standUp = new GoalsHostTests.FakeStandUp
        {
            Reading = Reading() with { Finished = [new StandUpGroupDto(PaidId, "Customers get paid faster", items)] }
        };

        var content = await new GoalsAiContentSource(standUp).ComposeAsync(new AiContentRequest("did the webhook retries land?", 1200), TestContext.Current.CancellationToken);

        Assert.True(content.Body.Length <= 1200, $"The body ran to {content.Body.Length} characters.");
        Assert.True(content.Trimmed);
        Assert.Equal(66, content.Total);
        Assert.True(content.Shown < content.Total);
        Assert.Contains("of 66 entries, selected by relevance to the question.", content.Body, StringComparison.Ordinal);
        Assert.Contains("Goal: Customers get paid faster", content.Body, StringComparison.Ordinal);
        Assert.Contains("Goal: Devbook search ships", content.Body, StringComparison.Ordinal);
        Assert.Contains("Finished: Wire the webhook retries", content.Body, StringComparison.Ordinal);
    }

    /// <summary>A goal without a due date is due at the end of next, and its line says so.</summary>
    [Fact]
    public void A_goal_without_a_due_date_reads_as_due_at_the_iteration_end()
    {
        var goal = new StandUpGoalDto(PaidId, "Customers get paid faster", new DateOnly(2026, 10, 23), new GoalProgressDto(9, 15, 3, 1, 2, 3), GoalStanding.OnTrack, new DateOnly(2026, 10, 23), DueIsIterationEnd: true);

        Assert.Equal(
            "Goal: Customers get paid faster, due 2026-10-23 (end of iteration), 9 of 15 done, 3 in last, 3 doing, 1 blocked, 2 to do, on track, lands 2026-10-23",
            GoalsAiContentSource.Text(goal));
    }

    /// <summary>A stand-up that cannot be read says so in the body, so the reader knows the
    /// answer has nothing behind it before they send the question.</summary>
    [Fact]
    public async Task A_stand_up_that_cannot_be_read_says_so()
    {
        var standUp = new GoalsHostTests.FakeStandUp { Failure = new Error("Progress.Unreadable", "The goals file is locked.") };

        var content = await new GoalsAiContentSource(standUp).ComposeAsync(new AiContentRequest("anything?", 6000), TestContext.Current.CancellationToken);

        Assert.Equal("Goals: 0 entries.\nThe stand-up could not be read: The goals file is locked.", content.Body);
        Assert.Equal(0, content.Total);
        Assert.False(content.Trimmed);
    }

    /// <summary>With no goal set and nothing in the stand-up, the body says there are no
    /// entries and claims no capacity it does not have.</summary>
    [Fact]
    public async Task An_empty_stand_up_is_an_empty_body_under_its_scope_line()
    {
        var standUp = new GoalsHostTests.FakeStandUp
        {
            Reading = new StandUpDto(
                new IterationWindowDto(IterationLength.Day, new DateOnly(2026, 10, 16), new DateOnly(2026, 10, 16)),
                new IterationWindowDto(IterationLength.Day, new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 19)),
                [], [], [], 0, null, [], 0)
        };

        var content = await new GoalsAiContentSource(standUp).ComposeAsync(new AiContentRequest("anything?", 6000), TestContext.Current.CancellationToken);

        Assert.Equal(
            "Goals (stand-up: last day 2026-10-16, next day 2026-10-19): 0 entries.\n"
            + "Planned effort 0 points, no capacity known. 0 impediments resolved in last.",
            content.Body);
    }

    private static StandUpDto Reading() => new(
        new IterationWindowDto(IterationLength.Week, new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 16)),
        new IterationWindowDto(IterationLength.Week, new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 23)),
        [
            new StandUpGoalDto(PaidId, "Customers get paid faster", new DateOnly(2026, 10, 30), new GoalProgressDto(9, 15, 3, 1, 2, 3), GoalStanding.AtRisk, null),
            new StandUpGoalDto(SearchId, "Devbook search ships", new DateOnly(2026, 10, 23), new GoalProgressDto(5, 6, 0, 0, 1, 0), GoalStanding.OnTrack, new DateOnly(2026, 10, 21))
        ],
        [new StandUpGroupDto(PaidId, "Customers get paid faster",
            [new StandUpItemDto(GoalAttachmentKind.PullRequest, null, "JSdotNet/Backlog#1357", "JSdotNet/Backlog", 1357, null, null, new DateOnly(2026, 10, 14))])],
        [
            new StandUpGroupDto(SearchId, "Devbook search ships",
                [new StandUpItemDto(GoalAttachmentKind.Task, TaskId, "Search the devbook index", "backlog", null, 5, new DateOnly(2026, 10, 21), null)]),
            new StandUpGroupDto(null, null,
                [new StandUpItemDto(GoalAttachmentKind.Task, Guid.NewGuid(), "Answer the bank", null, null, null, null, null)])
        ],
        5,
        18,
        [new StandUpImpedimentDto(BankId, "Bank sample files missing", ImpedimentState.Escalated, "Account manager", 3, new DateOnly(2026, 10, 20), PaidId, "Customers get paid faster", "Chased on Monday; promised by Friday.")],
        1);
}
