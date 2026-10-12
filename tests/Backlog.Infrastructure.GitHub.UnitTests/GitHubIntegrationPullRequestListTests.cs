using System.Text.Json;
using Backlog.Infrastructure.GitHub;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// The pull requests list as the app reaches it, through <see cref="GitHubIntegration"/>:
/// several repositories read together, one that cannot be read reported beside the
/// others rather than instead of them, and the two acts the list adds put into words
/// when GitHub refuses them — the same façade, and the same reason, as
/// <see cref="GitHubIntegrationMergeTests"/>.
/// </summary>
public sealed class GitHubIntegrationPullRequestListTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-pull-request-list-tests", Guid.NewGuid().ToString("n"));

    private static readonly GitHubRepositoryRef Backlog = new("backlog", "JSdotNet", "Backlog");
    private static readonly GitHubRepositoryRef Archify = new("archify", "JSdotNet", "Archify");
    private static readonly GitHubRepositoryRef Broken = new("broken", "octo", "broken");

    private static readonly GitHubOpenPullRequest Pull = new(
        712,
        "https://github.com/JSdotNet/Backlog/pull/712",
        "Pull requests page",
        "JSdotNet/Backlog",
        "PR_kwDOlist",
        IsDraft: true,
        HeadRefName: "claude/pr-page",
        HeadSha: "abc123",
        BaseRefName: "main",
        AuthorLogin: "JSdotNet",
        ViewerDidAuthor: true,
        Checks: GitHubCheckState.Passing,
        AutoMergeEnabled: false,
        MergeReady: false,
        IsBehind: true,
        HasConflicts: false,
        MergeStateStatus: "BEHIND",
        PreferredMergeMethod: GitHubMergeMethod.Squash,
        UpdatedAt: new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Every_repository_is_listed_and_the_newest_update_comes_first()
    {
        var transport = new RoutingTransport()
            .Returns("graphql#JSdotNet/Backlog", List(Node(1, "2026-10-01T08:00:00Z")))
            .Returns("graphql#JSdotNet/Archify", List(Node(2, "2026-10-01T10:00:00Z")));

        var listing = await Integration(transport).ListOpenPullRequestsAsync([Backlog, Archify], TestContext.Current.CancellationToken);

        Assert.Equal([2, 1], listing.PullRequests.Select(pull => pull.Number));
        Assert.Equal(["JSdotNet/Archify", "JSdotNet/Backlog"], listing.PullRequests.Select(pull => pull.RepositoryFullName));
        Assert.Empty(listing.Failures);
    }

    /// <summary>One repository the account cannot read is that repository's failure,
    /// in GitHub's words, and nothing else's: the others are listed as if it were not
    /// there.</summary>
    [Fact]
    public async Task A_repository_that_cannot_be_read_is_reported_and_the_others_still_come_back()
    {
        var transport = new RoutingTransport()
            .Returns("graphql#JSdotNet/Backlog", List(Node(1, "2026-10-01T08:00:00Z")))
            .Refuses("graphql#octo/broken", "gh: Could not resolve to a Repository with the name 'octo/broken'.")
            .Returns("graphql#JSdotNet/Archify", List(Node(2, "2026-10-01T10:00:00Z")));

        var listing = await Integration(transport).ListOpenPullRequestsAsync([Backlog, Broken, Archify], TestContext.Current.CancellationToken);

        Assert.Equal([2, 1], listing.PullRequests.Select(pull => pull.Number));

        var failure = Assert.Single(listing.Failures);
        Assert.Equal("octo/broken", failure.RepositoryFullName);
        Assert.Contains("Could not resolve", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_repository_named_twice_is_read_once()
    {
        var transport = new RoutingTransport().Returns("graphql#JSdotNet/Backlog", List(Node(1, "2026-10-01T08:00:00Z")));

        var listing = await Integration(transport).ListOpenPullRequestsAsync(
            [Backlog, Backlog with { Alias = "other" }],
            TestContext.Current.CancellationToken);

        Assert.Single(listing.PullRequests);
        Assert.Single(transport.Bodies, body => body!.Contains("pullRequests(states: OPEN", StringComparison.Ordinal));
    }

    /// <summary>The recently merged list is ordered by when each was merged, across
    /// repositories — the question it answers is "what landed lately", not "what was
    /// touched lately", which is the order GitHub can be asked for.</summary>
    [Fact]
    public async Task Merged_pull_requests_are_listed_newest_merge_first_across_repositories()
    {
        var transport = new RoutingTransport()
            .Returns("graphql#JSdotNet/Backlog", MergedList(Merged(1, "2026-10-01T08:00:00Z"), Merged(3, "2026-09-30T08:00:00Z")))
            .Returns("graphql#JSdotNet/Archify", MergedList(Merged(2, "2026-10-01T10:00:00Z")));

        var listing = await Integration(transport).ListMergedPullRequestsAsync([Backlog, Archify], TestContext.Current.CancellationToken);

        Assert.Equal([2, 1, 3], listing.PullRequests.Select(pull => pull.Number));
        Assert.Equal(["JSdotNet/Archify", "JSdotNet/Backlog", "JSdotNet/Backlog"], listing.PullRequests.Select(pull => pull.RepositoryFullName));
        Assert.Empty(listing.Failures);
    }

    [Fact]
    public async Task A_repository_whose_merged_pull_requests_cannot_be_read_is_reported_and_the_others_still_come_back()
    {
        var transport = new RoutingTransport()
            .Returns("graphql#JSdotNet/Backlog", MergedList(Merged(1, "2026-10-01T08:00:00Z")))
            .Refuses("graphql#octo/broken", "gh: Could not resolve to a Repository with the name 'octo/broken'.")
            .Returns("graphql#JSdotNet/Archify", MergedList(Merged(2, "2026-10-01T10:00:00Z")));

        var listing = await Integration(transport).ListMergedPullRequestsAsync([Backlog, Broken, Archify], TestContext.Current.CancellationToken);

        Assert.Equal([2, 1], listing.PullRequests.Select(pull => pull.Number));

        var failure = Assert.Single(listing.Failures);
        Assert.Equal("octo/broken", failure.RepositoryFullName);
        Assert.Contains("Could not resolve", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Fourteen days back from the clock, inclusive: older merges are what
    /// GitHub's own merged list is for, and GitHub returns them because it can only
    /// be asked for the most recently updated.</summary>
    [Fact]
    public async Task Merges_older_than_the_window_are_left_out()
    {
        var transport = new RoutingTransport().Returns(
            "graphql#JSdotNet/Backlog",
            MergedList(
                Merged(1, "2026-10-01T08:00:00Z"),
                Merged(2, "2026-09-17T12:00:00Z"),
                Merged(3, "2026-09-17T11:59:59Z"),
                Merged(4, "2026-08-01T08:00:00Z")));

        var listing = await Integration(transport).ListMergedPullRequestsAsync([Backlog], TestContext.Current.CancellationToken);

        Assert.Equal([1, 2], listing.PullRequests.Select(pull => pull.Number));
        Assert.Equal(TimeSpan.FromDays(14), GitHubIntegration.RecentlyMergedWindow);
    }

    /// <summary>A repository whose window the page cap could not reach the end of is
    /// named beside the rows, so the view does not pass a partial fortnight off as the
    /// whole one; a repository read to the end is not.</summary>
    [Fact]
    public async Task A_repository_the_page_cap_stopped_inside_the_window_is_named()
    {
        var busy = Enumerable.Range(1, GitHubClient.MergedPageCap + 1)
            .Select(page => $$"""
                { "data": { "repository": { "pullRequests": {
                    "pageInfo": { "hasNextPage": true, "endCursor": "cursor-{{page}}" },
                    "nodes": [ {{Merged(page, "2026-09-30T08:00:00Z")}} ] } } } }
                """)
            .ToArray();
        var transport = new RoutingTransport()
            .ReturnsInTurn("graphql#JSdotNet/Backlog", busy)
            .Returns("graphql#JSdotNet/Archify", MergedList(Merged(100, "2026-10-01T10:00:00Z")));

        var listing = await Integration(transport).ListMergedPullRequestsAsync([Backlog, Archify], TestContext.Current.CancellationToken);

        Assert.Equal(["JSdotNet/Backlog"], listing.Truncated);
        Assert.Equal(GitHubClient.MergedPageCap + 1, listing.PullRequests.Count);
    }

    [Fact]
    public async Task A_repository_named_twice_has_its_merged_pull_requests_read_once()
    {
        var transport = new RoutingTransport().Returns("graphql#JSdotNet/Backlog", MergedList(Merged(1, "2026-10-01T08:00:00Z")));

        var listing = await Integration(transport).ListMergedPullRequestsAsync(
            [Backlog, Backlog with { Alias = "other" }],
            TestContext.Current.CancellationToken);

        Assert.Single(listing.PullRequests);
        Assert.Equal(1, transport.CallsTo("graphql"));
    }

    [Fact]
    public async Task Updating_a_branch_sends_the_head_the_list_read()
    {
        var transport = new RoutingTransport().Returns(HttpMethod.Put, "update-branch", "{}");

        await Integration(transport).UpdateBranchAsync(Pull, TestContext.Current.CancellationToken);

        Assert.Equal("repos/JSdotNet/Backlog/pulls/712/update-branch", Assert.Single(transport.Paths));
        using var body = JsonDocument.Parse(transport.Bodies[0]!);
        Assert.Equal("abc123", body.RootElement.GetProperty("expected_head_sha").GetString());
    }

    [Fact]
    public async Task Marking_ready_sends_the_pull_requests_node()
    {
        var transport = new RoutingTransport().Returns("graphql", """{ "data": { "markPullRequestReadyForReview": { "clientMutationId": null } } }""");

        await Integration(transport).MarkReadyForReviewAsync(Pull, TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(Assert.Single(transport.Bodies)!);
        Assert.Equal("PR_kwDOlist", body.RootElement.GetProperty("variables").GetProperty("id").GetString());
    }

    /// <summary>What the façade does not recognise keeps GitHub's own words, behind a
    /// sentence naming the act and the pull request.</summary>
    [Fact]
    public async Task An_unrecognised_update_refusal_names_the_act_and_the_pull_request()
    {
        var transport = new RoutingTransport().Refuses("update-branch", "Something new GitHub started saying");

        var refused = await Assert.ThrowsAsync<GitHubException>(() =>
            Integration(transport).UpdateBranchAsync(Pull, TestContext.Current.CancellationToken));

        Assert.Equal("Couldn't update the branch of JSdotNet/Backlog#712: Something new GitHub started saying", refused.Message);
    }

    [Fact]
    public async Task An_unrecognised_ready_refusal_names_the_act_and_the_pull_request()
    {
        var transport = new RoutingTransport().Returns(
            "graphql",
            """{ "data": null, "errors": [ { "message": "Something new GitHub started saying" } ] }""");

        var refused = await Assert.ThrowsAsync<GitHubException>(() =>
            Integration(transport).MarkReadyForReviewAsync(Pull, TestContext.Current.CancellationToken));

        Assert.Equal("Couldn't mark JSdotNet/Backlog#712 ready for review: Something new GitHub started saying", refused.Message);
    }

    /// <summary>The head travels with the update so a branch that moved is refused
    /// rather than merged into; the refusal says to read the list again.</summary>
    [Fact]
    public async Task A_branch_that_moved_since_the_read_says_to_refresh()
    {
        var transport = new RoutingTransport().Refuses("update-branch", "expected head sha didn't match current head ref.");

        var refused = await Assert.ThrowsAsync<GitHubException>(() =>
            Integration(transport).UpdateBranchAsync(Pull, TestContext.Current.CancellationToken));

        Assert.StartsWith("Couldn't update the branch of JSdotNet/Backlog#712", refused.Message, StringComparison.Ordinal);
        Assert.Contains("Refresh", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_conflicting_update_says_the_conflict_is_to_be_resolved_on_the_branch()
    {
        var transport = new RoutingTransport().Refuses("update-branch", "merge conflict between base and head");

        var refused = await Assert.ThrowsAsync<GitHubException>(() =>
            Integration(transport).UpdateBranchAsync(Pull, TestContext.Current.CancellationToken));

        Assert.Contains("Resolve the conflicts", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>A permission refusal names the permission the act needed, not the
    /// merge one: telling somebody they cannot merge when they asked to update a
    /// branch would send them to look for the wrong thing.</summary>
    [Fact]
    public async Task A_permission_refusal_names_the_permission_the_act_needed()
    {
        var transport = new RoutingTransport().Refuses(HttpMethod.Put, "update-branch", System.Net.HttpStatusCode.Forbidden, "Forbidden");

        var refused = await Assert.ThrowsAsync<GitHubException>(() =>
            Integration(transport).UpdateBranchAsync(Pull, TestContext.Current.CancellationToken));

        Assert.Contains("doesn't have permission to push to branches in JSdotNet/Backlog", refused.Message, StringComparison.Ordinal);
    }

    // --- Re-run failed --------------------------------------------------------

    private static readonly GitHubOpenPullRequest Failing = Pull with
    {
        IsDraft = false,
        Checks = GitHubCheckState.Failing,
        HeadChecks =
        [
            new GitHubCheck("build", GitHubCheckState.Failing, TimeSpan.FromMinutes(3), null, 101),
            new GitHubCheck("lint", GitHubCheckState.Failing, TimeSpan.FromMinutes(1), null, 101),
            new GitHubCheck("e2e", GitHubCheckState.Failing, TimeSpan.FromMinutes(9), null, 202),
            new GitHubCheck("docs", GitHubCheckState.Passing, TimeSpan.FromMinutes(1), null, 303),
            new GitHubCheck("ci/external", GitHubCheckState.Failing, null, null, null)
        ]
    };

    /// <summary>Every failed GitHub Actions run once, however many of its checks
    /// failed; a passing run and another service's check are left alone.</summary>
    [Fact]
    public async Task Re_running_failed_checks_reruns_each_failed_actions_run_once()
    {
        var transport = new RoutingTransport().Returns(HttpMethod.Post, "rerun-failed-jobs", "{}");

        await Integration(transport).RerunFailedChecksAsync(Failing, TestContext.Current.CancellationToken);

        Assert.Equal(
            ["repos/JSdotNet/Backlog/actions/runs/101/rerun-failed-jobs", "repos/JSdotNet/Backlog/actions/runs/202/rerun-failed-jobs"],
            transport.Paths);
    }

    [Fact]
    public async Task Re_running_with_no_failed_actions_run_asks_github_nothing()
    {
        var transport = new RoutingTransport();
        var external = Failing with { HeadChecks = [new GitHubCheck("ci/external", GitHubCheckState.Failing, null, null, null)] };

        var refused = await Assert.ThrowsAsync<GitHubException>(() =>
            Integration(transport).RerunFailedChecksAsync(external, TestContext.Current.CancellationToken));

        Assert.Empty(transport.Paths);
        Assert.Contains("none of them is a GitHub Actions run", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>The runs are independent: one GitHub refuses does not stop the next
    /// from being asked, and the refusal says how many went through.</summary>
    [Fact]
    public async Task A_refused_run_does_not_stop_the_others_and_says_how_many_started()
    {
        var transport = new RoutingTransport()
            .Refuses("runs/101/", "Something new GitHub started saying")
            .Returns(HttpMethod.Post, "runs/202/", "{}");

        var refused = await Assert.ThrowsAsync<GitHubException>(() =>
            Integration(transport).RerunFailedChecksAsync(Failing, TestContext.Current.CancellationToken));

        Assert.Equal(2, transport.CallsTo("rerun-failed-jobs"));
        Assert.Equal(
            "Couldn't re-run the failed checks of JSdotNet/Backlog#712: Something new GitHub started saying 1 of the 2 workflow runs did start again.",
            refused.Message);
    }

    [Fact]
    public async Task A_run_still_going_says_to_wait_rather_than_that_permission_is_missing()
    {
        var transport = new RoutingTransport().Refuses(HttpMethod.Post, "rerun-failed-jobs", System.Net.HttpStatusCode.Forbidden, "This workflow is already running");
        var one = Failing with { HeadChecks = [new GitHubCheck("build", GitHubCheckState.Failing, null, null, 101)] };

        var refused = await Assert.ThrowsAsync<GitHubException>(() =>
            Integration(transport).RerunFailedChecksAsync(one, TestContext.Current.CancellationToken));

        Assert.StartsWith("Couldn't re-run the failed checks of JSdotNet/Backlog#712: a workflow run is still going", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_run_too_old_to_retry_says_so_rather_than_that_permission_is_missing()
    {
        var transport = new RoutingTransport().Refuses(HttpMethod.Post, "rerun-failed-jobs", System.Net.HttpStatusCode.Forbidden, "Unable to retry this workflow run because it was created over 30 days ago");
        var one = Failing with { HeadChecks = [new GitHubCheck("build", GitHubCheckState.Failing, null, null, 101)] };

        var refused = await Assert.ThrowsAsync<GitHubException>(() =>
            Integration(transport).RerunFailedChecksAsync(one, TestContext.Current.CancellationToken));

        Assert.Contains("within 30 days", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("permission", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_permission_refusal_of_a_rerun_names_re_running_workflows()
    {
        var transport = new RoutingTransport().Refuses(HttpMethod.Post, "rerun-failed-jobs", System.Net.HttpStatusCode.Forbidden, "Resource not accessible by integration");
        var one = Failing with { HeadChecks = [new GitHubCheck("build", GitHubCheckState.Failing, null, null, 101)] };

        var refused = await Assert.ThrowsAsync<GitHubException>(() =>
            Integration(transport).RerunFailedChecksAsync(one, TestContext.Current.CancellationToken));

        Assert.Contains("doesn't have permission to re-run workflows in JSdotNet/Backlog", refused.Message, StringComparison.Ordinal);
    }

    // --- Helpers --------------------------------------------------------------

    private static string List(params string[] nodes) => $$"""
        { "data": { "repository": {
            "mergeCommitAllowed": true, "squashMergeAllowed": true, "rebaseMergeAllowed": true,
            "pullRequests": { "nodes": [ {{string.Join(",", nodes)}} ] } } } }
        """;

    private static string Node(int number, string updatedAt) => $$"""
        { "id": "PR_{{number}}", "number": {{number}}, "title": "#{{number}}", "url": "https://github.com/x/y/pull/{{number}}",
          "isDraft": false, "headRefName": "branch-{{number}}", "headRefOid": "sha{{number}}", "baseRefName": "main",
          "mergeStateStatus": "CLEAN", "mergeable": "MERGEABLE", "viewerDidAuthor": true, "author": { "login": "JSdotNet" },
          "autoMergeRequest": null, "updatedAt": "{{updatedAt}}", "commits": { "nodes": [] } }
        """;

    // --- Pinned ---------------------------------------------------------------

    /// <summary>Pins are read one query per repository, every repository side by side,
    /// whether or not that repository is configured — a pin outlives the scope it was
    /// made in.</summary>
    [Fact]
    public async Task Pins_are_read_per_repository_and_come_back_together()
    {
        var transport = new RoutingTransport()
            .Returns("graphql#JSdotNet/Backlog", Pinned(("p1", PinnedNode(1, "OPEN", "2026-10-01T08:00:00Z")), ("p3", PinnedNode(3, "MERGED", "2026-10-01T11:00:00Z"))))
            .Returns("graphql#JSdotNet/Archify", Pinned(("p2", PinnedNode(2, "CLOSED", "2026-10-01T10:00:00Z"))));

        var listing = await Integration(transport).ListPinnedPullRequestsAsync(
            [new PullRequestPin("JSdotNet/Backlog", 1), new PullRequestPin("JSdotNet/Archify", 2), new PullRequestPin("jsdotnet/backlog", 3)],
            TestContext.Current.CancellationToken);

        Assert.Equal(2, transport.Bodies.Count(body => body!.Contains("pullRequest(number:", StringComparison.Ordinal)));
        Assert.Equal([3, 2, 1], listing.PullRequests.Select(pull => pull.Number));
        Assert.Equal([GitHubItemState.Merged, GitHubItemState.Closed, GitHubItemState.Open], listing.PullRequests.Select(pull => pull.State));
        Assert.Empty(listing.Failures);
    }

    [Fact]
    public async Task A_repository_whose_pins_cannot_be_read_is_reported_and_the_others_still_come_back()
    {
        var transport = new RoutingTransport()
            .Returns("graphql#JSdotNet/Backlog", Pinned(("p1", PinnedNode(1, "OPEN", "2026-10-01T08:00:00Z"))))
            .Refuses("graphql#octo/broken", "gh: Could not resolve to a Repository with the name 'octo/broken'.");

        var listing = await Integration(transport).ListPinnedPullRequestsAsync(
            [new PullRequestPin("JSdotNet/Backlog", 1), new PullRequestPin("octo/broken", 9)],
            TestContext.Current.CancellationToken);

        Assert.Equal(1, Assert.Single(listing.PullRequests).Number);
        var failure = Assert.Single(listing.Failures);
        Assert.Equal("octo/broken", failure.RepositoryFullName);
    }

    [Fact]
    public async Task No_pins_read_nothing()
    {
        var transport = new RoutingTransport();

        var listing = await Integration(transport).ListPinnedPullRequestsAsync([], TestContext.Current.CancellationToken);

        Assert.Empty(listing.PullRequests);
        Assert.Empty(transport.Paths);
    }

    private static string Pinned(params (string Alias, string Node)[] pulls) => $$"""
        { "data": { "repository": {
            "mergeCommitAllowed": true, "squashMergeAllowed": true, "rebaseMergeAllowed": true,
            {{string.Join(",", pulls.Select(pull => $"\"{pull.Alias}\": {pull.Node}"))}} } } }
        """;

    private static string PinnedNode(int number, string state, string updatedAt) => $$"""
        { "id": "PR_{{number}}", "number": {{number}}, "title": "#{{number}}", "url": "https://github.com/x/y/pull/{{number}}",
          "state": "{{state}}", "isDraft": false, "headRefName": "branch-{{number}}", "headRefOid": "sha{{number}}", "baseRefName": "main",
          "mergeStateStatus": "UNKNOWN", "mergeable": "UNKNOWN", "viewerDidAuthor": false, "author": { "login": "someone" },
          "autoMergeRequest": null, "updatedAt": "{{updatedAt}}", "commits": { "nodes": [] } }
        """;

    /// <summary>The clock every listing here is read at.</summary>
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static string MergedList(params string[] nodes) => $$"""
        { "data": { "repository": { "pullRequests": { "nodes": [ {{string.Join(",", nodes)}} ] } } } }
        """;

    private static string Merged(int number, string mergedAt) => $$"""
        { "id": "PR_{{number}}", "number": {{number}}, "title": "#{{number}}", "url": "https://github.com/x/y/pull/{{number}}",
          "headRefName": "branch-{{number}}", "baseRefName": "main", "viewerDidAuthor": true, "author": { "login": "JSdotNet" },
          "mergedAt": "{{mergedAt}}", "mergedBy": { "login": "JSdotNet" } }
        """;

    private GitHubIntegration Integration(RoutingTransport transport)
    {
        Directory.CreateDirectory(_root);
        return new GitHubIntegration(
            new GitHubSettingsStore(Path.Combine(_root, "github.json")),
            new GitHubClient(transport),
            new NoProbe(),
            time: new FakeTimeProvider(Now));
    }

    private sealed class NoProbe : IGitHubConnectionProbe
    {
        public Task<GitHubConnection> DescribeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitHubConnection(true, "Connected."));

        public void Invalidate()
        {
        }
    }
}
