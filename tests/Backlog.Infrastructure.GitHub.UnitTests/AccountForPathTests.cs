using Backlog.Infrastructure.GitHub;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// Which identity one API path's call has to go out as.
/// <para>
/// A pure function over the configuration, so it is read as a table: no files, no
/// subprocess, no HTTP. That is deliberate — the question "whose credential does
/// this call carry" was previously answered by a lookup that could only see
/// <c>repos/</c> paths and fell through to an arbitrary token for everything else,
/// and a rule nobody could enumerate is a rule nobody could check.
/// </para>
/// <para>
/// Five path shapes, because five is what the clients actually send. Verified
/// against every <c>SendAsync</c> call site: repository paths from the issue and
/// activity clients, <c>orgs/</c> from Copilot, <c>organizations/</c> and
/// <c>users/</c> from billing, and the pathless probe.
/// </para>
/// </summary>
public sealed class AccountForPathTests
{
    // --- repos/{owner}/{name} -------------------------------------------------

    [Fact]
    public void A_bound_repository_resolves_to_the_account_it_is_bound_to()
    {
        var settings = Configured(
            [Account("j-schepers_innobv"), Account("JSdotNet")],
            Repository("spec", "innovadis-dev", "spec-manager", account: "j-schepers_innobv"),
            Repository("backlog", "JSdotNet", "Backlog", account: "JSdotNet"));

        var choice = settings.AccountForPath("repos/innovadis-dev/spec-manager/issues");

        Assert.True(choice.IsBound);
        Assert.False(choice.IsUnsatisfied);
        Assert.Equal("j-schepers_innobv", choice.Account!.Login);
        Assert.Equal("innovadis-dev/spec-manager", choice.Subject);
    }

    /// <summary>The whole point. Two owners, one of them bound: the other one's
    /// credential is never what leaves.</summary>
    [Fact]
    public void A_bound_repository_never_resolves_to_another_accounts_credential()
    {
        var settings = Configured(
            [Account("j-schepers_innobv", token: "ghp_innobv"), Account("JSdotNet", token: "ghp_jsdotnet")],
            Repository("spec", "innovadis-dev", "spec-manager", account: "j-schepers_innobv"),
            Repository("backlog", "JSdotNet", "Backlog", account: "JSdotNet"));

        Assert.Equal("ghp_innobv", settings.AccountForPath("repos/innovadis-dev/spec-manager/issues").Token);
        Assert.Equal("ghp_jsdotnet", settings.AccountForPath("repos/JSdotNet/Backlog/issues").Token);
    }

    /// <summary>The day-one state of a second install: the workspace states a
    /// binding, this machine holds no credential for it. A real state with a name,
    /// reported rather than quietly answered with somebody else's identity.</summary>
    [Fact]
    public void A_binding_this_machine_has_no_account_for_is_unsatisfied_not_default()
    {
        var settings = Configured(
            [Account("JSdotNet", token: "ghp_jsdotnet")],
            Repository("spec", "innovadis-dev", "spec-manager", account: "j-schepers_innobv"));

        var choice = settings.AccountForPath("repos/innovadis-dev/spec-manager/issues");

        Assert.True(choice.IsBound);
        Assert.True(choice.IsUnsatisfied);
        Assert.False(choice.IsDefault);
        Assert.Equal("j-schepers_innobv", choice.Login);
        Assert.Null(choice.Account);
        Assert.Null(choice.Token);
    }

    /// <summary>An account that says "paste a token" and has none pasted is bound
    /// and unsatisfiable, which is a different thing from unbound.</summary>
    [Fact]
    public void A_token_account_with_nothing_pasted_into_it_is_unsatisfied()
    {
        var settings = Configured(
            [new GitHubAccount("JSdotNet") { Credential = GitHubCredentialKind.PersonalAccessToken }],
            Repository("backlog", "JSdotNet", "Backlog", account: "JSdotNet"));

        var choice = settings.AccountForPath("repos/JSdotNet/Backlog");

        Assert.True(choice.IsUnsatisfied);
        Assert.NotNull(choice.Account);
    }

    /// <summary>A CLI-backed account holds no token and never will; whether
    /// <c>gh</c> can produce one is a subprocess away, so this says only that the
    /// binding is satisfiable and leaves the fetching to the resolver.</summary>
    [Fact]
    public void A_cli_backed_account_is_bound_with_no_token_in_hand()
    {
        var settings = Configured(
            [Account("JSdotNet")],
            Repository("backlog", "JSdotNet", "Backlog", account: "JSdotNet"));

        var choice = settings.AccountForPath("repos/JSdotNet/Backlog");

        Assert.True(choice.IsBound);
        Assert.False(choice.IsUnsatisfied);
        Assert.Null(choice.Token);
        Assert.Equal(GitHubCredentialKind.GhCli, choice.Account!.Credential);
    }

