using System.Text.Json;
using Backlog.Infrastructure.GitHub;

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

    private GitHubIntegration Integration(RoutingTransport transport)
    {
        Directory.CreateDirectory(_root);
        return new GitHubIntegration(
            new GitHubSettingsStore(Path.Combine(_root, "github.json")),
            new GitHubClient(transport),
            new NoProbe());
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
