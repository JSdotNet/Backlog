using System.Globalization;

using Backlog.Modules.Progress.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Progress.Abstractions;

/// <summary>
/// What an impediment's held-back items say against the current state of the work
/// (<c>.devbook/domain/progress/domain.md#holds-back</c>, <c>#settled</c>,
/// <c>#ready-to-resolve</c>): whether each is settled, whether the impediment reads Ready to
/// resolve, and whether an item is one of a goal's.
/// <para>
/// Pure, over the published DTOs, and in the module's published surface beside
/// <see cref="StandUpReading"/>, so the desktop and the phone read alike (local ADR 0035 §8).
/// A repository and a plan tag compare without regard to case, a plan tag without its sigil,
/// whatever spelling each source keeps.
/// </para>
/// </summary>
public static class HeldBackItems
{
    /// <summary>
    /// Whether the work <paramref name="item"/> names is over: a task Done or Archived; a pull
    /// request Merged or Closed, read from a repository that is not archived; a plan that
    /// gathers at least one entry among <paramref name="tasks"/>, every one Done or Archived.
    /// A task the reading does not have — a deleted one — and a pull request with no answer
    /// or an Unknown one are never settled.
    /// </summary>
    public static bool Settled(HeldBackItemDto item, IReadOnlyList<ProgressTaskDto> tasks, IReadOnlyList<LinkedPullRequestDto> pullRequests)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(pullRequests);

        return item.Kind switch
        {
            HeldBackKind.Task => tasks.FirstOrDefault(task => task.Id == item.TaskId) is { } task && IsOver(task),
            HeldBackKind.PullRequest => AnswerFor(item, pullRequests) is { InArchivedRepository: false, State: LinkedPullRequestState.Merged or LinkedPullRequestState.Closed },
            HeldBackKind.Plan => EntriesOf(item, tasks).ToList() is { Count: > 0 } entries && entries.All(IsOver),
            _ => false,
        };
    }

    /// <summary>Whether <paramref name="impediment"/> reads Ready to resolve: it is open, holds
    /// back at least one item, and every item is <see cref="Settled"/>. A reading only.</summary>
    public static bool ReadyToResolve(ImpedimentDto impediment, IReadOnlyList<ProgressTaskDto> tasks, IReadOnlyList<LinkedPullRequestDto> pullRequests)
    {
        ArgumentNullException.ThrowIfNull(impediment);

        return impediment.IsOpen
            && impediment.HoldsBack.Count > 0
            && impediment.HoldsBack.All(item => Settled(item, tasks, pullRequests));
    }

    /// <summary>Whether holding <paramref name="item"/> back holds a goal back: it is a task
    /// among the goal's <paramref name="counted"/> tasks, a pull request among its
    /// <paramref name="countedPullRequests"/> — attached, or linked by a counted task — or a
    /// plan one of whose entries the goal counts.</summary>
    public static bool HoldsBackGoal(HeldBackItemDto item, IReadOnlyList<ProgressTaskDto> counted, IReadOnlyList<PullRequestRefDto> countedPullRequests)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(counted);
        ArgumentNullException.ThrowIfNull(countedPullRequests);

        return item.Kind switch
        {
            HeldBackKind.Task => counted.Any(task => task.Id == item.TaskId),
            HeldBackKind.PullRequest => countedPullRequests.Any(pull => SamePullRequest(item, pull.Repository, pull.Number)),
            HeldBackKind.Plan => EntriesOf(item, counted).Any(),
            _ => false,
        };
    }

    /// <summary>A held-back pull request in the words Tasks and GitHub use for one.</summary>
    public static PullRequestRefDto PullRequestRef(HeldBackItemDto item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new PullRequestRefDto(item.Repository ?? string.Empty, Number(item));
    }

    /// <summary>The entries among <paramref name="tasks"/> a held-back plan gathers by its tag.</summary>
    public static IEnumerable<ProgressTaskDto> EntriesOf(HeldBackItemDto item, IEnumerable<ProgressTaskDto> tasks)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(tasks);

        var plan = GoalItems.Bare(item.PlanTag);
        return plan.Length == 0
            ? []
            : tasks.Where(task => task.PlanTags.Any(tag => string.Equals(GoalItems.Bare(tag), plan, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>The answer a reading has for a held-back pull request, or null.</summary>
    public static LinkedPullRequestDto? AnswerFor(HeldBackItemDto item, IReadOnlyList<LinkedPullRequestDto> pullRequests)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(pullRequests);

        return item.Kind == HeldBackKind.PullRequest
            ? pullRequests.FirstOrDefault(answer => SamePullRequest(item, answer.Repository, answer.Number))
            : null;
    }

    private static bool IsOver(ProgressTaskDto task) =>
        task.Status is ProgressTaskStatus.Done or ProgressTaskStatus.Archived;

    private static bool SamePullRequest(HeldBackItemDto item, string repository, string number) =>
        item.Kind == HeldBackKind.PullRequest
        && item.Repository is not null
        && string.Equals(GoalItems.Key(item.Repository, Number(item)), GoalItems.Key(repository, number), StringComparison.OrdinalIgnoreCase);

    private static string Number(HeldBackItemDto item) =>
        item.Number?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
}
