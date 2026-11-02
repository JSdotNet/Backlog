namespace Backlog.Modules.RepositoryManagement.Abstractions;

/// <summary>Where a pull request stands. Merged is kept distinct from closed because
/// they mean opposite things: one landed its work, the other did not.</summary>
public enum PullRequestState
{
    Open,
    Draft,
    Merged,
    Closed
}

/// <summary>
/// The combined result of every check and commit status on a pull request's head
/// commit, in the four answers a reader can give.
/// <para>
/// <see cref="None"/> is its own member rather than a null: a pull request with no
/// checks configured is a real, common state and not a failure to read one. GitHub's
/// <c>ERROR</c> folds into <see cref="Failing"/> and <c>EXPECTED</c> into
/// <see cref="Pending"/>, because the reader's question is only ever "will this go
/// green on its own".
/// </para>
/// </summary>
public enum PullRequestCheckState
{
    None,
    Pending,
    Passing,
    Failing
}

/// <summary>What the reviewers' latest reviews add up to against the base branch's
/// rules. <see cref="None"/> is no decision at all — a repository that asks for no
/// review — rather than a null.</summary>
public enum PullRequestReviewDecision
{
    None,
    ReviewRequired,
    Approved,
    ChangesRequested
}

/// <summary>How a pull request is merged. Which of the three a repository allows is
/// its own setting; a merge sends the first it allows.</summary>
public enum PullRequestMergeMethod
{
    Merge,
    Squash,
    Rebase
}
