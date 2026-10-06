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

/// <summary>
/// One open pull request as the pull requests list shows it: where it lives, whose
/// it is, which branch it would merge where, and everything the three acts on that
/// list — update the branch, mark it ready, merge it — have to know beforehand.
/// <para>
/// Its own record rather than a wider <see cref="GitHubPullRequestStatus"/>, because
/// that one is what an entry's link reads about a pull request somebody already
/// recorded, and its every field is something a merge act needs. The list asks a
/// different question — which pull requests are there at all — and the head branch,
/// the author and the distance from the base are its answer, not a merge's. The two
/// meet in <see cref="ToStatus"/>, so the merge acts are written once.
/// </para>
/// </summary>
/// <param name="HeadSha">The head commit, sent back as <c>expected_head_sha</c> when
/// the branch is updated so GitHub refuses rather than merges into a branch that
/// moved since it was read.</param>
/// <param name="ViewerDidAuthor">Whether the account this repository is read as
/// opened it — GitHub's own answer, per repository, so "mine" means the identity the
/// repository is bound to rather than a login this app would have to guess.</param>
/// <param name="MergeReady">As <see cref="GitHubPullRequestStatus.MergeReady"/>.</param>
/// <param name="IsBehind">GitHub's <c>mergeStateStatus</c> is <c>BEHIND</c>: the base
/// branch moved on and the repository requires the head to be up to date, which is
/// the state "Update branch" exists for.</param>
/// <param name="HasConflicts"><c>mergeStateStatus</c> is <c>DIRTY</c>, or
/// <c>mergeable</c> is <c>CONFLICTING</c>. Both, because GitHub computes the merge
/// state lazily and a list read can carry the one before the other.</param>
/// <param name="MergeStateStatus">GitHub's word, verbatim, for the title of the
/// status cell; null where GitHub did not say.</param>
public sealed record GitHubOpenPullRequest(
    int Number,
    string Url,
    string Title,
    string RepositoryFullName,
    string NodeId,
    bool IsDraft,
    string HeadRefName,
    string? HeadSha,
    string BaseRefName,
    string? AuthorLogin,
    bool ViewerDidAuthor,
    GitHubCheckState Checks,
    bool AutoMergeEnabled,
    bool MergeReady,
    bool IsBehind,
    bool HasConflicts,
    string? MergeStateStatus,
    GitHubMergeMethod PreferredMergeMethod,
    DateTimeOffset? UpdatedAt)
{
    /// <summary>
    /// Draft or open for anything the open list read. A pinned read
    /// (<see cref="IGitHubClient.ListPullRequestsAsync"/>) asks for named pull requests
    /// whatever state they are in, so merged and closed are possible there and only
    /// there.
    /// </summary>
    public GitHubItemState State =>
        IsMerged ? GitHubItemState.Merged
        : IsClosed ? GitHubItemState.Closed
        : IsDraft ? GitHubItemState.Draft
        : GitHubItemState.Open;

    /// <summary>Neither merged nor closed — the only state any act on the list may be
    /// offered in.</summary>
    public bool IsOpen => !IsMerged && !IsClosed;

    /// <summary>Merged; set only by a pinned read.</summary>
    public bool IsMerged { get; init; }

    /// <summary>Closed without merging; set only by a pinned read.</summary>
    public bool IsClosed { get; init; }

    /// <summary>When it merged, for a merged pinned pull request.</summary>
    public DateTimeOffset? MergedAt { get; init; }

    /// <summary>When it closed, for a merged or closed pinned pull request.</summary>
    public DateTimeOffset? ClosedAt { get; init; }

    /// <summary>The label names, as GitHub spells them.</summary>
    public IReadOnlyList<string> Labels { get; init; } = [];

    /// <summary>How far the head commit's checks have got, or null where it has none
    /// or the token was refused them.</summary>
    public GitHubCheckCounts? CheckCounts { get; init; }

    /// <summary>What the reviewers decided.</summary>
    public GitHubReviewSummary Reviews { get; init; } = GitHubReviewSummary.None;

    /// <summary>The issues this pull request closes when it merges.</summary>
    public IReadOnlyList<GitHubIssueReference> ClosingIssues { get; init; } = [];

    /// <summary>The same pull request as the merge acts take it, so
    /// <see cref="GitHubIntegration.MergePullRequestAsync"/> and its two siblings act
    /// on a listed pull request exactly as they act on a recorded one.</summary>
    public GitHubPullRequestStatus ToStatus() =>
        new(Number, RepositoryFullName, NodeId, State, Checks, AutoMergeEnabled, MergeReady, PreferredMergeMethod);
}

/// <summary>One repository whose open pull requests could not be read, in the
/// sentence GitHub or the transport refused with.</summary>
public sealed record GitHubRepositoryFailure(string RepositoryFullName, string Message);

/// <summary>
/// The open pull requests of several repositories, read together, and the ones that
/// could not be read.
/// <para>
/// The failures travel beside the pull requests rather than as an exception, because
/// the repositories are independent: a token that lost access to one, or one renamed
/// on GitHub, says nothing about the others, and a list that went blank because of it
/// would be hiding everything it did read behind the one thing it could not.
/// </para>
/// </summary>
public sealed record GitHubPullRequestListing(
    IReadOnlyList<GitHubOpenPullRequest> PullRequests,
    IReadOnlyList<GitHubRepositoryFailure> Failures)
{
    public static GitHubPullRequestListing Empty { get; } = new([], []);
}

