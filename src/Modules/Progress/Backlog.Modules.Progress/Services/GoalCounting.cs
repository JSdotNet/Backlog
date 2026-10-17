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
/// Counts a goal's items from the current state of its work, over the aggregate: the rules
/// live in <see cref="GoalItems"/>, in the module's published surface, so the phone counts
/// with the same code (local ADR 0035 §8). This is that code over <see cref="Goal"/>, for
/// the module's own handlers.
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
        return GoalItems.TaskQuery(goals.Select(goal => goal.ToDto()));
    }

    /// <summary>The tasks <paramref name="goal"/> counts, among <paramref name="tasks"/>:
    /// its own and its plans' entries, less the entries another goal holds directly.</summary>
    public static IReadOnlyList<SprintTaskDto> TasksOf(Goal goal, IReadOnlyList<Goal> all, IReadOnlyList<SprintTaskDto> tasks)
    {
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(all);

        return GoalItems.TasksOf(goal.ToDto(), Dtos(all), tasks);
    }

    /// <summary>The pull requests a reading has to ask GitHub about for
    /// <paramref name="goal"/>: those its counted tasks link, and those attached.</summary>
    public static IReadOnlyList<PullRequestRefDto> PullRequestsOf(Goal goal, IReadOnlyList<SprintTaskDto> countedTasks)
    {
        ArgumentNullException.ThrowIfNull(goal);
        return GoalItems.PullRequestsOf(goal.ToDto(), countedTasks);
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
        DayWindow? window = null)
    {
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(all);

        var items = GoalItems.Items(goal.ToDto(), Dtos(all), tasks, pullRequests);
        var counted = GoalItems.Count(items, window?.First, window?.Last);

        return new GoalProgress(counted.Done, counted.Total, counted.Doing, counted.Blocked, counted.ToDo, counted.DoneInWindow);
    }

    /// <summary>The key a pull request is told apart by: its repository and number, the
    /// number without a leading <c>#</c>. Compare without regard to case.</summary>
    public static string KeyOf(PullRequestRefDto pull) => GoalItems.KeyOf(pull);

    private static List<GoalDto> Dtos(IReadOnlyList<Goal> all) => [.. all.Select(goal => goal.ToDto())];
}
