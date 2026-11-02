using Backlog.Modules.Progress.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Progress.Abstractions.Services;

/// <summary>
/// The state, merge day, checks and approvals of the pull requests goals count and
/// attached tasks link (<c>.devbook/domain/progress/features.md#pull-request-status</c>).
/// <para>
/// Progress reaches a pull request by its repository and number, and reads what GitHub
/// says about it through Repository Management's contract, the one every reader of a
/// pull request shares (local ADR 0042 §5, <c>.devbook/domain/progress/dependencies.md</c>);
/// Progress itself never names that module. Until a host registers an adapter,
/// the module's own answer reads every pull request as
/// <see cref="LinkedPullRequestState.Unknown"/> with no checks and no approvals, so a
/// task is then done by its status alone.
/// </para>
/// </summary>
public interface IGoalPullRequestReader
{
    /// <summary>One answer per pull request asked about, in the same order, carrying the
    /// caller's own spelling of the repository and number.</summary>
    Task<IReadOnlyList<GoalPullRequestDto>> ReadAsync(
        IReadOnlyList<PullRequestRefDto> pullRequests,
        CancellationToken cancellationToken = default);
}
