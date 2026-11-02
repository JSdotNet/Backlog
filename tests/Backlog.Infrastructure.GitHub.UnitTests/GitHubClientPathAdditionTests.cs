using Backlog.Infrastructure.GitHub;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// The pull request that added a path to the default branch: the oldest commit
/// GitHub lists for the path, then the pull request that commit belongs to. What
/// the devbook import keys a decision record by, since the record's Markdown does
/// not say which pull request it was decided in.
/// <para>
/// Every outcome is a result — found, not there, or not read with the reason —
/// because the import runs in the background and a throw there is a crash nobody
/// asked for.
/// </para>
/// </summary>
public sealed class GitHubClientPathAdditionTests
{
    private const string Path = ".devbook/arc42/adr/0044-subject-state.md";

    private static readonly GitHubRepositoryRef Repository = new("backlog", "JSdotNet", "Backlog");

    private static string Commit(string sha, string date) =>
        $$"""{ "sha": "{{sha}}", "commit": { "committer": { "date": "{{date}}" }, "author": { "date": "2000-01-01T00:00:00Z" } } }""";

    [Fact]
    public async Task The_oldest_commit_for_the_path_and_its_merged_pull_request_are_the_addition()
    {
        var transport = new RoutingTransport()
            .Returns("/commits?", $"[{Commit("c3", "2026-10-30T12:00:00Z")}, {Commit("c2", "2026-10-29T12:00:00Z")}, {Commit("c1", "2026-10-28T09:30:00Z")}]")
            .Returns("commits/c1/pulls", """[{ "number": 1590, "merged_at": null }, { "number": 1611, "merged_at": "2026-10-30T13:00:00Z" }]""");

        var read = await new GitHubClient(transport).FindPathAdditionAsync(Repository, Path, TestContext.Current.CancellationToken);

        Assert.True(read.IsRead);
        Assert.Equal(new GitHubPathAddition("c1", new DateTimeOffset(2026, 10, 28, 9, 30, 0, TimeSpan.Zero), 1611), read.Addition);
    }

