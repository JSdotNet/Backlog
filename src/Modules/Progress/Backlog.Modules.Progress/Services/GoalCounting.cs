using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Progress.DomainModels;

namespace Backlog.Modules.Progress.Services;

/// <summary>
/// How much of a goal is done, as <c>done/total</c> over its items
/// (<c>.devbook/domain/progress/domain.md#goal-progress</c>). Equality by value.
/// </summary>
/// <param name="DoneInWindow">Items done inside the window the reading asked about.</param>
public sealed record GoalProgress(int Done, int Total, int Doing, int Blocked, int ToDo, int DoneInWindow)
{
    public static readonly GoalProgress Nothing = new(0, 0, 0, 0, 0, 0);
}

/// <summary>A window of days, both ends included, that <see cref="GoalCounting"/> counts
/// the items done in. The iteration windows themselves are built elsewhere.</summary>
public readonly record struct DayWindow(DateOnly First, DateOnly Last)
{
    public bool Contains(DateOnly day) => day >= First && day <= Last;
}

/// <summary>
/// Counts a goal's items from the current state of its work — the rules of
/// <c>.devbook/domain/progress/domain.md#goal-progress</c> and <c>#done</c>, each item once:
/// <list type="bullet">
/// <item>an attached plan counts each entry filed under its tag, in any repository —
/// except an entry another goal holds directly as a task, which counts in that goal
/// (<c>#goal-assignment</c>);</item>
/// <item>an attached task counts 1, and the pull requests it links ride along and do not
/// count again;</item>
/// <item>an attached pull request counts 1, unless a task the goal counts links it;</item>
/// <item>a task attached directly and also an entry of an attached plan counts once.</item>
/// </list>
/// <para>
/// A task is done when its status is Done, or when it links pull requests and every one
/// is merged; a pull request when it is merged. An open item is blocked when its task is
/// marked blocked, doing when its task is in progress or its pull request is open or a
/// draft, and to do otherwise. The day an item was done is the day its task was
/// completed, or the day its last pull request merged.
/// </para>
/// <para>
/// Pure: it is handed what Tasks and GitHub answered and keeps nothing.
/// </para>
/// </summary>
public static class GoalCounting
{
    /// <summary>What a reading of <paramref name="goals"/> has to ask Tasks for: every
    /// attached task's id and every attached plan's tag, in any repository.</summary>
    public static SprintTaskQuery TaskQuery(IEnumerable<Goal> goals)
    {
        ArgumentNullException.ThrowIfNull(goals);

        var attachments = goals.SelectMany(goal => goal.Attachments).ToList();
        return new SprintTaskQuery(
            [.. attachments.Where(a => a.Kind == GoalAttachmentKind.Task).Select(a => a.TaskId!.Value).Distinct()],
            [.. attachments.Where(a => a.Kind == GoalAttachmentKind.Plan).Select(a => a.Plan!).Distinct(StringComparer.OrdinalIgnoreCase)],
            [],
            AnyRepository: true);
    }

