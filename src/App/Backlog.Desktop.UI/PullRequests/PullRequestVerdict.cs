using Backlog.Infrastructure.GitHub;

namespace Backlog.Desktop.UI.PullRequests;

/// <summary>
/// The one thing standing between a pull request and its merge, ranked: where several
/// apply, the first member wins. <see cref="Merged"/> is outside the ranking — it is what
/// a pull request with nothing left to get ready reads as.
/// </summary>
internal enum PullRequestVerdictKind
{
    /// <summary>GitHub cannot merge it until its conflicts with the base are resolved.</summary>
    Conflicts,

    /// <summary>At least one check failed.</summary>
    FailingChecks,

    /// <summary>A reviewer asked for changes.</summary>
    ChangesRequested,

    /// <summary>The base branch has moved on since the head branch left it.</summary>
    Behind,

    /// <summary>Still a draft.</summary>
    Draft,

    /// <summary>Checks have not finished.</summary>
    ChecksRunning,

    /// <summary>The base branch's rules want an approving review it does not have yet.</summary>
    ReviewRequired,

    /// <summary>Nothing stands in its way.</summary>
    Ready,

    /// <summary>Already merged.</summary>
    Merged
}

/// <summary>Which of the four lane tiles a pull request counts under. A merged pull
/// request is in none.</summary>
internal enum PullRequestLane
{
    /// <summary>Waiting on the reader: conflicts, failing checks, changes requested,
    /// behind.</summary>
    NeedsYou,

    /// <summary>Nothing in its way.</summary>
    ReadyToMerge,

    /// <summary>Waiting on somebody or something else: checks running, review
    /// required.</summary>
    Waiting,

    /// <summary>Still a draft.</summary>
    Drafts
}

/// <summary>
/// The badge tone scale of <c>.devbook/design/color-scheme.md</c>, the five tones a
/// verdict wears. Its own type here because no shared one carries the scale in code yet;
/// the names are the scale's, so the chip's class can be read straight off them.
/// </summary>
internal enum PullRequestTone
{
    /// <summary>Nothing has happened to it yet.</summary>
    Quiet,

    /// <summary>In progress, and the product may act on it.</summary>
    Live,

    /// <summary>Wants attention, but is not a fault.</summary>
    Alert,

    /// <summary>Failed, blocked, or contradicted.</summary>
    Fault,

    /// <summary>Finished, and correct.</summary>
    Settled
}

/// <summary>An act a verdict offers. Which merge act a <see cref="MergeWhenReady"/> or a
/// <see cref="Merge"/> becomes, against the auto-merge GitHub may already hold, is
/// <see cref="PullRequestMerge"/>'s question, not the verdict's.</summary>
internal enum PullRequestAct
{
    /// <summary>Open the pull request on GitHub.</summary>
    OpenOnGitHub,

    /// <summary>Run the failed jobs of its failed GitHub Actions runs again.</summary>
    RerunFailed,

    /// <summary>Open the pull request's review on GitHub.</summary>
    OpenReviewOnGitHub,

    /// <summary>Bring the head branch up to date with its base.</summary>
    UpdateBranch,

    /// <summary>Mark the draft ready for review.</summary>
    ReadyForReview,

    /// <summary>Hand it to GitHub's auto-merge, which merges it once it can.</summary>
    MergeWhenReady,

    /// <summary>Merge it now.</summary>
    Merge
}

