using Backlog.Modules.Progress.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Progress.Abstractions.Services;

/// <summary>
/// The state, merge day and author of the pull requests linked to member tasks.
/// <para>
/// Progress reaches a pull request only through the task that links it, and reads
/// what GitHub says about it through the same infrastructure the pull requests
/// list uses (<c>.devbook/domain/progress/dependencies.md</c>). Until a host
/// registers an adapter, the module's own answer reads every pull request as
/// <see cref="LinkedPullRequestState.Unknown"/> with no author, so a member is
/// then done by its task's status alone.
/// </para>
/// </summary>
public interface ISprintPullRequestReader
{
    /// <summary>One answer per pull request asked about, in the same order.</summary>
    Task<IReadOnlyList<LinkedPullRequestDto>> ReadAsync(
        IReadOnlyList<PullRequestRefDto> pullRequests,
        CancellationToken cancellationToken = default);
}
