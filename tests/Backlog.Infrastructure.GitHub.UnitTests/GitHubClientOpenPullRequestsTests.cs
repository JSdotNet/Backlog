using System.Text.Json;
using Backlog.Infrastructure.GitHub;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// Listing a repository's open pull requests, and the two acts the pull requests list
/// adds to the three merge acts: updating a branch that fell behind its base, and
/// taking a draft out of draft.
/// <para>
/// The list is one GraphQL query for the reason the status is: the check roll-up, the
/// auto-merge request and the merge state live nowhere in REST together. The update is
/// REST, because that endpoint is the one GitHub documents <c>expected_head_sha</c> on.
/// </para>
/// </summary>
public sealed class GitHubClientOpenPullRequestsTests
{
    private static readonly GitHubRepositoryRef Repository = new("backlog", "JSdotNet", "Backlog");

    [Fact]
    public async Task A_listed_pull_request_carries_what_the_list_shows()
    {
        var pulls = await Client(List(Node())).ListOpenPullRequestsAsync(Repository, TestContext.Current.CancellationToken);

        var pull = Assert.Single(pulls);
        Assert.Equal(712, pull.Number);
        Assert.Equal("PR_kwDOlist", pull.NodeId);
        Assert.Equal("Pull requests page", pull.Title);
        Assert.Equal("https://github.com/JSdotNet/Backlog/pull/712", pull.Url);
        Assert.Equal("JSdotNet/Backlog", pull.RepositoryFullName);
        Assert.Equal("claude/pr-page", pull.HeadRefName);
        Assert.Equal("main", pull.BaseRefName);
        Assert.Equal("abc123", pull.HeadSha);
        Assert.Equal("JSdotNet", pull.AuthorLogin);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 9, 30, 0, TimeSpan.Zero), pull.UpdatedAt);
        Assert.Equal(GitHubMergeMethod.Merge, pull.PreferredMergeMethod);
    }

    [Theory]
    [InlineData(true, GitHubItemState.Draft)]
    [InlineData(false, GitHubItemState.Open)]
    public async Task A_draft_is_read_as_a_draft(bool draft, GitHubItemState expected)
    {
        var pull = Assert.Single(await Client(List(Node(isDraft: draft)))
            .ListOpenPullRequestsAsync(Repository, TestContext.Current.CancellationToken));

        Assert.Equal(draft, pull.IsDraft);
        Assert.Equal(expected, pull.State);
    }

    /// <summary>BEHIND is the one state "Update branch" exists for; DIRTY is a
    /// conflict GitHub cannot resolve on its own, and so is a CONFLICTING mergeable
    /// that the lazily computed merge state has not caught up with yet.</summary>
    [Theory]
    [InlineData("BEHIND", "MERGEABLE", true, false, false)]
    [InlineData("DIRTY", "CONFLICTING", false, true, false)]
    [InlineData("UNKNOWN", "CONFLICTING", false, true, false)]
    [InlineData("CLEAN", "MERGEABLE", false, false, true)]
    [InlineData("UNSTABLE", "MERGEABLE", false, false, true)]
    [InlineData("BLOCKED", "MERGEABLE", false, false, false)]
    public async Task The_merge_state_says_behind_conflicting_or_ready(
        string mergeState, string mergeable, bool behind, bool conflicts, bool ready)
    {
        var pull = Assert.Single(await Client(List(Node(mergeStateStatus: mergeState, mergeable: mergeable)))
            .ListOpenPullRequestsAsync(Repository, TestContext.Current.CancellationToken));

        Assert.Equal(behind, pull.IsBehind);
        Assert.Equal(conflicts, pull.HasConflicts);
        Assert.Equal(ready, pull.MergeReady);
        Assert.Equal(mergeState, pull.MergeStateStatus);
    }

    [Theory]
    [InlineData("\"SUCCESS\"", GitHubCheckState.Passing)]
    [InlineData("\"FAILURE\"", GitHubCheckState.Failing)]
    [InlineData("\"PENDING\"", GitHubCheckState.Pending)]
    [InlineData("null", GitHubCheckState.None)]
    public async Task Each_pull_requests_checks_are_its_head_commits_roll_up(string rollup, GitHubCheckState expected)
    {
        var pull = Assert.Single(await Client(List(Node(rollup: rollup)))
            .ListOpenPullRequestsAsync(Repository, TestContext.Current.CancellationToken));

        Assert.Equal(expected, pull.Checks);
    }

    /// <summary>"Mine" is GitHub's answer for the account the repository is read as,
    /// not a login compared here: the same person can be bound to two repositories
    /// under two accounts.</summary>
    [Fact]
    public async Task Whether_the_viewer_authored_it_and_whether_auto_merge_is_on_are_read_per_pull_request()
    {
        var pulls = await Client(List(
                Node(number: 1, id: "PR_1", viewerDidAuthor: true, autoMerge: """{ "enabledAt": "2026-10-01T08:00:00Z" }"""),
                Node(number: 2, id: "PR_2", viewerDidAuthor: false, autoMerge: "null")))
            .ListOpenPullRequestsAsync(Repository, TestContext.Current.CancellationToken);

        Assert.Equal(2, pulls.Count);
        Assert.True(pulls[0].ViewerDidAuthor);
        Assert.True(pulls[0].AutoMergeEnabled);
        Assert.False(pulls[1].ViewerDidAuthor);
        Assert.False(pulls[1].AutoMergeEnabled);
    }

    /// <summary>A listed pull request becomes the status the merge acts already take,
    /// so they act on it without a second read.</summary>
    [Fact]
    public async Task A_listed_pull_request_projects_to_the_status_the_merge_acts_take()
    {
        var pull = Assert.Single(await Client(List(Node(isDraft: true, mergeStateStatus: "DRAFT")))
            .ListOpenPullRequestsAsync(Repository, TestContext.Current.CancellationToken));

        var status = pull.ToStatus();

        Assert.Equal(712, status.Number);
        Assert.Equal("JSdotNet/Backlog", status.RepositoryFullName);
        Assert.Equal("PR_kwDOlist", status.NodeId);
        Assert.Equal(GitHubItemState.Draft, status.State);
        Assert.Equal(GitHubCheckState.Passing, status.Checks);
        Assert.False(status.MergeReady);
    }

    [Fact]
    public async Task The_list_is_one_post_to_graphql_naming_the_repository_by_variables()
    {
        var transport = new RoutingTransport().Returns("graphql", List(Node()));

        await new GitHubClient(transport).ListOpenPullRequestsAsync(Repository, TestContext.Current.CancellationToken);

        Assert.Equal("graphql#JSdotNet/Backlog", Assert.Single(transport.Paths));

        using var body = JsonDocument.Parse(transport.Bodies[0]!);
        var variables = body.RootElement.GetProperty("variables");
        Assert.Equal("JSdotNet", variables.GetProperty("owner").GetString());
        Assert.Equal("Backlog", variables.GetProperty("name").GetString());

        var query = body.RootElement.GetProperty("query").GetString()!;
        Assert.Contains("pullRequests(states: OPEN", query, StringComparison.Ordinal);
        Assert.DoesNotContain("JSdotNet", query, StringComparison.Ordinal);
    }

    /// <summary>A token that may not see checks is refused the roll-up once per pull
    /// request; the list is still the list, with no checks on any row.</summary>
    [Fact]
    public async Task A_refusal_confined_to_the_check_roll_ups_reads_as_no_checks()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(List(Node(rollup: "null")))!;
        node["errors"] = new System.Text.Json.Nodes.JsonArray(System.Text.Json.Nodes.JsonNode.Parse(
            """{ "type": "FORBIDDEN", "message": "Resource not accessible by personal access token", "path": ["repository", "pullRequests", "nodes", 0, "commits", "nodes", 0, "commit", "statusCheckRollup"] }"""));

        var pull = Assert.Single(await Client(node.ToJsonString())
            .ListOpenPullRequestsAsync(Repository, TestContext.Current.CancellationToken));

        Assert.Equal(GitHubCheckState.None, pull.Checks);
    }

    [Fact]
    public async Task A_refusal_outside_the_check_roll_ups_is_a_failure()
    {
        var transport = new RoutingTransport().Returns(
            "graphql",
            """{ "data": { "repository": null }, "errors": [ { "type": "NOT_FOUND", "message": "Could not resolve to a Repository with the name 'JSdotNet/Backlog'.", "path": ["repository"] } ] }""");

        var refused = await Assert.ThrowsAsync<GitHubException>(() =>
            new GitHubClient(transport).ListOpenPullRequestsAsync(Repository, TestContext.Current.CancellationToken));

        Assert.True(refused.IsNotFound);
    }

    [Fact]
    public async Task A_node_without_an_id_is_left_out()
    {
        var pulls = await Client(List(Node(number: 1, id: ""), Node(number: 2, id: "PR_2")))
            .ListOpenPullRequestsAsync(Repository, TestContext.Current.CancellationToken);

        Assert.Equal(2, Assert.Single(pulls).Number);
    }

    [Fact]
    public async Task Marking_ready_for_review_names_the_pull_request()
    {
        var transport = new RoutingTransport().Returns("graphql", """{ "data": { "markPullRequestReadyForReview": { "clientMutationId": null } } }""");

        await new GitHubClient(transport).MarkReadyForReviewAsync(Repository, "PR_kwDOlist", TestContext.Current.CancellationToken);

        Assert.Equal("graphql#JSdotNet/Backlog", Assert.Single(transport.Paths));
        using var body = JsonDocument.Parse(transport.Bodies[0]!);
        Assert.Contains("markPullRequestReadyForReview", body.RootElement.GetProperty("query").GetString(), StringComparison.Ordinal);
        Assert.Equal("PR_kwDOlist", body.RootElement.GetProperty("variables").GetProperty("id").GetString());
    }

    [Fact]
    public async Task Updating_a_branch_puts_to_the_pull_requests_update_branch_with_the_head_it_read()
    {
        var transport = new RoutingTransport().Returns(HttpMethod.Put, "update-branch", """{ "message": "Updating pull request branch.", "url": "https://github.com/JSdotNet/Backlog/pull/712" }""");

        await new GitHubClient(transport).UpdateBranchAsync(Repository, 712, "abc123", TestContext.Current.CancellationToken);

        Assert.Equal("repos/JSdotNet/Backlog/pulls/712/update-branch", Assert.Single(transport.Paths));
        using var body = JsonDocument.Parse(transport.Bodies[0]!);
        Assert.Equal("abc123", body.RootElement.GetProperty("expected_head_sha").GetString());
    }

    [Fact]
    public async Task Updating_a_branch_without_a_head_sends_none()
    {
        var transport = new RoutingTransport().Returns(HttpMethod.Put, "update-branch", "{}");

        await new GitHubClient(transport).UpdateBranchAsync(Repository, 712, expectedHeadSha: null, TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(Assert.Single(transport.Bodies)!);
        Assert.False(body.RootElement.TryGetProperty("expected_head_sha", out _));
    }

    // --- Helpers --------------------------------------------------------------

    private static GitHubClient Client(string json) =>
        new(new RoutingTransport().Returns("graphql", json));

    private static string List(params string[] nodes) => $$"""
        {
          "data": {
            "repository": {
              "mergeCommitAllowed": true,
              "squashMergeAllowed": true,
              "rebaseMergeAllowed": false,
              "pullRequests": { "nodes": [ {{string.Join(",", nodes)}} ] }
            }
          }
        }
        """;

    private static string Node(
        int number = 712,
        string id = "PR_kwDOlist",
        bool isDraft = false,
        string mergeStateStatus = "BLOCKED",
        string mergeable = "MERGEABLE",
        bool viewerDidAuthor = true,
        string autoMerge = "null",
        string rollup = "\"SUCCESS\"")
    {
        var commit = rollup == "null"
            ? """{ "commit": { "statusCheckRollup": null } }"""
            : "{ \"commit\": { \"statusCheckRollup\": { \"state\": " + rollup + " } } }";

        return $$"""
            {
              "id": "{{id}}",
              "number": {{number}},
              "title": "Pull requests page",
              "url": "https://github.com/JSdotNet/Backlog/pull/{{number}}",
              "isDraft": {{Bool(isDraft)}},
              "headRefName": "claude/pr-page",
              "headRefOid": "abc123",
              "baseRefName": "main",
              "mergeStateStatus": "{{mergeStateStatus}}",
              "mergeable": "{{mergeable}}",
              "viewerDidAuthor": {{Bool(viewerDidAuthor)}},
              "author": { "login": "JSdotNet" },
              "autoMergeRequest": {{autoMerge}},
              "updatedAt": "2026-10-01T09:30:00Z",
              "commits": { "nodes": [ {{commit}} ] }
            }
            """;
    }

    private static string Bool(bool value) => value ? "true" : "false";
}
