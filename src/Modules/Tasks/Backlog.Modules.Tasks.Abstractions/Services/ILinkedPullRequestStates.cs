using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Tasks.Abstractions.Services;

/// <summary>
/// Where a pull request a task links to stands, in Tasks' own words.
/// <para>
/// Tasks' copy of Repository Management's four states rather than a reference to that
/// module (local ADR 0042 §4): a screen asks its own module, and an infrastructure
/// adapter that may see both contexts translates
/// (<c>ModuleBoundaryTests.A_module_ui_asks_only_its_own_modules_published_surface</c>).
/// </para>
/// </summary>
public enum LinkedPullRequestState
{
    /// <summary>Open and ready for review.</summary>
    Open,

    /// <summary>Open as a draft: not yet asking for review, and not mergeable.</summary>
    Draft,

    /// <summary>Merged. Final: GitHub never un-merges a pull request.</summary>
    Merged,

    /// <summary>Closed without merging. Not final: it can be reopened.</summary>
    Closed
}

/// <summary>The tone of a pull request's verdict, as Repository Management words it
/// (<c>.devbook/domain/repository-management/features.md#one-verdict-per-pull-request</c>):
/// the ink the task row's badge wears beside the same verdict the Pull requests pane
/// draws.</summary>
public enum LinkedPullRequestTone
{
    /// <summary>Nothing asks for anyone yet: a draft.</summary>
    Quiet,

    /// <summary>Waiting on something under way: checks running, or a review.</summary>
    Live,

    /// <summary>The person is asked for: a branch behind its base.</summary>
    Alert,

    /// <summary>Something failed: checks, conflicts, or changes requested.</summary>
    Fault,

    /// <summary>Done: merged, or ready to merge.</summary>
    Settled
}

/// <summary>Which verdict Repository Management gives a pull request — what its label
/// names — so a screen that words the pull request's other facts beside the label does
/// not say one twice.</summary>
public enum LinkedPullRequestVerdictKind
{
    /// <summary>Its branch conflicts with its base.</summary>
    Conflicts,

    /// <summary>A check failed: the verdict is about the checks.</summary>
    FailingChecks,

    /// <summary>A reviewer asked for changes: the verdict is about the review.</summary>
    ChangesRequested,

    /// <summary>Its branch is behind a base that requires it up to date.</summary>
    Behind,

    /// <summary>Still a draft: the verdict is the state.</summary>
    Draft,

    /// <summary>Checks still running: the verdict is about the checks.</summary>
    ChecksRunning,

    /// <summary>Waiting for an approving review: the verdict is about the review.</summary>
    ReviewRequired,

    /// <summary>Ready to merge.</summary>
    Ready,

    /// <summary>Merged.</summary>
    Merged
}

/// <summary>The roll-up of a pull request's checks on its head commit.</summary>
public enum LinkedPullRequestChecks
{
    /// <summary>No checks reported, or none read.</summary>
    None,

    /// <summary>At least one check still running.</summary>
    Pending,

    /// <summary>Every check passed.</summary>
    Passing,

    /// <summary>At least one check failed.</summary>
    Failing
}

/// <summary>What GitHub decided about a pull request's review.</summary>
public enum LinkedPullRequestReview
{
    /// <summary>No review decision, or none read.</summary>
    None,

    /// <summary>A review is required and has not been given.</summary>
    ReviewRequired,

    /// <summary>Approved.</summary>
    Approved,

    /// <summary>Changes were requested.</summary>
    ChangesRequested
}

/// <summary>The one merge act a task row offers on a pull request.</summary>
public enum LinkedPullRequestMerge
{
    /// <summary>Merge it now: GitHub reports it mergeable, and would refuse to queue it.</summary>
    MergeNow,

    /// <summary>Hand it to GitHub's native auto-merge, which merges it once its checks and
    /// other requirements pass.</summary>
    MergeWhenReady,

    /// <summary>Withdraw a pending auto-merge request.</summary>
    CancelAutoMerge
}

/// <summary>
/// A pull request a task links to, by repository (<c>owner/name</c>) and number. Two
/// keys are the same pull request whatever the repository's case or surrounding space,
/// as GitHub compares owner and name.
/// </summary>
public sealed record LinkedPullRequestKey(string Repository, int Number)
{
    public bool Equals(LinkedPullRequestKey? other) =>
        other is not null
        && Number == other.Number
        && string.Equals(Repository?.Trim(), other.Repository?.Trim(), StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() =>
        HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(Repository?.Trim() ?? string.Empty), Number);
}

/// <summary>
/// Tasks' small copy of a pull request's status (local ADR 0042 §4): its state, the
/// verdict Repository Management gives it — label and tone, the same the Pull requests
/// pane and the session card draw — the branches a merge went into, the facts the task
/// row's badge marks draw, and the merge act the row may offer.
/// <para>
/// Never kept by Tasks beyond the window: the contract's one cache, and its settled
/// facts, are what a later read or a restart reads.
/// </para>
/// </summary>
/// <param name="State">Open, draft, merged or closed.</param>
public sealed record LinkedPullRequest(LinkedPullRequestState State)
{
    /// <summary>The verdict's short label ("Failing checks", "Ready to merge"), or null
    /// for a closed pull request, one whose status was not read — a REST answer from an
    /// older GitHub Enterprise Server, or a merge read back from the settled facts — or an
    /// open one whose place in a stack could not be read.</summary>
    public string? VerdictLabel { get; init; }

