using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Dashboard.UI.Adapters;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The baseline adapter asks the search API <c>author:</c> a login, and the login
/// has to be the one each repository is worked as. One query for the default
/// login across every repository read a bound repository's record as nothing at
/// all — the pull requests there are authored by the other account.
/// </summary>
public sealed class GitHubActivityBaselineSourceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 7, 31, 0, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "github-activity-baseline-source-tests-" + Guid.NewGuid().ToString("N"));

    public GitHubActivityBaselineSourceTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task Repositories_bound_to_different_accounts_are_counted_under_each_accounts_login_and_summed()
    {
        var settings = new GitHubSettingsStore(Path.Combine(_root, "github.json"));
        Assert.Null(settings.SetRepositories(
        [
            new GitHubRepositoryRef("backlog", "JSdotNet", "Backlog"),
            new GitHubRepositoryRef("fincent", "innovadis-dev", "Fincent")
        ]));
        Assert.Null(settings.SetAccounts([new GitHubAccount("jsdotnet"), new GitHubAccount("j-schepers_innobv")]));
        Assert.Null(settings.SetRepositoryAccount("fincent", "j-schepers_innobv"));

        var client = new ScriptedBaselineClient
        {
            ["jsdotnet"] = (Merged: 4, Closed: 1),
            ["j-schepers_innobv"] = (Merged: 6, Closed: 2)
        };

        var source = new GitHubActivityBaselineSource(client, new SignedIn("jsdotnet"), settings);

        var baseline = await source.GetBaselineAsync(
            [new DashboardRepository("backlog", "JSdotNet/Backlog"), new DashboardRepository("fincent", "innovadis-dev/Fincent")],
            [new ActivityWindow(Now.AddDays(-28), Now)],
            TestContext.Current.CancellationToken);

        Assert.Equal(["JSdotNet/Backlog"], client.RepositoriesAskedFor("jsdotnet"));
        Assert.Equal(["innovadis-dev/Fincent"], client.RepositoriesAskedFor("j-schepers_innobv"));

        var block = Assert.Single(baseline.Blocks);
        Assert.Equal(10, block.MergedPullRequests);
        Assert.Equal(3, block.ClosedIssues);
        Assert.True(baseline.Complete);
    }

    [Fact]
    public async Task Repositories_on_the_default_account_go_out_as_one_query_under_the_signed_in_login()
    {
        var settings = new GitHubSettingsStore(Path.Combine(_root, "github.json"));
        Assert.Null(settings.SetRepositories(
        [
            new GitHubRepositoryRef("backlog", "JSdotNet", "Backlog"),
            new GitHubRepositoryRef("copilot", "JSdotNet", "Copilot")
        ]));

        var client = new ScriptedBaselineClient { ["jsdotnet"] = (Merged: 4, Closed: 1) };

        var source = new GitHubActivityBaselineSource(client, new SignedIn("jsdotnet"), settings);

        var baseline = await source.GetBaselineAsync(
            [new DashboardRepository("backlog", "JSdotNet/Backlog"), new DashboardRepository("copilot", "JSdotNet/Copilot")],
            [new ActivityWindow(Now.AddDays(-28), Now)],
            TestContext.Current.CancellationToken);

        Assert.Equal(1, client.Calls);
        Assert.Equal(["JSdotNet/Backlog", "JSdotNet/Copilot"], client.RepositoriesAskedFor("jsdotnet"));
        Assert.Equal(4, Assert.Single(baseline.Blocks).MergedPullRequests);
    }

    /// <summary>
    /// One account's searches refusing — a secondary rate limit, typically — leaves
    /// that account's record out and says so, rather than emptying the record the
    /// other account did set. An empty baseline takes the volume score off the card
    /// for everyone; an incomplete one keeps it and names the bar as a floor.
    /// </summary>
    [Fact]
    public async Task One_accounts_refusal_leaves_its_record_out_and_marks_the_baseline_incomplete()
    {
        var settings = new GitHubSettingsStore(Path.Combine(_root, "github.json"));
        Assert.Null(settings.SetRepositories(
        [
            new GitHubRepositoryRef("backlog", "JSdotNet", "Backlog"),
            new GitHubRepositoryRef("fincent", "innovadis-dev", "Fincent")
        ]));
        Assert.Null(settings.SetAccounts([new GitHubAccount("jsdotnet"), new GitHubAccount("j-schepers_innobv")]));
        Assert.Null(settings.SetRepositoryAccount("fincent", "j-schepers_innobv"));

        var client = new ScriptedBaselineClient
        {
            ["jsdotnet"] = (Merged: 4, Closed: 1),
            Refusing = "j-schepers_innobv"
        };

        var source = new GitHubActivityBaselineSource(client, new SignedIn("jsdotnet"), settings);

        var baseline = await source.GetBaselineAsync(
            [new DashboardRepository("backlog", "JSdotNet/Backlog"), new DashboardRepository("fincent", "innovadis-dev/Fincent")],
            [new ActivityWindow(Now.AddDays(-28), Now)],
            TestContext.Current.CancellationToken);

        var block = Assert.Single(baseline.Blocks);
        Assert.Equal(4, block.MergedPullRequests);
        Assert.False(baseline.Complete);
    }

    private sealed class SignedIn(string login) : IGitHubIdentityClient
    {
        public Task<string?> GetLoginAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(login);
    }

    /// <summary>One count pair per author, applied to every block asked for, and a
    /// record of which repositories each author was asked about.</summary>
    private sealed class ScriptedBaselineClient : IGitHubActivityBaselineClient
    {
        private readonly Dictionary<string, (int Merged, int Closed)> _answers = new(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, List<string>> _asked = new(StringComparer.OrdinalIgnoreCase);

        public (int Merged, int Closed) this[string author]
        {
            set => _answers[author] = value;
        }

        public int Calls { get; private set; }

        /// <summary>The author whose call throws instead of answering.</summary>
        public string? Refusing { get; init; }

        public IReadOnlyList<string> RepositoriesAskedFor(string author) => _asked[author];

        public Task<GitHubActivityBaseline> GetBaselineAsync(
            IReadOnlyList<GitHubRepositoryRef> repositories,
            IReadOnlyList<ActivityBlock> blocks,
            string author,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            _asked[author] = [.. repositories.Select(repository => repository.FullName)];

            if (string.Equals(author, Refusing, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromException<GitHubActivityBaseline>(new GitHubException("Secondary rate limit"));
            }

            var (merged, closed) = _answers[author];

            return Task.FromResult(new GitHubActivityBaseline(
                [.. blocks.Select(block => new ActivityBlockCounts(block.From, block.To, merged, closed))],
                Complete: true));
        }
    }
}
