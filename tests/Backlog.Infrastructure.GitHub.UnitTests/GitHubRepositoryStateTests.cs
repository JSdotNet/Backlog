using System.Net;
using Backlog.Infrastructure.GitHub;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// GitHub's own word on a repository, read when the repository list in Settings
/// opens: whether GitHub reports it archived, or cannot find it. The answer only
/// raises a hint on the row; it never archives anything (local ADR 0029,
/// Verification 7), so these tests also hold that the registry is untouched.
/// </summary>
public sealed class GitHubRepositoryStateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-repository-state-tests", Guid.NewGuid().ToString("n"));

    private static readonly GitHubRepositoryRef Backlog = new("backlog", "JSdotNet", "Backlog");
    private static readonly GitHubRepositoryRef Budgetbeheer = new("budgetbeheer", "innovadis-dev", "Budgetbeheer");
    private static readonly GitHubRepositoryRef Gone = new("gone", "octo", "gone");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task A_repository_github_reports_archived_reads_archived_and_stays_active_here()
    {
        var transport = new RoutingTransport()
            .Returns("repos/innovadis-dev/Budgetbeheer", """{ "full_name": "innovadis-dev/Budgetbeheer", "archived": true }""");
        var integration = Integration(transport, Backlog, Budgetbeheer);

        var states = await integration.ReadRepositoryStatesAsync([Budgetbeheer], TestContext.Current.CancellationToken);

        Assert.Equal(GitHubRepositoryState.Archived, states["innovadis-dev/Budgetbeheer"]);
        Assert.False(integration.Settings.Current.Find("budgetbeheer")!.Archived);
    }

    [Fact]
    public async Task A_repository_github_reports_live_reads_active()
    {
        var transport = new RoutingTransport()
            .Returns("repos/JSdotNet/Backlog", """{ "full_name": "JSdotNet/Backlog", "archived": false }""");

        var states = await Integration(transport, Backlog).ReadRepositoryStatesAsync([Backlog], TestContext.Current.CancellationToken);

        Assert.Equal(GitHubRepositoryState.Active, states["JSdotNet/Backlog"]);
        Assert.Equal(["repos/JSdotNet/Backlog"], transport.Paths);
    }

    [Fact]
    public async Task A_repository_github_cannot_find_reads_not_found_and_stays_active_here()
    {
        var transport = new RoutingTransport()
            .Refuses(HttpMethod.Get, "repos/octo/gone", HttpStatusCode.NotFound, "Not Found");
        var integration = Integration(transport, Backlog, Gone);

        var states = await integration.ReadRepositoryStatesAsync([Gone], TestContext.Current.CancellationToken);

        Assert.Equal(GitHubRepositoryState.NotFound, states["octo/gone"]);
        Assert.False(integration.Settings.Current.Find("gone")!.Archived);
    }

    /// <summary>A refusal that is not a not-found — no credential, a rate limit, the
    /// network — says nothing about the repository, so it is left out rather than
    /// read as a reason to archive; the others still come back.</summary>
    [Fact]
    public async Task A_refusal_that_is_not_a_not_found_is_left_out_and_the_others_still_come_back()
    {
        var transport = new RoutingTransport()
            .Refuses("repos/octo/gone", "API rate limit exceeded.")
            .Returns("repos/JSdotNet/Backlog", """{ "archived": false }""");

        var states = await Integration(transport, Backlog, Gone).ReadRepositoryStatesAsync([Backlog, Gone], TestContext.Current.CancellationToken);

        Assert.Equal(GitHubRepositoryState.Active, states["JSdotNet/Backlog"]);
        Assert.False(states.ContainsKey("octo/gone"));
    }

    /// <summary>A repository already archived here is not asked about: no GitHub call
    /// is made for an archived repository (local ADR 0029, Verification 3).</summary>
    [Fact]
    public async Task A_repository_archived_here_is_not_asked_about()
    {
        var transport = new RoutingTransport()
            .Returns("repos/", """{ "archived": true }""");
        var integration = Integration(transport, Backlog, Budgetbeheer);
        Assert.Null(integration.Settings.SetRepositoryArchived("budgetbeheer", archived: true));

        var states = await integration.ReadRepositoryStatesAsync([Backlog, Budgetbeheer], TestContext.Current.CancellationToken);

        Assert.Equal(0, transport.CallsTo("Budgetbeheer"));
        Assert.False(states.ContainsKey("innovadis-dev/Budgetbeheer"));
        Assert.Equal(GitHubRepositoryState.Archived, states["JSdotNet/Backlog"]);
    }

    [Fact]
    public async Task The_answer_is_looked_up_without_regard_to_case()
    {
        var transport = new RoutingTransport()
            .Returns("repos/JSdotNet/Backlog", """{ "archived": true }""");

        var states = await Integration(transport, Backlog).ReadRepositoryStatesAsync([Backlog], TestContext.Current.CancellationToken);

        Assert.Equal(GitHubRepositoryState.Archived, states["jsdotnet/backlog"]);
    }

    private GitHubIntegration Integration(RoutingTransport transport, params GitHubRepositoryRef[] configured)
    {
        Directory.CreateDirectory(_root);
        var integration = new GitHubIntegration(
            new GitHubSettingsStore(Path.Combine(_root, "github.json")),
            new GitHubClient(transport),
            new NoProbe());
        Assert.Null(integration.Settings.SetRepositories(configured));
        return integration;
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
