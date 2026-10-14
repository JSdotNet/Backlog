using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.DomainModels;
using Backlog.Modules.Progress.Services;

namespace Backlog.Modules.Progress.UnitTests;

/// <summary>
/// The Sprint Progress domain service: members, the goal set, the counts, the pace and
/// whether the goal is met, against <c>.devbook/domain/progress/requirements.md</c>.
/// </summary>
public class SprintProgressTests
{
    private static readonly DateOnly Monday = Make.Monday;

    /// <summary>Thursday of the sprint's first week: four working days in.</summary>
    private static readonly DateOnly Thursday = Monday.AddDays(3);

    private static SprintProgressDto Read(
        SprintSeries series,
        IEnumerable<SprintTaskDto> tasks,
        IReadOnlyList<LinkedPullRequestDto>? pulls = null,
        DateOnly? today = null) =>
        SprintProgress.Read(series, series.Sprints[^1], tasks, pulls ?? [], today ?? Thursday);

    private static void Link(SprintSeries series, string plan, DateOnly? on = null) =>
        Assert.True(series.Change(series.Sprints[^1].Number, sprint => sprint.LinkPlan(plan, on ?? Monday)).IsSuccess);

    private static void Add(SprintSeries series, Guid task, DateOnly? on = null) =>
        Assert.True(series.Change(series.Sprints[^1].Number, sprint => sprint.AddTask(task, on ?? Monday)).IsSuccess);

    [Fact]
    public void A_linked_plan_brings_in_every_task_of_the_plan_finished_or_not()
    {
        var series = Make.SeriesWithOpenSprint();
        Link(series, "+progress-sprints");
        var tasks = new[]
        {
            Make.Task("Model", SprintTaskStatus.Done, "+progress-sprints", completedOn: Monday),
            Make.Task("Sync", SprintTaskStatus.InProgress, "+progress-sprints"),
            Make.Task("View", SprintTaskStatus.Ready, "+progress-sprints", repository: "docs")
        };

        var progress = Read(series, tasks);

        Assert.Equal(3, progress.Members.Count);
        Assert.All(progress.Members, member => Assert.Equal("progress-sprints", member.PlanTag));
    }

    [Fact]
    public void A_task_of_the_plan_outside_the_series_repositories_is_not_a_member()
    {
        var series = Make.SeriesWithOpenSprint();
        Link(series, "+progress-sprints");

        var progress = Read(series, [Make.Task("Tools work", plan: "+progress-sprints", repository: "tools")]);

        Assert.Empty(progress.Members);
    }

    [Fact]
    public void A_task_added_directly_and_brought_in_by_a_plan_is_one_member()
    {
        var series = Make.SeriesWithOpenSprint();
        var task = Make.Task("Both ways", plan: "+progress-sprints");
        Link(series, "+progress-sprints");
        Add(series, task.Id);

        var progress = Read(series, [task, task]);

        var member = Assert.Single(progress.Members);
        Assert.True(member.AddedDirectly);
    }

    [Fact]
    public void Each_member_carries_its_status_plan_repository_done_date_and_pull_requests()
    {
        var series = Make.SeriesWithOpenSprint();
        Link(series, "+progress-sprints");
        var pull = new PullRequestRefDto("JSdotNet/Backlog", "1210");
        var task = Make.Task("Model", SprintTaskStatus.Done, "+progress-sprints", repository: "docs", completedOn: Monday.AddDays(1), pullRequests: [pull]);

        var member = Assert.Single(Read(series, [task]).Members);

        Assert.Equal(SprintTaskStatus.Done, member.Status);
        Assert.Equal("progress-sprints", member.PlanTag);
        Assert.Equal("docs", member.Repository);
        Assert.Equal(Monday.AddDays(1), member.DoneOn);
        Assert.Equal("1210", Assert.Single(member.PullRequests).Number);
    }

    [Fact]
    public void Merged_pull_requests_make_a_task_done_and_their_authors_its_owners()
    {
        var series = Make.SeriesWithOpenSprint();
        var first = new PullRequestRefDto("JSdotNet/Backlog", "1");
        var second = new PullRequestRefDto("JSdotNet/Backlog", "2");
        var task = Make.Task("Two pulls", SprintTaskStatus.InProgress, pullRequests: [first, second]);
        Add(series, task.Id);

        var progress = Read(series, [task],
        [
            new LinkedPullRequestDto("JSdotNet/Backlog", "1", LinkedPullRequestState.Merged, Monday.AddDays(1), "jsdotnet"),
            new LinkedPullRequestDto("JSdotNet/Backlog", "2", LinkedPullRequestState.Merged, Monday.AddDays(2), "jsdotnet")
        ]);

        var member = Assert.Single(progress.Members);
        Assert.Equal(SprintWorkState.Done, member.State);
        Assert.Equal(Monday.AddDays(2), member.DoneOn);
        Assert.Equal(["jsdotnet"], member.Owners);
    }

