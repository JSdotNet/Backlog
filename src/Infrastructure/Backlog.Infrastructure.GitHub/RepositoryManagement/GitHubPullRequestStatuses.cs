using Backlog.Modules.RepositoryManagement.Abstractions;
using Backlog.Modules.RepositoryManagement.Abstractions.DataTransferObjects;

namespace Backlog.Infrastructure.GitHub.RepositoryManagement;

/// <summary>
/// This adapter's pull request records mapped to Repository Management's published
/// status and back (local ADR 0042).
/// <para>
/// One mapping for every reader, so the pane, the session card and a task's row hand
/// the verdict the same status. The way back, <see cref="ToGitHub"/>, rebuilds the
/// record the acts on <see cref="GitHubIntegration"/> take — every field
/// <see cref="GitHubOpenPullRequest.ToStatus"/>, the branch update and the re-run read —
/// for as long as the screens still act through it.
/// </para>
/// </summary>
public static class GitHubPullRequestStatuses
{
    /// <summary>An open — or, from a pinned read, merged or closed — pull request.</summary>
    public static PullRequestStatus Map(GitHubOpenPullRequest pull)
    {
        ArgumentNullException.ThrowIfNull(pull);

        return new PullRequestStatus(
            pull.RepositoryFullName,
            pull.Number,
            pull.Url,
            pull.Title,
            Map(pull.State),
            pull.HeadRefName,
            pull.BaseRefName,
            pull.AuthorLogin,
            pull.ViewerDidAuthor,
            pull.UpdatedAt)
        {
            MergedAt = pull.MergedAt,
            ClosedAt = pull.ClosedAt,
            DefaultBranch = pull.DefaultBranch,
            Checks = Map(pull.Checks),
            CheckCounts = pull.CheckCounts is { } counts ? new PullRequestCheckCounts(counts.Passed, counts.Failed, counts.Pending) : null,
            HeadChecks = [.. pull.HeadChecks.Select(check => new PullRequestCheck(check.Name, Map(check.State), check.Duration, check.Url, check.WorkflowRunId))],
            Approvals = Map(pull.Reviews),
            MergeReady = pull.MergeReady,
            HasConflicts = pull.HasConflicts,
            IsBehind = pull.IsBehind,
            BehindBy = pull.BehindBy,
            MergeStateStatus = pull.MergeStateStatus,
            AutoMergeEnabled = pull.AutoMergeEnabled,
            Labels = pull.Labels,
            ClosingIssues = [.. pull.ClosingIssues.Select(issue => new IssueReference(issue.RepositoryFullName, issue.Number))],
            Additions = pull.Additions,
            Deletions = pull.Deletions,
            IsCrossRepository = pull.IsCrossRepository,
            ViewerReviewRequested = pull.ViewerReviewRequested,
            Handle = new PullRequestHandle(pull.NodeId, pull.HeadSha, Map(pull.PreferredMergeMethod))
        };
    }

    /// <summary>A pull request from the recently merged list.</summary>
    public static MergedPullRequest Map(GitHubMergedPullRequest pull)
    {
        ArgumentNullException.ThrowIfNull(pull);

        return new MergedPullRequest(
            pull.RepositoryFullName,
            pull.Number,
            pull.Url,
            pull.Title,
            pull.HeadRefName,
            pull.BaseRefName,
            pull.AuthorLogin,
            pull.ViewerDidAuthor,
            pull.MergedAt,
            pull.MergedByLogin)
        {
            Labels = pull.Labels,
            ClosingIssues = [.. pull.ClosingIssues.Select(issue => new IssueReference(issue.RepositoryFullName, issue.Number))]
        };
    }

    /// <summary>
    /// A pull request a REST read found: its state and branches, and nothing a verdict
    /// is ranked from — so its <see cref="PullRequestStatus.StatusRead"/> is false, and
    /// no default on it passes for GitHub's word. A head branch the read does not name
    /// is empty.
    /// </summary>
    public static PullRequestStatus Map(GitHubPullRequest pull)
    {
        ArgumentNullException.ThrowIfNull(pull);

        return new PullRequestStatus(
            pull.RepositoryFullName ?? "",
            pull.Number,
            pull.Url,
            pull.Title,
            Map(pull.State),
            HeadRefName: "",
            BaseRefName: pull.BaseRefName ?? "",
            AuthorLogin: null,
            ViewerDidAuthor: false,
            UpdatedAt: null)
        {
            DefaultBranch = pull.DefaultBranch,
            StatusRead = false
        };
    }

    public static PullRequestListing Map(GitHubPullRequestListing listing)
    {
        ArgumentNullException.ThrowIfNull(listing);

        return new PullRequestListing(
            [.. listing.PullRequests.Select(Map)],
            [.. listing.Failures.Select(Map)]);
    }

    public static MergedPullRequestListing Map(GitHubMergedPullRequestListing listing)
    {
        ArgumentNullException.ThrowIfNull(listing);

        return new MergedPullRequestListing(
            [.. listing.PullRequests.Select(Map)],
            [.. listing.Failures.Select(Map)],
            listing.Truncated);
    }

    public static RepositoryReadFailure Map(GitHubRepositoryFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new RepositoryReadFailure(failure.RepositoryFullName, failure.Message);
    }

