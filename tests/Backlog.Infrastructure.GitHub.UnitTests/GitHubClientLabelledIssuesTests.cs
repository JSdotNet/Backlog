using Backlog.Infrastructure.GitHub;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// One repository's open issues carrying a label — what the dashboard's drift part reads
/// the <c>devbook-drift</c> issues with (entry 21).
/// <para>
/// Through the REST issue listing, <c>repos/{owner}/{name}/issues</c>, not
/// <c>search/issues</c>. Search shares one budget of thirty requests a minute across
/// every search the app makes, and the drift read went out once per repository for each
/// part that shows drift; it is also where GitHub answers "Validation Failed" (422) for a
/// <c>repo:</c> qualifier naming a repository the credential cannot see. The listing
/// counts against the core limit, and its path routes to the repository's account the
/// way every other <c>repos/</c> call does.
/// </para>
/// </summary>
public sealed class GitHubClientLabelledIssuesTests
{
    private static readonly GitHubRepositoryRef Repository = new("backlog", "JSdotNet", "Backlog");

    [Fact]
    public void The_listing_names_the_repositorys_open_issues_carrying_the_label()
    {
        var path = GitHubClient.LabelledIssuesPath(Repository, "devbook-drift", page: 2);

        Assert.Equal(
            "repos/JSdotNet/Backlog/issues?labels=devbook-drift&state=open&sort=created&direction=asc&per_page=100&page=2",
            path);
    }

    /// <summary>A label with a space or a comma stays one label: the listing reads a comma
    /// as "and this label too".</summary>
    [Fact]
    public void A_label_is_escaped_so_it_stays_one_label()
    {
        var path = GitHubClient.LabelledIssuesPath(Repository, "needs review, later", page: 1);

        Assert.Contains("labels=needs%20review%2C%20later&", path, StringComparison.Ordinal);
    }

    [Fact]
    public void The_listing_routes_to_the_repositorys_account()
    {
        var settings = new GitHubSettings
        {
            Repositories = [Repository with { Account = "work-login" }],
            Accounts = [new GitHubAccount("work-login") { Credential = GitHubCredentialKind.PersonalAccessToken, Token = "t" }],
        };

        var choice = settings.AccountForPath(GitHubClient.LabelledIssuesPath(Repository, "devbook-drift", page: 1));

        Assert.True(choice.IsBound);
        Assert.Equal("work-login", choice.Login);
    }

    [Fact]
    public async Task The_labelled_read_never_spends_the_search_budget()
    {
        var transport = new RoutingTransport().Returns("/issues?labels=", Listing(Issue(1, "devbook-drift")));

        var read = await new GitHubClient(transport)
            .ListOpenIssuesWithLabelAsync(Repository, "devbook-drift", TestContext.Current.CancellationToken);

        Assert.Equal(0, transport.CallsTo("search/"));
        Assert.Equal(1, Assert.Single(read.Issues).Number);
        Assert.False(read.Truncated);
    }

    /// <summary>The listing returns pull requests beside issues; a row carrying a
    /// <c>pull_request</c> object is left out.</summary>
    [Fact]
    public async Task A_pull_request_in_the_listing_is_left_out()
    {
        var transport = new RoutingTransport().Returns("/issues?labels=", Listing(Issue(1, "devbook-drift"), PullRequest(2)));

        var read = await new GitHubClient(transport)
            .ListOpenIssuesWithLabelAsync(Repository, "devbook-drift", TestContext.Current.CancellationToken);

        Assert.Equal(1, Assert.Single(read.Issues).Number);
    }

    [Fact]
    public async Task The_labels_and_the_state_are_read_from_each_row()
    {
        var transport = new RoutingTransport().Returns("/issues?labels=", Listing(Issue(9, "devbook-drift", "sync-failed")));

        var issue = Assert.Single((await new GitHubClient(transport)
            .ListOpenIssuesWithLabelAsync(Repository, "devbook-drift", TestContext.Current.CancellationToken)).Issues);

        Assert.Equal(["devbook-drift", "sync-failed"], issue.Labels);
        Assert.True(issue.IsOpen);
        Assert.Equal("[Devbook drift] chapter 9", issue.Title);
        Assert.Equal("https://github.com/JSdotNet/Backlog/issues/9", issue.Url);
    }

    [Fact]
    public async Task A_full_page_is_followed_by_the_next_and_a_short_one_ends_the_walk()
    {
        var full = Listing([.. Enumerable.Range(1, GitHubClient.SearchPageSize).Select(number => Issue(number, "devbook-drift"))]);
        var transport = new RoutingTransport()
            .Returns("&page=1", full)
            .Returns("&page=2", Listing(Issue(101, "devbook-drift")));

        var read = await new GitHubClient(transport)
            .ListOpenIssuesWithLabelAsync(Repository, "devbook-drift", TestContext.Current.CancellationToken);

        Assert.Equal(101, read.Issues.Count);
        Assert.Equal(2, transport.Paths.Count);
        Assert.False(read.Truncated);
    }

    [Fact]
    public async Task A_walk_stopped_at_the_page_cap_says_it_was_cut_short()
    {
        var full = Listing([.. Enumerable.Range(1, GitHubClient.SearchPageSize).Select(number => Issue(number, "devbook-drift"))]);
        var transport = new RoutingTransport().Returns("/issues?labels=", full);

        var read = await new GitHubClient(transport)
            .ListOpenIssuesWithLabelAsync(Repository, "devbook-drift", TestContext.Current.CancellationToken);

        Assert.Equal(GitHubClient.SearchPageCap, transport.Paths.Count);
        Assert.True(read.Truncated);
    }

    [Fact]
    public async Task A_refused_listing_throws()
    {
        var transport = new RoutingTransport().Refuses("/issues?labels=", "gh: Not Found (HTTP 404)");

        await Assert.ThrowsAsync<GitHubException>(() => new GitHubClient(transport)
            .ListOpenIssuesWithLabelAsync(Repository, "devbook-drift", TestContext.Current.CancellationToken));
    }

    private static string Listing(params string[] items) => $"[{string.Join(',', items)}]";

    private static string PullRequest(int number) => Row(number, ["devbook-drift"], pullRequest: true);

    private static string Issue(int number, params string[] labels) => Row(number, labels, pullRequest: false);

    private static string Row(int number, string[] labels, bool pullRequest)
    {
        var labelJson = string.Join(',', labels.Select(name => $$"""{ "name": "{{name}}" }"""));
        var pull = pullRequest ? """, "pull_request": { "url": "x" }""" : string.Empty;

        return $$"""
            {
              "node_id": "I_{{number}}",
              "number": {{number}},
              "title": "[Devbook drift] chapter {{number}}",
              "html_url": "https://github.com/JSdotNet/Backlog/issues/{{number}}",
              "body": null,
              "state": "open",
              "state_reason": null,
              "assignee": null,
              "updated_at": "2026-10-02T08:00:00Z",
              "labels": [{{labelJson}}]{{pull}}
            }
            """;
    }
}
