using System.Text.Json;

namespace Backlog.Infrastructure.GitHub;

internal static class GitHubJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
}

/// <summary>Where an issue or pull request currently stands. Merged is kept
/// distinct from closed because for a pull request they mean opposite
/// things.</summary>
public enum GitHubItemState
{
    Open,
    Draft,
    Merged,
    Closed
}

/// <summary>A GitHub issue as the app cares about it: enough to link to it and
/// to say whether it is still open.</summary>
public sealed record GitHubIssue(
    int Number,
    string Url,
    string Title,
    GitHubItemState State,
    DateTimeOffset? UpdatedAt);

/// <summary>A pull request that references an issue.</summary>
public sealed record GitHubPullRequest(
    int Number,
    string Url,
    string Title,
    GitHubItemState State,
    string? RepositoryFullName);

/// <summary>
/// The combined result of every check and commit status on a pull request's head
/// commit — GitHub's <c>statusCheckRollup</c>, in the four answers a link can give.
/// <para>
/// <see cref="None"/> is its own member rather than a null: a pull request with no
/// checks configured is a real, common state and not a failure to read one, and a
/// link that drew "pending" for it would be promising a result that never comes.
/// GitHub's <c>ERROR</c> folds into <see cref="Failing"/> and <c>EXPECTED</c> into
/// <see cref="Pending"/>, because the reader's question is only ever "will this go
/// green on its own".
/// </para>
/// </summary>
public enum GitHubCheckState
{
    None,
    Pending,
    Passing,
    Failing
}

/// <summary>How a pull request is merged — GitHub's <c>PullRequestMergeMethod</c>.
/// Which of the three a repository allows is its own setting.</summary>
public enum GitHubMergeMethod
{
    Merge,
    Squash,
    Rebase
}

/// <summary>
/// One pull request as the merge controls need it: what state it is in, how its
/// checks stand, and whether GitHub is already holding it for auto-merge.
/// <para>
/// Read in one GraphQL query rather than assembled from REST, because REST has no
/// home for the check roll-up or the auto-merge request and no endpoint for
/// enabling auto-merge at all. <see cref="NodeId"/> is what the mutations name the
/// pull request by, and <see cref="PreferredMergeMethod"/> is the method they send —
/// so reading this is everything a merge act has to know beforehand.
/// </para>
/// </summary>
/// <param name="MergeReady">GitHub's <c>mergeStateStatus</c> is <c>CLEAN</c>,
/// <c>UNSTABLE</c> or <c>HAS_HOOKS</c>: the pull request can merge right now. GitHub
/// refuses to queue auto-merge in exactly these states, which is why they are the
/// ones that turn the offer into "merge now".</param>
/// <param name="PreferredMergeMethod">The first method the repository allows, in the
/// order its merge button lists them: merge commit, then squash, then rebase.</param>
public sealed record GitHubPullRequestStatus(
    int Number,
    string RepositoryFullName,
    string NodeId,
    GitHubItemState State,
    GitHubCheckState Checks,
    bool AutoMergeEnabled,
    bool MergeReady,
    GitHubMergeMethod PreferredMergeMethod);

/// <summary>Everything one refresh learned about a pushed entry: the issue and
/// the pull requests that mention it.</summary>
public sealed record GitHubIssueSnapshot(
    GitHubIssue Issue,
    IReadOnlyList<GitHubPullRequest> PullRequests,
    DateTimeOffset RetrievedAt)
{
    /// <summary>The pull request worth showing when there is only room for one:
    /// a merged one if any, otherwise the most recently opened.</summary>
    public GitHubPullRequest? Headline =>
        PullRequests.FirstOrDefault(p => p.State == GitHubItemState.Merged)
        ?? PullRequests.FirstOrDefault(p => p.State is GitHubItemState.Open or GitHubItemState.Draft)
        ?? PullRequests.FirstOrDefault();
}

/// <summary>A compressed screenshot captured by the app for a feedback report.</summary>
public sealed record GitHubFeedbackScreenshot(
    string DataUrl,
    string MediaType,
    int Width,
    int Height,
    long SizeBytes);

/// <summary>A file committed to a repository through the Contents API.</summary>
public sealed record GitHubUploadedFile(string Path, string DownloadUrl);

/// <summary>The outcome of <see cref="IGitHubClient.CommitFileAsync"/>:
/// <see cref="Committed"/> is false when GitHub already held these exact bytes
/// and nothing was written. <see cref="Sha"/> is the blob's id either way.</summary>
public sealed record GitHubCommittedFile(string Path, string Sha, bool Committed);
