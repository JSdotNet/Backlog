using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.DomainModels;
using Backlog.Modules.Progress.Services;

namespace Backlog.Modules.Progress.UnitTests;

/// <summary>
/// Goal Progress counted per item, exactly as
/// <c>.devbook/domain/progress/domain.md#goal-progress</c> and <c>#done</c> say — one test per
/// scenario of <c>.devbook/domain/progress/requirements.md#attaching-work-to-a-goal</c>, and
/// the open-item states and the window count beside them.
/// </summary>
public sealed class GoalCountingTests
{
    private const string Repo = "JSdotNet/Backlog";

    private static PullRequestRefDto Pr(int number) => new(Repo, number.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static LinkedPullRequestDto Merged(int number, DateOnly on) =>
        new(Repo, number.ToString(System.Globalization.CultureInfo.InvariantCulture), LinkedPullRequestState.Merged, on);

    private static LinkedPullRequestDto Open(int number) =>
        new(Repo, number.ToString(System.Globalization.CultureInfo.InvariantCulture), LinkedPullRequestState.Open, null);

    private static List<ProgressTaskDto> Plan(string tag, int entries, int done, string repository = "backlog") =>
    [
        .. Enumerable.Range(0, entries).Select(i => Make.Task(
            $"Entry {i}",
            i < done ? ProgressTaskStatus.Done : ProgressTaskStatus.Ready,
            plan: tag,
            repository: repository,
            completedOn: i < done ? MakeGoal.Today : null))
    ];

    private static GoalProgress Count(Goal goal, IReadOnlyList<ProgressTaskDto> tasks, IReadOnlyList<LinkedPullRequestDto>? pulls = null, IReadOnlyList<Goal>? all = null, DayWindow? window = null) =>
        GoalCounting.Count(goal, all ?? [goal], tasks, pulls ?? [], window);

    /// <summary>Scenario "A plan of nine entries": 5/9.</summary>
    [Fact]
    public void An_attached_plan_counts_each_of_its_entries()
    {
        var goal = MakeGoal.Named("G", MakeGoal.Plan("+bank-integration"));

        var progress = Count(goal, Plan("bank-integration", 9, 5));

        Assert.Equal(5, progress.Done);
        Assert.Equal(9, progress.Total);
    }

    /// <summary>Scenario "An entry filed later": the goal reads 5/10 once a new task is
    /// filed under the plan — counted on every reading, nothing stored.</summary>
    [Fact]
    public void An_entry_filed_later_counts_at_once()
    {
        var goal = MakeGoal.Named("G", MakeGoal.Plan("+bank-integration"));
        var tasks = Plan("bank-integration", 9, 5);
        tasks.Add(Make.Task("Filed later", plan: "bank-integration"));

        Assert.Equal((5, 10), (Count(goal, tasks).Done, Count(goal, tasks).Total));
    }

    /// <summary>"Every entry filed under the tag, in any repository."</summary>
    [Fact]
    public void A_plan_counts_its_entries_in_every_repository()
    {
        var goal = MakeGoal.Named("G", MakeGoal.Plan("bank-integration"));
        var tasks = Plan("bank-integration", 2, 0, "backlog").Concat(Plan("bank-integration", 3, 1, "docs")).ToList();

        Assert.Equal(5, Count(goal, tasks).Total);
        Assert.True(GoalCounting.TaskQuery([goal]).AnyRepository);
    }

    /// <summary>Scenario "A task with two pull requests": its total is 1.</summary>
    [Fact]
    public void An_attached_task_counts_once_with_its_pull_requests()
    {
        var task = Make.Task("Invoice PDF export", ProgressTaskStatus.InProgress, pullRequests: [Pr(1338), Pr(1340)]);
        var goal = MakeGoal.Named("G", MakeGoal.Task(task.Id));

        var progress = Count(goal, [task], [Open(1338), Open(1340)]);

        Assert.Equal(1, progress.Total);
        Assert.Equal(1, progress.Doing);
    }

    /// <summary>Scenario "A pull request attached beside its task": still 1.</summary>
    [Fact]
    public void A_pull_request_attached_beside_its_task_does_not_count_again()
    {
        var task = Make.Task("Invoice PDF export", pullRequests: [Pr(1338)]);
        var goal = MakeGoal.Named("G", MakeGoal.Task(task.Id), MakeGoal.PullRequest(Repo, 1338));

        Assert.Equal(1, Count(goal, [task], [Open(1338)]).Total);
    }

    /// <summary>Scenario "A merged pull request": 1/1.</summary>
    [Fact]
    public void An_attached_pull_request_counts_once_and_is_done_once_merged()
    {
        var goal = MakeGoal.Named("G", MakeGoal.PullRequest(Repo, 1352));

        var progress = Count(goal, [], [Merged(1352, MakeGoal.Today)]);

        Assert.Equal((1, 1), (progress.Done, progress.Total));
    }

    [Fact]
    public void A_pull_request_nobody_has_read_counts_as_to_do()
    {
        var goal = MakeGoal.Named("G", MakeGoal.PullRequest(Repo, 1352));

        var progress = Count(goal, []);

        Assert.Equal((0, 1, 1), (progress.Done, progress.Total, progress.ToDo));
    }

    /// <summary>Scenario "Attached twice over": 9, not 10.</summary>
    [Fact]
    public void A_task_in_an_attached_plan_and_attached_directly_counts_once()
    {
        var tasks = Plan("bank-integration", 9, 0);
        var goal = MakeGoal.Named("G", MakeGoal.Plan("+bank-integration"), MakeGoal.Task(tasks[3].Id));

        Assert.Equal(9, Count(goal, tasks).Total);
    }

    /// <summary>Scenario "An entry another goal holds directly": it counts in that goal only.</summary>
    [Fact]
    public void An_entry_another_goal_holds_directly_counts_in_that_goal_only()
    {
        var tasks = Plan("bank-integration", 9, 0);
        var mapping = tasks[0];
        var search = MakeGoal.Named("Devbook search ships", MakeGoal.Task(mapping.Id));
        var paid = MakeGoal.Named("Customers get paid faster", MakeGoal.Plan("+bank-integration"));
        Goal[] all = [search, paid];

        Assert.Equal(1, Count(search, tasks, all: all).Total);
        Assert.Equal(8, Count(paid, tasks, all: all).Total);
        Assert.DoesNotContain(GoalCounting.TasksOf(paid, all, tasks), task => task.Id == mapping.Id);
    }

    /// <summary>Done is "status Done, or every linked pull request merged".</summary>
    [Fact]
    public void A_task_is_done_when_every_pull_request_it_links_is_merged()
    {
        var merged = Make.Task("Both merged", ProgressTaskStatus.InProgress, pullRequests: [Pr(1), Pr(2)]);
        var half = Make.Task("One merged", ProgressTaskStatus.InProgress, pullRequests: [Pr(3), Pr(4)]);
        var goal = MakeGoal.Named("G", MakeGoal.Task(merged.Id), MakeGoal.Task(half.Id));

        var progress = Count(
            goal,
            [merged, half],
            [Merged(1, MakeGoal.Today), Merged(2, MakeGoal.Today), Merged(3, MakeGoal.Today), Open(4)]);

        Assert.Equal((1, 2, 1), (progress.Done, progress.Total, progress.Doing));
    }

    [Fact]
    public void A_task_without_a_pull_request_is_done_by_its_status_alone()
    {
        var task = Make.Task("No PR", ProgressTaskStatus.InProgress);
        var goal = MakeGoal.Named("G", MakeGoal.Task(task.Id));

        Assert.Equal(0, Count(goal, [task]).Done);
    }

    /// <summary>The open items stand as doing, blocked and to do.</summary>
    [Fact]
    public void Open_items_stand_as_doing_blocked_or_to_do()
    {
        var doing = Make.Task("Doing", ProgressTaskStatus.InProgress);
        var blocked = Make.Task("Blocked", ProgressTaskStatus.InProgress, blocked: true);
        var todo = Make.Task("To do", ProgressTaskStatus.Ready);
        var done = Make.Task("Done", ProgressTaskStatus.Done, blocked: true, completedOn: MakeGoal.Today);
        var goal = MakeGoal.Named("G", MakeGoal.Task(doing.Id), MakeGoal.Task(blocked.Id), MakeGoal.Task(todo.Id), MakeGoal.Task(done.Id));

        var progress = Count(goal, [doing, blocked, todo, done]);

        Assert.Equal(new GoalProgress(Done: 1, Total: 4, Doing: 1, Blocked: 1, ToDo: 1, DoneInWindow: 0), progress);
    }

    /// <summary>The <c>+3</c>: items done inside the window, by the day the task was
    /// completed or its last pull request merged.</summary>
    [Fact]
    public void Items_done_inside_the_window_are_counted_beside_the_total()
    {
        var monday = new DateOnly(2026, 10, 12);
        var friday = new DateOnly(2026, 10, 16);
        var inside = Make.Task("Inside", ProgressTaskStatus.Done, completedOn: monday.AddDays(1));
        var before = Make.Task("Before", ProgressTaskStatus.Done, completedOn: monday.AddDays(-1));
        var byMerge = Make.Task("By merge", ProgressTaskStatus.InProgress, pullRequests: [Pr(7), Pr(8)]);
        var goal = MakeGoal.Named("G", MakeGoal.Task(inside.Id), MakeGoal.Task(before.Id), MakeGoal.Task(byMerge.Id), MakeGoal.PullRequest(Repo, 9));

        var progress = Count(
            goal,
            [inside, before, byMerge],
            [Merged(7, monday.AddDays(-3)), Merged(8, friday), Merged(9, friday.AddDays(1))],
            window: new DayWindow(monday, friday));

        Assert.Equal(4, progress.Done);
        Assert.Equal(2, progress.DoneInWindow);
    }

    [Fact]
    public void A_task_that_no_longer_lives_is_not_counted()
    {
        var goal = MakeGoal.Named("G", MakeGoal.Task(Guid.NewGuid()));

        Assert.Equal(GoalProgress.Nothing, Count(goal, []));
    }

    [Fact]
    public void A_reading_asks_github_about_linked_and_attached_pull_requests_once_each()
    {
        var task = Make.Task("Invoice PDF export", pullRequests: [Pr(1338)]);
        var goal = MakeGoal.Named("G", MakeGoal.Task(task.Id), MakeGoal.PullRequest(Repo, 1338), MakeGoal.PullRequest(Repo, 1352));

        var asked = GoalCounting.PullRequestsOf(goal, GoalCounting.TasksOf(goal, [goal], [task]));

        Assert.Equal(["1338", "1352"], asked.Select(pull => pull.Number));
    }
}
