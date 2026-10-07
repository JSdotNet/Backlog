using Backlog.Desktop.UI.PullRequests;
using Backlog.Infrastructure.GitHub;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The one readiness verdict a pull request gets: which of the eight applies when several
/// do, the lane and tone each puts it in, the acts each offers, how a stacked pull request
/// names the one it waits on, and that a merged one is Merged and a closed one has none.
/// </summary>
public sealed class PullRequestVerdictTests
{
    /// <summary>What can stand between an open pull request and its merge, in the order
    /// the verdict ranks them — Ready is what is left when none applies.</summary>
    public enum Condition
    {
        Conflicts,
        FailingChecks,
        ChangesRequested,
        Behind,
        Draft,
        ChecksRunning,
        ReviewRequired
    }

    private static readonly Dictionary<Condition, PullRequestVerdictKind> KindOf = new()
    {
        [Condition.Conflicts] = PullRequestVerdictKind.Conflicts,
        [Condition.FailingChecks] = PullRequestVerdictKind.FailingChecks,
        [Condition.ChangesRequested] = PullRequestVerdictKind.ChangesRequested,
        [Condition.Behind] = PullRequestVerdictKind.Behind,
        [Condition.Draft] = PullRequestVerdictKind.Draft,
        [Condition.ChecksRunning] = PullRequestVerdictKind.ChecksRunning,
        [Condition.ReviewRequired] = PullRequestVerdictKind.ReviewRequired
    };

    /// <summary>An open pull request with exactly the given conditions: with none, it is
    /// up to date with <c>main</c>, its five checks pass and one reviewer approved.</summary>
    private static GitHubOpenPullRequest Pull(params Condition[] conditions)
    {
        var has = conditions.ToHashSet();
        var failing = has.Contains(Condition.FailingChecks);
        var running = has.Contains(Condition.ChecksRunning);

        return new GitHubOpenPullRequest(
            Number: 42,
            Url: "https://github.com/acme/app/pull/42",
            Title: "Add the thing",
            RepositoryFullName: "acme/app",
            NodeId: "PR_42",
            IsDraft: has.Contains(Condition.Draft),
            HeadRefName: "feature/thing",
            HeadSha: "abc123",
            BaseRefName: "main",
            AuthorLogin: "octocat",
            ViewerDidAuthor: true,
            Checks: failing ? GitHubCheckState.Failing : running ? GitHubCheckState.Pending : GitHubCheckState.Passing,
            AutoMergeEnabled: false,
            MergeReady: has.Count == 0,
            IsBehind: has.Contains(Condition.Behind),
            HasConflicts: has.Contains(Condition.Conflicts),
            MergeStateStatus: null,
            PreferredMergeMethod: GitHubMergeMethod.Squash,
            UpdatedAt: DateTimeOffset.Parse("2026-10-08T10:00:00Z"))
        {
            CheckCounts = new GitHubCheckCounts(
                Passed: 5 - (failing ? 2 : 0) - (running ? 1 : 0),
                Failed: failing ? 2 : 0,
                Pending: running ? 1 : 0),
            Reviews = has.Contains(Condition.ChangesRequested)
                ? new GitHubReviewSummary(
                    has.Contains(Condition.ReviewRequired) ? GitHubReviewDecision.ReviewRequired : GitHubReviewDecision.ChangesRequested,
                    Approvals: 0,
                    ChangesRequested: 1)
                : has.Contains(Condition.ReviewRequired)
                    ? new GitHubReviewSummary(GitHubReviewDecision.ReviewRequired, 0, 0)
                    : new GitHubReviewSummary(GitHubReviewDecision.Approved, 1, 0)
        };
    }

    private static GitHubOpenPullRequest Parent() =>
        Pull() with { Number = 41, HeadRefName = "feature/base", BaseRefName = "main" };

    private static PullRequestVerdict Open(GitHubOpenPullRequest pull, GitHubOpenPullRequest? waitsOn = null, bool failedCheckIsActionsRun = false) =>
        PullRequestVerdict.Of(pull, waitsOn, failedCheckIsActionsRun)!;

