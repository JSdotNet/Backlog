namespace Backlog.Modules.Progress.Abstractions.DataTransferObjects;

/// <summary>
/// One task as Progress reads it from Tasks, through <see cref="Services.ISprintTaskSource"/>.
/// A snapshot for one reading: Progress keeps no copy of it.
/// </summary>
/// <param name="IsBlocked">Whether a person marked the task blocked.</param>
/// <param name="PlanTags">The plan tags the task is filed under, without the
/// <c>+</c> sigil.</param>
/// <param name="RepositoryAliases">The repositories the task belongs to, by alias.</param>
/// <param name="Tags">The task's tags as written, plan tags included.</param>
/// <param name="CompletedOn">The day the task was finished, when it is.</param>
/// <param name="CreatedOn">The day the task was created, when known.</param>
/// <param name="PullRequests">The pull requests linked to the task.</param>
/// <param name="StartedOn">The day work on the task first moved to in progress, when
/// Tasks knows it. Last and defaulted, so a caller written before it still constructs one.</param>
/// <param name="BlockedSince">The day a person marked the task blocked, while it is.</param>
/// <param name="DueOn">The day the task is due, when it has one — what the stand-up's
/// Planned reads the items due in the next window from. Defaulted, as the ones above.</param>
/// <param name="Effort">The task's <c>effort:</c> in story points, when it carries one.</param>
/// <param name="Order">The task's place in the backlog's order — a plan's order among its entries.</param>
/// <param name="WaitsOnOpenWork">Whether one of the task's <c>after:</c> dependencies, resolved
/// against the whole backlog, names a task that is not done or names no live task — so the
/// task is not ready yet (<c>.devbook/domain/tasks/domain.md#readiness</c>).</param>
public sealed record SprintTaskDto(
    Guid Id,
    string Title,
    SprintTaskStatus Status,
    bool IsBlocked,
    IReadOnlyList<string> PlanTags,
    IReadOnlyList<string> RepositoryAliases,
    IReadOnlyList<string> Tags,
    DateOnly? CompletedOn,
    DateOnly? CreatedOn,
    IReadOnlyList<PullRequestRefDto> PullRequests,
    DateOnly? StartedOn = null,
    DateOnly? BlockedSince = null,
    DateOnly? DueOn = null,
    int? Effort = null,
    int Order = 0,
    bool WaitsOnOpenWork = false);

/// <summary>A pull request linked to a task: the repository it lives in and its
/// number, as the task records it.</summary>
public sealed record PullRequestRefDto(string Repository, string Number);

/// <summary>What a linked pull request says about itself: its state, when it merged,
/// who opened it and when. <see cref="LinkedPullRequestState.Unknown"/> and no author until
/// something reads it.</summary>
/// <param name="OpenedOn">The day it was opened, when read. Last and defaulted, so a caller
/// written before it still constructs one.</param>
public sealed record LinkedPullRequestDto(
    string Repository,
    string Number,
    LinkedPullRequestState State,
    DateOnly? MergedOn,
    string? Author,
    DateOnly? OpenedOn = null)
{
    public static LinkedPullRequestDto Unread(PullRequestRefDto pullRequest) =>
        new(pullRequest.Repository, pullRequest.Number, LinkedPullRequestState.Unknown, null, null);
}
