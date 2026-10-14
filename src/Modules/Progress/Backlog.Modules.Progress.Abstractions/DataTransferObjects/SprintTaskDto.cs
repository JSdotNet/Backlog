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
    IReadOnlyList<PullRequestRefDto> PullRequests);

/// <summary>A pull request linked to a task: the repository it lives in and its
/// number, as the task records it.</summary>
public sealed record PullRequestRefDto(string Repository, string Number);

/// <summary>What a linked pull request says about itself: its state, when it merged,
/// and who opened it. <see cref="LinkedPullRequestState.Unknown"/> and no author until
/// something reads it.</summary>
public sealed record LinkedPullRequestDto(
    string Repository,
    string Number,
    LinkedPullRequestState State,
    DateOnly? MergedOn,
    string? Author)
{
    public static LinkedPullRequestDto Unread(PullRequestRefDto pullRequest) =>
        new(pullRequest.Repository, pullRequest.Number, LinkedPullRequestState.Unknown, null, null);
}
