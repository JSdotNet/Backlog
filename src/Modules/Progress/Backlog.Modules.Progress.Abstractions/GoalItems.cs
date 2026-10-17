using System.Globalization;

using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;

namespace Backlog.Modules.Progress.Abstractions;

/// <summary>Where one item a goal counts stands: the first that applies of done, blocked,
/// doing and to do (<see cref="GoalItems"/>).</summary>
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
public sealed record GoalItem(ProgressTaskDto? Task, PullRequestRefDto? PullRequest, GoalItemState State, DateOnly? DoneOn);

/// <summary>
/// Counts a goal's items from the current state of its work — the rules of
/// <c>.devbook/domain/progress/domain.md#goal-progress</c> and <c>#done</c>, each item once:
/// <list type="bullet">
/// <item>an attached plan counts each entry its roadmap item gathers by tag — the slugs
/// <see cref="ProgressTaskDto.PlanTags"/> carries — in any repository,
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
/// Pure, over the published <see cref="GoalDto"/>, and in the module's published surface so
/// every head counts alike: the desktop over what Tasks and GitHub answered, the phone over
/// what the task feed carried (local ADR 0035 §8).
/// </para>
/// </summary>
public static class GoalItems
{
    /// <summary>What a reading of <paramref name="goals"/> has to ask Tasks for: every
    /// attached task's id and every attached plan's tag, in any repository.</summary>
    public static ProgressTaskQuery TaskQuery(IEnumerable<GoalDto> goals)
    {
        ArgumentNullException.ThrowIfNull(goals);

        var items = goals.SelectMany(goal => goal.Attachments).Select(attachment => attachment.Item).ToList();
        return new ProgressTaskQuery(
            [.. items.Where(item => item.Kind == GoalAttachmentKind.Task && item.TaskId is not null).Select(item => item.TaskId!.Value).Distinct()],
            [.. items.Where(item => item.Kind == GoalAttachmentKind.Plan).Select(item => Bare(item.PlanTag)).Where(tag => tag.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)],
            [],
            AnyRepository: true);
    }

    /// <summary>The tasks <paramref name="goal"/> counts, among <paramref name="tasks"/>:
    /// its own and its plans' entries, less the entries another goal holds directly.</summary>
    public static IReadOnlyList<ProgressTaskDto> TasksOf(GoalDto goal, IReadOnlyList<GoalDto> all, IReadOnlyList<ProgressTaskDto> tasks)
    {
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(all);
        ArgumentNullException.ThrowIfNull(tasks);

        var direct = TaskIdsOf(goal).ToHashSet();

        var plans = goal.Attachments
            .Select(attachment => attachment.Item)
            .Where(item => item.Kind == GoalAttachmentKind.Plan)
            .Select(item => Bare(item.PlanTag))
            .Where(tag => tag.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var heldElsewhere = all
            .Where(other => other.Id != goal.Id)
            .SelectMany(TaskIdsOf)
            .ToHashSet();

        return
        [
            .. tasks
                .Where(task => direct.Contains(task.Id)
                    || (!heldElsewhere.Contains(task.Id) && task.PlanTags.Any(tag => plans.Contains(Bare(tag)))))
                .DistinctBy(task => task.Id)
        ];
    }

    /// <summary>The pull requests a reading has to ask GitHub about for
    /// <paramref name="goal"/>: those its counted tasks link, and those attached.</summary>
    public static IReadOnlyList<PullRequestRefDto> PullRequestsOf(GoalDto goal, IReadOnlyList<ProgressTaskDto> countedTasks)
    {
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(countedTasks);

        return
        [
            .. countedTasks.SelectMany(task => task.PullRequests)
                .Concat(AttachedPullRequests(goal))
                .DistinctBy(KeyOf, StringComparer.OrdinalIgnoreCase)
        ];
    }

    /// <summary>Every pull request a reading of <paramref name="goals"/> has to ask about,
    /// each once: the ones each goal's counted tasks link, and the ones attached.</summary>
    public static IReadOnlyList<PullRequestRefDto> PullRequestsOf(IReadOnlyList<GoalDto> goals, IReadOnlyList<ProgressTaskDto> tasks)
    {
        ArgumentNullException.ThrowIfNull(goals);
        ArgumentNullException.ThrowIfNull(tasks);

        return
        [
            .. goals.SelectMany(goal => PullRequestsOf(goal, TasksOf(goal, goals, tasks)))
                .DistinctBy(KeyOf, StringComparer.OrdinalIgnoreCase)
        ];
    }

    /// <summary>
    /// Each item <paramref name="goal"/> counts, once, with where it stands: its counted
    /// tasks first, then the attached pull requests no counted task links. A task id no
    /// answer has is a task that no longer lives, and is not counted; a pull request with
    /// no answer reads as not merged.
    /// </summary>
    public static IReadOnlyList<GoalItem> Items(
        GoalDto goal,
        IReadOnlyList<GoalDto> all,
        IReadOnlyList<ProgressTaskDto> tasks,
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

        var ridingAlong = counted.SelectMany(task => task.PullRequests).Select(KeyOf).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var pull in AttachedPullRequests(goal))
        {
            if (ridingAlong.Contains(KeyOf(pull))) continue;

            answers.TryGetValue(KeyOf(pull), out var answer);
            var (state, doneOn) = StateOf(answer);
            items.Add(new GoalItem(null, pull, state, doneOn));
        }

        return items;
    }

