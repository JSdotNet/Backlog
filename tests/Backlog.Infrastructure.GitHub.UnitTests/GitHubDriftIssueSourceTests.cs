using Backlog.Infrastructure.GitHub;
using Backlog.Infrastructure.GitHub.Dashboard;
using Backlog.Modules.Dashboard.Abstractions.Services;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// The drift part's issues: one labelled listing per repository in scope, the
/// <c>sync-failed</c> ones marked, a repository that fails skipped with the reason kept,
/// and every repository failing thrown — nothing read is not nothing open.
/// </summary>
public sealed class GitHubDriftIssueSourceTests : IDisposable
{
    private static readonly DashboardRepository Backlog = new("backlog", "JSdotNet/Backlog");

    private static readonly DashboardRepository Specs = new("specs", "JSdotNet/spec-manager");

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "github-drift-issue-source-tests-" + Guid.NewGuid().ToString("N"));

    public GitHubDriftIssueSourceTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task Each_repository_is_listed_by_the_drift_label_and_sync_failed_is_marked()
    {
        var client = new ScriptedClient
        {
            ["Backlog"] = [Issue(912, "devbook-drift", "sync-failed"), Issue(913, "devbook-drift")]
        };

        var read = await new GitHubDriftIssueSource(client, Settings()).GetOpenAsync([Backlog], TestContext.Current.CancellationToken);

        Assert.Equal([("Backlog", "devbook-drift")], client.Asked);
        Assert.True(read.Complete);
        Assert.Null(read.Failure);
        Assert.Equal([912, 913], read.Issues.Select(issue => issue.Number));
        Assert.Equal([true, false], read.Issues.Select(issue => issue.SyncFailed));
        Assert.All(read.Issues, issue => Assert.Equal("backlog", issue.RepositoryAlias));
    }

    [Fact]
    public async Task A_repository_that_fails_is_skipped_and_its_reason_kept()
    {
        var client = new ScriptedClient { ["Backlog"] = [Issue(913, "devbook-drift")] };
        client.Failing["spec-manager"] = "API rate limit exceeded.";

        var read = await new GitHubDriftIssueSource(client, Settings()).GetOpenAsync([Backlog, Specs], TestContext.Current.CancellationToken);

        Assert.False(read.Complete);
        Assert.Equal("JSdotNet/spec-manager: API rate limit exceeded.", read.Failure);
        Assert.Single(read.Issues);
    }

    /// <summary>
    /// Entry 21's 422. GitHub's issue search answers "Validation Failed" when its
    /// <c>repo:</c> qualifier names a repository the routed account cannot see, and every
    /// search shares thirty requests a minute. The drift read no longer searches: the
    /// repository's own issue listing answers it, so a search GitHub refuses cannot fail
    /// the drift part, and opening the dashboard spends none of the search budget.
    /// </summary>
    [Fact]
    public async Task A_search_GitHub_refuses_with_422_no_longer_reaches_the_drift_read()
    {
        var transport = new RoutingTransport()
            .Refuses("search/issues", "gh: Validation Failed (HTTP 422)")
            .Returns("repos/JSdotNet/Backlog/issues?labels=devbook-drift", """
                [{ "node_id": "I_912", "number": 912, "title": "[Devbook drift] chapter", "html_url": "https://github.com/JSdotNet/Backlog/issues/912",
                   "body": null, "state": "open", "state_reason": null, "assignee": null, "updated_at": "2026-10-02T08:00:00Z",
                   "labels": [{ "name": "devbook-drift" }, { "name": "sync-failed" }] }]
                """);

        var read = await new GitHubDriftIssueSource(new GitHubClient(transport), Settings())
            .GetOpenAsync([Backlog], TestContext.Current.CancellationToken);

        Assert.True(read.Complete);
        Assert.True(Assert.Single(read.Issues).SyncFailed);
        Assert.Equal(0, transport.CallsTo("search/"));
    }

    [Fact]
    public async Task Every_repository_failing_throws_so_the_part_says_why()
    {
        var client = new ScriptedClient();
        client.Failing["Backlog"] = "API rate limit exceeded.";

        var thrown = await Assert.ThrowsAsync<GitHubException>(() =>
            new GitHubDriftIssueSource(client, Settings()).GetOpenAsync([Backlog], TestContext.Current.CancellationToken));

        Assert.Equal("JSdotNet/Backlog: API rate limit exceeded.", thrown.Message);
    }

    [Fact]
    public async Task A_repository_settings_does_not_know_narrows_to_nothing()
    {
        var client = new ScriptedClient();

        var read = await new GitHubDriftIssueSource(client, Settings()).GetOpenAsync(
            [new DashboardRepository("gone", "JSdotNet/gone")],
            TestContext.Current.CancellationToken);

        Assert.Empty(client.Asked);
        Assert.True(read.Complete);
        Assert.Empty(read.Issues);
    }

    private GitHubSettingsStore Settings()
    {
        var settings = new GitHubSettingsStore(Path.Combine(_root, "github.json"));
        Assert.Null(settings.SetRepositories(
        [
            new GitHubRepositoryRef("backlog", "JSdotNet", "Backlog"),
            new GitHubRepositoryRef("specs", "JSdotNet", "spec-manager")
        ]));
        return settings;
    }

    private static GitHubSearchedIssue Issue(int number, params string[] labels) =>
        new($"node-{number}", number, $"https://github.com/JSdotNet/Backlog/issues/{number}",
            $"[Devbook drift] chapter {number}", string.Empty, IsOpen: true, StateReason: null,
            AssigneeLogin: null, UpdatedAt: DateTimeOffset.UnixEpoch, Labels: labels);

    /// <summary>Answers the labelled listing per repository name; every other member keeps
    /// its default body, which refuses.</summary>
    private sealed class ScriptedClient : Dictionary<string, GitHubSearchedIssue[]>, IGitHubClient
    {
        public Dictionary<string, string> Failing { get; } = [];

        public List<(string Repository, string Label)> Asked { get; } = [];

        public Task<GitHubIssueSearchRead> ListOpenIssuesWithLabelAsync(
            GitHubRepositoryRef repository,
            string label,
            CancellationToken cancellationToken = default)
        {
            Asked.Add((repository.Name, label));

            if (Failing.TryGetValue(repository.Name, out var message))
            {
                return Task.FromException<GitHubIssueSearchRead>(new GitHubException(message));
            }

            return Task.FromResult(new GitHubIssueSearchRead(TryGetValue(repository.Name, out var issues) ? issues : [], Truncated: false));
        }

        public Task<GitHubCommittedFile> CommitFileAsync(GitHubRepositoryRef repository, string path, byte[] content, string commitMessage, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubIssue> CreateIssueAsync(GitHubRepositoryRef repository, string title, string? body, IEnumerable<string>? labels = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubIssueSnapshot> GetIssueAsync(GitHubRepositoryRef repository, int number, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubUploadedFile> UploadFileAsync(GitHubRepositoryRef repository, string path, string branch, byte[] content, string commitMessage, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