    /// <summary>
    /// The status as the acts on <see cref="GitHubIntegration"/> take it. A status with
    /// no <see cref="PullRequestStatus.Handle"/> — one no status read answered — has no
    /// node id, so GitHub refuses any act on it rather than acting on the wrong one.
    /// </summary>
    public static GitHubOpenPullRequest ToGitHub(PullRequestStatus pull)
    {
        ArgumentNullException.ThrowIfNull(pull);

        var handle = pull.Handle;
        return new GitHubOpenPullRequest(
            pull.Number,
            pull.Url,
            pull.Title,
            pull.RepositoryFullName,
            handle?.NodeId ?? "",
            pull.State == PullRequestState.Draft,
            pull.HeadRefName,
            handle?.HeadSha,
            pull.BaseRefName,
            pull.AuthorLogin,
            pull.ViewerDidAuthor,
            ToGitHub(pull.Checks),
            pull.AutoMergeEnabled,
            pull.MergeReady,
            pull.IsBehind,
            pull.HasConflicts,
            pull.MergeStateStatus,
            ToGitHub(handle?.MergeMethod ?? PullRequestMergeMethod.Merge),
            pull.UpdatedAt)
        {
            IsMerged = pull.State == PullRequestState.Merged,
            IsClosed = pull.State == PullRequestState.Closed,
            MergedAt = pull.MergedAt,
            ClosedAt = pull.ClosedAt,
            DefaultBranch = pull.DefaultBranch,
            Labels = pull.Labels,
            CheckCounts = pull.CheckCounts is { } counts ? new GitHubCheckCounts(counts.Passed, counts.Failed, counts.Pending) : null,
            HeadChecks = [.. pull.HeadChecks.Select(check => new GitHubCheck(check.Name, ToGitHub(check.State), check.Duration, check.Url, check.WorkflowRunId))],
            BehindBy = pull.BehindBy,
            IsCrossRepository = pull.IsCrossRepository,
            ViewerReviewRequested = pull.ViewerReviewRequested,
            Additions = pull.Additions,
            Deletions = pull.Deletions,
            Reviews = new GitHubReviewSummary(ToGitHub(pull.Approvals.Decision), pull.Approvals.Approvals, pull.Approvals.ChangesRequested)
            {
                Required = pull.Approvals.Required,
                RequiredRead = pull.Approvals.RequiredRead
            },
            ClosingIssues = [.. pull.ClosingIssues.Select(issue => new GitHubIssueReference(issue.RepositoryFullName, issue.Number))]
        };
    }

    public static PullRequestState Map(GitHubItemState state) => state switch
    {
        GitHubItemState.Open => PullRequestState.Open,
        GitHubItemState.Draft => PullRequestState.Draft,
        GitHubItemState.Merged => PullRequestState.Merged,
        GitHubItemState.Closed => PullRequestState.Closed
    };

    public static PullRequestCheckState Map(GitHubCheckState state) => state switch
    {
        GitHubCheckState.None => PullRequestCheckState.None,
        GitHubCheckState.Pending => PullRequestCheckState.Pending,
        GitHubCheckState.Passing => PullRequestCheckState.Passing,
        GitHubCheckState.Failing => PullRequestCheckState.Failing
    };

    public static GitHubCheckState ToGitHub(PullRequestCheckState state) => state switch
    {
        PullRequestCheckState.None => GitHubCheckState.None,
        PullRequestCheckState.Pending => GitHubCheckState.Pending,
        PullRequestCheckState.Passing => GitHubCheckState.Passing,
        PullRequestCheckState.Failing => GitHubCheckState.Failing
    };

    public static PullRequestReviewDecision Map(GitHubReviewState state) => state switch
    {
        GitHubReviewState.None => PullRequestReviewDecision.None,
        GitHubReviewState.ReviewRequired => PullRequestReviewDecision.ReviewRequired,
        GitHubReviewState.Approved => PullRequestReviewDecision.Approved,
        GitHubReviewState.ChangesRequested => PullRequestReviewDecision.ChangesRequested
    };

    public static GitHubReviewState ToGitHub(PullRequestReviewDecision decision) => decision switch
    {
        PullRequestReviewDecision.None => GitHubReviewState.None,
        PullRequestReviewDecision.ReviewRequired => GitHubReviewState.ReviewRequired,
        PullRequestReviewDecision.Approved => GitHubReviewState.Approved,
        PullRequestReviewDecision.ChangesRequested => GitHubReviewState.ChangesRequested
    };

    public static PullRequestMergeMethod Map(GitHubMergeMethod method) => method switch
    {
        GitHubMergeMethod.Merge => PullRequestMergeMethod.Merge,
        GitHubMergeMethod.Squash => PullRequestMergeMethod.Squash,
        GitHubMergeMethod.Rebase => PullRequestMergeMethod.Rebase
    };

    public static GitHubMergeMethod ToGitHub(PullRequestMergeMethod method) => method switch
    {
        PullRequestMergeMethod.Merge => GitHubMergeMethod.Merge,
        PullRequestMergeMethod.Squash => GitHubMergeMethod.Squash,
        PullRequestMergeMethod.Rebase => GitHubMergeMethod.Rebase
    };

    private static PullRequestApprovals Map(GitHubReviewSummary reviews) =>
        new(Map(reviews.Decision), reviews.Approvals, reviews.ChangesRequested)
        {
            Required = reviews.Required,
            RequiredRead = reviews.RequiredRead
        };
}
