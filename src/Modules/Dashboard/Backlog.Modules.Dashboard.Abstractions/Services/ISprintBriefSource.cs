namespace Backlog.Modules.Dashboard.Abstractions.Services;

/// <summary>
/// The state of a pull request linked to a sprint member, as the sprint read it.
/// <para>
/// A mirror of the Progress context's own state rather than that type: nothing in this
/// module may name a Progress type (guideline ADR 0005), and the weekly brief has to tell
/// a merged pull request from one still waiting on review.
/// </para>
/// </summary>
public enum SprintBriefPullRequestState
{
    Unknown,
    Open,
    Draft,
    Merged,
    Closed
}

/// <summary>A pull request linked to a sprint member.</summary>
/// <param name="Repository">The repository it lives in, as the task records it —
/// <c>owner/name</c> when it can be linked to.</param>
/// <param name="Number">Its number, as the task records it.</param>
/// <param name="MergedOn">The day it merged, when it did and the day is known.</param>
public sealed record SprintBriefPullRequest(
    string Repository,
    string Number,
    SprintBriefPullRequestState State,
    DateOnly? MergedOn);

/// <summary>One member task of a sprint, reduced to what the weekly brief reports.</summary>
/// <param name="PlanTag">The plan the task is filed under, without the sigil, or null.</param>
/// <param name="IsGoal">Whether one of the series' goal rules picks it.</param>
/// <param name="IsBlocked">Marked blocked by hand and not done.</param>
/// <param name="IsDone">Done by the Progress context's rule: the task is Done, or every
/// pull request linked to it is merged.</param>
/// <param name="DoneOn">The day it was done, when it is and the day is known.</param>
/// <param name="AddedAfterStart">Whether it joined the sprint after its first day.</param>
/// <param name="Removed">Whether closing the sprint sent it back to the backlog or
/// dropped it. A task taken out of an open sprint leaves no member behind.</param>
public sealed record SprintBriefMember(
    Guid TaskId,
    string Title,
    string? PlanTag,
    bool IsGoal,
    bool IsBlocked,
    bool IsDone,
    DateOnly? DoneOn,
    bool AddedAfterStart,
    bool Removed,
    IReadOnlyList<SprintBriefPullRequest> PullRequests)
{
    /// <summary>Being worked on by the Progress context's rule, neither done nor blocked
    /// — what the sprint view's table writes as In progress or In review rather than Not
    /// started.</summary>
    public bool IsInProgress { get; init; }

    /// <summary>The repository the task belongs to, by alias, or null.</summary>
    public string? Repository { get; init; }

    /// <summary>Who works on it, as the sprint view's Owner column lists them.</summary>
    public IReadOnlyList<string> Owners { get; init; } = [];

    /// <summary>The sprint's note on the item, or null.</summary>
    public string? Note { get; init; }

    /// <summary>Whether the item is marked as an open question in the sprint.</summary>
    public bool OpenQuestion { get; init; }
}

/// <summary>One sprint of a series shown in the weekly brief, with its members read
/// against their current state.</summary>
/// <param name="Repositories">The series' repository aliases, in the order they were
/// added.</param>
/// <param name="Name">The sprint's name: the series' pattern with its number filled in.</param>
/// <param name="Start">First day, inclusive.</param>
/// <param name="End">Last day, inclusive.</param>
public sealed record SprintBriefSprint(
    Guid SeriesId,
    string SeriesName,
    IReadOnlyList<string> Repositories,
    int Number,
    string Name,
    DateOnly Start,
    DateOnly End,
    IReadOnlyList<SprintBriefMember> Members)
{
    /// <summary>The plans the series' "in these plans" goal rule names, without the
    /// sigil — the groups the sprint's table puts first.</summary>
    public IReadOnlyList<string> GoalPlans { get; init; } = [];
}

/// <summary>
/// PORT — the sprints the weekly brief reports, for its Sprint block.
/// <para>
/// A window in the signature, on <see cref="IPlanProgressSource"/>'s pattern: the brief
/// steps back over weeks it already read, so it asks once for every week it can show and
/// picks the reported week's sprint itself. Nothing in this contract names a Progress
/// type; the adapter that answers it may see both contexts, nothing in this module may.
/// </para>
/// </summary>
public interface ISprintBriefSource
{
    /// <summary>
    /// For every sprint series marked to show in the weekly brief, each of its sprints
    /// whose start–end overlaps <paramref name="from"/> to <paramref name="to"/>, both
    /// inclusive. Empty — never a failure — while Progress is switched off, when no
    /// series is marked, or when a series cannot be read.
    /// </summary>
    Task<IReadOnlyList<SprintBriefSprint>> ReadAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);
}
