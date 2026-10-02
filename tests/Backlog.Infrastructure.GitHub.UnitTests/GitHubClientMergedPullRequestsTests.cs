using System.Text.Json;
using Backlog.Infrastructure.GitHub;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// Listing a repository's recently merged pull requests — the pull requests list's
/// second view, so a pull request merged from the list does not simply vanish.
/// <para>
/// GraphQL, sent the way the open list is so the repository's account routing applies.
/// GitHub has no order by merge time, so the client pages through the merged pull
/// requests by update, newest first, until a page reaches back past the window: a pull
/// request merged inside the window was updated no earlier than it was merged, so
/// nothing further down the order can still be inside it.
/// </para>
/// </summary>
public sealed class GitHubClientMergedPullRequestsTests
{
    private static readonly GitHubRepositoryRef Repository = new("backlog", "JSdotNet", "Backlog");

    /// <summary>The start of the window every read here asks about.</summary>
    private static readonly DateTimeOffset Since = new(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_merged_pull_request_carries_what_the_list_shows()
    {
        var read = await Client(Page(Node())).ListMergedPullRequestsAsync(Repository, Since, TestContext.Current.CancellationToken);

        var pull = Assert.Single(read.PullRequests);
        Assert.Equal(712, pull.Number);
        Assert.Equal("Pull requests page", pull.Title);
        Assert.Equal("https://github.com/JSdotNet/Backlog/pull/712", pull.Url);
        Assert.Equal("JSdotNet/Backlog", pull.RepositoryFullName);
        Assert.Equal("claude/pr-page", pull.HeadRefName);
        Assert.Equal("main", pull.BaseRefName);
        Assert.Equal("JSdotNet", pull.AuthorLogin);
        Assert.True(pull.ViewerDidAuthor);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 9, 30, 0, TimeSpan.Zero), pull.MergedAt);
        Assert.Equal("merger", pull.MergedByLogin);
        Assert.False(read.Truncated);
    }

    /// <summary>A merger GitHub no longer knows — a deleted account — is no name
    /// rather than a reason to drop the row.</summary>
    [Fact]
    public async Task A_merger_github_does_not_name_is_read_as_none()
    {
        var pull = Assert.Single((await Client(Page(Node(mergedBy: "null", viewerDidAuthor: false)))
            .ListMergedPullRequestsAsync(Repository, Since, TestContext.Current.CancellationToken)).PullRequests);

        Assert.Null(pull.MergedByLogin);
        Assert.False(pull.ViewerDidAuthor);
    }

    /// <summary>Updated inside the window is not merged inside it: an old pull request
    /// commented on today is on the first page and is not recent work.</summary>
    [Fact]
    public async Task A_pull_request_merged_before_the_window_is_left_out()
    {
        var read = await Client(Page(
                Node(number: 1, mergedAt: "\"2026-10-01T09:30:00Z\""),
                Node(number: 2, mergedAt: "\"2025-01-01T09:30:00Z\"")))
            .ListMergedPullRequestsAsync(Repository, Since, TestContext.Current.CancellationToken);

        Assert.Equal(1, Assert.Single(read.PullRequests).Number);
    }

    [Fact]
    public async Task The_query_asks_for_merged_pull_requests_by_update_a_hundred_at_a_time_and_who_merged_them()
    {
        var transport = new RoutingTransport().Returns("graphql", Page(Node()));

        await new GitHubClient(transport).ListMergedPullRequestsAsync(Repository, Since, TestContext.Current.CancellationToken);

        // The repository's own path, so the account bound to it answers.
        Assert.Equal("graphql#JSdotNet/Backlog", Assert.Single(transport.Paths));

        using var body = JsonDocument.Parse(transport.Bodies[0]!);
        var variables = body.RootElement.GetProperty("variables");
        Assert.Equal("JSdotNet", variables.GetProperty("owner").GetString());
        Assert.Equal("Backlog", variables.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, variables.GetProperty("after").ValueKind);
        Assert.Equal(GitHubClient.MergedPageSize, variables.GetProperty("first").GetInt32());
        Assert.Equal(100, GitHubClient.MergedPageSize);

        var query = body.RootElement.GetProperty("query").GetString()!;
        Assert.Contains("pullRequests(states: MERGED, first: $first, after: $after", query, StringComparison.Ordinal);
        Assert.Contains("field: UPDATED_AT, direction: DESC", query, StringComparison.Ordinal);
        Assert.Contains("pageInfo { hasNextPage endCursor }", query, StringComparison.Ordinal);
        Assert.Contains("updatedAt", query, StringComparison.Ordinal);
        Assert.Contains("mergedAt", query, StringComparison.Ordinal);
        Assert.Contains("mergedBy { login }", query, StringComparison.Ordinal);
        Assert.Contains("viewerDidAuthor", query, StringComparison.Ordinal);
        Assert.DoesNotContain("JSdotNet", query, StringComparison.Ordinal);
    }

    /// <summary>A page whose oldest update is still inside the window may be followed by
    /// more merges inside it, so the next page is asked for, after the cursor the first
    /// one ended on.</summary>
    [Fact]
    public async Task Paging_continues_after_the_cursor_while_the_page_is_inside_the_window()
    {
        var transport = new RoutingTransport().ReturnsInTurn(
            "graphql",
            Page(hasNextPage: true, endCursor: "cursor-1", Node(number: 1, updatedAt: "2026-09-20T00:00:00Z")),
            Page(hasNextPage: false, endCursor: "cursor-2", Node(number: 2, updatedAt: "2026-09-18T00:00:00Z", mergedAt: "\"2026-09-18T00:00:00Z\"")));

        var read = await new GitHubClient(transport).ListMergedPullRequestsAsync(Repository, Since, TestContext.Current.CancellationToken);

        Assert.Equal([1, 2], read.PullRequests.Select(pull => pull.Number));
        Assert.Equal(2, transport.CallsTo("graphql"));
        Assert.All(transport.Paths, path => Assert.Equal("graphql#JSdotNet/Backlog", path));

        using var second = JsonDocument.Parse(transport.Bodies[1]!);
        Assert.Equal("cursor-1", second.RootElement.GetProperty("variables").GetProperty("after").GetString());
        Assert.False(read.Truncated);
    }

    /// <summary>Once a page reaches back past the window, nothing after it can have
    /// been merged inside the window, so no further page is asked for.</summary>
    [Fact]
    public async Task Paging_stops_at_the_first_page_whose_oldest_update_is_before_the_window()
    {
        var transport = new RoutingTransport().ReturnsInTurn(
            "graphql",
            Page(hasNextPage: true, endCursor: "cursor-1", nodes:
            [
                Node(number: 1, updatedAt: "2026-09-30T00:00:00Z"),
                Node(number: 2, updatedAt: "2026-09-10T00:00:00Z", mergedAt: "\"2026-09-10T00:00:00Z\"")
            ]),
            Page(hasNextPage: true, endCursor: "cursor-2", Node(number: 3)));

        var read = await new GitHubClient(transport).ListMergedPullRequestsAsync(Repository, Since, TestContext.Current.CancellationToken);

        Assert.Equal(1, transport.CallsTo("graphql"));
        Assert.Equal(1, Assert.Single(read.PullRequests).Number);
        Assert.False(read.Truncated);
    }

    /// <summary>A pull request updated while the walk is between pages moves up the
    /// order and comes back on the next page; it is one row, the first one read, because
    /// the table keys its rows by repository and number.</summary>
    [Fact]
    public async Task A_pull_request_seen_on_two_pages_is_listed_once()
    {
        var transport = new RoutingTransport().ReturnsInTurn(
            "graphql",
            Page(true, "cursor-1",
            [
                Node(number: 1, updatedAt: "2026-09-30T00:00:00Z"),
                Node(number: 2, updatedAt: "2026-09-29T00:00:00Z")
            ]),
            Page(false, "cursor-2",
            [
                Node(number: 2, updatedAt: "2026-09-28T00:00:00Z", mergedAt: "\"2026-09-20T00:00:00Z\""),
                Node(number: 3, updatedAt: "2026-09-27T00:00:00Z")
            ]));

        var read = await new GitHubClient(transport).ListMergedPullRequestsAsync(Repository, Since, TestContext.Current.CancellationToken);

        Assert.Equal([1, 2, 3], read.PullRequests.Select(pull => pull.Number));
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 9, 30, 0, TimeSpan.Zero), read.PullRequests[1].MergedAt);
    }

    [Fact]
    public async Task Paging_stops_when_github_says_there_is_no_next_page()
    {
        var transport = new RoutingTransport().ReturnsInTurn(
            "graphql",
            Page(hasNextPage: false, endCursor: "cursor-1", Node(number: 1, updatedAt: "2026-09-30T00:00:00Z")),
            Page(Node(number: 2)));

        var read = await new GitHubClient(transport).ListMergedPullRequestsAsync(Repository, Since, TestContext.Current.CancellationToken);

        Assert.Equal(1, transport.CallsTo("graphql"));
        Assert.Single(read.PullRequests);
        Assert.False(read.Truncated);
    }

    /// <summary>A repository that merges faster than the cap can page through says so,
    /// rather than passing the most recent few hundred off as the whole window.</summary>
    [Fact]
    public async Task A_window_the_page_cap_cannot_reach_the_end_of_is_reported_as_truncated()
    {
        var pages = Enumerable.Range(1, GitHubClient.MergedPageCap + 1)
            .Select(page => Page(hasNextPage: true, endCursor: $"cursor-{page}", Node(number: page, updatedAt: "2026-09-30T00:00:00Z")))
            .ToArray();
        var transport = new RoutingTransport().ReturnsInTurn("graphql", pages);

        var read = await new GitHubClient(transport).ListMergedPullRequestsAsync(Repository, Since, TestContext.Current.CancellationToken);

        Assert.Equal(GitHubClient.MergedPageCap, transport.CallsTo("graphql"));
        Assert.Equal(GitHubClient.MergedPageCap, read.PullRequests.Count);
        Assert.True(read.Truncated);
    }

    /// <summary>The cap is not truncation when the last page it read already ended the
    /// window or the list.</summary>
    [Fact]
    public async Task Reaching_the_cap_on_the_last_page_is_not_truncation()
    {
        var pages = Enumerable.Range(1, GitHubClient.MergedPageCap)
            .Select(page => Page(hasNextPage: page < GitHubClient.MergedPageCap, endCursor: $"cursor-{page}", Node(number: page, updatedAt: "2026-09-30T00:00:00Z")))
            .ToArray();
        var transport = new RoutingTransport().ReturnsInTurn("graphql", pages);

        var read = await new GitHubClient(transport).ListMergedPullRequestsAsync(Repository, Since, TestContext.Current.CancellationToken);

        Assert.Equal(GitHubClient.MergedPageCap, transport.CallsTo("graphql"));
        Assert.False(read.Truncated);
    }

    [Fact]
    public async Task A_missing_repository_is_a_not_found()
    {
        var transport = new RoutingTransport().Returns(
            "graphql",
            """{ "data": { "repository": null }, "errors": [ { "type": "NOT_FOUND", "message": "Could not resolve to a Repository with the name 'JSdotNet/Backlog'.", "path": ["repository"] } ] }""");

        var refused = await Assert.ThrowsAsync<GitHubException>(() =>
            new GitHubClient(transport).ListMergedPullRequestsAsync(Repository, Since, TestContext.Current.CancellationToken));

        Assert.True(refused.IsNotFound);
    }

    [Fact]
    public async Task A_repository_answered_as_null_without_errors_is_a_not_found()
    {
        var refused = await Assert.ThrowsAsync<GitHubException>(() =>
            Client("""{ "data": { "repository": null } }""").ListMergedPullRequestsAsync(Repository, Since, TestContext.Current.CancellationToken));

        Assert.True(refused.IsNotFound);
    }

    /// <summary>A node without a number, or without the merge time the list is
    /// ordered and windowed by, is left out rather than drawn as a row that sorts
    /// nowhere.</summary>
    [Fact]
    public async Task A_node_without_a_number_or_a_merge_time_is_left_out()
    {
        var read = await Client(Page(
                Node(number: 0),
                Node(number: 2, mergedAt: "null"),
                "null",
                Node(number: 3)))
            .ListMergedPullRequestsAsync(Repository, Since, TestContext.Current.CancellationToken);

        Assert.Equal(3, Assert.Single(read.PullRequests).Number);
    }

    [Fact]
    public async Task A_client_that_cannot_list_merged_pull_requests_refuses_like_github()
    {
        IGitHubClient client = new NothingClient();

        await Assert.ThrowsAsync<GitHubException>(() =>
            client.ListMergedPullRequestsAsync(Repository, Since, TestContext.Current.CancellationToken));
    }

    // --- Helpers --------------------------------------------------------------

    private static GitHubClient Client(string json) =>
        new(new RoutingTransport().Returns("graphql", json));

    private static string Page(params string[] nodes) => Page(false, null, nodes);

    private static string Page(bool hasNextPage, string? endCursor, params string[] nodes) => $$"""
        {
          "data": {
            "repository": {
              "pullRequests": {
                "pageInfo": { "hasNextPage": {{(hasNextPage ? "true" : "false")}}, "endCursor": {{(endCursor is null ? "null" : $"\"{endCursor}\"")}} },
                "nodes": [ {{string.Join(",", nodes)}} ]
              }
            }
          }
        }
        """;

    private static string Node(
        int number = 712,
        bool viewerDidAuthor = true,
        string updatedAt = "2026-10-01T10:00:00Z",
        string mergedAt = "\"2026-10-01T09:30:00Z\"",
        string mergedBy = """{ "login": "merger" }""") => $$"""
        {
          "id": "PR_{{number}}",
          "number": {{number}},
          "title": "Pull requests page",
          "url": "https://github.com/JSdotNet/Backlog/pull/{{number}}",
          "headRefName": "claude/pr-page",
          "baseRefName": "main",
          "viewerDidAuthor": {{(viewerDidAuthor ? "true" : "false")}},
          "author": { "login": "JSdotNet" },
          "updatedAt": "{{updatedAt}}",
          "mergedAt": {{mergedAt}},
          "mergedBy": {{mergedBy}}
        }
        """;

    private sealed class NothingClient : IGitHubClient
    {
        public Task<GitHubIssue> CreateIssueAsync(GitHubRepositoryRef repository, string title, string? body, IEnumerable<string>? labels = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubIssueSnapshot> GetIssueAsync(GitHubRepositoryRef repository, int number, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubUploadedFile> UploadFileAsync(GitHubRepositoryRef repository, string path, string branch, byte[] content, string commitMessage, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubCommittedFile> CommitFileAsync(GitHubRepositoryRef repository, string path, byte[] content, string commitMessage, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
