namespace Backlog.Modules.RepositoryManagement.Abstractions.DataTransferObjects;

/// <summary>How far a head commit's checks have got, check runs and commit statuses
/// together — GitHub's own "8/9".</summary>
public sealed record PullRequestCheckCounts(int Passed, int Failed, int Pending)
{
    public int Total => Passed + Failed + Pending;
}

/// <summary>One check on a head commit: a check run, or a commit status another service
/// set. Never <see cref="PullRequestCheckState.None"/>.</summary>
/// <param name="Duration">From start to completion, for a check run that has completed;
/// null for one still running and for a commit status.</param>
/// <param name="Url">The check's own page, or null where it names none.</param>
/// <param name="WorkflowRunId">The GitHub Actions workflow run the check run belongs to;
/// null for a check run another app made and for every commit status.</param>
public sealed record PullRequestCheck(
    string Name,
    PullRequestCheckState State,
    TimeSpan? Duration,
    string? Url,
    long? WorkflowRunId)
{
    /// <summary>A GitHub Actions run, which GitHub can be asked to run again.</summary>
    public bool IsGitHubActions => WorkflowRunId is not null;
}

/// <summary>
/// What a pull request's reviewers decided: the decision, when the repository asks for
/// one, how many of the latest opinionated reviews approve or ask for changes, and —
/// where the read asked — how many approvals the base branch requires.
/// </summary>
public sealed record PullRequestApprovals(PullRequestReviewDecision Decision, int Approvals, int ChangesRequested)
{
    public static PullRequestApprovals None { get; } = new(PullRequestReviewDecision.None, 0, 0);

    /// <summary>The approving reviews the base branch requires before a merge, or null
    /// where no rule asks for one or the rules were not read.</summary>
    public int? Required { get; init; }

    /// <summary>Whether the base branch's rules were read at all, so a null
    /// <see cref="Required"/> means "no rule asks" rather than "nobody asked".</summary>
    public bool RequiredRead { get; init; }
}

/// <summary>
/// What a command sends GitHub to name the pull request by: its node id, the head commit
/// an update is pinned to, and the merge method the repository allows first. Opaque to
/// every reader but the source that read it.
/// </summary>
public sealed record PullRequestHandle(string NodeId, string? HeadSha, PullRequestMergeMethod MergeMethod);