    /// <summary>The tasks <paramref name="goal"/> counts, among <paramref name="tasks"/>:
    /// its own and its plans' entries, less the entries another goal holds directly.</summary>
    public static IReadOnlyList<SprintTaskDto> TasksOf(Goal goal, IReadOnlyList<Goal> all, IReadOnlyList<SprintTaskDto> tasks)
    {
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(all);
        ArgumentNullException.ThrowIfNull(tasks);

        var direct = goal.Attachments
            .Where(a => a.Kind == GoalAttachmentKind.Task)
            .Select(a => a.TaskId!.Value)
            .ToHashSet();

        var plans = goal.Attachments
            .Where(a => a.Kind == GoalAttachmentKind.Plan)
            .Select(a => a.Plan!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var heldElsewhere = all
            .Where(other => other.Id != goal.Id)
            .SelectMany(other => other.Attachments)
            .Where(a => a.Kind == GoalAttachmentKind.Task)
            .Select(a => a.TaskId!.Value)
            .ToHashSet();

        return
        [
            .. tasks
                .Where(task => direct.Contains(task.Id)
                    || (!heldElsewhere.Contains(task.Id) && task.PlanTags.Any(tag => plans.Contains(PlanTags.Bare(tag)))))
                .DistinctBy(task => task.Id)
        ];
    }

    /// <summary>The pull requests a reading has to ask GitHub about for
    /// <paramref name="goal"/>: those its counted tasks link, and those attached.</summary>
    public static IReadOnlyList<PullRequestRefDto> PullRequestsOf(Goal goal, IReadOnlyList<SprintTaskDto> countedTasks)
    {
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(countedTasks);

        return
        [
            .. countedTasks.SelectMany(task => task.PullRequests)
                .Concat(goal.Attachments
                    .Where(a => a.Kind == GoalAttachmentKind.PullRequest)
                    .Select(PullRequestRefOf))
                .DistinctBy(Key, StringComparer.OrdinalIgnoreCase)
        ];
    }

    /// <summary>
    /// <paramref name="goal"/>'s progress among <paramref name="all"/> goals, from the
    /// tasks and pull request answers a reading gathered. A task id no answer has is a task
    /// that no longer lives, and is not counted; a pull request with no answer reads as
    /// not merged.
    /// </summary>
    public static GoalProgress Count(
        Goal goal,
        IReadOnlyList<Goal> all,
        IReadOnlyList<SprintTaskDto> tasks,
        IReadOnlyList<LinkedPullRequestDto> pullRequests,
        DayWindow? window = null) =>
        Count(Items(goal, all, tasks, pullRequests), window);

    /// <summary>The progress of a goal whose <see cref="Items"/> a reading already resolved,
    /// so a reading that lists the items too resolves them once.</summary>
    public static GoalProgress Count(IReadOnlyList<GoalItem> items, DayWindow? window = null)
    {
        ArgumentNullException.ThrowIfNull(items);

        return new GoalProgress(
            items.Count(item => item.State == GoalItemState.Done),
            items.Count,
            items.Count(item => item.State == GoalItemState.Doing),
            items.Count(item => item.State == GoalItemState.Blocked),
            items.Count(item => item.State == GoalItemState.ToDo),
            window is { } days ? items.Count(item => item.DoneOn is { } on && days.Contains(on)) : 0);
    }

    /// <summary>
    /// Each item <paramref name="goal"/> counts, once, with where it stands: its counted
    /// tasks first, then the attached pull requests no counted task links — the items
    /// <see cref="Count"/> counts, for a reading that lists them.
    /// </summary>
    public static IReadOnlyList<GoalItem> Items(
        Goal goal,
        IReadOnlyList<Goal> all,
        IReadOnlyList<SprintTaskDto> tasks,
        IReadOnlyList<LinkedPullRequestDto> pullRequests)
    {
        ArgumentNullException.ThrowIfNull(pullRequests);

        var answers = new Dictionary<string, LinkedPullRequestDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var answer in pullRequests) answers.TryAdd(Key(answer.Repository, answer.Number), answer);

        var counted = TasksOf(goal, all, tasks);
        var items = new List<GoalItem>();

        foreach (var task in counted)
        {
            var (state, doneOn) = StateOf(task, answers);
            items.Add(new GoalItem(task, null, state, doneOn));
        }

        var ridingAlong = counted.SelectMany(task => task.PullRequests).ToList();
        foreach (var attached in goal.Attachments.Where(a => a.Kind == GoalAttachmentKind.PullRequest))
        {
            if (ridingAlong.Any(pull => attached.IsPullRequest(pull.Repository, pull.Number))) continue;

            var pull = PullRequestRefOf(attached);
            answers.TryGetValue(Key(pull), out var answer);
            var (state, doneOn) = StateOf(answer);
            items.Add(new GoalItem(null, pull, state, doneOn));
        }

        return items;
    }

    /// <summary>The key a pull request is told apart by: its repository and number, the
    /// number without a leading <c>#</c>. Compare without regard to case.</summary>
    public static string KeyOf(PullRequestRefDto pull)
    {
        ArgumentNullException.ThrowIfNull(pull);
        return Key(pull);
    }

    private static (GoalItemState, DateOnly?) StateOf(SprintTaskDto task, Dictionary<string, LinkedPullRequestDto> answers)
    {
        var linked = task.PullRequests
            .Select(pull => answers.TryGetValue(Key(pull.Repository, pull.Number), out var answer) ? answer : null)
            .ToList();

        if (task.Status == SprintTaskStatus.Done) return (GoalItemState.Done, task.CompletedOn);

        if (linked.Count > 0 && linked.All(answer => answer?.State == LinkedPullRequestState.Merged))
        {
            return (GoalItemState.Done, linked.Max(answer => answer!.MergedOn));
        }

        if (task.IsBlocked) return (GoalItemState.Blocked, null);
        return task.Status == SprintTaskStatus.InProgress ? (GoalItemState.Doing, null) : (GoalItemState.ToDo, null);
    }

    private static (GoalItemState, DateOnly?) StateOf(LinkedPullRequestDto? answer) =>
        answer?.State switch
        {
            LinkedPullRequestState.Merged => (GoalItemState.Done, answer.MergedOn),
            LinkedPullRequestState.Open or LinkedPullRequestState.Draft => (GoalItemState.Doing, null),
            _ => (GoalItemState.ToDo, null),
        };

    /// <summary>A pull request attachment in the words Tasks and GitHub use for one.</summary>
    private static PullRequestRefDto PullRequestRefOf(GoalAttachment attachment) =>
        new(attachment.Repository!, attachment.Number!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static string Key(PullRequestRefDto pull) => Key(pull.Repository, pull.Number);

    private static string Key(string repository, string number) =>
        $"{repository.Trim()}#{number.Trim().TrimStart('#')}";
}

/// <summary>Where one item a goal counts stands: the first that applies of done, blocked,
/// doing and to do (<see cref="GoalCounting"/>).</summary>
public enum GoalItemState
{
    ToDo,
    Doing,
    Blocked,
    Done
}

/// <summary>One item a goal counts: a task — an attached one or a plan's entry — or an
/// attached pull request no counted task links. Exactly one of <see cref="Task"/> and
/// <see cref="PullRequest"/> is set.</summary>
/// <param name="DoneOn">The day it was done, when it is: its task's completion day, or the
/// day its last pull request merged.</param>
public sealed record GoalItem(SprintTaskDto? Task, PullRequestRefDto? PullRequest, GoalItemState State, DateOnly? DoneOn);
