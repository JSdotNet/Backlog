using Backlog.Modules.RepositoryManagement.Abstractions.DataTransferObjects;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.RepositoryManagement.Abstractions.Services;

/// <summary>
/// Where Repository Management reads pull requests from and sends its acts to — GitHub,
/// answered by an adapter (local ADR 0042). One repository per call, so the module's
/// cache decides what is asked and when.
/// <para>
/// Never throws for GitHub's sake: a refused read is the read's
/// <see cref="PullRequestSourceRead.Failure"/>, and a refused act is a failed
/// <see cref="Result"/> whose message is worded for a person. Cancellation still throws.
/// </para>
/// </summary>
public interface IPullRequestSource
{
    /// <summary>The repository's open and draft pull requests.</summary>
    Task<PullRequestSourceRead> ListOpenAsync(string repositoryFullName, CancellationToken cancellationToken);

    /// <summary>The pull requests <paramref name="numbers"/> name, in any state. A number
    /// GitHub cannot find is left out. A merged answer carries its
    /// <see cref="PullRequestStatus.DefaultBranch"/>.</summary>
    Task<PullRequestSourceRead> ReadAsync(string repositoryFullName, IReadOnlyCollection<int> numbers, CancellationToken cancellationToken);

    /// <summary>The repository's pull requests merged within the recently merged window.</summary>
    Task<MergedPullRequestSourceRead> ListMergedAsync(string repositoryFullName, CancellationToken cancellationToken);

    Task<Result> MergeAsync(PullRequestStatus pullRequest, CancellationToken cancellationToken);

    Task<Result> SetAutoMergeAsync(PullRequestStatus pullRequest, bool enable, CancellationToken cancellationToken);

    Task<Result> UpdateBranchAsync(PullRequestStatus pullRequest, CancellationToken cancellationToken);

    Task<Result> MarkReadyForReviewAsync(PullRequestStatus pullRequest, CancellationToken cancellationToken);

    /// <summary>Asks every failed Actions run behind the head commit's failed checks to run
    /// again, once each and all of them. A partial refusal is a failure that says how many
    /// went through.</summary>
    Task<Result> RerunFailedAsync(PullRequestStatus pullRequest, CancellationToken cancellationToken);
}
