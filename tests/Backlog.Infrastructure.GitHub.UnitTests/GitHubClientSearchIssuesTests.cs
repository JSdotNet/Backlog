using Backlog.Infrastructure.GitHub;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// Searching one repository's issues — what the GitHub connector fetches linked
/// tasks with (local ADR 0020).
/// <para>
/// Two queries: every open issue, and, from the second sync on, every issue closed
/// since the last one. Both go through <c>search/issues</c> with a <c>repo:</c>
/// qualifier, which is what routes the call to the account the repository is bound
/// to. Paged a hundred at a time, oldest first, so an issue opened mid-walk lands on
/// a later page rather than pushing one already read onto the next.
/// </para>
/// </summary>
public sealed class GitHubClientSearchIssuesTests
{
    private static readonly GitHubRepositoryRef Repository = new("backlog", "JSdotNet", "Backlog");

    private static readonly DateTimeOffset Since = new(2026, 10, 1, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void The_open_query_names_issues_in_the_repository_that_are_open()
    {
        var path = GitHubClient.SearchIssuesPath(Repository, closedSince: null, page: 1);

        Assert.Equal(
            "search/issues?q=is:issue+repo:JSdotNet/Backlog+is:open&sort=created&order=asc&per_page=100&page=1",
            path);
    }

    [Fact]
    public void The_closed_query_names_issues_closed_at_or_after_the_last_run()
    {
        var path = GitHubClient.SearchIssuesPath(Repository, Since, page: 3);

        Assert.Equal(
            "search/issues?q=is:issue+repo:JSdotNet/Backlog+is:closed+closed:%3E%3D2026-10-01T09%3A30%3A00Z&sort=created&order=asc&per_page=100&page=3",
            path);
    }

    /// <summary>The query's <c>repo:</c> qualifier is what the settings read the
    /// account from, so it has to survive the escaping.</summary>
    [Fact]
    public void The_query_routes_to_the_repositorys_account()
    {
        var settings = new GitHubSettings
        {
            Repositories = [Repository with { Account = "work-login" }],
            Accounts = [new GitHubAccount("work-login") { Credential = GitHubCredentialKind.PersonalAccessToken, Token = "t" }],
        };

        var choice = settings.AccountForPath(GitHubClient.SearchIssuesPath(Repository, Since, page: 1));

        Assert.True(choice.IsBound);
        Assert.Equal("work-login", choice.Login);
    }

    [Fact]
    public async Task Without_a_since_only_the_open_issues_are_asked_for()
    {
        var transport = new RoutingTransport().Returns("search/issues", Page(total: 1, Issue(1)));

        var read = await new GitHubClient(transport).SearchIssuesAsync(Repository, closedSince: null, TestContext.Current.CancellationToken);

        Assert.Equal("is:open", Assert.Single(transport.Paths).Split('+')[2].Split('&')[0]);
        Assert.Single(read.Issues);
        Assert.False(read.Truncated);
    }

    [Fact]
    public async Task With_a_since_the_open_and_the_recently_closed_issues_are_both_asked_for()
    {
        var transport = new RoutingTransport()
            .Returns("is:open", Page(total: 1, Issue(1)))
            .Returns("is:closed", Page(total: 1, Issue(2, state: "closed", stateReason: "completed")));

        var read = await new GitHubClient(transport).SearchIssuesAsync(Repository, Since, TestContext.Current.CancellationToken);

        Assert.Equal(2, transport.Paths.Count);
        Assert.Equal([1, 2], read.Issues.Select(issue => issue.Number));
    }

    [Fact]
    public async Task Pages_are_followed_until_every_result_is_read()
    {
        var first = Page(total: 101, [.. Enumerable.Range(1, 100).Select(n => Issue(n))]);
        var second = Page(total: 101, Issue(101));
        var transport = new RoutingTransport().ReturnsInTurn("search/issues", first, second);

        var read = await new GitHubClient(transport).SearchIssuesAsync(Repository, closedSince: null, TestContext.Current.CancellationToken);

        Assert.Equal(101, read.Issues.Count);
        Assert.EndsWith("&page=2", transport.Paths[1]);
        Assert.False(read.Truncated);
    }

    /// <summary>Search answers at most a thousand results per query. A repository
    /// with more open issues than that cannot be read whole, and the answer says
    /// so rather than passing a partial list off as the full one.</summary>
    [Fact]
    public async Task More_results_than_search_will_page_to_is_reported_as_truncated()
    {
        var full = Page(total: 1500, [.. Enumerable.Range(1, 100).Select(n => Issue(n))]);
        var transport = new RoutingTransport().Returns("search/issues", full);

        var read = await new GitHubClient(transport).SearchIssuesAsync(Repository, closedSince: null, TestContext.Current.CancellationToken);

        Assert.Equal(GitHubClient.SearchPageCap, transport.Paths.Count);
        Assert.True(read.Truncated);
    }

    [Fact]
    public async Task An_answer_github_marks_incomplete_is_reported_as_truncated()
    {
        var transport = new RoutingTransport().Returns("search/issues", Page(total: 1, incomplete: true, Issue(1)));

        var read = await new GitHubClient(transport).SearchIssuesAsync(Repository, closedSince: null, TestContext.Current.CancellationToken);

        Assert.True(read.Truncated);
    }

    [Fact]
    public async Task A_searched_issue_carries_what_a_linked_task_needs()
    {
        var transport = new RoutingTransport().Returns("search/issues", Page(total: 1, Issue(
            412,
            state: "closed",
            stateReason: "not_planned",
            labels: ["bug", "points:5"],
            assignee: "octocat",
            body: "Steps to reproduce")));

        var issue = Assert.Single((await new GitHubClient(transport)
            .SearchIssuesAsync(Repository, closedSince: null, TestContext.Current.CancellationToken)).Issues);

        Assert.Equal("I_412", issue.NodeId);
        Assert.Equal(412, issue.Number);
        Assert.Equal("Issue 412", issue.Title);
        Assert.Equal("https://github.com/JSdotNet/Backlog/issues/412", issue.Url);
        Assert.Equal("Steps to reproduce", issue.Body);
        Assert.False(issue.IsOpen);
        Assert.Equal("not_planned", issue.StateReason);
        Assert.Equal("octocat", issue.AssigneeLogin);
        Assert.Equal(new DateTimeOffset(2026, 10, 2, 8, 0, 0, TimeSpan.Zero), issue.UpdatedAt);
        Assert.Equal(["bug", "points:5"], issue.Labels);
    }

    /// <summary><c>is:issue</c> already leaves pull requests out; a row carrying a
    /// <c>pull_request</c> object is dropped anyway, because a pull request is not
    /// something this sync brings in.</summary>
    [Fact]
    public async Task A_pull_request_in_the_answer_is_left_out()
    {
        var transport = new RoutingTransport().Returns("search/issues", Page(total: 2, Issue(1), Issue(2, pullRequest: true)));

        var read = await new GitHubClient(transport).SearchIssuesAsync(Repository, closedSince: null, TestContext.Current.CancellationToken);

        Assert.Equal(1, Assert.Single(read.Issues).Number);
    }

    /// <summary>An issue that closed between the two queries, or that search has not
    /// re-indexed yet, can come back from both. It is read once, and as closed: the
    /// closed query asked after the open one, so its answer is the later one.</summary>
    [Fact]
    public async Task An_issue_answered_twice_is_read_once_as_the_closed_query_saw_it()
    {
        var transport = new RoutingTransport()
            .Returns("is:open", Page(total: 1, Issue(7)))
            .Returns("is:closed", Page(total: 1, Issue(7, state: "closed", stateReason: "completed")));

        var read = await new GitHubClient(transport).SearchIssuesAsync(Repository, Since, TestContext.Current.CancellationToken);

        var issue = Assert.Single(read.Issues);
        Assert.False(issue.IsOpen);
        Assert.Equal("completed", issue.StateReason);
    }

    [Fact]
    public async Task A_refused_search_throws()
    {
        var transport = new RoutingTransport().Refuses("search/issues");

        await Assert.ThrowsAsync<GitHubException>(() =>
            new GitHubClient(transport).SearchIssuesAsync(Repository, closedSince: null, TestContext.Current.CancellationToken));
    }

    private static string Page(int total, params string[] items) => Page(total, incomplete: false, items);

    private static string Page(int total, bool incomplete, params string[] items) =>
        $$"""{ "total_count": {{total}}, "incomplete_results": {{(incomplete ? "true" : "false")}}, "items": [{{string.Join(',', items)}}] }""";

    private static string Issue(
        int number,
        string state = "open",
        string? stateReason = null,
        string[]? labels = null,
        string? assignee = null,
        string? body = null,
        bool pullRequest = false)
    {
        var labelJson = string.Join(',', (labels ?? []).Select(name => $$"""{ "name": "{{name}}" }"""));
        var reason = stateReason is null ? "null" : $"\"{stateReason}\"";
        var who = assignee is null ? "null" : $$"""{ "login": "{{assignee}}" }""";
        var text = body is null ? "null" : $"\"{body}\"";
        var pull = pullRequest ? """, "pull_request": { "url": "x" }""" : string.Empty;

        return $$"""
            {
              "node_id": "I_{{number}}",
              "number": {{number}},
              "title": "Issue {{number}}",
              "html_url": "https://github.com/JSdotNet/Backlog/issues/{{number}}",
              "body": {{text}},
              "state": "{{state}}",
              "state_reason": {{reason}},
              "assignee": {{who}},
              "updated_at": "2026-10-02T08:00:00Z",
              "labels": [{{labelJson}}]{{pull}}
            }
            """;
    }
}
