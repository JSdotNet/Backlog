using Backlog.Modules.RepositoryManagement.Abstractions.DataTransferObjects;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.RepositoryManagement.Abstractions.Services;

/// <summary>
/// Repository Management's one contract for pull-request state (local ADR 0042 §2–§3):
/// every reader — the pull requests pane, the session card, a task's row, a task's side
/// panel, Progress — reads pull requests through it, and every act on one goes through
/// it, so one pull request is read once and answers the same everywhere.
/// <para>
/// Behind it is one cache, on the terms ADR 0042 §3 sets: a merged pull request is read
/// once, kept, and remembered across a restart; a number GitHub answered without is not
/// asked about again; any other answer is reused while younger than the reader's
/// <see cref="PullRequestReadOptions.MaxAge"/>, and a read of it still out is waited on;
/// a closed one is asked about again once it is stale, because GitHub can reopen it; a
/// refused read is kept nowhere. An archived repository is never read (local ADR 0029).
/// </para>
/// <para>
/// Nothing here throws for GitHub's sake: a repository that refused is a
/// <see cref="RepositoryReadFailure"/> beside the others' answers, and a refused act is
/// a failed <see cref="Result"/> whose message is already worded for a person.
/// Cancellation still throws.
/// </para>
/// </summary>
public interface IPullRequests
{
    /// <summary>At least one repository is registered, so there is anything to read.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// The open and draft pull requests of <paramref name="repositories"/>, by
    /// <c>owner/name</c>, or of every active repository when null — newest update first,
    /// each with its <see cref="PullRequestStatus.StackParent"/> set. An archived
    /// repository is skipped with no failure. Each row also answers a later
    /// <see cref="ReadAsync"/> of its number.
    /// </summary>
    Task<PullRequestListing> ListOpenAsync(
        IReadOnlyCollection<string>? repositories = null,
        PullRequestReadOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>The pull requests merged within the recently merged window, newest merge
    /// first, of <paramref name="repositories"/> or of every active repository when
    /// null.</summary>
    Task<MergedPullRequestListing> ListMergedAsync(
        IReadOnlyCollection<string>? repositories = null,
        PullRequestReadOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The pull requests <paramref name="pullRequests"/> name, in any state, in the order
    /// asked. One whose repository answered without it is absent with no failure. An
    /// archived repository's are answered from what is kept, and nothing is read for
    /// them.
    /// </summary>
    Task<PullRequestListing> ReadAsync(
        IReadOnlyCollection<PullRequestReference> pullRequests,
        PullRequestReadOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>What is kept for <paramref name="pullRequests"/>, however old — in memory,
    /// or remembered across a restart — reading nothing. What a first frame, and a
    /// reader with GitHub out of reach, draws.</summary>
    PullRequestListing Kept(IReadOnlyCollection<PullRequestReference> pullRequests);

    /// <summary>Merges the pull request now.</summary>
    Task<Result> MergeAsync(PullRequestReference pullRequest, CancellationToken cancellationToken = default);

    /// <summary>Hands the pull request to GitHub's auto-merge, or withdraws that request.</summary>
    Task<Result> SetAutoMergeAsync(PullRequestReference pullRequest, bool enable, CancellationToken cancellationToken = default);

    /// <summary>Brings the pull request's branch up to date with its base.</summary>
    Task<Result> UpdateBranchAsync(PullRequestReference pullRequest, CancellationToken cancellationToken = default);

    /// <summary>Takes a draft out of draft.</summary>
    Task<Result> MarkReadyForReviewAsync(PullRequestReference pullRequest, CancellationToken cancellationToken = default);

    /// <summary>Runs the failed GitHub Actions runs behind the head commit's failed checks
    /// again, each once and all of them; a partial refusal says how many went through.</summary>
    Task<Result> RerunFailedAsync(PullRequestReference pullRequest, CancellationToken cancellationToken = default);

    /// <summary>
    /// Raised after a command has acted and its repository has been read again — whether
    /// the act went through or was refused — on whatever thread the re-read landed on.
    /// Every command above acts on the status kept for the pull request, reading it
    /// first when none is, and fails with no GitHub call for an archived repository.
    /// </summary>
    event Action<PullRequestsChanged>? Changed;
}