    // ---- Every verdict ----------------------------------------------------------------

    [Fact]
    public void Conflicts_needs_you_as_a_fault_and_sends_you_to_GitHub()
    {
        var verdict = Open(Pull(Condition.Conflicts));

        Assert.Equal(PullRequestVerdictKind.Conflicts, verdict.Kind);
        Assert.Equal(PullRequestLane.NeedsYou, verdict.Lane);
        Assert.Equal(PullRequestTone.Fault, verdict.Tone);
        Assert.Equal("Conflicts", verdict.Label);
        Assert.Equal("Conflicts with main", verdict.Headline);
        Assert.Equal(PullRequestAct.OpenOnGitHub, verdict.PrimaryAct);
        Assert.Null(verdict.SecondaryAct);
    }

    [Fact]
    public void Failing_checks_from_GitHub_Actions_offer_Re_run_failed_and_then_GitHub()
    {
        var verdict = Open(Pull(Condition.FailingChecks), failedCheckIsActionsRun: true);

        Assert.Equal(PullRequestVerdictKind.FailingChecks, verdict.Kind);
        Assert.Equal(PullRequestLane.NeedsYou, verdict.Lane);
        Assert.Equal(PullRequestTone.Fault, verdict.Tone);
        Assert.Equal("2 failing", verdict.Label);
        Assert.Equal("2 of 5 checks failing", verdict.Headline);
        Assert.Equal(PullRequestAct.RerunFailed, verdict.PrimaryAct);
        Assert.Equal(PullRequestAct.OpenOnGitHub, verdict.SecondaryAct);
    }

    /// <summary>A check from another service can only be re-run there, so the one act
    /// left is the one that goes there.</summary>
    [Fact]
    public void Failing_checks_from_another_service_offer_only_GitHub()
    {
        var verdict = Open(Pull(Condition.FailingChecks), failedCheckIsActionsRun: false);

        Assert.Equal(PullRequestVerdictKind.FailingChecks, verdict.Kind);
        Assert.Equal(PullRequestAct.OpenOnGitHub, verdict.PrimaryAct);
        Assert.Null(verdict.SecondaryAct);
    }

    [Fact]
    public void Failing_checks_without_counts_still_read_as_failing()
    {
        var verdict = Open(Pull(Condition.FailingChecks) with { CheckCounts = null });

        Assert.Equal("Failing checks", verdict.Label);
        Assert.Equal("Checks failing", verdict.Headline);
    }

    /// <summary>The roll-up can still say pending while one check has already failed;
    /// the failure is what decides.</summary>
    [Fact]
    public void A_failed_check_counts_as_failing_while_others_still_run()
    {
        var pull = Pull(Condition.ChecksRunning) with { CheckCounts = new GitHubCheckCounts(3, 1, 1) };

        Assert.Equal(PullRequestVerdictKind.FailingChecks, Open(pull).Kind);
    }

    [Fact]
    public void Changes_requested_needs_you_as_a_fault_and_opens_the_review()
    {
        var verdict = Open(Pull(Condition.ChangesRequested));

        Assert.Equal(PullRequestVerdictKind.ChangesRequested, verdict.Kind);
        Assert.Equal(PullRequestLane.NeedsYou, verdict.Lane);
        Assert.Equal(PullRequestTone.Fault, verdict.Tone);
        Assert.Equal("Changes requested", verdict.Label);
        Assert.Equal(PullRequestAct.OpenReviewOnGitHub, verdict.PrimaryAct);
        Assert.Null(verdict.SecondaryAct);
    }

    /// <summary>A repository that asks for no review has no decision, but a reviewer who
    /// asked for changes still did.</summary>
    [Fact]
    public void Changes_requested_without_a_review_rule_still_counts()
    {
        var pull = Pull() with { Reviews = new GitHubReviewSummary(null, 0, 1) };

        Assert.Equal(PullRequestVerdictKind.ChangesRequested, Open(pull).Kind);
    }