    /// <summary>The progress of a goal whose <see cref="Items"/> a reading resolved: its
    /// <c>done/total</c>, how the open ones stand, and how many were done from
    /// <paramref name="from"/> through <paramref name="through"/> when both are given.</summary>
    public static GoalProgressDto Count(IReadOnlyList<GoalItem> items, DateOnly? from = null, DateOnly? through = null)
    {
        ArgumentNullException.ThrowIfNull(items);

        return new GoalProgressDto(
            items.Count(item => item.State == GoalItemState.Done),
            items.Count,
            items.Count(item => item.State == GoalItemState.Doing),
            items.Count(item => item.State == GoalItemState.Blocked),
            items.Count(item => item.State == GoalItemState.ToDo),
            from is { } first && through is { } last
                ? items.Count(item => item.DoneOn is { } on && on >= first && on <= last)
                : 0);
    }

    /// <summary>The key a pull request is told apart by: its repository and number, the
    /// number without a leading <c>#</c>. Compare without regard to case.</summary>
    public static string KeyOf(PullRequestRefDto pull)
    {
        ArgumentNullException.ThrowIfNull(pull);
        return Key(pull.Repository, pull.Number);
    }

    /// <summary>The key a pull request is told apart by, from its two parts.</summary>
    public static string Key(string repository, string number)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(number);
        return $"{repository.Trim()}#{number.Trim().TrimStart('#')}";
    }

    /// <summary>A plan tag without its <c>+</c> sigil, trimmed.</summary>
    public static string Bare(string? tag)
    {
        var trimmed = tag?.Trim() ?? string.Empty;
        return trimmed.StartsWith('+') ? trimmed[1..].Trim() : trimmed;
    }

    private static IEnumerable<Guid> TaskIdsOf(GoalDto goal) =>
        goal.Attachments
            .Select(attachment => attachment.Item)
            .Where(item => item.Kind == GoalAttachmentKind.Task && item.TaskId is not null)
            .Select(item => item.TaskId!.Value);

    /// <summary>The pull request attachments in the words Tasks and GitHub use for one.</summary>
    private static IEnumerable<PullRequestRefDto> AttachedPullRequests(GoalDto goal) =>
        goal.Attachments
            .Select(attachment => attachment.Item)
            .Where(item => item.Kind == GoalAttachmentKind.PullRequest && item.Repository is not null && item.Number is not null)
            .Select(item => new PullRequestRefDto(item.Repository!.Trim(), item.Number!.Value.ToString(CultureInfo.InvariantCulture)));

    private static (GoalItemState, DateOnly?) StateOf(ProgressTaskDto task, Dictionary<string, LinkedPullRequestDto> answers)
    {
        var linked = task.PullRequests
            .Select(pull => answers.TryGetValue(KeyOf(pull), out var answer) ? answer : null)
            .ToList();

        if (task.Status == ProgressTaskStatus.Done) return (GoalItemState.Done, task.CompletedOn);

        if (linked.Count > 0 && linked.All(answer => answer?.State == LinkedPullRequestState.Merged))
        {
            return (GoalItemState.Done, linked.Max(answer => answer!.MergedOn));
        }

        if (task.IsBlocked) return (GoalItemState.Blocked, null);
        return task.Status == ProgressTaskStatus.InProgress ? (GoalItemState.Doing, null) : (GoalItemState.ToDo, null);
    }

    private static (GoalItemState, DateOnly?) StateOf(LinkedPullRequestDto? answer) =>
        answer?.State switch
        {
            LinkedPullRequestState.Merged => (GoalItemState.Done, answer.MergedOn),
            LinkedPullRequestState.Open or LinkedPullRequestState.Draft => (GoalItemState.Doing, null),
            _ => (GoalItemState.ToDo, null),
        };
}
