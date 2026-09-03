using Backlog.Infrastructure.GitHub;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// Which credential one call leaves with, and what happens when there isn't one.
/// <para>
/// The seam the whole fix turns on. It answers the question every real request
/// asks — "which credential authenticates <em>this</em> path" — with no
/// cross-repository fallback in it at all, and it answers separately the question
/// the availability probe asks, which is "is there any token anywhere". Those were
/// one question and one answer, and the answer was the defect.
/// </para>
/// </summary>
public sealed class GitHubCredentialResolverTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "github-credential-resolver-tests-" + Guid.NewGuid().ToString("N"));

    // --- The default path -----------------------------------------------------

    /// <summary>Null means "this call goes out as this machine's default identity",
    /// which is what every call did before any of this existed. It is not an
    /// error.</summary>
    [Fact]
    public async Task An_unbound_repository_with_no_token_resolves_to_nothing()
    {
        var resolver = Resolver(new GitHubSettings
        {
            Repositories = [new GitHubRepositoryRef("backlog", "octo", "demo")]
        });

        Assert.Null(await resolver.ResolveAsync("repos/octo/demo/issues"));
        Assert.Null(await resolver.ResolveAsync(null));
        Assert.Null(await resolver.ResolveAsync("user"));
    }

    /// <summary>An unbound repository's own token is a credential, and it is
    /// deliberately not bound — so the resolving transport keeps it underneath the
    /// CLI, exactly where the token control in Settings says it is.</summary>
    [Fact]
    public async Task An_unbound_repositorys_own_token_resolves_unbound()
    {
        var resolver = Resolver(new GitHubSettings
        {
            Repositories = [new GitHubRepositoryRef("backlog", "octo", "demo") { Token = "ghp_demo" }]
        });

        var credential = await resolver.ResolveAsync("repos/octo/demo/issues");

        Assert.NotNull(credential);
        Assert.Equal("ghp_demo", credential.Token);
        Assert.Null(credential.Account);
        Assert.False(credential.IsBound);
    }

    // --- A binding ------------------------------------------------------------

    [Fact]
    public async Task A_bound_account_with_a_pasted_token_resolves_bound()
    {
        var resolver = Resolver(Bound("JSdotNet", token: "ghp_jsdotnet"));

        var credential = await resolver.ResolveAsync("repos/JSdotNet/Backlog/issues");

        Assert.NotNull(credential);
        Assert.Equal("ghp_jsdotnet", credential.Token);
        Assert.Equal("JSdotNet", credential.Account);
        Assert.True(credential.IsBound);
    }

    /// <summary>A CLI-backed account holds no token, so one is fetched when a call
    /// needs it — by login, which is the thing <c>gh api</c> cannot be told.</summary>
    [Fact]
    public async Task A_bound_cli_account_fetches_its_token_from_the_cli_by_login()
    {
        var accounts = new StubGhCliAccountSource { Tokens = { ["j-schepers_innobv"] = "gho_innobv" } };
        var settings = new GitHubSettings
        {
            Accounts = [new GitHubAccount("j-schepers_innobv") { Host = "github.com" }],
            Repositories = [new GitHubRepositoryRef("spec", "innovadis-dev", "spec-manager") { Account = "j-schepers_innobv" }]
        };

        var credential = await new GitHubCredentialResolver(() => settings, accounts)
            .ResolveAsync("repos/innovadis-dev/spec-manager/issues");

        Assert.Equal("gho_innobv", credential!.Token);
        Assert.Equal("j-schepers_innobv", credential.Account);
        Assert.Equal(["j-schepers_innobv"], accounts.Asked);
    }

    /// <summary>
    /// The rule that has to hold or none of the rest matters. Never fall through to
    /// another identity: falling through is the 404.
    /// </summary>
    [Theory]
    [InlineData("repos/innovadis-dev/spec-manager/issues")]
    [InlineData("orgs/innovadis-dev/copilot/billing/seats")]
    [InlineData("organizations/innovadis-dev/settings/billing/usage")]
    public async Task A_binding_this_machine_cannot_satisfy_fails_naming_it(string path)
    {
        var resolver = Resolver(new GitHubSettings
        {
            // A perfectly good credential for somebody else, which must not be
            // reached for.
            Accounts = [new GitHubAccount("JSdotNet") { Credential = GitHubCredentialKind.PersonalAccessToken, Token = "ghp_jsdotnet" }],
            Repositories = [new GitHubRepositoryRef("spec", "innovadis-dev", "spec-manager") { Account = "j-schepers_innobv" }]
        });

        var exception = await Assert.ThrowsAsync<GitHubNotConfiguredException>(() => resolver.ResolveAsync(path));

        Assert.Contains("j-schepers_innobv", exception.Message, StringComparison.Ordinal);
        Assert.Contains("this machine has no credential", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("ghp_jsdotnet", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>The message names the thing being reached for as well as the account,
    /// because "which repository" and "as whom" are both needed to act on it.</summary>
    [Fact]
    public async Task The_refusal_names_the_repository_and_the_account()
    {
        var resolver = Resolver(new GitHubSettings
        {
            Repositories = [new GitHubRepositoryRef("spec", "innovadis-dev", "spec-manager") { Account = "j-schepers_innobv" }]
        });

        var exception = await Assert.ThrowsAsync<GitHubNotConfiguredException>(() =>
            resolver.ResolveAsync("repos/innovadis-dev/spec-manager/issues"));

        Assert.Equal(
            "innovadis-dev/spec-manager is worked as 'j-schepers_innobv', "
            + "and this machine has no credential for 'j-schepers_innobv'.",
            exception.Message);
    }

    [Fact]
    public async Task A_bound_cli_account_the_cli_has_no_token_for_fails_naming_it()
    {
        var settings = new GitHubSettings
        {
            Accounts = [new GitHubAccount("j-schepers_innobv")],
            Repositories = [new GitHubRepositoryRef("spec", "innovadis-dev", "spec-manager") { Account = "j-schepers_innobv" }]
        };

        var resolver = new GitHubCredentialResolver(() => settings, new StubGhCliAccountSource());

        var exception = await Assert.ThrowsAsync<GitHubNotConfiguredException>(() =>
            resolver.ResolveAsync("repos/innovadis-dev/spec-manager/issues"));

        Assert.Equal(
            "innovadis-dev/spec-manager is worked as 'j-schepers_innobv', "
            + "and the GitHub CLI has no token for 'j-schepers_innobv'.",
            exception.Message);
    }

    /// <summary>A binding beats the token left lying on the repository, because the
    /// binding is the newer deliberate act — and it fails rather than quietly using
    /// that token, which would be a call going out as an identity nobody
    /// chose.</summary>
    [Fact]
    public async Task An_unsatisfiable_binding_does_not_fall_back_to_the_repositorys_own_token()
    {
        var resolver = Resolver(new GitHubSettings
        {
            Repositories =
            [
                new GitHubRepositoryRef("spec", "innovadis-dev", "spec-manager")
                {
                    Token = "ghp_left_over",
                    Account = "j-schepers_innobv"
                }
            ]
        });

        await Assert.ThrowsAsync<GitHubNotConfiguredException>(() =>
            resolver.ResolveAsync("repos/innovadis-dev/spec-manager/issues"));
    }

    // --- The availability predicate -------------------------------------------

    [Fact]
    public void The_availability_predicate_is_about_the_machine_not_about_a_path()
    {
        Assert.False(Resolver(new GitHubSettings()).HasAnyCredential);

        Assert.True(Resolver(new GitHubSettings
        {
            Repositories = [new GitHubRepositoryRef("backlog", "octo", "demo") { Token = "ghp_demo" }]
        }).HasAnyCredential);

        Assert.True(Resolver(Bound("JSdotNet", token: "ghp_jsdotnet")).HasAnyCredential);
    }

    // --- Reading the settings per call ----------------------------------------

    /// <summary>
    /// The fix for the live defect this type replaces. The token lookup used to be a
    /// method group bound to the settings snapshot that existed at construction, so a
    /// token configured afterwards — or a workspace move, which is what
    /// <c>Reload()</c> is wired to — was invisible to it.
    /// </summary>
    [Fact]
    public async Task The_settings_are_read_per_call()
    {
        var store = new GitHubSettingsStore(Path.Combine(_root, "github.json"));
        var resolver = new GitHubCredentialResolver(store, new StubGhCliAccountSource());

        Assert.False(resolver.HasAnyCredential);
        Assert.Null(await resolver.ResolveAsync("repos/octo/demo/issues"));

        Assert.Null(store.SetRepositories([new GitHubRepositoryRef("backlog", "octo", "demo")]));
        Assert.Null(store.SetRepositoryToken("backlog", "ghp_added_after_construction"));

        Assert.True(resolver.HasAnyCredential);
        Assert.Equal(
            "ghp_added_after_construction",
            (await resolver.ResolveAsync("repos/octo/demo/issues"))!.Token);
    }

    // --- The hard rule --------------------------------------------------------

    /// <summary>
    /// The mirror of <c>A_token_never_reaches_the_shared_registry</c>, for the
    /// credential that is not pasted but fetched.
    /// <para>
    /// A <c>gho_</c> token is an OAuth token <c>gh</c> refreshes and rotates. One
    /// written into the settings file would be a stale secret in a file — a
    /// correctness regression and a security one — so it is held in memory for the
    /// length of one short cache window and never written down.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_gh_sourced_token_never_reaches_the_settings_file()
    {
        var store = new GitHubSettingsStore(Path.Combine(_root, "github.json"));

        Assert.Null(store.SetAccounts([new GitHubAccount("j-schepers_innobv")]));
        Assert.Null(store.SetRepositories([new GitHubRepositoryRef("spec", "innovadis-dev", "spec-manager")]));
        Assert.Null(store.SetRepositoryAccount("spec", "j-schepers_innobv"));

        var accounts = new StubGhCliAccountSource { Tokens = { ["j-schepers_innobv"] = "gho_never_written_down" } };
        var resolver = new GitHubCredentialResolver(store, accounts);

        // The call resolves, so the token really was in hand.
        Assert.Equal(
            "gho_never_written_down",
            (await resolver.ResolveAsync("repos/innovadis-dev/spec-manager/issues"))!.Token);

        // And a write after it does not put it anywhere.
        Assert.Null(store.SetShowRepositoryColours(true));

        Assert.DoesNotContain("gho_never_written_down", File.ReadAllText(store.SettingsPath), StringComparison.Ordinal);
        Assert.DoesNotContain("gho_never_written_down", File.ReadAllText(store.RegistryPath), StringComparison.Ordinal);
        Assert.Null(store.Current.Account("j-schepers_innobv")!.Token);
    }

    private static GitHubCredentialResolver Resolver(GitHubSettings settings) =>
        new(() => settings, new StubGhCliAccountSource());

    private static GitHubSettings Bound(string login, string token) => new()
    {
        Accounts = [new GitHubAccount(login) { Credential = GitHubCredentialKind.PersonalAccessToken, Token = token }],
        Repositories = [new GitHubRepositoryRef("backlog", login, "Backlog") { Account = login }]
    };

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