/// <summary>
/// One pull request's readiness verdict: which lane it counts under, the tone its chip
/// wears, the chip's label, the banner's headline and one-sentence reason, and the act —
/// with at most one more — that fits it.
/// <para>
/// One verdict rather than a row of badges, because a reader acts on one thing at a time
/// (<c>.devbook/domain/sessions/features.md#one-verdict-per-pull-request</c>). Derived
/// from nothing but what GitHub already reported, so the same pull request reads the
/// same everywhere it appears.
/// </para>
/// </summary>
/// <param name="Lane">Null only for <see cref="PullRequestVerdictKind.Merged"/>.</param>
/// <param name="PrimaryAct">Null for <see cref="PullRequestVerdictKind.Merged"/>, and for a
/// stacked pull request whose only act would have been a merge: merged now, it would land
/// on its parent's branch rather than where the stack is going, so the reason names the
/// parent instead — the rule the pane's <c>OfferedMergeFor</c> already keeps.</param>
internal sealed record PullRequestVerdict(
    PullRequestVerdictKind Kind,
    PullRequestLane? Lane,
    PullRequestTone Tone,
    string Label,
    string Headline,
    string Reason,
    PullRequestAct? PrimaryAct,
    PullRequestAct? SecondaryAct)
{
    /// <summary>
    /// The verdict on a pull request GitHub reported as open, or merged by a pinned read.
    /// </summary>
    /// <param name="pull">The pull request.</param>
    /// <param name="waitsOn">The open pull request it is stacked on —
    /// <see cref="PullRequestStacks.ParentOf"/> — named in the reason; null for one at
    /// the bottom of a stack or in none.</param>
    /// <param name="failedCheckIsActionsRun">Whether at least one failed check is a GitHub
    /// Actions run, the only kind of check GitHub can be asked to run again, so the only
    /// one Re-run failed is offered for.</param>
    /// <returns>Null for a pull request closed without merging: there is nothing left
    /// to get ready, and nothing to merge.</returns>
    public static PullRequestVerdict? Of(
        GitHubOpenPullRequest pull,
        GitHubOpenPullRequest? waitsOn = null,
        bool failedCheckIsActionsRun = false)
    {
        ArgumentNullException.ThrowIfNull(pull);

        if (pull.IsMerged)
        {
            return Merged(pull.BaseRefName, mergedBy: null);
        }

        if (pull.IsClosed)
        {
            return null;
        }

        var verdict = Open(pull, failedCheckIsActionsRun);
        if (waitsOn is null)
        {
            return verdict with { Reason = $"{verdict.Reason}." };
        }

        // Merged now, it would land on its parent's branch rather than where the stack
        // is going, so its merge acts go and the reason names the parent instead.
        var acts = new[] { verdict.PrimaryAct, verdict.SecondaryAct }
            .Where(act => act is { } a && !IsMerge(a))
            .ToList();
        return verdict with
        {
            Reason = $"{verdict.Reason}, and it waits on #{waitsOn.Number}, which has to merge first.",
            PrimaryAct = acts.ElementAtOrDefault(0),
            SecondaryAct = acts.ElementAtOrDefault(1)
        };
    }

    private static bool IsMerge(PullRequestAct act) => act is PullRequestAct.Merge or PullRequestAct.MergeWhenReady;

    /// <summary>The verdict on a pull request from the Recently merged list.</summary>
    public static PullRequestVerdict Of(GitHubMergedPullRequest pull)
    {
        ArgumentNullException.ThrowIfNull(pull);
        return Merged(pull.BaseRefName, pull.MergedByLogin);
    }

    /// <summary>The verdict by precedence, its reason still a clause without its stop so
    /// a stack can be named in the same sentence.</summary>
    private static PullRequestVerdict Open(GitHubOpenPullRequest pull, bool failedCheckIsActionsRun)
    {
        var counts = pull.CheckCounts;
        var baseRef = pull.BaseRefName;

        if (pull.HasConflicts)
        {
            return new(
                PullRequestVerdictKind.Conflicts, PullRequestLane.NeedsYou, PullRequestTone.Fault,
                "Conflicts",
                $"Conflicts with {baseRef}",
                $"GitHub cannot merge this until its conflicts with {baseRef} are resolved",
                PullRequestAct.OpenOnGitHub, null);
        }

        // The roll-up can still read pending while one check has already failed; the
        // failure is what decides, so a failed count is enough.
        if (pull.Checks == GitHubCheckState.Failing || counts is { Failed: > 0 })
        {
            var failed = counts is { Failed: > 0 } ? counts : null;
            return new(
                PullRequestVerdictKind.FailingChecks, PullRequestLane.NeedsYou, PullRequestTone.Fault,
                failed is null ? "Failing checks" : $"{failed.Failed} failing",
                failed is null ? "Checks failing" : $"{failed.Failed} of {failed.Total} checks failing",
                "Merging is blocked until the failing checks pass",
                failedCheckIsActionsRun ? PullRequestAct.RerunFailed : PullRequestAct.OpenOnGitHub,
                failedCheckIsActionsRun ? PullRequestAct.OpenOnGitHub : null);
        }

        // A repository that asks for no review has no decision, but a reviewer who asked
        // for changes still did.
        if (pull.Reviews.Decision == GitHubReviewDecision.ChangesRequested || pull.Reviews.ChangesRequested > 0)
        {
            return new(
                PullRequestVerdictKind.ChangesRequested, PullRequestLane.NeedsYou, PullRequestTone.Fault,
                "Changes requested",
                "A reviewer asked for changes",
                "Nothing merges until the changes are made or the review is dismissed on GitHub",
                PullRequestAct.OpenReviewOnGitHub, null);
        }

        if (pull.IsBehind)
        {
            return new(
                PullRequestVerdictKind.Behind, PullRequestLane.NeedsYou, PullRequestTone.Alert,
                "Behind",
                $"Behind {baseRef}",
                $"{baseRef} has moved on, so bring the branch up to date before it can merge",
                PullRequestAct.UpdateBranch, PullRequestAct.MergeWhenReady);
        }

        if (pull.IsDraft)
        {
            return new(
                PullRequestVerdictKind.Draft, PullRequestLane.Drafts, PullRequestTone.Quiet,
                "Draft",
                "Still a draft",
                "Nothing merges until it is marked ready for review",
                PullRequestAct.ReadyForReview, null);
        }

        if (pull.Checks == GitHubCheckState.Pending || counts is { Pending: > 0 })
        {
            var pending = counts is { Pending: > 0 } ? counts.Pending : (int?)null;
            return new(
                PullRequestVerdictKind.ChecksRunning, PullRequestLane.Waiting, PullRequestTone.Live,
                "Checks running",
                pending switch
                {
                    1 => "1 check still running",
                    { } n => $"{n} checks still running",
                    null => "Checks still running"
                },
                "Merge when ready hands it to GitHub, which merges it once its checks pass",
                PullRequestAct.MergeWhenReady, null);
        }

        if (pull.Reviews.Decision == GitHubReviewDecision.ReviewRequired)
        {
            return new(
                PullRequestVerdictKind.ReviewRequired, PullRequestLane.Waiting, PullRequestTone.Live,
                "Review required",
                "Waiting for an approving review",
                $"The rules on {baseRef} need an approving review before it can merge",
                PullRequestAct.MergeWhenReady, null);
        }

        var checks = pull.Checks == GitHubCheckState.Passing ? "Checks pass" : "No checks stand in its way";
        var approvals = pull.Reviews.Approvals switch
        {
            0 => "",
            1 => " with one approval",
            var n => $" with {n} approvals"
        };

        // GitHub can still hold back a pull request none of the above describes — a rule
        // this read does not name. Merge now would be refused there, so the act hands it
        // to auto-merge instead, as the pane's merge act already does.
        return new(
            PullRequestVerdictKind.Ready, PullRequestLane.ReadyToMerge, PullRequestTone.Settled,
            "Ready to merge",
            "Ready to merge",
            $"{checks} and it is up to date with {baseRef}{approvals}",
            pull.MergeReady ? PullRequestAct.Merge : PullRequestAct.MergeWhenReady, null);
    }

    private static PullRequestVerdict Merged(string baseRef, string? mergedBy) => new(
        PullRequestVerdictKind.Merged, null, PullRequestTone.Settled,
        "Merged",
        $"Merged into {baseRef}",
        mergedBy is null
            ? "Nothing is left to do from here."
            : $"Merged by {mergedBy}, so nothing is left to do from here.",
        null, null);
}