    [Fact]
    public void Behind_needs_you_as_an_alert_and_offers_Update_branch_then_Merge_when_ready()
    {
        var verdict = Open(Pull(Condition.Behind));

        Assert.Equal(PullRequestVerdictKind.Behind, verdict.Kind);
        Assert.Equal(PullRequestLane.NeedsYou, verdict.Lane);
        Assert.Equal(PullRequestTone.Alert, verdict.Tone);
        Assert.Equal("Behind", verdict.Label);
        Assert.Equal("Behind main", verdict.Headline);
        Assert.Equal(PullRequestAct.UpdateBranch, verdict.PrimaryAct);
        Assert.Equal(PullRequestAct.MergeWhenReady, verdict.SecondaryAct);
    }

    [Fact]
    public void Draft_is_a_quiet_draft_and_offers_Ready_for_review()
    {
        var verdict = Open(Pull(Condition.Draft));

        Assert.Equal(PullRequestVerdictKind.Draft, verdict.Kind);
        Assert.Equal(PullRequestLane.Drafts, verdict.Lane);
        Assert.Equal(PullRequestTone.Quiet, verdict.Tone);
        Assert.Equal("Draft", verdict.Label);
        Assert.Equal("Still a draft", verdict.Headline);
        Assert.Equal(PullRequestAct.ReadyForReview, verdict.PrimaryAct);
        Assert.Null(verdict.SecondaryAct);
    }

    [Fact]
    public void Checks_running_waits_live_and_offers_Merge_when_ready()
    {
        var verdict = Open(Pull(Condition.ChecksRunning));

        Assert.Equal(PullRequestVerdictKind.ChecksRunning, verdict.Kind);
        Assert.Equal(PullRequestLane.Waiting, verdict.Lane);
        Assert.Equal(PullRequestTone.Live, verdict.Tone);
        Assert.Equal("Checks running", verdict.Label);
        Assert.Equal("1 check still running", verdict.Headline);
        Assert.Equal(PullRequestAct.MergeWhenReady, verdict.PrimaryAct);
        Assert.Null(verdict.SecondaryAct);
    }

    [Fact]
    public void Checks_running_counts_in_the_plural()
    {
        var pull = Pull(Condition.ChecksRunning) with { CheckCounts = new GitHubCheckCounts(2, 0, 3) };

        Assert.Equal("3 checks still running", Open(pull).Headline);
    }

    [Fact]
    public void Review_required_waits_live_and_offers_Merge_when_ready()
    {
        var verdict = Open(Pull(Condition.ReviewRequired));

        Assert.Equal(PullRequestVerdictKind.ReviewRequired, verdict.Kind);
        Assert.Equal(PullRequestLane.Waiting, verdict.Lane);
        Assert.Equal(PullRequestTone.Live, verdict.Tone);
        Assert.Equal("Review required", verdict.Label);
        Assert.Equal("Waiting for an approving review", verdict.Headline);
        Assert.Equal(PullRequestAct.MergeWhenReady, verdict.PrimaryAct);
        Assert.Null(verdict.SecondaryAct);
    }

    [Fact]
    public void Ready_is_settled_in_Ready_to_merge_and_offers_Merge()
    {
        var verdict = Open(Pull());

        Assert.Equal(PullRequestVerdictKind.Ready, verdict.Kind);
        Assert.Equal(PullRequestLane.ReadyToMerge, verdict.Lane);
        Assert.Equal(PullRequestTone.Settled, verdict.Tone);
        Assert.Equal("Ready to merge", verdict.Label);
        Assert.Equal("Ready to merge", verdict.Headline);
        Assert.Equal(PullRequestAct.Merge, verdict.PrimaryAct);
        Assert.Null(verdict.SecondaryAct);
    }

    [Fact]
    public void Ready_without_checks_says_none_stand_in_its_way()
    {
        var verdict = Open(Pull() with { Checks = GitHubCheckState.None, CheckCounts = null });

        Assert.Equal(PullRequestVerdictKind.Ready, verdict.Kind);
        Assert.Equal("No checks stand in its way and it is up to date with main with one approval.", verdict.Reason);
    }