/// <summary>
/// One pull request as Repository Management reports it to every reader — the pull
/// requests pane, the session card, a task's row, Progress (local ADR 0042 §2): its
/// identity and branches, its state and merge day, its checks, its approvals, whether
/// it is mergeable, behind or held for auto-merge, and what the pane draws of it.
/// <para>
/// One record for every reader, so the same pull request gives the same verdict
/// wherever it appears (<see cref="PullRequestVerdict"/>). The positional fields are
/// the identity and the branches; the rest are init, so a fixture states only what it
/// needs.
/// </para>
/// </summary>
/// <param name="ViewerDidAuthor">Whether the account this repository is read as opened
/// it — GitHub's own answer, per repository.</param>
public sealed record PullRequestStatus(
    string RepositoryFullName,
    int Number,
    string Url,
    string Title,
    PullRequestState State,
    string HeadRefName,
    string BaseRefName,
    string? AuthorLogin,
    bool ViewerDidAuthor,
    DateTimeOffset? UpdatedAt)
{
    /// <summary>The pull request by repository and number.</summary>
    public PullRequestReference Reference => new(RepositoryFullName, Number);

    /// <summary>When it merged, for a merged pull request.</summary>
    public DateTimeOffset? MergedAt { get; init; }

    /// <summary>When it closed, for a merged or closed pull request.</summary>
    public DateTimeOffset? ClosedAt { get; init; }

    /// <summary>The repository's default branch at the read, or null where the read did
    /// not say. With <see cref="BaseRefName"/>, what tells a merge that landed work from
    /// one into a stacked parent's branch.</summary>
    public string? DefaultBranch { get; init; }

    /// <summary>The roll-up of the head commit's checks — the state GitHub gates the
    /// merge on.</summary>
    public PullRequestCheckState Checks { get; init; }

    /// <summary>How far the head commit's checks have got, or null where it has none or
    /// they were not read.</summary>
    public PullRequestCheckCounts? CheckCounts { get; init; }

    /// <summary>The head commit's checks one by one, in GitHub's order, up to the
    /// client's limit — or every one where a failed check lies past it.</summary>
    public IReadOnlyList<PullRequestCheck> HeadChecks { get; init; } = [];

    /// <summary>What the reviewers decided.</summary>
    public PullRequestApprovals Approvals { get; init; } = PullRequestApprovals.None;

    /// <summary>GitHub's <c>mergeStateStatus</c> is <c>CLEAN</c>, <c>UNSTABLE</c> or
    /// <c>HAS_HOOKS</c>: it can merge right now, and GitHub refuses to queue auto-merge in
    /// exactly these states.</summary>
    public bool MergeReady { get; init; }

    /// <summary>GitHub cannot merge it until its conflicts with the base are resolved.</summary>
    public bool HasConflicts { get; init; }

    /// <summary>GitHub's <c>BEHIND</c>: the base moved on and the repository requires
    /// the head to be up to date.</summary>
    public bool IsBehind { get; init; }

    /// <summary>How many commits the base has that the head does not, or null where
    /// GitHub could not compare the two. Zero is up to date.</summary>
    public int? BehindBy { get; init; }

    /// <summary>GitHub's merge state word, verbatim, or null where it did not say.</summary>
    public string? MergeStateStatus { get; init; }

    /// <summary>GitHub is holding it for auto-merge.</summary>
    public bool AutoMergeEnabled { get; init; }

    /// <summary>The open pull request this one is stacked on — its base branch is that
    /// one's head branch — or null for one at the bottom of a stack or in none. Set by
    /// <see cref="PullRequestStacks.Place"/> — every open list the contract answers is
    /// placed so — never by the source that read it.</summary>
    public PullRequestReference? StackParent { get; init; }

    /// <summary>The label names, as GitHub spells them.</summary>
    public IReadOnlyList<string> Labels { get; init; } = [];

    /// <summary>The issues this pull request closes when it merges.</summary>
    public IReadOnlyList<IssueReference> ClosingIssues { get; init; } = [];

    /// <summary>Lines it adds, or null where GitHub did not say.</summary>
    public int? Additions { get; init; }

    /// <summary>Lines it removes, or null where GitHub did not say.</summary>
    public int? Deletions { get; init; }

    /// <summary>The head branch lives in another repository — a fork.</summary>
    public bool IsCrossRepository { get; init; }

    /// <summary>What a command names the pull request by, or null where the read that
    /// answered carried none.</summary>
    public PullRequestHandle? Handle { get; init; }

    /// <summary>Whether the status half was read at all. False for an answer that
    /// carries state and branches only — a fallback read or a settled fact — so no
    /// verdict fact on it is GitHub's word.</summary>
    public bool StatusRead { get; init; } = true;

    /// <summary>Neither merged nor closed — the only state an act may be offered in.</summary>
    public bool IsOpen => State is PullRequestState.Open or PullRequestState.Draft;

    /// <summary>Still a draft.</summary>
    public bool IsDraft => State is PullRequestState.Draft;

    /// <summary>The GitHub Actions workflow runs behind the head commit's failed checks,
    /// each once — what Re-run failed asks GitHub to run again.</summary>
    public IReadOnlyList<long> FailedWorkflowRunIds =>
        [.. HeadChecks
            .Where(check => check.State == PullRequestCheckState.Failing && check.WorkflowRunId is not null)
            .Select(check => check.WorkflowRunId!.Value)
            .Distinct()];

    /// <summary>Re-run failed may be offered: it is still open and at least one of its
    /// failed checks is a GitHub Actions run.</summary>
    public bool CanRerunFailed => IsOpen && HeadChecks.Any(check =>
        check.State == PullRequestCheckState.Failing && check.IsGitHubActions);

    /// <summary>The names of the failed checks, each once, in GitHub's order.</summary>
    public IReadOnlyList<string> FailingCheckNames =>
        [.. HeadChecks
            .Where(check => check.State == PullRequestCheckState.Failing)
            .Select(check => check.Name)
            .Distinct(StringComparer.Ordinal)];

    /// <summary>
    /// A merged pull request as it was remembered across a restart: merged, with its
    /// merge day, branches, closing issues and labels, and nothing a verdict is ranked
    /// from — so <see cref="StatusRead"/> is false. A branch the memory does not hold is
    /// empty.
    /// </summary>
    public static PullRequestStatus Settled(SettledPullRequest pullRequest)
    {
        ArgumentNullException.ThrowIfNull(pullRequest);

        return new PullRequestStatus(
            pullRequest.RepositoryFullName,
            pullRequest.Number,
            pullRequest.Url,
            pullRequest.Title,
            PullRequestState.Merged,
            pullRequest.HeadRefName,
            BaseRefName: pullRequest.BaseRefName ?? "",
            pullRequest.AuthorLogin,
            ViewerDidAuthor: false,
            UpdatedAt: null)
        {
            MergedAt = pullRequest.MergedAt,
            ClosedAt = pullRequest.MergedAt,
            DefaultBranch = pullRequest.DefaultBranch,
            ClosingIssues = pullRequest.ClosingIssues,
            Labels = pullRequest.Labels,
            StatusRead = false
        };
    }
}