    [Fact]
    public void One_open_pull_request_keeps_the_task_unfinished()
    {
        var series = Make.SeriesWithOpenSprint();
        var task = Make.Task("Two pulls", SprintTaskStatus.InProgress,
            pullRequests: [new PullRequestRefDto("r", "1"), new PullRequestRefDto("r", "2")]);
        Add(series, task.Id);

        var member = Assert.Single(Read(series, [task],
        [
            new LinkedPullRequestDto("r", "1", LinkedPullRequestState.Merged, Monday, "a"),
            new LinkedPullRequestDto("r", "2", LinkedPullRequestState.Open, null, "b")
        ]).Members);

        Assert.Equal(SprintWorkState.InProgress, member.State);
        Assert.Null(member.DoneOn);
        Assert.Equal(["a", "b"], member.Owners);
    }

    [Fact]
    public void A_task_with_no_linked_pull_request_shows_no_owner_and_is_done_by_its_status_alone()
    {
        var series = Make.SeriesWithOpenSprint();
        var task = Make.Task("Plain", SprintTaskStatus.InProgress);
        Add(series, task.Id);

        var member = Assert.Single(Read(series, [task]).Members);

        Assert.Empty(member.Owners);
        Assert.Equal(SprintWorkState.InProgress, member.State);
    }

    [Fact]
    public void An_archived_task_with_no_merged_pull_request_is_not_done()
    {
        var series = Make.SeriesWithOpenSprint();
        var task = Make.Task("Filed away", SprintTaskStatus.Archived);
        Add(series, task.Id);

        var member = Assert.Single(Read(series, [task]).Members);

        Assert.NotEqual(SprintWorkState.Done, member.State);
        Assert.Null(member.DoneOn);
    }

    [Fact]
    public void The_counts_split_every_member_by_state_with_blocked_before_in_progress()
    {
        var series = Make.SeriesWithOpenSprint();
        Link(series, "+plan");
        var tasks = new[]
        {
            Make.Task("Done", SprintTaskStatus.Done, "+plan", completedOn: Monday),
            Make.Task("Going", SprintTaskStatus.InProgress, "+plan"),
            Make.Task("Stuck", SprintTaskStatus.InProgress, "+plan", blocked: true),
            Make.Task("Waiting", SprintTaskStatus.Ready, "+plan"),
            Make.Task("Idea", SprintTaskStatus.Draft, "+plan")
        };

        var all = Read(series, tasks).AllWork;

        Assert.Equal(new SprintCountsDto(Done: 1, InProgress: 1, Blocked: 1, NotStarted: 2, AddedAfterStart: 0), all);
        Assert.Equal(5, all.Total);
    }

    [Fact]
    public void Members_that_joined_after_the_first_day_are_counted_as_added_after_the_start()
    {
        var series = Make.SeriesWithOpenSprint();
        Link(series, "+plan");
        Link(series, "+late", Monday.AddDays(2));
        var onTime = Make.Task("On time", plan: "+plan", createdOn: Monday.AddDays(-3));
        var filedLater = Make.Task("Filed later", plan: "+plan", createdOn: Monday.AddDays(1));
        var latePlan = Make.Task("Late plan", plan: "+late", createdOn: Monday.AddDays(-10));
        var addedLater = Make.Task("Added later");
        Add(series, addedLater.Id, Monday.AddDays(3));

        var progress = Read(series, [onTime, filedLater, latePlan, addedLater]);

        Assert.Equal(3, progress.AllWork.AddedAfterStart);
        Assert.False(progress.Members.Single(member => member.TaskId == onTime.Id).AddedAfterStart);
    }

    [Fact]
    public void A_goal_task_picked_by_two_rules_counts_once()
    {
        var series = Make.SeriesWithOpenSprint();
        series.SetGoalRules(
            [GoalRule.Create(GoalRuleKind.InPlans, ["+progress-sprints"]).Value, GoalRule.Create(GoalRuleKind.MarkedAsGoal, null).Value],
            100);
        Link(series, "+progress-sprints");
        var both = Make.Task("Both", plan: "+progress-sprints");
        var outside = Make.Task("Outside the goal");
        Add(series, outside.Id);
        series.Change(1, sprint => sprint.Record(both.Id, item => item.MarkGoal(true)));

        var progress = Read(series, [both, outside]);

        Assert.Equal([both.Id], progress.GoalTaskIds);
        Assert.Equal(1, progress.Goal.Total);
        Assert.Equal(2, progress.AllWork.Total);
        Assert.Equal(2, progress.GoalRules.Count);
    }

