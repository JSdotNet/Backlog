using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Dashboard.UI.Adapters;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The activity adapter folds every configured repository into one report. A
/// repository GitHub refuses is left out rather than failing the fetch — the
/// others are true — but the report has to say it is short: the parts cache a
/// successful report for the session, and a refusal folded in as complete
/// would read as the whole truth until the next Refresh.
/// </summary>
public sealed class GitHubActivitySourceTests : IDisposable
{
    private static readonly DateTimeOffset From = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = new(2026, 7, 31, 0, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "github-activity-source-tests-" + Guid.NewGuid().ToString("N"));

    public GitHubActivitySourceTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task A_repository_github_refuses_is_left_out_and_the_report_says_it_is_incomplete()
    {
        var settings = new GitHubSettingsStore(Path.Combine(_root, "github.json"));
        Assert.Null(settings.SetRepositories(
        [
            new GitHubRepositoryRef("backlog", "JSdotNet", "Backlog"),
            new GitHubRepositoryRef("renamed", "JSdotNet", "Renamed")
        ]));

        var client = new ScriptedActivityClient
        {
            ["JSdotNet/Backlog"] = new GitHubRepositoryActivity("JSdotNet/Backlog", [MergedPullRequest(7)], []),
            ["JSdotNet/Renamed"] = new GitHubException("Not Found")
        };

        var source = new GitHubActivitySource(client, new SignedIn("jsdotnet"), settings);

        var report = await source.GetActivityAsync(
            [new DashboardRepository("backlog", "JSdotNet/Backlog"), new DashboardRepository("renamed", "JSdotNet/Renamed")],
            From,
            To,
            TestContext.Current.CancellationToken);

        var pull = Assert.Single(report.PullRequests);
        Assert.Equal("backlog", pull.RepositoryAlias);
        Assert.Equal(7, pull.Number);
        Assert.False(report.Complete);
    }

    [Fact]
    public async Task Every_repository_answering_in_full_is_a_complete_report()
    {
        var settings = new GitHubSettingsStore(Path.Combine(_root, "github.json"));
        Assert.Null(settings.SetRepositories([new GitHubRepositoryRef("backlog", "JSdotNet", "Backlog")]));

        var client = new ScriptedActivityClient
        {
            ["JSdotNet/Backlog"] = new GitHubRepositoryActivity("JSdotNet/Backlog", [MergedPullRequest(7)], [])
        };

        var source = new GitHubActivitySource(client, new SignedIn("jsdotnet"), settings);

        var report = await source.GetActivityAsync(
            [new DashboardRepository("backlog", "JSdotNet/Backlog")],
            From,
            To,
            TestContext.Current.CancellationToken);

        Assert.Single(report.PullRequests);
        Assert.True(report.Complete);
    }

    /// <summary>
    /// Whose work a repository is filtered to follows the account its calls go out
    /// as, not the machine's default. Every repository used to be filtered by the
    /// one login <c>GET user</c> answered for the default account, so a repository
    /// bound to a second account authenticated correctly and then dropped every
    /// pull request in it — they were authored by the other login.
    /// </summary>
    [Fact]
    public async Task A_repository_bound_to_a_second_account_is_filtered_to_that_accounts_login()
    {
        var settings = new GitHubSettingsStore(Path.Combine(_root, "github.json"));
        Assert.Null(settings.SetRepositories(
        [
            new GitHubRepositoryRef("backlog", "JSdotNet", "Backlog"),
            new GitHubRepositoryRef("fincent", "innovadis-dev", "Fincent")
        ]));
        Assert.Null(settings.SetAccounts([new GitHubAccount("jsdotnet"), new GitHubAccount("j-schepers_innobv")]));
        Assert.Null(settings.SetRepositoryAccount("fincent", "j-schepers_innobv"));

        var client = new ScriptedActivityClient
        {
            ["JSdotNet/Backlog"] = new GitHubRepositoryActivity("JSdotNet/Backlog", [MergedPullRequest(7)], []),
            ["innovadis-dev/Fincent"] = new GitHubRepositoryActivity("innovadis-dev/Fincent", [MergedPullRequest(9)], [])
        };

        var source = new GitHubActivitySource(client, new SignedIn("jsdotnet"), settings);

        var report = await source.GetActivityAsync(
            [new DashboardRepository("backlog", "JSdotNet/Backlog"), new DashboardRepository("fincent", "innovadis-dev/Fincent")],
            From,
            To,
            TestContext.Current.CancellationToken);

        Assert.Equal("jsdotnet", client.AuthorAskedFor("JSdotNet/Backlog"));
        Assert.Equal("j-schepers_innobv", client.AuthorAskedFor("innovadis-dev/Fincent"));
        Assert.Equal(2, report.PullRequests.Count);
        Assert.True(report.Complete);
    }

    private static GitHubReviewedPullRequest MergedPullRequest(int number) => new(
        number,
        $"https://github.com/JSdotNet/Backlog/pull/{number}",
        $"Pull request {number}",
        From.AddDays(1),
        From.AddDays(2),
        FirstReviewedAt: null,
        ReviewRounds: 0,
        ChangesRequested: 0,
        CommitsAfterFirstReview: 0,
        ForcePushesAfterFirstReview: 0,
        FilesRetouched: 0,
        ChurnComplete: true)
    {
        SizeKnown = true,
        SyncsKnown = true
    };

    private sealed class SignedIn(string login) : IGitHubIdentityClient
    {
        public Task<string?> GetLoginAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(login);
    }

    /// <summary>One answer per repository full name: a report to return, or an
    /// exception to throw.</summary>
    private sealed class ScriptedActivityClient : IGitHubActivityClient
    {
        private readonly Dictionary<string, object> _answers = new(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, string> _authors = new(StringComparer.OrdinalIgnoreCase);

        public object this[string fullName]
        {
            set => _answers[fullName] = value;
        }

        /// <summary>The author the last call for one repository was filtered to.</summary>
        public string AuthorAskedFor(string fullName) => _authors[fullName];

        public Task<GitHubActivityAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitHubActivityAvailability(true, string.Empty));

        public Task<GitHubRepositoryActivity> GetActivityAsync(
            GitHubRepositoryRef repository,
            DateTimeOffset from,
            DateTimeOffset to,
            string author,
            CancellationToken cancellationToken = default)
        {
            _authors[$"{repository.Owner}/{repository.Name}"] = author;

            return _answers[$"{repository.Owner}/{repository.Name}"] switch
            {
                GitHubRepositoryActivity report => Task.FromResult(report),
                Exception exception => Task.FromException<GitHubRepositoryActivity>(exception),
                var other => throw new InvalidOperationException($"Unexpected answer {other}.")
            };
        }
    }
}