    /// <summary>The path is asked about by name on the default branch — no
    /// <c>sha</c>, so GitHub lists the default branch's history — escaped, a full
    /// page at a time; the pull request is asked of the oldest commit alone.</summary>
    [Fact]
    public async Task The_history_is_asked_of_the_default_branch_by_path_and_the_pull_request_of_the_oldest_commit()
    {
        var transport = new RoutingTransport()
            .Returns("/commits?", $"[{Commit("c1", "2026-10-28T09:30:00Z")}]")
            .Returns("commits/c1/pulls", """[{ "number": 1611, "merged_at": "2026-10-30T13:00:00Z" }]""");

        await new GitHubClient(transport).FindPathAdditionAsync(Repository, "./" + Path, TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                "repos/JSdotNet/Backlog/commits?path=.devbook%2Farc42%2Fadr%2F0044-subject-state.md&per_page=100&page=1",
                "repos/JSdotNet/Backlog/commits/c1/pulls",
            ],
            transport.Paths);
        Assert.All(transport.Methods, method => Assert.Equal(HttpMethod.Get, method));
    }

    /// <summary>A history longer than a page is read to its last page, because the
    /// listing is newest first and the addition is the last row of all.</summary>
    [Fact]
    public async Task A_history_longer_than_a_page_is_read_to_its_end()
    {
        var full = "[" + string.Join(", ", Enumerable.Range(0, 100).Select(i => Commit($"n{i}", "2026-10-30T12:00:00Z"))) + "]";
        var transport = new RoutingTransport()
            .Returns("&page=1", full)
            .Returns("&page=2", $"[{Commit("first", "2026-01-02T03:04:05Z")}]")
            .Returns("commits/first/pulls", """[{ "number": 7, "merged_at": "2026-01-03T00:00:00Z" }]""");

        var read = await new GitHubClient(transport).FindPathAdditionAsync(Repository, Path, TestContext.Current.CancellationToken);

        Assert.Equal("first", read.Addition!.CommitSha);
        Assert.Equal(7, read.Addition.PullRequest);
        Assert.Equal(1, transport.CallsTo("&page=2"));
    }

    [Fact]
    public async Task A_path_with_no_history_on_the_default_branch_is_no_addition()
    {
        var transport = new RoutingTransport().Returns("/commits?", "[]");

        var read = await new GitHubClient(transport).FindPathAdditionAsync(Repository, Path, TestContext.Current.CancellationToken);

        Assert.True(read.IsRead);
        Assert.Null(read.Addition);
    }

    /// <summary>A commit pushed straight to the default branch belongs to no pull
    /// request: the addition is still read, with no pull request to key by.</summary>
    [Fact]
    public async Task A_commit_no_pull_request_holds_is_an_addition_without_one()
    {
        var transport = new RoutingTransport()
            .Returns("/commits?", $"[{Commit("c1", "2026-10-28T09:30:00Z")}]")
            .Returns("commits/c1/pulls", "[]");

        var read = await new GitHubClient(transport).FindPathAdditionAsync(Repository, Path, TestContext.Current.CancellationToken);

        Assert.True(read.IsRead);
        Assert.Equal("c1", read.Addition!.CommitSha);
        Assert.Null(read.Addition.PullRequest);
    }

    /// <summary>A history past the page limit is an answer, not a refusal: GitHub
    /// would give the same one next time, so it is unsettled — no addition, with the
    /// reason — rather than a failure a caller retries for ever.</summary>
    [Fact]
    public async Task A_history_past_the_page_limit_is_unsettled_not_failed()
    {
        var full = "[" + string.Join(", ", Enumerable.Range(0, 100).Select(i => Commit($"n{i}", "2026-10-30T12:00:00Z"))) + "]";
        var transport = new RoutingTransport().Returns("/commits?", full);

        var read = await new GitHubClient(transport).FindPathAdditionAsync(Repository, Path, TestContext.Current.CancellationToken);

        Assert.True(read.IsRead);
        Assert.Null(read.Addition);
        Assert.Contains("2000", read.Unanswered);
        Assert.Equal(0, transport.CallsTo("/pulls"));
    }

    [Fact]
    public async Task A_commit_listed_without_a_date_is_unsettled_not_failed()
    {
        var transport = new RoutingTransport().Returns("/commits?", """[{ "sha": "c1", "commit": { } }]""");

        var read = await new GitHubClient(transport).FindPathAdditionAsync(Repository, Path, TestContext.Current.CancellationToken);

        Assert.True(read.IsRead);
        Assert.Null(read.Addition);
        Assert.Contains("c1", read.Unanswered);
    }

    [Fact]
    public async Task A_refusal_is_a_result_with_githubs_reason_never_a_throw()
    {
        var transport = new RoutingTransport().Refuses("/commits?", "API rate limit exceeded");

        var read = await new GitHubClient(transport).FindPathAdditionAsync(Repository, Path, TestContext.Current.CancellationToken);

        Assert.False(read.IsRead);
        Assert.Null(read.Addition);
        Assert.Contains("API rate limit exceeded", read.Failure);
    }

    [Fact]
    public async Task A_refused_pull_request_lookup_is_a_result_too()
    {
        var transport = new RoutingTransport()
            .Returns("/commits?", $"[{Commit("c1", "2026-10-28T09:30:00Z")}]")
            .Refuses("commits/c1/pulls", "Bad credentials");

        var read = await new GitHubClient(transport).FindPathAdditionAsync(Repository, Path, TestContext.Current.CancellationToken);

        Assert.False(read.IsRead);
        Assert.Contains("Bad credentials", read.Failure);
    }

    /// <summary>A client that cannot read history — every test double that is not
    /// about it — answers like a GitHub that refused, as a result.</summary>
    [Fact]
    public async Task A_client_that_cannot_read_history_answers_not_read()
    {
        IGitHubClient client = new NoHistoryClient();

        var read = await client.FindPathAdditionAsync(Repository, Path, TestContext.Current.CancellationToken);

        Assert.False(read.IsRead);
    }

    private sealed class NoHistoryClient : IGitHubClient
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
