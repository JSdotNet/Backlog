using Backlog.Infrastructure.GitHub;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// Where an account lives, where a binding lives, and what survives.
/// <para>
/// The account concept splits across the same seam the repository already does,
/// and the split falls in a different place than "the account record". Which
/// account a repository is worked as is workspace data — "that is my work
/// repository" is true on every install — so it travels in the shared registry
/// beside the alias and the hue. Whether <em>this</em> machine holds a credential
/// for that login is a fact about this machine, so the account list lives in the
/// per-user file. One row is never split across two files.
/// </para>
/// <para>
/// Asserted against the files themselves rather than only through
/// <c>Current</c>, in the idiom <see cref="RepositoryRegistrySplitTests"/>
/// established: a store that composed the right answer while writing a token into
/// the synced folder would pass every test that only read it back.
/// </para>
/// </summary>
public sealed class AccountBindingTests : IDisposable
{
    private const string RegistryUnreadable =
        "The repository registry couldn't be read, so repositories can't be changed right now.";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "account-binding-tests-" + Guid.NewGuid().ToString("N"));

    // --- The highest-value test in the change ---------------------------------

    /// <summary>
    /// <c>SetRepositories</c> rebuilds every row from parsed text and the grammar
    /// has no account in it, so anything
    /// <c>PreserveExistingRepositorySettings</c> does not carry across is destroyed
    /// the moment somebody edits the repositories text box. For the binding that
    /// would mean silently sending the next call out as the wrong identity — the
    /// exact failure the binding exists to stop — and it would happen with no error
    /// and nothing on screen to notice.
    /// </summary>
    [Fact]
    public void Binding_survives_the_repository_list_being_retyped()
    {
        var store = Store();
        Assert.Null(store.SetAccounts([Account("j-schepers_innobv"), Account("JSdotNet")]));
        Assert.Null(store.SetRepositories([Repository("spec", "innovadis-dev", "spec-manager"), Repository("backlog", "JSdotNet", "Backlog")]));
        Assert.Null(store.SetRepositoryAccount("spec", "j-schepers_innobv"));
        Assert.Null(store.SetRepositoryAccount("backlog", "JSdotNet"));

        // Exactly what Settings does when somebody retypes the text box — same
        // repositories, freshly parsed, no account anywhere in the text.
        var (repositories, errors) = GitHubSettings.ParseText(
            "spec = innovadis-dev/spec-manager\nbacklog = JSdotNet/Backlog");
        Assert.Empty(errors);
        Assert.Null(store.SetRepositories(repositories));

        Assert.Equal("j-schepers_innobv", store.Current.Find("spec")!.Account);
        Assert.Equal("JSdotNet", store.Current.Find("backlog")!.Account);

        // Read through a restart, which is what makes this a test of the file and
        // not of the list that happened to be in memory.
        var reopened = Store();
        Assert.Equal("j-schepers_innobv", reopened.Current.Find("spec")!.Account);
        Assert.Equal("JSdotNet", reopened.Current.Find("backlog")!.Account);
    }

    /// <summary>The other way a row is rebuilt: the alias is a display label, so
    /// renaming it is an ordinary thing to do and must not orphan the binding. The
    /// registry keys on the id, so it survives by definition.</summary>
    [Fact]
    public void A_binding_survives_the_alias_being_renamed()
    {
        var store = Store();
        Assert.Null(store.SetAccounts([Account("JSdotNet")]));
        Assert.Null(store.SetRepositories([Repository("backlog", "JSdotNet", "Backlog")]));
        Assert.Null(store.SetRepositoryAccount("backlog", "JSdotNet"));

        var (repositories, errors) = GitHubSettings.ParseText("bl = JSdotNet/Backlog");
        Assert.Empty(errors);
        Assert.Null(store.SetRepositories(repositories));

        var reopened = Store();
        Assert.Null(reopened.Current.Find("backlog"));
        Assert.Equal("JSdotNet", reopened.Current.Find("bl")!.Account);
    }

    /// <summary>And the binding survives every other mutator too, because each one
    /// rebuilds the whole value rather than patching one field.</summary>
    [Fact]
    public void A_binding_and_the_accounts_survive_every_other_setting_being_changed()
    {
        var store = Store();
        Assert.Null(store.SetAccounts([Account("JSdotNet", token: "ghp_account")]));
        Assert.Null(store.SetRepositories([Repository("backlog", "JSdotNet", "Backlog")]));
        Assert.Null(store.SetRepositoryAccount("backlog", "JSdotNet"));

        Assert.Null(store.SetRepositoryColour("backlog", 4));
        Assert.Null(store.SetCloneDirectory("backlog", Path.Combine(_root, "clone")));
        Assert.Null(store.SetRepositoryToken("backlog", "ghp_repository"));
        Assert.Null(store.SetApiEndpoint("https://ghe.example.internal/api/v3"));
        Assert.Null(store.SetShowRepositoryColours(true));
        Assert.Null(store.SetKnowledgeFolder("backlog", ".domain", enabled: false, path: null));

        var reopened = Store();
        Assert.Equal("JSdotNet", reopened.Current.Find("backlog")!.Account);
        Assert.Equal("ghp_account", Assert.Single(reopened.Current.Accounts).Token);
    }

    // --- Which file holds what ------------------------------------------------

    [Fact]
    public void The_binding_is_registry_data_and_the_credential_is_local()
    {
        var store = Store();
        Assert.Null(store.SetAccounts([Account("JSdotNet", token: "ghp_account")]));
        Assert.Null(store.SetRepositories([Repository("backlog", "JSdotNet", "Backlog")]));
        Assert.Null(store.SetRepositoryAccount("backlog", "JSdotNet"));

        // The binding travels: it is a statement about the workspace.
        var registry = File.ReadAllText(store.RegistryPath);
        Assert.Contains("\"account\": \"JSdotNet\"", registry, StringComparison.Ordinal);

        // The credential does not, and nothing about the account list does either:
        // install #2 may have no gh at all, or a different set of logins.
        Assert.DoesNotContain("ghp_account", registry, StringComparison.Ordinal);
        Assert.DoesNotContain("\"accounts\"", registry, StringComparison.Ordinal);
        Assert.DoesNotContain("\"credential\"", registry, StringComparison.Ordinal);

        var local = File.ReadAllText(store.SettingsPath);
        Assert.Contains("\"accounts\"", local, StringComparison.Ordinal);
        Assert.Contains("\"login\": \"JSdotNet\"", local, StringComparison.Ordinal);
        Assert.Contains("ghp_account", local, StringComparison.Ordinal);

        // And the binding is stated once, in the file that is the authority on it.
        Assert.DoesNotContain("\"account\":", local, StringComparison.Ordinal);
    }

    /// <summary>The mirror of <c>A_token_never_reaches_the_shared_registry</c>: an
    /// account's pasted token is a secret and has no business travelling with a
    /// synced folder either.</summary>
    [Fact]
    public void An_account_token_never_reaches_the_shared_registry()
    {
        var store = Store();
        Assert.Null(store.SetAccounts([Account("JSdotNet", token: "ghp_account")]));
        Assert.Null(store.SetRepositories([Repository("backlog", "JSdotNet", "Backlog")]));
        Assert.Null(store.SetRepositoryAccount("backlog", "JSdotNet"));

        Assert.DoesNotContain("ghp_account", File.ReadAllText(store.RegistryPath), StringComparison.Ordinal);

        // Install #2 over the same workspace sees the binding and no credential —
        // an unsatisfied binding, which is exactly the day-one state of a second
        // machine and is a state with a name rather than a wrong-identity call.
        var second = Store("install-2");
        Assert.Equal("JSdotNet", second.Current.Find("backlog")!.Account);
        Assert.Empty(second.Current.Accounts);
        Assert.True(second.Current.AccountForPath("repos/JSdotNet/Backlog").IsUnsatisfied);
    }

    /// <summary>
    /// The hard rule of the credential design, enforced at the model: a
    /// CLI-backed account stores no token, whatever it is handed.
    /// <para>
    /// A <c>gho_</c> token is an OAuth token <c>gh</c> rotates. One written into
    /// this file would go stale and be used anyway — a correctness regression and a
    /// security one.
    /// </para>
    /// </summary>
    [Fact]
    public void A_gh_backed_account_stores_no_token_at_all()
    {
        var store = Store();
        Assert.Null(store.SetAccounts(
        [
            new GitHubAccount("JSdotNet") { Credential = GitHubCredentialKind.GhCli, Token = "gho_from_the_cli" }
        ]));

        Assert.Null(Assert.Single(store.Current.Accounts).Token);
        Assert.DoesNotContain("gho_from_the_cli", File.ReadAllText(store.SettingsPath), StringComparison.Ordinal);

        // And switching an account back to the CLI forgets the token it used to
        // have, rather than leaving a secret behind that nothing will ever use.
        Assert.Null(store.SetAccounts([Account("octocat", token: "ghp_pasted")]));
        Assert.Null(store.SetAccountCredential("octocat", GitHubCredentialKind.GhCli, "gho_from_the_cli"));

        Assert.Null(Assert.Single(store.Current.Accounts).Token);
        Assert.DoesNotContain("ghp_pasted", File.ReadAllText(store.SettingsPath), StringComparison.Ordinal);
    }

    // --- Migration ------------------------------------------------------------

    /// <summary>
    /// A workspace nobody has bound anything in writes a registry byte for byte
    /// identical to the one today's build writes. An install that never opens the
    /// Accounts panel produces no diff at all — which is the property that makes
    /// this change safe to ship.
    /// </summary>
    [Fact]
    public void An_unbound_workspace_writes_no_account_key_at_all()
    {
        var store = Store();
        Assert.Null(store.SetRepositories([Repository("backlog", "JSdotNet", "Backlog")]));
        Assert.Null(store.SetRepositoryColour("backlog", 3));

        Assert.Equal(
            """
            {
              "repositories": [
                {
                  "id": "JSdotNet/Backlog",
                  "alias": "backlog",
                  "colour": 3
                }
              ]
            }
            """.ReplaceLineEndings(),
            File.ReadAllText(store.RegistryPath).ReplaceLineEndings());
    }

    /// <summary>A registry written before accounts existed has no <c>account</c>
    /// key, which reads as every repository unbound — today's behaviour, and
    /// nothing to migrate.</summary>
    [Fact]
    public void A_registry_from_before_accounts_reads_as_every_repository_unbound()
    {
        WriteRegistryFile("""
            {
              "repositories": [
                { "id": "JSdotNet/Backlog", "alias": "backlog", "colour": 3 },
                { "id": "innovadis-dev/spec-manager", "alias": "spec" }
              ]
            }
            """);

        var store = Store();

        Assert.All(store.Current.Repositories, r => Assert.Null(r.Account));
        Assert.True(store.Current.AccountForPath("repos/JSdotNet/Backlog").IsDefault);
    }

    /// <summary>And a per-user file written before accounts existed reads as no
    /// accounts, losing none of what it did state.</summary>
    [Fact]
    public void A_settings_file_from_before_accounts_loses_nothing_and_reads_as_no_accounts()
    {
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
                  "colour": 3,
                  "knowledgeFolders": [ { "key": ".domain", "enabled": false, "path": "/tmp/domain" } ]
                }
              ],
              "apiEndpoint": "https://ghe.example.internal/api/v3"
            }
            """);

        var store = Store();

        Assert.Empty(store.Current.Accounts);

        var repository = Assert.Single(store.Current.Repositories);
        Assert.Equal("backlog", repository.Alias);
        Assert.Equal("JSdotNet/Backlog", repository.FullName);
        Assert.Equal(3, repository.Colour);
        Assert.Equal("ghp_secret", repository.Token);
        Assert.Equal("/tmp/backlog-clone", repository.CloneDirectory);
        Assert.Null(repository.Account);
        Assert.Equal("https://ghe.example.internal/api/v3", store.Current.ApiEndpoint);

        var domain = repository.KnowledgeFolders.Single(f => f.Key == ".domain");
        Assert.False(domain.Enabled);
        Assert.Equal("/tmp/domain", domain.Path);
    }

    /// <summary>
    /// Idempotent by construction, which is the discipline every settings document
    /// in this repository follows and the reason none of them carries a version
    /// field: reading a migrated workspace a second time changes neither file.
    /// </summary>
    [Fact]
    public void Reading_a_migrated_workspace_again_changes_neither_file()
    {
        var store = Store();
        Assert.Null(store.SetAccounts([Account("JSdotNet", token: "ghp_account"), Account("octocat")]));
        Assert.Null(store.SetRepositories([Repository("backlog", "JSdotNet", "Backlog")]));
        Assert.Null(store.SetRepositoryAccount("backlog", "JSdotNet"));

        var registry = File.ReadAllText(store.RegistryPath);
        var local = File.ReadAllText(store.SettingsPath);

        // Two more opens, because a migration that is only idempotent once is not
        // idempotent.
        _ = Store();
        _ = Store();

        Assert.Equal(registry, File.ReadAllText(store.RegistryPath));
        Assert.Equal(local, File.ReadAllText(store.SettingsPath));
    }

    /// <summary>The carry-over from a legacy per-user file still works, and now
    /// carries the binding through the registry row it writes.</summary>
    [Fact]
    public void A_carried_over_repository_can_be_bound_and_stays_bound()
    {
        WriteLocalFile(
            """
            {
              "repositories": [
                { "alias": "backlog", "owner": "JSdotNet", "name": "Backlog", "token": "ghp_secret", "colour": 3 }
              ],
              "apiEndpoint": "https://api.github.com"
            }
            """);

        var store = Store();
        Assert.Null(store.SetAccounts([Account("JSdotNet")]));
        Assert.Null(store.SetRepositoryAccount("backlog", "JSdotNet"));

        var reopened = Store();
        var repository = Assert.Single(reopened.Current.Repositories);
        Assert.Equal("JSdotNet", repository.Account);
        Assert.Equal(3, repository.Colour);
        Assert.Equal("ghp_secret", repository.Token);
    }

    // --- The mutators ---------------------------------------------------------

    [Fact]
    public void A_binding_can_be_cleared_back_to_the_default()
    {
        var store = Store();
        Assert.Null(store.SetAccounts([Account("JSdotNet")]));
        Assert.Null(store.SetRepositories([Repository("backlog", "JSdotNet", "Backlog")]));
        Assert.Null(store.SetRepositoryAccount("backlog", "JSdotNet"));

        Assert.Null(store.SetRepositoryAccount("backlog", null));

        Assert.Null(store.Current.Find("backlog")!.Account);
        Assert.DoesNotContain("\"account\"", File.ReadAllText(store.RegistryPath), StringComparison.Ordinal);
        Assert.Null(Store().Current.Find("backlog")!.Account);
    }

    /// <summary>A login nobody configured is refused rather than stored, because a
    /// binding to a name this machine cannot resolve is a typo that would surface
    /// as a 404 — which is the class of failure this whole change exists to
    /// remove.</summary>
    [Fact]
    public void Binding_to_a_login_that_names_no_account_is_refused()
    {
        var store = Store();
        Assert.Null(store.SetAccounts([Account("JSdotNet")]));
        Assert.Null(store.SetRepositories([Repository("backlog", "JSdotNet", "Backlog")]));

        Assert.Equal(
            "'JSdotNett' is not a configured account.",
            store.SetRepositoryAccount("backlog", "JSdotNett"));

        Assert.Null(store.Current.Find("backlog")!.Account);
    }

    [Fact]
    public void Binding_a_repository_that_is_no_longer_configured_says_so()
    {
        var store = Store();
        Assert.Null(store.SetAccounts([Account("JSdotNet")]));

        Assert.Equal("That repository is no longer configured.", store.SetRepositoryAccount("backlog", "JSdotNet"));
    }

    /// <summary>The binding is registry data, so it is refused while the registry
    /// cannot be read — exactly like the hue, and for the same reason: the list of
    /// what is configured is the thing that could not be read.</summary>
    [Fact]
    public void Binding_while_the_registry_is_unreadable_is_refused()
    {
        WriteLocalFile(
            """
            {
              "repositories": [ { "alias": "backlog", "owner": "JSdotNet", "name": "Backlog" } ],
              "apiEndpoint": "https://api.github.com"
            }
            """);
        WriteRegistryFile("{ this is not json");

        var store = Store();
        Assert.Null(store.SetAccounts([Account("JSdotNet")]));

        Assert.Equal(RegistryUnreadable, store.SetRepositoryAccount("backlog", "JSdotNet"));
        Assert.Null(store.Current.Find("backlog")!.Account);

        // Every repository correctly reads as unbound and falls back to the
        // default: degrade, do not fail.
        Assert.True(store.Current.AccountForPath("repos/JSdotNet/Backlog").IsDefault);

        // And the accounts are local, so they survive an unreadable registry — a
        // local write is not a statement about which repositories exist.
        Assert.Equal("JSdotNet", Assert.Single(store.Current.Accounts).Login);
        Assert.Equal("{ this is not json", File.ReadAllText(store.RegistryPath));
    }

    /// <summary>
    /// Removing an account is a machine act and deliberately leaves the workspace's
    /// bindings alone. Rewriting the shared registry because somebody forgot a
    /// local credential would let one install erase the other's configuration; what
    /// it leaves instead is an unsatisfied binding, which has a name.
    /// </summary>
    [Fact]
    public void Removing_an_account_leaves_the_workspaces_bindings_alone()
    {
        var store = Store();
        Assert.Null(store.SetAccounts([Account("JSdotNet", token: "ghp_account")]));
        Assert.Null(store.SetRepositories([Repository("backlog", "JSdotNet", "Backlog")]));
        Assert.Null(store.SetRepositoryAccount("backlog", "JSdotNet"));

        Assert.Null(store.RemoveAccount("jsdotnet"));

        Assert.Empty(store.Current.Accounts);
        Assert.Equal("JSdotNet", store.Current.Find("backlog")!.Account);
        Assert.True(store.Current.AccountForPath("repos/JSdotNet/Backlog").IsUnsatisfied);

        // And the secret went with it, which is what "remove" owes.
        Assert.DoesNotContain("ghp_account", File.ReadAllText(store.SettingsPath), StringComparison.Ordinal);
    }

    [Fact]
    public void Removing_or_changing_an_account_that_is_not_configured_says_so()
    {
        var store = Store();

        Assert.Equal("'octocat' is not a configured account.", store.RemoveAccount("octocat"));
        Assert.Equal(
            "'octocat' is not a configured account.",
            store.SetAccountCredential("octocat", GitHubCredentialKind.PersonalAccessToken, "ghp_x"));
    }

    [Fact]
    public void Accounts_are_stored_once_per_login_and_blank_logins_are_dropped()
    {
        var store = Store();
        Assert.Null(store.SetAccounts(
        [
            new GitHubAccount("  JSdotNet  "),
            new GitHubAccount("jsdotnet") { DisplayName = "the duplicate" },
            new GitHubAccount("   ")
        ]));

        var account = Assert.Single(store.Current.Accounts);
        Assert.Equal("JSdotNet", account.Login);
        Assert.Null(account.DisplayName);
    }

    /// <summary>The binding is stored with the account's own spelling rather than
    /// whatever was typed, so the shared registry states the login the way its
    /// owner writes it.</summary>
    [Fact]
    public void A_binding_is_stored_with_the_accounts_own_spelling()
    {
        var store = Store();
        Assert.Null(store.SetAccounts([Account("JSdotNet")]));
        Assert.Null(store.SetRepositories([Repository("backlog", "JSdotNet", "Backlog")]));

        Assert.Null(store.SetRepositoryAccount("backlog", "JSDOTNET"));

        Assert.Equal("JSdotNet", store.Current.Find("backlog")!.Account);
        Assert.Contains("\"account\": \"JSdotNet\"", File.ReadAllText(store.RegistryPath), StringComparison.Ordinal);
    }

    [Fact]
    public void An_accounts_credential_kind_round_trips_through_the_file()
    {
        var store = Store();
        Assert.Null(store.SetAccounts([Account("JSdotNet"), Account("octocat", token: "ghp_octocat")]));

        var reopened = Store();

        Assert.Equal(GitHubCredentialKind.GhCli, reopened.Current.Account("JSdotNet")!.Credential);
        Assert.Equal(GitHubCredentialKind.PersonalAccessToken, reopened.Current.Account("octocat")!.Credential);
        Assert.Equal("ghp_octocat", reopened.Current.Account("octocat")!.Token);

        // Stored by name, so reordering the enum cannot silently re-point every
        // account at a different kind.
        Assert.Contains("\"credential\": \"GhCli\"", File.ReadAllText(store.SettingsPath), StringComparison.Ordinal);
    }

    /// <summary>A kind this build has never heard of degrades to "ask gh" rather
    /// than refusing to open the file.</summary>
    [Fact]
    public void An_unknown_credential_kind_reads_as_the_cli()
    {
        WriteLocalFile(
            """
            {
              "repositories": [],
              "accounts": [ { "login": "JSdotNet", "credential": "DeviceFlow" } ],
              "apiEndpoint": "https://api.github.com"
            }
            """);

        Assert.Equal(GitHubCredentialKind.GhCli, Assert.Single(Store().Current.Accounts).Credential);
    }

    // --- Helpers --------------------------------------------------------------

    private string WorkspaceRoot => Path.Combine(_root, "workspace");

    private string LocalPath(string install = "install-1") => Path.Combine(_root, install, "github.json");

    private GitHubSettingsStore Store(string install = "install-1") => new(LocalPath(install), () => WorkspaceRoot);

    private static GitHubRepositoryRef Repository(string alias, string owner, string name, string? token = null) =>
        new(alias, owner, name) { Token = token };

    private static GitHubAccount Account(string login, string? token = null) =>
        token is null
            ? new GitHubAccount(login)
            : new GitHubAccount(login) { Credential = GitHubCredentialKind.PersonalAccessToken, Token = token };

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
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