    /// <summary>Precedence rule 2, unchanged from before accounts existed: an
    /// unbound repository with a token of its own uses it.</summary>
    [Fact]
    public void An_unbound_repository_with_a_token_of_its_own_uses_it()
    {
        var settings = Configured([], Repository("backlog", "octo", "demo", token: "ghp_demo"));

        var choice = settings.AccountForPath("repos/octo/demo/issues");

        Assert.False(choice.IsBound);
        Assert.Equal("ghp_demo", choice.Token);
        Assert.Equal("octo/demo", choice.Subject);
    }

    /// <summary>The binding wins over a leftover repository token, because the
    /// binding is the newer deliberate act and the token control has always called
    /// itself a fallback.</summary>
    [Fact]
    public void A_binding_beats_a_repositorys_own_token()
    {
        var settings = Configured(
            [Account("JSdotNet", token: "ghp_account")],
            Repository("backlog", "JSdotNet", "Backlog", token: "ghp_repository", account: "JSdotNet"));

        Assert.Equal("ghp_account", settings.AccountForPath("repos/JSdotNet/Backlog").Token);
    }

    /// <summary>The unbound, untokened repository — the great majority — goes out
    /// exactly the way it does today.</summary>
    [Fact]
    public void An_unbound_repository_with_no_token_is_the_default()
    {
        var settings = Configured([], Repository("backlog", "octo", "demo"));

        Assert.True(settings.AccountForPath("repos/octo/demo/issues").IsDefault);
    }

    [Fact]
    public void A_repository_nothing_is_configured_for_is_the_default()
    {
        var settings = Configured(
            [Account("JSdotNet", token: "ghp_jsdotnet")],
            Repository("backlog", "JSdotNet", "Backlog", account: "JSdotNet"));

        Assert.True(settings.AccountForPath("repos/someone/else/issues").IsDefault);
    }

    /// <summary>GitHub matches an owner and a name without regard to case, and the
    /// clients build paths with and without a leading slash.</summary>
    [Fact]
    public void The_owner_and_name_are_matched_the_way_github_matches_them()
    {
        var settings = Configured(
            [Account("JSdotNet", token: "ghp_jsdotnet")],
            Repository("backlog", "JSdotNet", "Backlog", account: "JSdotNet"));

        Assert.Equal("ghp_jsdotnet", settings.AccountForPath("repos/jsdotnet/backlog/issues").Token);
        Assert.Equal("ghp_jsdotnet", settings.AccountForPath("/repos/JSDOTNET/BACKLOG").Token);
    }

    /// <summary>And a login is matched the same way, so a binding typed in the
    /// wrong case still finds its account.</summary>
    [Fact]
    public void A_login_is_matched_without_regard_to_case()
    {
        var settings = Configured(
            [Account("JSdotNet", token: "ghp_jsdotnet")],
            Repository("backlog", "JSdotNet", "Backlog", account: "jsdotnet"));

        Assert.Equal("ghp_jsdotnet", settings.AccountForPath("repos/JSdotNet/Backlog").Token);
    }

    // --- orgs/{org} and organizations/{org} -----------------------------------

    /// <summary>Copilot reports and the organization billing endpoints name no
    /// repository, so the answer is whatever the repositories under that owner
    /// agree on. This is the shape that used to fall past the repository lookup
    /// entirely and take the arbitrary first token in the list.</summary>
    [Theory]
    [InlineData("orgs/innovadis-dev/copilot/billing/seats")]
    [InlineData("organizations/innovadis-dev/settings/billing/usage")]
    public void An_organization_path_takes_the_binding_its_repositories_agree_on(string path)
    {
        var settings = Configured(
            [Account("j-schepers_innobv", token: "ghp_innobv"), Account("JSdotNet", token: "ghp_jsdotnet")],
            Repository("spec", "innovadis-dev", "spec-manager", account: "j-schepers_innobv"),
            Repository("tools", "innovadis-dev", "tools", account: "j-schepers_innobv"),
            Repository("backlog", "JSdotNet", "Backlog", account: "JSdotNet"));

        Assert.Equal("ghp_innobv", settings.AccountForPath(path).Token);
    }

    /// <summary>Repositories that disagree fall back to the default rather than
    /// picking one, because there is no honest way to choose: an organization
    /// report is about all of them at once.</summary>
    [Fact]
    public void An_organization_whose_repositories_disagree_falls_back_to_the_default()
    {
        var settings = Configured(
            [Account("one", token: "ghp_one"), Account("two", token: "ghp_two")],
            Repository("a", "acme", "a", account: "one"),
            Repository("b", "acme", "b", account: "two"));

        Assert.True(settings.AccountForPath("orgs/acme/copilot/billing/seats").IsDefault);
    }

    [Fact]
    public void An_organization_with_nothing_bound_under_it_is_the_default()
    {
        var settings = Configured(
            [Account("one", token: "ghp_one")],
            Repository("a", "acme", "a"),
            Repository("backlog", "JSdotNet", "Backlog", account: "one"));

        Assert.True(settings.AccountForPath("orgs/acme/copilot/billing/seats").IsDefault);
    }