    /// <summary>Which verdict <see cref="VerdictLabel"/> words, null wherever it is.</summary>
    public LinkedPullRequestVerdictKind? VerdictKind { get; init; }

    /// <summary>The verdict's tone, null wherever <see cref="VerdictLabel"/> is.</summary>
    public LinkedPullRequestTone? VerdictTone { get; init; }

    /// <summary>The roll-up of its checks; <see cref="LinkedPullRequestChecks.None"/> when
    /// none were read.</summary>
    public LinkedPullRequestChecks Checks { get; init; }

    /// <summary>GitHub's review decision; <see cref="LinkedPullRequestReview.None"/> when
    /// none was read.</summary>
    public LinkedPullRequestReview Review { get; init; }

    /// <summary>Whether GitHub is holding it for auto-merge.</summary>
    public bool AutoMerge { get; init; }

    /// <summary>The merge act the row may offer, by the rule the Pull requests pane
    /// follows: none for one not open, a draft, one in conflict, one stacked on another
    /// open pull request, one whose status was not read, or one whose place in a stack
    /// could not be read.</summary>
    public LinkedPullRequestMerge? MergeOffer { get; init; }

    /// <summary>The branch it merges, or merged, into; null when the read did not say.</summary>
    public string? BaseRefName { get; init; }

    /// <summary>The repository's default branch at the read; null when the read did not say.</summary>
    public string? DefaultBranch { get; init; }
}

/// <summary>
/// One read of the pull requests a task list links to.
/// </summary>
/// <param name="States">Each pull request read, keyed by the key asked. One GitHub does
/// not know, or of a repository that could not be read, is absent.</param>
/// <param name="UnreadRepositories">The repositories (<c>owner/name</c>, compared without
/// regard to case) whose read was refused: their pull requests are asked again at the
/// list's recheck interval.</param>
public sealed record LinkedPullRequestRead(
    IReadOnlyDictionary<LinkedPullRequestKey, LinkedPullRequest> States,
    IReadOnlySet<string> UnreadRepositories);

/// <summary>
/// The state of the pull requests a task links to, and the two merge acts its row
/// offers, for the work badges and the row's menu.
/// <para>
/// A port on Tasks' surface answered by an adapter over Repository Management's
/// contract (local ADR 0042 §4), the shape <see cref="ILinkedSessionStates"/> takes for
/// the same reason. Through it the task list shares the contract's one cache with the
/// Pull requests pane, Progress and the side panel: a read one of them made within the
/// minute answers the others, and a merge from the row is what the pane reads next. A
/// host that composes no Repository Management answers nothing, and a badge with no
/// state draws as it did before it had one.
/// </para>
/// </summary>
public interface ILinkedPullRequestStates
{
    /// <summary>What is kept for these pull requests, however old, read from nothing:
    /// the first frame after a load, and offline. Never throws.</summary>
    IReadOnlyDictionary<LinkedPullRequestKey, LinkedPullRequest> Kept(IReadOnlyCollection<LinkedPullRequestKey> pullRequests);

    /// <summary>
    /// Reads <paramref name="pullRequests"/> together, one read per repository, through
    /// the contract's one cache.
    /// </summary>
    /// <param name="pullRequests">The pull requests to read.</param>
    /// <param name="fresh">True when a person asked — the sync button, a row's Refresh:
    /// GitHub is asked again whatever was kept, except about a merged pull request, which
    /// is final. False takes what another reader kept within the minute.</param>
    /// <param name="cancellationToken">Ends the wait; the read itself runs on.</param>
    Task<LinkedPullRequestRead> StatesOfAsync(
        IReadOnlyCollection<LinkedPullRequestKey> pullRequests,
        bool fresh = false,
        CancellationToken cancellationToken = default);

    /// <summary>Merges the pull request now. A refusal is a failure whose message a
    /// person can read as it stands.</summary>
    Task<Result> MergeNowAsync(LinkedPullRequestKey pullRequest, CancellationToken cancellationToken = default);

    /// <summary>Hands the pull request to GitHub's auto-merge, or takes it back. A refusal
    /// is a failure whose message a person can read as it stands.</summary>
    Task<Result> SetAutoMergeAsync(LinkedPullRequestKey pullRequest, bool enable, CancellationToken cancellationToken = default);

    /// <summary>The pull requests a merge act changed — from this list or from anywhere
    /// else, the Pull requests pane included — once the read that followed it has
    /// landed. Raised on whatever thread that read landed on.</summary>
    event Action<IReadOnlyCollection<LinkedPullRequestKey>>? Changed;
}
