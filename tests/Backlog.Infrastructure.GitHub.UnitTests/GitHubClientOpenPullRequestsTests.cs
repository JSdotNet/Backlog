using System.Net;
using System.Text.Json;
using Backlog.Infrastructure.GitHub;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// Listing a repository's open pull requests, and the two acts the pull requests list
/// adds to the three merge acts: updating a branch that fell behind its base, and
/// taking a draft out of draft.
/// <para>
/// The list is GraphQL, a page per request, for the reason the status is: the check roll-up, the
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

        // The list, then the distance of each branch from its base — a second query,
        // because GitHub's comparison takes the other branch as an argument.
        Assert.All(transport.Paths, path => Assert.Equal("graphql#JSdotNet/Backlog", path));
        Assert.Single(transport.Bodies, sent => sent!.Contains("pullRequests(states: OPEN", StringComparison.Ordinal));

        using var body = JsonDocument.Parse(transport.Bodies[0]!);
        var variables = body.RootElement.GetProperty("variables");
        Assert.Equal("JSdotNet", variables.GetProperty("owner").GetString());
        Assert.Equal("Backlog", variables.GetProperty("name").GetString());

        var query = body.RootElement.GetProperty("query").GetString()!;
        Assert.Contains("pullRequests(states: OPEN", query, StringComparison.Ordinal);
        Assert.DoesNotContain("JSdotNet", query, StringComparison.Ordinal);
    }

    /// <summary>Fifty rows in one request ran into GitHub's GraphQL timeout on a
    /// repository with a long queue, so the list is two pages of twenty-five, the
    /// second asked for after the first's cursor, and no third even when GitHub has
    /// more.</summary>
    [Fact]
    public async Task The_list_is_read_in_pages_of_twenty_five_up_to_fifty()
    {
        var transport = new RoutingTransport().ReturnsInTurn(
            "graphql",
            ListPage("cursor-1", Nodes(1, GitHubClient.OpenPullRequestPageSize)),
            ListPage("cursor-2", Nodes(26, GitHubClient.OpenPullRequestPageSize)));

        var pulls = await new GitHubClient(transport).ListOpenPullRequestsAsync(Repository, TestContext.Current.CancellationToken);

        Assert.Equal(GitHubClient.OpenPullRequestLimit, pulls.Count);
        Assert.Equal(Enumerable.Range(1, 50), pulls.Select(pull => pull.Number));

        var pages = ListRequests(transport);
        Assert.Equal(2, pages.Count);
        Assert.Equal(25, pages[0].GetProperty("first").GetInt32());
        Assert.Equal(JsonValueKind.Null, pages[0].GetProperty("after").ValueKind);
        Assert.Equal("cursor-1", pages[1].GetProperty("after").GetString());
    }

    [Fact]
    public async Task A_list_without_a_next_page_is_one_request()
    {
        var transport = new RoutingTransport().Returns("graphql", ListPage(next: null, Nodes(1, 3)));

        var pulls = await new GitHubClient(transport).ListOpenPullRequestsAsync(Repository, TestContext.Current.CancellationToken);

        Assert.Equal(3, pulls.Count);
        Assert.Single(ListRequests(transport));
    }

    /// <summary>The list is ordered by last update, so a pull request updated between
    /// the two pages moves up and can be read on both; it is one row.</summary>
    [Fact]
    public async Task A_pull_request_read_on_both_pages_is_one_row()
    {
        var transport = new RoutingTransport().ReturnsInTurn(
            "graphql",
            ListPage("cursor-1", Nodes(1, 25)),
            ListPage(next: null, [Node(number: 25, id: "PR_25"), Node(number: 26, id: "PR_26")]));

        var pulls = await new GitHubClient(transport).ListOpenPullRequestsAsync(Repository, TestContext.Current.CancellationToken);

        Assert.Equal(Enumerable.Range(1, 26), pulls.Select(pull => pull.Number));
    }

    [Fact]
    public async Task An_answer_that_names_a_next_page_without_new_rows_is_followed_only_up_to_the_cap()
    {
        var transport = new RoutingTransport().ReturnsInTurn(
            "graphql",
            ListPage("cursor-1", Nodes(1, 3)),
            ListPage("cursor-2", Nodes(1, 3)));

        var pulls = await new GitHubClient(transport).ListOpenPullRequestsAsync(Repository, TestContext.Current.CancellationToken);

        Assert.Equal(3, pulls.Count);
        Assert.Equal(GitHubClient.OpenPullRequestPageCap, ListRequests(transport).Count);
    }

    /// <summary>GitHub's time per page varies, so a page it gave up on is asked for once
    /// more at half the size, and the pages after it go back to the full size.</summary>
    [Theory]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task A_page_github_gave_up_on_is_asked_again_at_half_the_size(HttpStatusCode status)
    {
        var transport = new RoutingTransport().RefusesOnceThenReturnsInTurn(
            "graphql",
            status,
            ListPage("cursor-1", Nodes(1, 13)),
            ListPage(next: null, Nodes(14, 25)));

        var pulls = await new GitHubClient(transport).ListOpenPullRequestsAsync(Repository, TestContext.Current.CancellationToken);

        Assert.Equal(Enumerable.Range(1, 38), pulls.Select(pull => pull.Number));
        Assert.Equal([25, 13, 25], ListRequests(transport).Select(page => page.GetProperty("first").GetInt32()));
        Assert.Equal("cursor-1", ListRequests(transport)[2].GetProperty("after").GetString());
    }

    [Fact]
    public async Task A_page_github_gives_up_on_twice_fails_the_list()
    {
        var transport = new RoutingTransport().Refuses(HttpMethod.Post, "graphql", HttpStatusCode.GatewayTimeout);

        var refused = await Assert.ThrowsAsync<GitHubException>(() =>
            new GitHubClient(transport).ListOpenPullRequestsAsync(Repository, TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.GatewayTimeout, refused.Status);
        Assert.Equal([25, 13], ListRequests(transport).Select(page => page.GetProperty("first").GetInt32()));
    }

    [Fact]
    public async Task A_page_refused_for_another_reason_is_not_asked_again()
    {
        var transport = new RoutingTransport().Refuses(HttpMethod.Post, "graphql", HttpStatusCode.Forbidden);

        await Assert.ThrowsAsync<GitHubException>(() =>
            new GitHubClient(transport).ListOpenPullRequestsAsync(Repository, TestContext.Current.CancellationToken));

        Assert.Single(ListRequests(transport));
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

    private static string ListPage(string? next, string[] nodes) => $$"""
        {
          "data": {
            "repository": {
              "mergeCommitAllowed": true,
              "squashMergeAllowed": true,
              "rebaseMergeAllowed": false,
              "pullRequests": {
                "pageInfo": { "hasNextPage": {{Bool(next is not null)}}, "endCursor": {{(next is null ? "null" : $"\"{next}\"")}} },
                "nodes": [ {{string.Join(",", nodes)}} ]
              }
            }
          }
        }
        """;

    private static string[] Nodes(int from, int count) =>
        [.. Enumerable.Range(from, count).Select(number => Node(number: number, id: $"PR_{number}"))];

    /// <summary>The variables of each list request the transport saw, in order —
    /// leaving out the distance query that follows the list.</summary>
    private static List<JsonElement> ListRequests(RoutingTransport transport) =>
    [
        .. transport.Bodies
            .Where(sent => sent!.Contains("pullRequests(states: OPEN", StringComparison.Ordinal))
            .Select(sent => JsonDocument.Parse(sent!).RootElement.GetProperty("variables").Clone())
    ];

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
