using Backlog.Infrastructure.GitHub;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Backlog Management's repository directory, over the repositories Settings
/// already holds.
/// <para>
/// Asserted against the settings store rather than against what the adapter
/// returned, because the store is what every other surface reads: an import that
/// "registered" a repository only in its own answer would leave Settings, the
/// filter and the push flow exactly as they were, and the reader would find
/// nothing to correct the placeholder on.
/// </para>
/// </summary>
public sealed class BacklogRepositoryDirectoryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "backlog-repository-directory-tests",
        Guid.NewGuid().ToString("n"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void It_offers_the_repositories_settings_holds()
    {
        var directory = new SettingsRepositoryDirectory(StoreWith("JSdotNet/Backlog"));

        var repository = Assert.Single(directory.Repositories);
        Assert.Equal("backlog", repository.Alias);
        Assert.Equal("JSdotNet", repository.Owner);
        Assert.Equal("Backlog", repository.Name);
    }

    /// <summary>A plan writes a name however its author felt like writing it, and
    /// the alias it has to meet is stored lower-cased — so the match is on the
    /// normalized form rather than on the two strings as typed.</summary>
    [Theory]
    [InlineData("backlog")]
    [InlineData("Backlog")]
    [InlineData("  BACKLOG  ")]
    public void It_resolves_a_known_name_however_it_was_written(string name)
    {
        var directory = new SettingsRepositoryDirectory(StoreWith("JSdotNet/Backlog"));

        Assert.Equal("backlog", directory.Resolve(name)!.Alias);
    }

    [Fact]
    public void It_resolves_a_name_it_has_never_seen_to_nothing()
    {
        var directory = new SettingsRepositoryDirectory(StoreWith("JSdotNet/Backlog"));

        Assert.Null(directory.Resolve("some-other-repo"));
    }

    /// <summary>
    /// Per ADR 0004 a plan may introduce a repository just by mentioning it. What
    /// the plan states about it is its name and nothing else, so that is what it
    /// is registered with — owner and name standing in as the alias until
    /// somebody corrects them in Settings, which is a placeholder that reads as
    /// obviously unverified rather than as a real GitHub coordinate.
    /// </summary>
    [Fact]
    public void It_registers_a_name_nothing_knows_and_settings_keeps_it()
    {
        var settings = StoreWith("JSdotNet/Backlog");
        var directory = new SettingsRepositoryDirectory(settings);

        var registered = directory.Register("Newcomer");

        Assert.Equal("newcomer", registered.Alias);
        Assert.NotNull(settings.Current.Find("newcomer"));

        // Registered beside what was already configured, not instead of it.
        Assert.Equal(["backlog", "newcomer"], settings.Current.Repositories.Select(r => r.Alias));
    }

    /// <summary>Registering a name the registry already knows answers with what it
    /// already has. A plan naming the same repository in every entry is one
    /// repository; a directory that added it each time would turn a plan into a
    /// workspace full of duplicates.</summary>
    [Fact]
    public void Registering_a_known_name_returns_the_existing_repository_and_adds_nothing()
    {
        var settings = StoreWith("JSdotNet/Backlog");
        var directory = new SettingsRepositoryDirectory(settings);

        var registered = directory.Register("Backlog");

        Assert.Equal("backlog", registered.Alias);
        Assert.Equal("JSdotNet", registered.Owner);
        Assert.Single(settings.Current.Repositories);
    }

    [Fact]
    public void Registering_the_same_new_name_twice_registers_it_once()
    {
        var settings = StoreWith("JSdotNet/Backlog");
        var directory = new SettingsRepositoryDirectory(settings);

        directory.Register("newcomer");
        directory.Register("newcomer");

        Assert.Equal(["backlog", "newcomer"], settings.Current.Repositories.Select(r => r.Alias));
    }

    private GitHubSettingsStore StoreWith(string configuredLines)
    {
        var store = new GitHubSettingsStore(Path.Combine(_root, "github", "github.json"));
        var (repositories, errors) = GitHubSettings.ParseText(configuredLines);

        Assert.Empty(errors);
        Assert.Null(store.SetRepositories(repositories));

        return store;
    }
}
