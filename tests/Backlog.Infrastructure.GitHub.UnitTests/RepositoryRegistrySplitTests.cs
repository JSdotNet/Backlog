using Backlog.Infrastructure.GitHub;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// The two files a repository is configured in, and the one façade over both.
/// <para>
/// Identity — the <c>owner/name</c> an entry files itself against, the alias
/// somebody types, the hue it wears — is workspace data and lives in the backlog
/// folder, which is the thing that gets synced. The clone directory and the
/// token are machine data and stay in the per-user file. Asserted against the
/// files themselves rather than only through <c>Current</c>, because "which file
/// is this in" is the whole subject: a store that composed the right answer
/// while writing the token into the synced folder would pass every test that
/// only read it back.
/// </para>
/// <para>
/// An install is a per-user file plus a workspace root, so a second install is
/// modelled as a second per-user file over the same root. That is exactly the
/// arrangement the split exists for.
/// </para>
/// </summary>
public class RepositoryRegistrySplitTests : IDisposable
{
    private const string RegistryUnreadable =
        "The repository registry couldn't be read, so repositories can't be changed right now.";

    private const string SaveFailed = "Changed, but the choice couldn't be saved for next time.";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "repository-registry-split-tests-" + Guid.NewGuid().ToString("N"));

    private string WorkspaceRoot => Path.Combine(_root, "workspace");

    private string LocalPath(string install = "install-1") => Path.Combine(_root, install, "github.json");

    private GitHubSettingsStore Store(string install = "install-1") => new(LocalPath(install), () => WorkspaceRoot);

    private static GitHubRepositoryRef Repository(string alias, string name) => new(alias, "JSdotNet", name);

    // --- The split itself -----------------------------------------------------

    [Fact]
    public void The_registry_holds_identity_and_the_local_file_holds_the_machine_half()
    {
        var store = Store();
        Assert.Null(store.SetRepositories([Repository("backlog", "Backlog")]));
        Assert.Null(store.SetRepositoryColour("backlog", 3));
        Assert.Null(store.SetRepositoryToken("backlog", "ghp_secret"));
        Assert.Null(store.SetCloneDirectory("backlog", Path.Combine(_root, "clone")));

        var registry = File.ReadAllText(store.RegistryPath);
        Assert.Contains("\"id\": \"JSdotNet/Backlog\"", registry, StringComparison.Ordinal);
        Assert.Contains("\"alias\": \"backlog\"", registry, StringComparison.Ordinal);
        Assert.Contains("\"colour\": 3", registry, StringComparison.Ordinal);

        // The two things that must never travel with a synced folder: a secret,
        // and a path off this machine's drive letters.
        Assert.DoesNotContain("ghp_secret", registry, StringComparison.Ordinal);
        Assert.DoesNotContain("cloneDirectory", registry, StringComparison.Ordinal);

        var local = File.ReadAllText(store.SettingsPath);
        Assert.Contains("\"id\": \"JSdotNet/Backlog\"", local, StringComparison.Ordinal);
        Assert.Contains("ghp_secret", local, StringComparison.Ordinal);
        Assert.Contains("cloneDirectory", local, StringComparison.Ordinal);

        // And the identity is stated once, in the file that is the authority on it.
        Assert.DoesNotContain("\"alias\"", local, StringComparison.Ordinal);
        Assert.DoesNotContain("\"owner\"", local, StringComparison.Ordinal);
        Assert.DoesNotContain("\"colour\"", local, StringComparison.Ordinal);
    }

    /// <summary>
    /// The alias is a display label now, so renaming it is an ordinary thing to
    /// do — and it must not orphan the machine half. The overlay keys on the id,
    /// so it survives by definition rather than by the luck of an
    /// alias-or-full-name fallback.
    /// </summary>
    [Fact]
    public void A_clone_directory_survives_the_alias_being_renamed()
    {
        var clone = Path.Combine(_root, "clone");

        var store = Store();
        Assert.Null(store.SetRepositories([Repository("backlog", "Backlog")]));
        Assert.Null(store.SetCloneDirectory("backlog", clone));
        Assert.Null(store.SetRepositoryToken("backlog", "ghp_secret"));

        // What Settings does when somebody retypes the text box with a new label.
        var (repositories, errors) = GitHubSettings.ParseText("bl = JSdotNet/Backlog");
        Assert.Empty(errors);
        Assert.Null(store.SetRepositories(repositories));

        // Read through a restart, which is what makes this a test of the file and
        // not of the list that happened to be in memory.
        var reopened = Store();
        Assert.Null(reopened.Current.Find("backlog"));
        Assert.Equal(clone, reopened.Current.Find("bl")!.CloneDirectory);
        Assert.Equal("ghp_secret", reopened.Current.Find("bl")!.Token);
    }

    [Fact]
    public void A_token_never_reaches_the_shared_registry()
    {
        var store = Store();
        Assert.Null(store.SetRepositories([Repository("backlog", "Backlog")]));
        Assert.Null(store.SetRepositoryToken("backlog", "ghp_secret"));

        Assert.DoesNotContain("ghp_secret", File.ReadAllText(store.RegistryPath), StringComparison.Ordinal);

        // Still remembered, though — local is where it belongs, not where it is
        // forgotten.
        Assert.Equal("ghp_secret", Store().Current.Find("backlog")!.Token);

        // And a second install over the same workspace never sees it.
        Assert.Null(Store("install-2").Current.Find("backlog")!.Token);
    }

    /// <summary>A token is a secret. Leaving one behind after "remove" is worse
    /// than losing the clone path beside it, so the overlay row goes with the
    /// registry row.</summary>
    [Fact]
    public void Removing_a_repository_takes_its_token_with_it()
    {
        var store = Store();
        Assert.Null(store.SetRepositories([Repository("backlog", "Backlog"), Repository("docs", "Docs")]));
        Assert.Null(store.SetRepositoryToken("docs", "ghp_docs"));

        Assert.Null(store.RemoveRepository("docs"));

        Assert.DoesNotContain("ghp_docs", File.ReadAllText(store.SettingsPath), StringComparison.Ordinal);
        Assert.DoesNotContain("JSdotNet/Docs", File.ReadAllText(store.RegistryPath), StringComparison.Ordinal);
    }

    /// <summary>The same pruning for the other way a repository leaves the list:
    /// somebody retypes the text box without its line.</summary>
    [Fact]
    public void Retyping_the_list_without_a_line_prunes_its_overlay_row()
    {
        var store = Store();
        Assert.Null(store.SetRepositories([Repository("backlog", "Backlog"), Repository("docs", "Docs")]));
        Assert.Null(store.SetRepositoryToken("docs", "ghp_docs"));

        var (repositories, errors) = GitHubSettings.ParseText("backlog = JSdotNet/Backlog");
        Assert.Empty(errors);
        Assert.Null(store.SetRepositories(repositories));

        Assert.DoesNotContain("ghp_docs", File.ReadAllText(store.SettingsPath), StringComparison.Ordinal);
        Assert.Equal(["backlog"], Store().Current.Repositories.Select(r => r.Alias));
    }

    // --- Degraded registry ----------------------------------------------------

    /// <summary>
    /// A corrupt shared file must never stop the app from opening — the rule the
    /// local file has always followed. What it may not do is read as an empty
    /// list: that would look like somebody had cleared their repositories, and
    /// the next write would make it true. So an un-migrated install opens on its
    /// legacy fields and every write that would touch the shared file is refused
    /// with the honest reason.
    /// </summary>
    [Fact]
    public void A_corrupt_registry_still_opens_the_app_and_refuses_repository_writes()
    {
        WriteLegacyLocalFile();
        WriteRegistryFile("{ this is not json");

        var store = Store();

        Assert.Equal(RegistryUnreadable, store.RegistryError);
        Assert.Equal("backlog", Assert.Single(store.Current.Repositories).Alias);

        Assert.Equal(RegistryUnreadable, store.SetRepositories([Repository("other", "Other")]));
        Assert.Equal(RegistryUnreadable, store.SetRepositoryColour("backlog", 2));
        Assert.Equal(RegistryUnreadable, store.RemoveRepository("backlog"));

        // Refused, not applied-and-reported: the in-memory list is untouched.
        Assert.Equal("backlog", Assert.Single(store.Current.Repositories).Alias);

        // And nothing wrote over the file that could not be read, so whatever it
        // holds is still there to be repaired by hand.
        Assert.Equal("{ this is not json", File.ReadAllText(store.RegistryPath));
    }

    /// <summary>
    /// An install that already went through the split states no identity locally,
    /// so a corrupt registry leaves it with an empty list. That must not be read
    /// as "these repositories are gone, prune their machine half" — the tokens
    /// and clone paths belong to repositories that are perfectly fine, and the
    /// list that says so is exactly the file that could not be read.
    /// </summary>
    [Fact]
    public void A_corrupt_registry_never_prunes_the_overlay()
    {
        WriteLocalFile(
            """
            {
              "repositories": [
                { "id": "JSdotNet/Backlog", "token": "ghp_one" },
                { "id": "JSdotNet/Docs", "token": "ghp_two" }
              ],
              "apiEndpoint": "https://api.github.com"
            }
            """);
        WriteRegistryFile("{ this is not json");

        var store = Store();
        Assert.Empty(store.Current.Repositories);

        // An already-migrated install has no legacy fields to fall back on, so the
        // list is genuinely empty — which is why the reason has to be readable
        // straight away rather than only after somebody tries to write. Settings
        // renders this on load; without it an intact-but-unparseable file reads as
        // "nobody has configured a repository yet".
        Assert.Equal(RegistryUnreadable, store.RegistryError);

        // A local-only write is still allowed — it is not a statement about which
        // repositories exist.
        Assert.Null(store.SetApiEndpoint("https://github.example/api/v3"));

        var local = File.ReadAllText(store.SettingsPath);
        Assert.Contains("ghp_one", local, StringComparison.Ordinal);
        Assert.Contains("ghp_two", local, StringComparison.Ordinal);
    }

    /// <summary>Today's contract, verbatim: the change takes effect for this
    /// session and the person is told it may not survive a restart. A registry
    /// that cannot be written is not a reason to refuse the change.</summary>
    [Fact]
    public void An_unwritable_registry_changes_the_session_and_says_so()
    {
        // A folder where the file should be. Nothing can write that path, and
        // nothing reads it as an existing file either, so this is the "missing but
        // unwritable" case rather than the corrupt one.
        Directory.CreateDirectory(Path.Combine(WorkspaceRoot, "config", "repos.json"));

        var store = Store();

        Assert.Equal(SaveFailed, store.SetRepositories([Repository("backlog", "Backlog")]));
        Assert.Equal("backlog", Assert.Single(store.Current.Repositories).Alias);
    }

    // --- Carry-over -----------------------------------------------------------

    [Fact]
    public void A_legacy_github_json_carries_over_once_and_the_local_file_is_reduced()
    {
        WriteLegacyLocalFile();

        var store = Store();

        var registry = File.ReadAllText(store.RegistryPath);
        Assert.Contains("\"id\": \"JSdotNet/Backlog\"", registry, StringComparison.Ordinal);
        Assert.Contains("\"alias\": \"backlog\"", registry, StringComparison.Ordinal);
        Assert.Contains("\"colour\": 3", registry, StringComparison.Ordinal);

        // The reduced write follows the successful shared one, so the identity is
        // stated in one file rather than two.
        var local = File.ReadAllText(store.SettingsPath);
        Assert.Contains("\"id\": \"JSdotNet/Backlog\"", local, StringComparison.Ordinal);
        Assert.Contains("ghp_secret", local, StringComparison.Ordinal);
        Assert.DoesNotContain("\"alias\"", local, StringComparison.Ordinal);
        Assert.DoesNotContain("\"owner\"", local, StringComparison.Ordinal);
        Assert.DoesNotContain("\"colour\"", local, StringComparison.Ordinal);

        // And nothing was lost on the way through.
        var repository = Assert.Single(store.Current.Repositories);
        Assert.Equal("backlog", repository.Alias);
        Assert.Equal("JSdotNet/Backlog", repository.FullName);
        Assert.Equal(3, repository.Colour);
        Assert.Equal("ghp_secret", repository.Token);
        Assert.Equal("/tmp/backlog-clone", repository.CloneDirectory);
    }

    /// <summary>
    /// Gap-filling only. Install #2's stale legacy file contributes the repository
    /// install #1 never had, and says nothing about the one it already holds — not
    /// its alias, not its hue, not its position. Anything else and the second
    /// machine to start would quietly overwrite the first machine's choices.
    /// </summary>
    [Fact]
    public void A_carry_over_only_adds_ids_the_registry_does_not_have()
    {
        WriteRegistryFile("""
            {
              "repositories": [ { "id": "JSdotNet/Backlog", "alias": "bl", "colour": 2 } ]
            }
            """);
        WriteLocalFile(
            """
            {
              "repositories": [
                { "alias": "backlog", "owner": "JSdotNet", "name": "Backlog", "colour": 5 },
                { "alias": "docs", "owner": "JSdotNet", "name": "Docs" }
              ],
              "apiEndpoint": "https://api.github.com"
            }
            """);

        var store = Store();

        Assert.Equal(["bl", "docs"], store.Current.Repositories.Select(r => r.Alias));
        Assert.Equal(2, store.Current.Find("bl")!.Colour);
        Assert.Equal("JSdotNet/Docs", store.Current.Find("docs")!.FullName);
    }

    /// <summary>
    /// The bounded hazard the reduced write closes. A repository deliberately
    /// removed and then resurrected by the very file that was migrated a moment
    /// ago would make "remove" mean nothing across a restart.
    /// </summary>
    [Fact]
    public void A_second_start_carries_nothing_over()
    {
        WriteLocalFile(
            """
            {
              "repositories": [
                { "alias": "backlog", "owner": "JSdotNet", "name": "Backlog" },
                { "alias": "docs", "owner": "JSdotNet", "name": "Docs", "token": "ghp_docs" }
              ],
              "apiEndpoint": "https://api.github.com"
            }
            """);

        var first = Store();
        Assert.Equal(["backlog", "docs"], first.Current.Repositories.Select(r => r.Alias));
        Assert.Null(first.RemoveRepository("docs"));

        var second = Store();
        Assert.Equal(["backlog"], second.Current.Repositories.Select(r => r.Alias));
    }

    // --- Reading the registry -------------------------------------------------

    /// <summary>The same tolerance the local file has always had for a row
    /// missing its owner or name: the row is dropped and the ones around it still
    /// take effect. The id is one string in the file because the registry is the
    /// authority on the value <c>repo_ids</c> stores — so a value that is not a
    /// coordinate is not a repository.</summary>
    [Fact]
    public void A_registry_row_whose_id_is_not_owner_slash_name_is_dropped_on_read()
    {
        WriteRegistryFile("""
            {
              "repositories": [
                { "id": "JSdotNet/Backlog", "alias": "backlog" },
                { "id": "", "alias": "blank" },
                { "id": "lonely", "alias": "lonely" },
                { "id": "too/many/parts", "alias": "deep" },
                { "id": "/leading", "alias": "leading" },
                { "id": "trailing/", "alias": "trailing" }
              ]
            }
            """);

        var store = Store();

        Assert.Equal(["backlog"], store.Current.Repositories.Select(r => r.Alias));
    }

    /// <summary>The registry is the authority on casing, so both spellings of one
    /// coordinate reach the one row and both read back the way GitHub spells it.
    /// That is what makes an id safe to store: two casings are one target, written
    /// down once.</summary>
    [Fact]
    public void Two_casings_of_one_id_resolve_to_the_registrys_casing()
    {
        var store = Store();
        Assert.Null(store.SetRepositories([Repository("backlog", "Backlog")]));

        Assert.Equal("JSdotNet/Backlog", store.Current.Find("jsdotnet/backlog")!.FullName);
        Assert.Equal("JSdotNet/Backlog", store.Current.Find("JSDOTNET/BACKLOG")!.FullName);

        // And the alias branch answers with the same row, so a caller never has to
        // know which branch it took.
        Assert.Equal("JSdotNet/Backlog", store.Current.Find("backlog")!.FullName);
    }

    // --- Following the workspace ----------------------------------------------

    /// <summary>
    /// The registry lives under the backlog folder, so moving that folder moves
    /// the registry with it — exactly as it moves the task database and the
    /// roadmap plan. The repositories on screen have to become the new
    /// workspace's, which is why the hosts wire <c>RootChanged</c> to
    /// <c>Reload</c> rather than leaving the old list in memory.
    /// </summary>
    [Fact]
    public void Moving_the_workspace_root_reloads_the_registry()
    {
        var root = Path.Combine(_root, "workspace-one");
        var store = new GitHubSettingsStore(LocalPath(), () => root);

        // Deliberately nothing machine-local, so this install has no overlay row
        // to carry into the workspace it moves to.
        Assert.Null(store.SetRepositories([Repository("backlog", "Backlog")]));
        Assert.Equal(["backlog"], store.Current.Repositories.Select(r => r.Alias));

        var moved = Path.Combine(_root, "workspace-two");
        Directory.CreateDirectory(Path.Combine(moved, "config"));
        File.WriteAllText(
            Path.Combine(moved, "config", "repos.json"),
            """{ "repositories": [ { "id": "Someone/Else", "alias": "else" } ] }""");

        var announced = 0;
        store.Changed += () => announced++;

        root = moved;
        store.Reload();

        Assert.Equal(["else"], store.Current.Repositories.Select(r => r.Alias));

        // Announced like any other change, because every surface reading the list
        // is already on screen when the folder moves.
        Assert.Equal(1, announced);
    }

    // --- Helpers --------------------------------------------------------------

    /// <summary>A per-user file exactly as a build from before the split left it:
    /// identity and machine half in one row, and no registry anywhere.</summary>
    private void WriteLegacyLocalFile() =>
        WriteLocalFile(
            """
            {
              "repositories": [
                {
                  "alias": "backlog",
                  "owner": "JSdotNet",
                  "name": "Backlog",
                  "cloneDirectory": "/tmp/backlog-clone",
                  "token": "ghp_secret",
                  "colour": 3
                }
              ],
              "apiEndpoint": "https://api.github.com"
            }
            """);

    private void WriteLocalFile(string json)
    {
        var path = LocalPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }

    private void WriteRegistryFile(string json)
    {
        var path = Path.Combine(WorkspaceRoot, "config", "repos.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