/// <summary>
/// One merged pull request as the pull requests list's "Recently merged" view shows
/// it: where it lives, whose it is, which branch went where, and when and by whom it
/// was merged.
/// <para>
/// Its own record rather than a <see cref="GitHubOpenPullRequest"/> with a merge time,
/// because nearly everything that one carries — the checks, the merge state, the node
/// id and the head commit — is what an act on an open pull request needs, and a merged
/// one has no act left.
/// </para>
/// </summary>
/// <param name="ViewerDidAuthor">As <see cref="GitHubOpenPullRequest.ViewerDidAuthor"/>.</param>
/// <param name="MergedByLogin">Who merged it; null where GitHub no longer knows the
/// account.</param>
public sealed record GitHubMergedPullRequest(
    int Number,
    string Url,
    string Title,
    string RepositoryFullName,
    string HeadRefName,
    string BaseRefName,
    string? AuthorLogin,
    bool ViewerDidAuthor,
    DateTimeOffset MergedAt,
    string? MergedByLogin)
{
    /// <summary>The label names, as GitHub spells them.</summary>
    public IReadOnlyList<string> Labels { get; init; } = [];

    /// <summary>The issues this pull request closed.</summary>
    public IReadOnlyList<GitHubIssueReference> ClosingIssues { get; init; } = [];
}

/// <summary>An issue named by its repository's <c>owner/name</c> and its number — what
/// a pull request's <c>closingIssuesReferences</c> answers with.</summary>
public sealed record GitHubIssueReference(string RepositoryFullName, int Number);

/// <summary>
/// How far a head commit's checks have got, check runs and commit statuses together —
/// GitHub's own "8/9".
/// <para>
/// Beside <see cref="GitHubCheckState"/> rather than instead of it: the roll-up's
/// state is the one GitHub gates the merge on, and these counts are what the row
/// shows of it.
/// </para>
/// </summary>
public sealed record GitHubCheckCounts(int Passed, int Failed, int Pending)
{
    public int Total => Passed + Failed + Pending;
}

/// <summary>GitHub's <c>reviewDecision</c>: what the reviewers' latest reviews add up
/// to against the base branch's rules.</summary>
public enum GitHubReviewDecision
{
    Approved,
    ChangesRequested,
    ReviewRequired
}

/// <summary>
/// What a pull request's reviewers decided: GitHub's decision, when the repository
/// asks for one, and how many of the latest opinionated reviews approve or ask for
/// changes. No "of N required": the number required needs rights to read the branch's
/// protection that most accounts do not have, and <see cref="GitHubReviewDecision.ReviewRequired"/>
/// already says approvals are still missing.
/// </summary>
public sealed record GitHubReviewSummary(GitHubReviewDecision? Decision, int Approvals, int ChangesRequested)
{
    public static GitHubReviewSummary None { get; } = new(null, 0, 0);
}

/// <summary>One pull request somebody pinned on this device, by <c>owner/name</c> and
/// number.</summary>
public sealed record PullRequestPin(string RepositoryFullName, int Number)
{
    /// <summary>Two pins name the same pull request: the repository compared without
    /// regard to case, the way GitHub compares it.</summary>
    public bool Names(string repositoryFullName, int number) =>
        Number == number && string.Equals(RepositoryFullName, repositoryFullName, StringComparison.OrdinalIgnoreCase);
}

/// <summary>One repository's pull requests merged since a moment, as
/// <see cref="IGitHubClient.ListMergedPullRequestsAsync"/> read them.</summary>
/// <param name="Truncated">The read stopped at the client's page cap while still inside
/// the window, so older merges inside it were not read. Said rather than hidden, so a
/// busy repository's list is not passed off as complete.</param>
public sealed record GitHubMergedPullRequestRead(
    IReadOnlyList<GitHubMergedPullRequest> PullRequests,
    bool Truncated);

/// <summary>The recently merged pull requests of several repositories, read together,
/// and the ones that could not be read — for the reason
/// <see cref="GitHubPullRequestListing"/> keeps its failures beside its rows.</summary>
/// <param name="Truncated">The repositories, by <c>owner/name</c>, whose read the page
/// cap stopped inside the window. See <see cref="GitHubMergedPullRequestRead.Truncated"/>.</param>
public sealed record GitHubMergedPullRequestListing(
    IReadOnlyList<GitHubMergedPullRequest> PullRequests,
    IReadOnlyList<GitHubRepositoryFailure> Failures,
    IReadOnlyList<string> Truncated)
{
    public static GitHubMergedPullRequestListing Empty { get; } = new([], [], []);
}

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

/// <summary>
/// One issue as the issue search answers it — what the GitHub connector turns into
/// a linked task's item.
/// </summary>
/// <param name="NodeId">GitHub's global id for the issue. Stable across a transfer
/// or a rename, which the number and the repository are not.</param>
/// <param name="StateReason">Why a closed issue closed — <c>completed</c>,
/// <c>not_planned</c>, <c>duplicate</c> — or null, which an issue closed before
/// GitHub recorded reasons answers with.</param>
/// <param name="AssigneeLogin">The first assignee's login, or null.</param>
/// <param name="Labels">The label names, as GitHub spells them.</param>
public sealed record GitHubSearchedIssue(
    string NodeId,
    int Number,
    string Url,
    string Title,
    string Body,
    bool IsOpen,
    string? StateReason,
    string? AssigneeLogin,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<string> Labels);

/// <summary>What one repository's issue search read. <see cref="Truncated"/> is set
/// when search would not hand back every match — past its thousand-result limit, or
/// an answer GitHub itself marked incomplete — so the list is not the whole
/// repository.</summary>
public sealed record GitHubIssueSearchRead(IReadOnlyList<GitHubSearchedIssue> Issues, bool Truncated);

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