    [Fact]
    public void A_tagged_goal_rule_picks_the_members_carrying_one_of_its_tags()
    {
        var series = Make.SeriesWithOpenSprint();
        series.SetGoalRules([GoalRule.Create(GoalRuleKind.Tagged, ["#release"]).Value], 100);
        var tagged = Make.Task("Tagged", tags: ["#Release"]);
        var plain = Make.Task("Plain");
        Add(series, tagged.Id);
        Add(series, plain.Id);

        Assert.Equal([tagged.Id], Read(series, [tagged, plain]).GoalTaskIds);
    }

    [Theory]
    [InlineData(100, false)]
    [InlineData(80, true)]
    public void The_goal_is_met_when_the_share_of_done_goal_tasks_reaches_the_threshold(int share, bool met)
    {
        var series = Make.SeriesWithOpenSprint();
        series.SetGoalRules([GoalRule.Create(GoalRuleKind.Everything, null).Value], share);
        Link(series, "+plan");
        var tasks = Enumerable.Range(0, 5)
            .Select(i => Make.Task($"Goal {i}", i < 4 ? SprintTaskStatus.Done : SprintTaskStatus.InProgress, "+plan", completedOn: Monday))
            .ToList();

        var progress = Read(series, tasks);

        Assert.Equal(4, progress.Goal.Done);
        Assert.Equal(5, progress.Goal.Total);
        Assert.Equal(met, progress.GoalMet);
        Assert.Equal(share, progress.GoalShare);
    }

    [Fact]
    public void A_sprint_with_no_goal_task_has_not_met_its_goal()
    {
        var series = Make.SeriesWithOpenSprint();
        series.SetGoalRules([GoalRule.Create(GoalRuleKind.MarkedAsGoal, null).Value], 100);

        Assert.False(Read(series, []).GoalMet);
    }

    [Fact]
    public void Done_per_working_day_divides_the_work_done_in_the_window_by_the_working_days_so_far()
    {
        var series = Make.SeriesWithOpenSprint();
        Link(series, "+plan");
        var tasks = new[]
        {
            Make.Task("Before the sprint", SprintTaskStatus.Done, "+plan", completedOn: Monday.AddDays(-2)),
            Make.Task("Monday", SprintTaskStatus.Done, "+plan", completedOn: Monday),
            Make.Task("Wednesday", SprintTaskStatus.Done, "+plan", completedOn: Monday.AddDays(2)),
            Make.Task("Open", SprintTaskStatus.InProgress, "+plan")
        };

        var progress = Read(series, tasks, today: Thursday);

        Assert.Equal(10, progress.WorkingDays);
        Assert.Equal(4, progress.WorkingDaysElapsed);
        Assert.Equal(0.5, progress.DonePerWorkingDay);
    }

    [Fact]
    public void Before_the_first_working_day_the_pace_is_zero()
    {
        var series = Make.SeriesWithOpenSprint();

        var progress = Read(series, [], today: Monday.AddDays(-1));

        Assert.Equal(0, progress.WorkingDaysElapsed);
        Assert.Equal(0, progress.DonePerWorkingDay);
    }

    [Fact]
    public void The_reading_names_the_sprint_by_the_pattern()
    {
        var progress = Read(Make.SeriesWithOpenSprint(), []);

        Assert.Equal("Sprint 1", progress.Name);
        Assert.Equal("Ship the sprint model", progress.SprintGoal);
        Assert.Equal(Monday, progress.Start);
    }

    [Fact]
    public void A_closed_sprint_keeps_the_members_it_had_after_its_plan_moves_on()
    {
        var series = Make.SeriesWithOpenSprint();
        Link(series, "+plan");
        var kept = Make.Task("Kept", SprintTaskStatus.Done, "+plan", completedOn: Monday);
        series.CloseSprint([kept.Id], [], new Dictionary<Guid, CloseOutcome>(), "Next");
        var filedAfterClosing = Make.Task("Filed after closing", plan: "+plan");

        var (ids, plans) = SprintProgress.Query(series.SprintNumber(1)!);
        var progress = SprintProgress.Read(series, series.SprintNumber(1)!, [kept, filedAfterClosing], [], Thursday);

        Assert.Equal([kept.Id], ids);
        Assert.Empty(plans);
        Assert.Equal([kept.Id], progress.Members.Select(member => member.TaskId));
        Assert.False(progress.IsOpen);
    }
}