    [Theory]
    [InlineData(0, "Checks pass and it is up to date with main.")]
    [InlineData(1, "Checks pass and it is up to date with main with one approval.")]
    [InlineData(2, "Checks pass and it is up to date with main with 2 approvals.")]
    public void Ready_counts_its_approvals(int approvals, string reason)
    {
        var pull = Pull() with { Reviews = new GitHubReviewSummary(approvals == 0 ? null : GitHubReviewDecision.Approved, approvals, 0) };

        Assert.Equal(reason, Open(pull).Reason);
    }

    /// <summary>GitHub can hold back a pull request for a rule this read does not name;
    /// Merge now would be refused there, so it goes to auto-merge.</summary>
    [Fact]
    public void Ready_that_GitHub_will_not_merge_yet_offers_Merge_when_ready()
    {
        var verdict = Open(Pull() with { MergeReady = false });

        Assert.Equal(PullRequestVerdictKind.Ready, verdict.Kind);
        Assert.Equal(PullRequestAct.MergeWhenReady, verdict.PrimaryAct);
    }

    [Fact]
    public void A_merged_pull_request_is_Merged_settled_with_no_act()
    {
        var verdict = Open(Pull() with { IsMerged = true });

        Assert.Equal(PullRequestVerdictKind.Merged, verdict.Kind);
        Assert.Null(verdict.Lane);
        Assert.Equal(PullRequestTone.Settled, verdict.Tone);
        Assert.Equal("Merged", verdict.Label);
        Assert.Equal("Merged into main", verdict.Headline);
        Assert.Null(verdict.PrimaryAct);
        Assert.Null(verdict.SecondaryAct);
    }

    /// <summary>Whatever stood in its way before, a merged pull request has nothing left
    /// to get ready.</summary>
    [Fact]
    public void A_merged_pull_request_is_Merged_whatever_it_last_reported()
    {
        var pull = Pull(Condition.Conflicts, Condition.FailingChecks, Condition.Draft) with { IsMerged = true };

        Assert.Equal(PullRequestVerdictKind.Merged, Open(pull).Kind);
    }

    [Fact]
    public void A_pull_request_from_the_merged_list_is_Merged_by_whoever_merged_it()
    {
        var merged = new GitHubMergedPullRequest(
            7, "https://github.com/acme/app/pull/7", "Done", "acme/app", "feature/done", "main",
            "octocat", ViewerDidAuthor: true, MergedAt: DateTimeOffset.Parse("2026-10-07T10:00:00Z"), MergedByLogin: "hubot");

        var verdict = PullRequestVerdict.Of(merged);

        Assert.Equal(PullRequestVerdictKind.Merged, verdict.Kind);
        Assert.Equal(PullRequestTone.Settled, verdict.Tone);
        Assert.Equal("Merged into main", verdict.Headline);
        Assert.Contains("hubot", verdict.Reason, StringComparison.Ordinal);
        Assert.Null(verdict.PrimaryAct);
    }

    [Fact]
    public void A_closed_pull_request_has_no_verdict()
    {
        Assert.Null(PullRequestVerdict.Of(Pull() with { IsClosed = true }));
    }

    [Fact]
    public void Every_verdict_has_a_label_a_headline_and_a_one_sentence_reason()
    {
        var verdicts = Enum.GetValues<Condition>().Select(condition => Open(Pull(condition)))
            .Append(Open(Pull()))
            .Append(Open(Pull() with { IsMerged = true }));

        foreach (var verdict in verdicts)
        {
            Assert.False(string.IsNullOrWhiteSpace(verdict.Label));
            Assert.False(string.IsNullOrWhiteSpace(verdict.Headline));
            AssertOneSentence(verdict.Reason);
        }
    }

    // ---- Precedence -------------------------------------------------------------------

