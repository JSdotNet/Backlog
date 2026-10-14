using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;

namespace Backlog.Modules.Progress.Services;

/// <summary>
/// The module's own answer to <see cref="ISprintPullRequestReader"/> until a host
/// registers one that reads GitHub: every pull request
/// <see cref="Abstractions.LinkedPullRequestState.Unknown"/>, with no author. A member is
/// then done by its task's status alone.
/// </summary>
public sealed class UnreadPullRequests : ISprintPullRequestReader
{
    public Task<IReadOnlyList<LinkedPullRequestDto>> ReadAsync(
        IReadOnlyList<PullRequestRefDto> pullRequests,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pullRequests);
        return Task.FromResult<IReadOnlyList<LinkedPullRequestDto>>([.. pullRequests.Select(LinkedPullRequestDto.Unread)]);
    }
}
