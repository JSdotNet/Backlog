using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Knowledge.Abstractions;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Tasks' repository directory, over the repositories Settings
/// already holds.
/// <para>
/// Asserted against the settings store rather than against what the adapter
/// returned, because the store is what every other surface reads: an import that
/// "registered" a repository only in its own answer would leave Settings, the
/// filter and the push flow exactly as they were, and the reader would find
/// nothing to correct the placeholder on.
/// </para>
/// </summary>
public sealed class TasksRepositoryDirectoryTests : IDisposable
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
    /// normalized form rather than on the two strings as typed.
    /// <para>
    /// The last two are the same repository named by its <c>owner/name</c>
    /// identity rather than by its label, which is the spelling a stored
    /// <c>repo_id</c> arrives in. Both spellings reach the one row, because a
    /// person typing a coordinate and an entry holding one are asking the same
    /// question.
    /// </para></summary>
    [Theory]
    [InlineData("backlog")]
    [InlineData("Backlog")]
    [InlineData("  BACKLOG  ")]
    [InlineData("JSdotNet/Backlog")]
    [InlineData("jsdotnet/backlog")]
    public void It_resolves_a_known_name_however_it_was_written(string name)
    {
        var directory = new SettingsRepositoryDirectory(StoreWith("JSdotNet/Backlog"));

        Assert.Equal("backlog", directory.Resolve(name)!.Alias);
    }

    /// <summary>The registry is the authority on how a repository is spelled, so a
    /// coordinate typed in any casing comes back spelled the way GitHub spells it.
    /// This is what makes the value safe to store: <c>repo:jsdotnet/backlog</c> and
    /// <c>repo:JSdotNet/Backlog</c> are one target, written down once.</summary>
    [Fact]
    public void Resolving_an_id_returns_the_registrys_casing()
    {
        var directory = new SettingsRepositoryDirectory(StoreWith("JSdotNet/Backlog"));

        Assert.Equal("JSdotNet/Backlog", directory.Resolve("jsdotnet/backlog")!.Id);
        Assert.Equal("JSdotNet/Backlog", directory.Resolve("JSDOTNET/BACKLOG")!.Id);

        // And the alias branch answers with the same identity, so a caller never
        // has to know which branch it took.
        Assert.Equal("JSdotNet/Backlog", directory.Resolve("backlog")!.Id);
    }

    [Fact]
    public void It_resolves_a_name_it_has_never_seen_to_nothing()
    {
        var directory = new SettingsRepositoryDirectory(StoreWith("JSdotNet/Backlog"));

        Assert.Null(directory.Resolve("some-other-repo"));
    }

    /// <summary>
    /// Per ADR 0007 a plan may introduce a repository just by mentioning it. What
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

    /// <summary>
    /// A name that states a real coordinate is registered as that coordinate,
    /// through the one grammar the Settings text box already reads. Registration
    /// used to put the whole name in all three fields, which turned
    /// <c>foo/bar</c> into the full name <c>foo/bar/foo/bar</c> — a repository
    /// nothing could ever push to and nobody could correct without retyping it.
    /// </summary>
    [Fact]
    public void Registering_an_owner_slash_name_keeps_it_as_one_coordinate()
    {
        var settings = StoreWith("JSdotNet/Backlog");
        var directory = new SettingsRepositoryDirectory(settings);

        var registered = directory.Register("foo/bar");

        Assert.Equal("foo/bar", registered.Id);
        Assert.Equal("foo", registered.Owner);
        Assert.Equal("bar", registered.Name);

        // The alias is the repository name, which is TryParse's documented default
        // for a line with no explicit alias and what somebody typing `repo:bar`
        // next would expect.
        Assert.Equal("bar", registered.Alias);

        // And Settings holds exactly that, so the row is an ordinary configured
        // repository from the moment it appears.
        Assert.Equal("foo/bar", settings.Current.Find("bar")!.FullName);
    }

    /// <summary>
    /// Two repositories can honestly want the same label. The list is judged
    /// invalid on a duplicate alias and the identity hues are keyed on it, so the
    /// newcomer takes a distinct one — and the alias already configured is left
    /// alone, because renaming it would be this code changing a label somebody
    /// chose and would orphan the roadmap bands keyed on it.
    /// </summary>
    [Fact]
    public void Registering_a_name_whose_alias_is_taken_gets_a_distinct_one()
    {
        var settings = StoreWith("JSdotNet/Backlog");
        var directory = new SettingsRepositoryDirectory(settings);

        var registered = directory.Register("someone-else/Backlog");

        Assert.Equal("someone-else-backlog", registered.Alias);
        Assert.Equal("someone-else/Backlog", registered.Id);

        // The repository that was already there keeps its label.
        Assert.Equal("JSdotNet/Backlog", settings.Current.Find("backlog")!.FullName);
        Assert.Equal(["backlog", "someone-else-backlog"], settings.Current.Repositories.Select(r => r.Alias));
    }

    /// <summary>
    /// A registered repository is directory-less: it gets the shared identity row
    /// and nothing machine-local at all. That is not a lesser state — it is
    /// exactly how a repository registered on another install arrives here, and
    /// the knowledge and push surfaces already say what a blank clone directory
    /// means rather than failing on it.
    /// </summary>
    [Fact]
    public void A_registered_repository_has_no_clone_directory_and_no_token()
    {
        var settings = StoreWith("JSdotNet/Backlog");
        var directory = new SettingsRepositoryDirectory(settings);

        directory.Register("foo/bar");

        var registered = settings.Current.Find("foo/bar")!;
        Assert.Null(registered.CloneDirectory);
        Assert.Null(registered.Token);
        Assert.Equal(
            KnowledgeFolderSetting.Defaults().Select(folder => folder.Key),
            registered.KnowledgeFolders.Select(folder => folder.Key));
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
