using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;

namespace Backlog.Modules.Progress.Services;

/// <summary>
/// The module's own answer to <see cref="IGoalPullRequestReader"/> until a host
/// registers one that reads GitHub: every pull request
/// <see cref="Abstractions.LinkedPullRequestState.Unknown"/>, with no checks, no
/// approvals and no author. A task is then done by its status alone.
/// </summary>
public sealed class UnreadPullRequests : IGoalPullRequestReader
{
    public Task<IReadOnlyList<GoalPullRequestDto>> ReadAsync(
        IReadOnlyList<PullRequestRefDto> pullRequests,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pullRequests);
        return Task.FromResult<IReadOnlyList<GoalPullRequestDto>>([.. pullRequests.Select(GoalPullRequestDto.Unread)]);
    }
}