    /// <summary>Every pair of conditions, the higher-ranked first. Each condition against
    /// Ready is the single-condition facts above.</summary>
    public static TheoryData<Condition, Condition> Pairs()
    {
        var data = new TheoryData<Condition, Condition>();
        var all = Enum.GetValues<Condition>();
        for (var higher = 0; higher < all.Length; higher++)
        {
            for (var lower = higher + 1; lower < all.Length; lower++)
            {
                data.Add(all[higher], all[lower]);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void The_higher_ranked_condition_wins(Condition higher, Condition lower)
    {
        Assert.Equal(KindOf[higher], Open(Pull(higher, lower)).Kind);
    }

    [Fact]
    public void Pairs_cover_every_pair_of_conditions()
    {
        var count = Enum.GetValues<Condition>().Length;
        Assert.Equal(count * (count - 1) / 2, Pairs().Count);
    }

    [Fact]
    public void Each_condition_alone_beats_Ready()
    {
        foreach (var condition in Enum.GetValues<Condition>())
        {
            Assert.Equal(KindOf[condition], Open(Pull(condition)).Kind);
        }
    }

    [Fact]
    public void Every_condition_at_once_is_Conflicts()
    {
        Assert.Equal(PullRequestVerdictKind.Conflicts, Open(Pull(Enum.GetValues<Condition>())).Kind);
    }

    // ---- Stacks -----------------------------------------------------------------------

    public static TheoryData<Condition?> EveryOpenVerdict()
    {
        var data = new TheoryData<Condition?> { (Condition?)null };
        foreach (var condition in Enum.GetValues<Condition>())
        {
            data.Add(condition);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryOpenVerdict))]
    public void A_stacked_pull_request_names_the_one_it_waits_on(Condition? condition)
    {
        var pull = (condition is { } c ? Pull(c) : Pull()) with { BaseRefName = "feature/base" };

        var verdict = Open(pull, waitsOn: Parent());

        Assert.Contains("#41", verdict.Reason, StringComparison.Ordinal);
        AssertOneSentence(verdict.Reason);
    }

    [Fact]
    public void A_stack_keeps_the_verdict_and_changes_the_reason()
    {
        var alone = Open(Pull(Condition.Draft));
        var stacked = Open(Pull(Condition.Draft), waitsOn: Parent());

        Assert.Equal(alone with { Reason = stacked.Reason }, stacked);
        Assert.NotEqual(alone.Reason, stacked.Reason);
    }

    /// <summary>Merged now, a stacked pull request would land on its parent's branch
    /// rather than where the stack is going, so it is offered no merge — the reason names
    /// the parent instead. Every other act stays.</summary>
    [Theory]
    [InlineData(Condition.Behind, PullRequestAct.UpdateBranch)]
    [InlineData(Condition.ChecksRunning, null)]
    [InlineData(Condition.ReviewRequired, null)]
    [InlineData(null, null)]
    public void A_stacked_pull_request_is_offered_no_merge(Condition? condition, object? primary)
    {
        var pull = condition is { } c ? Pull(c) : Pull();

        var verdict = Open(pull, waitsOn: Parent());

        Assert.Equal(primary, (object?)verdict.PrimaryAct);
        Assert.Null(verdict.SecondaryAct);
    }

    [Fact]
    public void A_stacked_pull_request_keeps_its_other_acts()
    {
        var verdict = Open(Pull(Condition.FailingChecks), waitsOn: Parent(), failedCheckIsActionsRun: true);

        Assert.Equal(PullRequestAct.RerunFailed, verdict.PrimaryAct);
        Assert.Equal(PullRequestAct.OpenOnGitHub, verdict.SecondaryAct);
    }

    [Fact]
    public void A_pull_request_in_no_stack_names_no_other()
    {
        Assert.DoesNotContain("#", Open(Pull(Condition.Draft)).Reason, StringComparison.Ordinal);
    }

    private static void AssertOneSentence(string reason)
    {
        Assert.False(string.IsNullOrWhiteSpace(reason));
        Assert.EndsWith(".", reason, StringComparison.Ordinal);
        Assert.DoesNotContain(". ", reason, StringComparison.Ordinal);
    }
}