    /// <summary>An organization every one of whose repositories names an account
    /// this machine lacks is unsatisfied, not default — the same rule the
    /// repository shape follows, for the same reason.</summary>
    [Fact]
    public void An_organization_bound_to_a_login_this_machine_lacks_is_unsatisfied()
    {
        var settings = Configured([], Repository("spec", "innovadis-dev", "spec-manager", account: "j-schepers_innobv"));

        var choice = settings.AccountForPath("orgs/innovadis-dev/copilot/billing/seats");

        Assert.True(choice.IsUnsatisfied);
        Assert.Equal("innovadis-dev", choice.Subject);
    }

    // --- users/{login} --------------------------------------------------------

    /// <summary>The user billing endpoints name a login outright, which is a
    /// stronger statement about whose report this is than any repository could
    /// make.</summary>
    [Fact]
    public void A_user_path_resolves_to_the_account_of_that_login()
    {
        var settings = Configured(
            [Account("JSdotNet", token: "ghp_jsdotnet"), Account("octocat", token: "ghp_octocat")]);

        var choice = settings.AccountForPath("users/octocat/settings/billing/usage");

        Assert.Equal("ghp_octocat", choice.Token);
        Assert.Equal("octocat", choice.Subject);
    }

    [Fact]
    public void A_user_path_for_a_login_this_machine_has_no_account_for_is_the_default()
    {
        var settings = Configured([Account("JSdotNet", token: "ghp_jsdotnet")]);

        Assert.True(settings.AccountForPath("users/octocat/settings/billing/usage").IsDefault);
    }

    // --- everything else ------------------------------------------------------

    /// <summary>Including the pathless probe, which is the shape that used to be
    /// the one place the cross-repository fallback was load bearing.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("user")]
    [InlineData("/")]
    [InlineData("repos/octo")]
    [InlineData("orgs")]
    [InlineData("rate_limit")]
    public void Anything_that_names_no_identity_is_the_default(string? path)
    {
        var settings = Configured(
            [Account("JSdotNet", token: "ghp_jsdotnet")],
            Repository("backlog", "JSdotNet", "Backlog", token: "ghp_repository", account: "JSdotNet"));

        var choice = settings.AccountForPath(path);

        Assert.True(choice.IsDefault);
        Assert.Null(choice.Token);
        Assert.Null(choice.Login);
    }

    /// <summary>Nothing configured at all is the default, which is the state every
    /// install starts in and the reason this change is invisible to somebody who
    /// never opens the Accounts panel.</summary>
    [Fact]
    public void An_empty_configuration_is_the_default_everywhere()
    {
        var settings = new GitHubSettings();

        Assert.True(settings.AccountForPath("repos/octo/demo/issues").IsDefault);
        Assert.True(settings.AccountForPath("orgs/acme/copilot/billing/seats").IsDefault);
        Assert.True(settings.AccountForPath("users/octocat/settings/billing/usage").IsDefault);
        Assert.False(settings.HasAnyCredential);
    }

    // --- HasAnyCredential -----------------------------------------------------
    //
    // The other half of Amendment A: "is this machine configured to reach GitHub
    // with a token at all" is a different question from "which credential
    // authenticates this path", and conflating them is what made the availability
    // probe the one caller that needed the cross-repository fallback.

    [Fact]
    public void A_repository_token_is_a_credential_this_machine_has()
    {
        Assert.True(Configured([], Repository("backlog", "octo", "demo", token: "ghp_demo")).HasAnyCredential);
        Assert.False(Configured([], Repository("backlog", "octo", "demo")).HasAnyCredential);
    }

    [Fact]
    public void An_account_token_is_a_credential_this_machine_has()
    {
        Assert.True(Configured([Account("JSdotNet", token: "ghp_jsdotnet")]).HasAnyCredential);
    }

    /// <summary>A CLI-backed account is not counted, and cannot be: this is a
    /// synchronous predicate and whether <c>gh</c> can produce a token is a
    /// subprocess away. A machine whose only credential is <c>gh</c> is answered by
    /// the CLI transport, which probes it properly.</summary>
    [Fact]
    public void A_cli_backed_account_is_not_a_token_this_machine_holds()
    {
        Assert.False(Configured([Account("JSdotNet")]).HasAnyCredential);
    }

    private static GitHubSettings Configured(
        GitHubAccount[] accounts,
        params GitHubRepositoryRef[] repositories) =>
        new() { Accounts = [.. accounts], Repositories = [.. repositories] };

    private static GitHubAccount Account(string login, string? token = null) =>
        token is null
            ? new GitHubAccount(login)
            : new GitHubAccount(login) { Credential = GitHubCredentialKind.PersonalAccessToken, Token = token };

    private static GitHubRepositoryRef Repository(
        string alias,
        string owner,
        string name,
        string? token = null,
        string? account = null) =>
        new(alias, owner, name) { Token = token, Account = account };
}
