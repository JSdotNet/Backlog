using Backlog.Infrastructure.GitHub;
using Backlog.Infrastructure.GitHub.RepositoryManagement;
using Backlog.Modules.RepositoryManagement.Abstractions;
using Backlog.Modules.RepositoryManagement.Abstractions.DataTransferObjects;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// GitHub's pull request records mapped to Repository Management's status and back
/// (local ADR 0042). The way back is what the pane's acts still send GitHub through
/// until the module's commands take them over, so every field an act reads must survive
/// the round trip — the merge acts' <see cref="GitHubOpenPullRequest.ToStatus"/>, the
/// branch update's head commit, and the re-run's failed workflow runs.
/// </summary>
public sealed class GitHubPullRequestStatusesTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Every field set, none at its default, so a field the mapping drops shows.</summary>
    private static GitHubOpenPullRequest Open() =>
        new(
            Number: 42,
            Url: "https://github.com/acme/app/pull/42",
            Title: "Add the thing",
            RepositoryFullName: "acme/app",
            NodeId: "PR_kwDO42",
            IsDraft: false,
            HeadRefName: "feature/thing",
            HeadSha: "abc123",
            BaseRefName: "main",
            AuthorLogin: "octocat",
            ViewerDidAuthor: true,
            Checks: GitHubCheckState.Failing,
            AutoMergeEnabled: true,
            MergeReady: true,
            IsBehind: true,
            HasConflicts: true,
            MergeStateStatus: "BEHIND",
            PreferredMergeMethod: GitHubMergeMethod.Squash,
            UpdatedAt: Noon)
        {
            Labels = ["frontend", "ready"],
            CheckCounts = new GitHubCheckCounts(3, 2, 1),
            HeadChecks =
            [
                new GitHubCheck("build", GitHubCheckState.Failing, TimeSpan.FromSeconds(90), "https://ci/build", WorkflowRunId: 101),
                new GitHubCheck("lint", GitHubCheckState.Passing, null, null, WorkflowRunId: null)
            ],
            BehindBy = 3,
            IsCrossRepository = true,
            Additions = 10,
            Deletions = 4,
            Reviews = new GitHubReviewSummary(GitHubReviewState.ChangesRequested, 1, 1) { Required = 2, RequiredRead = true },
            ClosingIssues = [new GitHubIssueReference("acme/app", 7)],
            ViewerReviewRequested = true,
            MergedAt = Noon.AddHours(-1),
            ClosedAt = Noon.AddHours(-2)
        };

    [Fact]
    public void An_open_pull_request_maps_to_a_status_with_every_fact()
    {
        var status = GitHubPullRequestStatuses.Map(Open());

        Assert.Equal(new PullRequestReference("acme/app", 42), status.Reference);
        Assert.Equal(PullRequestState.Open, status.State);
        Assert.Equal(("feature/thing", "main", "octocat", true), (status.HeadRefName, status.BaseRefName, status.AuthorLogin, status.ViewerDidAuthor));
        Assert.Equal(PullRequestCheckState.Failing, status.Checks);
        Assert.Equal(new PullRequestCheckCounts(3, 2, 1), status.CheckCounts);
        Assert.Equal(
            [new PullRequestCheck("build", PullRequestCheckState.Failing, TimeSpan.FromSeconds(90), "https://ci/build", 101), new PullRequestCheck("lint", PullRequestCheckState.Passing, null, null, null)],
            status.HeadChecks);
        Assert.Equal(new PullRequestApprovals(PullRequestReviewDecision.ChangesRequested, 1, 1) { Required = 2, RequiredRead = true }, status.Approvals);
        Assert.Equal((true, true, true, 3, "BEHIND", true), (status.MergeReady, status.HasConflicts, status.IsBehind, status.BehindBy, status.MergeStateStatus, status.AutoMergeEnabled));
        Assert.Equal(["frontend", "ready"], status.Labels);
        Assert.Equal([new IssueReference("acme/app", 7)], status.ClosingIssues);
        Assert.Equal((10, 4, true), (status.Additions, status.Deletions, status.IsCrossRepository));
        Assert.True(status.ViewerReviewRequested);
        Assert.Equal(new PullRequestHandle("PR_kwDO42", "abc123", PullRequestMergeMethod.Squash), status.Handle);
        Assert.True(status.StatusRead);
        Assert.Null(status.StackParent);
        Assert.Equal([101L], status.FailedWorkflowRunIds);
        Assert.True(status.CanRerunFailed);
    }

    [Theory]
    [InlineData(false, false, false, PullRequestState.Open)]
    [InlineData(true, false, false, PullRequestState.Draft)]
    [InlineData(false, true, false, PullRequestState.Merged)]
    [InlineData(false, false, true, PullRequestState.Closed)]
    public void The_state_maps_both_ways(bool draft, bool merged, bool closed, PullRequestState state)
    {
        var pull = Open() with { IsDraft = draft, IsMerged = merged, IsClosed = closed };

        var status = GitHubPullRequestStatuses.Map(pull);
        var back = GitHubPullRequestStatuses.ToGitHub(status);

        Assert.Equal(state, status.State);
        Assert.Equal(pull.State, back.State);
    }

    /// <summary>The acts read the record the way back gives them: the merge acts through
    /// <see cref="GitHubOpenPullRequest.ToStatus"/>, Update branch its head commit and
    /// node id, Re-run failed its failed workflow runs.</summary>
    [Fact]
    public void The_way_back_keeps_every_field_the_acts_read()
    {
        var pull = Open();

        var back = GitHubPullRequestStatuses.ToGitHub(GitHubPullRequestStatuses.Map(pull));

        Assert.Equal(pull.ToStatus(), back.ToStatus());
        Assert.Equal((pull.NodeId, pull.HeadSha, pull.RepositoryFullName, pull.Number), (back.NodeId, back.HeadSha, back.RepositoryFullName, back.Number));
        Assert.Equal(pull.FailedWorkflowRunIds, back.FailedWorkflowRunIds);
        Assert.Equal(pull.CanRerunFailed, back.CanRerunFailed);
        Assert.Equal(pull.HeadChecks, back.HeadChecks);
        Assert.Equal(pull.Reviews, back.Reviews);
        Assert.Equal(pull.ClosingIssues, back.ClosingIssues);
        Assert.Equal(pull.Labels, back.Labels);
        Assert.Equal(pull.CheckCounts, back.CheckCounts);
        Assert.True(back.ViewerReviewRequested);
        Assert.Equal(
            (pull.Url, pull.Title, pull.HeadRefName, pull.BaseRefName, pull.AuthorLogin, pull.ViewerDidAuthor, pull.UpdatedAt),
            (back.Url, back.Title, back.HeadRefName, back.BaseRefName, back.AuthorLogin, back.ViewerDidAuthor, back.UpdatedAt));
        Assert.Equal(
            (pull.IsBehind, pull.BehindBy, pull.HasConflicts, pull.MergeStateStatus, pull.IsCrossRepository, pull.Additions, pull.Deletions, pull.MergedAt, pull.ClosedAt),
            (back.IsBehind, back.BehindBy, back.HasConflicts, back.MergeStateStatus, back.IsCrossRepository, back.Additions, back.Deletions, back.MergedAt, back.ClosedAt));
    }

    [Fact]
    public void A_merged_pull_request_maps_with_who_merged_it()
    {
        var merged = new GitHubMergedPullRequest(
            7, "https://github.com/acme/app/pull/7", "Done", "acme/app", "feature/done", "main", "octocat", true, Noon, "hubot")
        {
            Labels = ["docs"],
            ClosingIssues = [new GitHubIssueReference("acme/app", 3)]
        };

        var mapped = GitHubPullRequestStatuses.Map(merged);

        Assert.Equal(
            ("acme/app", 7, "https://github.com/acme/app/pull/7", "Done", "feature/done", "main", "octocat", true, Noon, "hubot"),
            (mapped.RepositoryFullName, mapped.Number, mapped.Url, mapped.Title, mapped.HeadRefName, mapped.BaseRefName, mapped.AuthorLogin, mapped.ViewerDidAuthor, mapped.MergedAt, mapped.MergedByLogin));
        Assert.Equal(["docs"], mapped.Labels);
        Assert.Equal([new IssueReference("acme/app", 3)], mapped.ClosingIssues);
    }

    [Fact]
    public void A_listing_keeps_its_rows_and_its_failures()
    {
        var listing = new GitHubPullRequestListing([Open()], [new GitHubRepositoryFailure("acme/other", "Not found.")]);

        var mapped = GitHubPullRequestStatuses.Map(listing);

        Assert.Equal(42, Assert.Single(mapped.PullRequests).Number);
        Assert.Equal(new RepositoryReadFailure("acme/other", "Not found."), Assert.Single(mapped.Failures));
    }

    [Fact]
    public void A_merged_listing_keeps_its_rows_its_failures_and_what_was_cut_short()
    {
        var listing = new GitHubMergedPullRequestListing(
            [new GitHubMergedPullRequest(7, "u", "t", "acme/app", "h", "main", null, false, Noon, null)],
            [new GitHubRepositoryFailure("acme/other", "Refused.")],
            ["acme/busy"]);

        var mapped = GitHubPullRequestStatuses.Map(listing);

        Assert.Equal(7, Assert.Single(mapped.PullRequests).Number);
        Assert.Equal(new RepositoryReadFailure("acme/other", "Refused."), Assert.Single(mapped.Failures));
        Assert.Equal(["acme/busy"], mapped.Truncated);
    }

    /// <summary>A REST read names state and branches only, so its status says it was not
    /// read rather than passing defaults off as GitHub's word.</summary>
    [Fact]
    public void A_rest_read_maps_to_a_status_that_says_its_status_was_not_read()
    {
        var rest = new GitHubPullRequest(9, "https://github.com/acme/app/pull/9", "Fix", GitHubItemState.Merged, "acme/app")
        {
            BaseRefName = "main",
            DefaultBranch = "main"
        };

        var status = GitHubPullRequestStatuses.Map(rest);

        Assert.Equal(new PullRequestReference("acme/app", 9), status.Reference);
        Assert.Equal(PullRequestState.Merged, status.State);
        Assert.Equal(("main", "main"), (status.BaseRefName, status.DefaultBranch));
        Assert.False(status.StatusRead);
        Assert.False(status.ViewerReviewRequested);
        Assert.Null(status.Handle);
    }

    [Theory]
    [InlineData(GitHubCheckState.None, PullRequestCheckState.None)]
    [InlineData(GitHubCheckState.Pending, PullRequestCheckState.Pending)]
    [InlineData(GitHubCheckState.Passing, PullRequestCheckState.Passing)]
    [InlineData(GitHubCheckState.Failing, PullRequestCheckState.Failing)]
    public void Check_states_map_both_ways(GitHubCheckState gitHub, PullRequestCheckState module)
    {
        Assert.Equal(module, GitHubPullRequestStatuses.Map(gitHub));
        Assert.Equal(gitHub, GitHubPullRequestStatuses.ToGitHub(module));
    }

    [Theory]
    [InlineData(GitHubReviewState.None, PullRequestReviewDecision.None)]
    [InlineData(GitHubReviewState.ReviewRequired, PullRequestReviewDecision.ReviewRequired)]
    [InlineData(GitHubReviewState.Approved, PullRequestReviewDecision.Approved)]
    [InlineData(GitHubReviewState.ChangesRequested, PullRequestReviewDecision.ChangesRequested)]
    public void Review_decisions_map_both_ways(GitHubReviewState gitHub, PullRequestReviewDecision module)
    {
        Assert.Equal(module, GitHubPullRequestStatuses.Map(gitHub));
        Assert.Equal(gitHub, GitHubPullRequestStatuses.ToGitHub(module));
    }

    [Theory]
    [InlineData(GitHubMergeMethod.Merge, PullRequestMergeMethod.Merge)]
    [InlineData(GitHubMergeMethod.Squash, PullRequestMergeMethod.Squash)]
    [InlineData(GitHubMergeMethod.Rebase, PullRequestMergeMethod.Rebase)]
    public void Merge_methods_map_both_ways(GitHubMergeMethod gitHub, PullRequestMergeMethod module)
    {
        Assert.Equal(module, GitHubPullRequestStatuses.Map(gitHub));
        Assert.Equal(gitHub, GitHubPullRequestStatuses.ToGitHub(module));
    }

    /// <summary>The verdict a mapped status gets is the one the old shell rule gave the
    /// GitHub record: a failing check that is an Actions run offers Re-run failed.</summary>
    [Fact]
    public void A_mapped_status_gets_the_verdict_its_facts_call_for()
    {
        var status = GitHubPullRequestStatuses.Map(Open() with { HasConflicts = false, IsBehind = false });

        var verdict = PullRequestVerdict.Of(status)!;

        Assert.Equal(PullRequestVerdictKind.FailingChecks, verdict.Kind);
        Assert.Equal(PullRequestAct.RerunFailed, verdict.PrimaryAct);
    }
}
