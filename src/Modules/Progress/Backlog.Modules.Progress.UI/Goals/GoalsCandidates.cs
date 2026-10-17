using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.UI.Components.Progress;

namespace Backlog.Modules.Progress.UI.Goals;

/// <summary>
/// One of the shell's tasks as the Goals view reads it: Progress UI may not reach Tasks
/// (local ADR 0028, ADR 0035 §6), so the shell builds these from its task list. Read on
/// every asking, never kept.
/// </summary>
/// <param name="Id">The task's stored id.</param>
/// <param name="Title">What the task reads.</param>
/// <param name="Status">Its status as a dot: in progress is <see cref="GoalTaskStatus.Doing"/>,
/// in progress and blocked <see cref="GoalTaskStatus.Blocked"/>.</param>
/// <param name="PlanTags">The roadmap slugs its tags carry, with or without the sigil — what
/// the roadmap item of each gathers by tag, a bare <c>#slug</c> tag included.</param>
/// <param name="PullRequests">The pull requests it links.</param>
/// <param name="InScope">Whether the header's repository scope holds it.</param>
public sealed record GoalsTaskCandidate(
    Guid Id,
    string Title,
    GoalTaskStatus Status,
    IReadOnlyList<string> PlanTags,
    IReadOnlyList<PullRequestRefDto> PullRequests,
    bool InScope);

/// <summary>An open pull request the shell's reader found.</summary>
/// <param name="Repository">Its repository, <c>owner/name</c>.</param>
/// <param name="InScope">Whether the header's repository scope holds its repository.</param>
/// <param name="Url">Where it opens, when known.</param>
public sealed record GoalsPullRequestCandidate(string Repository, int Number, string Title, bool InScope, string? Url = null);

/// <summary>
/// Everything the shell hands the Goals view: its tasks, the plan tags Roadmap Planning
/// holds, and the open pull requests its reader found — what the attach search matches
/// and what No goal is read from.
/// </summary>
public sealed record GoalsCandidates(
    IReadOnlyList<GoalsTaskCandidate> Tasks,
    IReadOnlyList<string> Plans,
    IReadOnlyList<GoalsPullRequestCandidate> PullRequests)
{
    /// <summary>A shell with nothing to hand in.</summary>
    public static GoalsCandidates Empty { get; } = new([], [], []);
}
