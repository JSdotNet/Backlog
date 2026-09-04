using System.Net;
using System.Text;
using Backlog.Infrastructure.GitHub;

namespace Backlog.Infrastructure.GitHub.UnitTests;

public sealed class TokenTransportTests
{
    [Fact]
    public async Task Requests_use_the_configured_endpoint_root()
    {
        var handler = new RecordingHandler();
        var transport = new TokenTransport(
            StubCredentialResolver.WithToken(),
            () => "https://ghe.example.internal/api/v3/",
            new HttpClient(handler));

        await transport.SendAsync(HttpMethod.Get, "orgs/acme/copilot/billing/seats");

        Assert.Equal(
            "https://ghe.example.internal/api/v3/orgs/acme/copilot/billing/seats",
            handler.Request!.RequestUri!.ToString());
    }

    [Fact]
    public async Task Invalid_endpoint_is_rejected_before_any_http_call()
    {
        var handler = new RecordingHandler();
        var transport = new TokenTransport(StubCredentialResolver.WithToken(), () => "not-a-url", new HttpClient(handler));

        await Assert.ThrowsAsync<GitHubNotConfiguredException>(() =>
            transport.SendAsync(HttpMethod.Get, "orgs/acme/copilot/billing/seats"));

        Assert.Equal(0, handler.RequestCount);
    }

    /// <summary>
    /// The version travels per request rather than being pinned on the client, because
    /// GitHub does not move its endpoints to a new version together: the billing usage
    /// reports only exist from 2026-03-10 while everything else this app calls is
    /// documented against 2022-11-28.
    /// </summary>
    [Fact]
    public async Task A_caller_that_asks_for_an_api_version_gets_it()
    {
        var handler = new RecordingHandler();
        var transport = new TokenTransport(StubCredentialResolver.WithToken(), http: new HttpClient(handler));

        await transport.SendAsync(
            HttpMethod.Get,
            "users/jsdotnet/settings/billing/ai_credit/usage",
            apiVersion: "2026-03-10");

        Assert.Equal(
            "2026-03-10",
            Assert.Single(handler.Request!.Headers.GetValues("X-GitHub-Api-Version")));
    }

    [Fact]
    public async Task A_caller_that_asks_for_nothing_gets_the_version_the_rest_of_the_app_uses()
    {
        var handler = new RecordingHandler();
        var transport = new TokenTransport(StubCredentialResolver.WithToken(), http: new HttpClient(handler));

        await transport.SendAsync(HttpMethod.Get, "orgs/acme/copilot/billing/seats");

        Assert.Equal(
            IGitHubTransport.DefaultApiVersion,
            Assert.Single(handler.Request!.Headers.GetValues("X-GitHub-Api-Version")));
    }

    /// <summary>
    /// One header, not two. The version used to be a default header on the client, and
    /// leaving that in place alongside the per-request one would send both values and
    /// let GitHub pick.
    /// </summary>
    [Fact]
    public async Task The_api_version_is_sent_once()
    {
        var handler = new RecordingHandler();
        var transport = new TokenTransport(StubCredentialResolver.WithToken(), http: new HttpClient(handler));

        await transport.SendAsync(HttpMethod.Get, "orgs/acme/copilot/billing/seats", apiVersion: "2026-03-10");

        Assert.Single(handler.Request!.Headers.GetValues("X-GitHub-Api-Version"));
    }

    // --- Which token a path resolves to ---------------------------------------
    //
    // GitHubSettings.AccountForPath is the lookup behind the resolver this transport
    // is constructed with, so its semantics are this transport's semantics and are
    // pinned beside it rather than off in a settings test.
    //
    // These were written as characterization pins, and three of them stated a defect
    // rather than an intent. Stage 2 of the multi-account work deleted the "first
    // repository with any token" fallback — the reason a call for one owner's
    // repository could leave carrying another owner's credential and come back a 404
    // — so those three are rewritten to state its absence. Each says which behaviour
    // it used to hold and why it changed; none was deleted.
    //
    // The full table of what every path shape resolves to now lives in
    // AccountForPathTests, which is where the five shapes are enumerated.

    [Fact]
    public void A_repository_path_gets_that_repositorys_own_token()
    {
        var settings = Configured(
            Repository("backlog", "octo", "demo", "ghp_demo"),
            Repository("tools", "acme", "tools", "ghp_tools"));

        Assert.Equal("ghp_demo", settings.TokenForPath("repos/octo/demo/issues"));
        Assert.Equal("ghp_tools", settings.TokenForPath("repos/acme/tools/pulls/7"));
    }

    /// <summary>GitHub treats an owner and a repository name without regard to case,
    /// and the path may or may not have a leading slash depending on which client built
    /// it.</summary>
    [Fact]
    public void The_owner_and_name_are_matched_the_way_github_matches_them()
    {
        var settings = Configured(Repository("backlog", "octo", "demo", "ghp_demo"));

        Assert.Equal("ghp_demo", settings.TokenForPath("repos/OCTO/Demo/issues"));
        Assert.Equal("ghp_demo", settings.TokenForPath("/repos/octo/demo"));
    }

    /// <summary>
    /// Pinned the other way round in stage 0, as a known defect: a repository with no
    /// token of its own was handed the first token in the list, which belongs to
    /// somebody else. Stage 2 deleted that fallback, so the pin is rewritten rather
    /// than removed and now states its absence.
    /// <para>
    /// Null here does not mean "no way to authenticate". It means "nothing about this
    /// repository names a credential", and the call goes out as this machine's default
    /// identity — which is what it did before any of this existed.
    /// </para>
    /// </summary>
    [Fact]
    public void A_repository_with_no_token_of_its_own_never_borrows_one()
    {
        var settings = Configured(
            Repository("tools", "acme", "tools", "ghp_tools"),
            Repository("backlog", "octo", "demo"));

        Assert.Null(settings.TokenForPath("repos/octo/demo/issues"));
        Assert.True(settings.AccountForPath("repos/octo/demo/issues").IsDefault);

        // And the repository that does have one still gets its own.
        Assert.Equal("ghp_tools", settings.TokenForPath("repos/acme/tools/pulls/7"));
    }

    /// <summary>
    /// The same rewritten pin on the other route, and the one that mattered most in
    /// practice: a path this lookup could not read as a repository at all — every
    /// organization and user shape the Copilot and billing clients send — used to be
    /// handed the first token too. That is the third manifestation of the reported
    /// 404, and it is gone.
    /// </summary>
    [Fact]
    public void A_path_that_names_no_configured_repository_never_borrows_one_either()
    {
        var settings = Configured(
            Repository("tools", "acme", "tools", "ghp_tools"),
            Repository("backlog", "octo", "demo", "ghp_demo"));

        Assert.Null(settings.TokenForPath("orgs/octo/copilot/billing/seats"));
        Assert.Null(settings.TokenForPath("users/octocat/settings/billing/usage"));
        Assert.Null(settings.TokenForPath("repos/octo"));
    }

    /// <summary>
    /// The one place the old fallback was load bearing rather than a defect: the
    /// availability probe asked with no path at all, meaning "is there any token
    /// anywhere", and the fallback was the only branch that could answer it.
    /// <para>
    /// Stage 0 pinned that outcome, reached by that route. Stage 2 keeps the outcome
    /// and replaces the route: the question is asked directly, of
    /// <see cref="GitHubSettings.HasAnyCredential"/>, so the probe stops being
    /// expressed as a null path — which is what made it look like the same question as
    /// "which credential authenticates this path" in the first place.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_availability_probe_asks_whether_there_is_any_token_anywhere()
    {
        var settings = Configured(Repository("backlog", "octo", "demo", "ghp_demo"));

        // The pathless lookup no longer answers with somebody's token, and no longer
        // has to.
        Assert.Null(settings.TokenForPath(null));
        Assert.True(settings.HasAnyCredential);
        Assert.True(await Transport(settings).IsAvailableAsync());

        var none = Configured(Repository("backlog", "octo", "demo"));

        Assert.False(none.HasAnyCredential);
        Assert.False(await Transport(none).IsAvailableAsync());

        // An account's pasted token counts too, which is the case that did not exist
        // when the fallback was written.
        var account = new GitHubSettings
        {
            Accounts = [TokenAccount("octocat", "ghp_octocat")]
        };

        Assert.True(account.HasAnyCredential);
        Assert.True(await Transport(account).IsAvailableAsync());
    }

    [Fact]
    public void No_token_anywhere_is_no_token()
    {
        var settings = Configured(Repository("backlog", "octo", "demo"));

        Assert.Null(settings.TokenForPath("repos/octo/demo/issues"));
        Assert.Null(new GitHubSettings().TokenForPath("repos/octo/demo/issues"));
    }

    /// <summary>And the resolved token is the one that actually travels, which is what
    /// made the old fallback a wrong-identity call rather than a curiosity.</summary>
    [Fact]
    public async Task The_token_the_path_resolved_to_is_the_one_that_is_sent()
    {
        var settings = Configured(
            Repository("tools", "acme", "tools", "ghp_tools"),
            Repository("backlog", "octo", "demo", "ghp_demo"));

        var handler = new RecordingHandler();
        await Transport(settings, handler).SendAsync(HttpMethod.Get, "repos/octo/demo/issues");

        Assert.Equal("Bearer ghp_demo", handler.Request!.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task A_path_with_no_token_to_resolve_to_is_a_configuration_problem()
    {
        var handler = new RecordingHandler();
        var transport = Transport(new GitHubSettings(), handler);

        var exception = await Assert.ThrowsAsync<GitHubNotConfiguredException>(() =>
            transport.SendAsync(HttpMethod.Get, "repos/octo/demo/issues"));

        Assert.Equal("No GitHub token is configured.", exception.Message);
        Assert.Equal(0, handler.RequestCount);
    }

    // --- A binding, and what it refuses to do ---------------------------------

    /// <summary>
    /// The rule the whole change turns on: a repository bound to an account this
    /// machine cannot satisfy fails naming it, and sends nothing.
    /// <para>
    /// Falling through to another identity is what produced the reported 404, and
    /// "this machine has no credential for that account" is a sentence somebody can
    /// act on where "GitHub couldn't find that repository" is not.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_bound_repository_never_borrows_another_accounts_credential()
    {
        var settings = new GitHubSettings
        {
            Accounts = [TokenAccount("JSdotNet", "ghp_jsdotnet")],
            Repositories =
            [
                new GitHubRepositoryRef("spec", "innovadis-dev", "spec-manager") { Account = "j-schepers_innobv" },
                new GitHubRepositoryRef("backlog", "JSdotNet", "Backlog")
                {
                    Token = "ghp_repository",
                    Account = "JSdotNet"
                }
            ]
        };

        var handler = new RecordingHandler();
        var transport = Transport(settings, handler);

        var exception = await Assert.ThrowsAsync<GitHubNotConfiguredException>(() =>
            transport.SendAsync(HttpMethod.Get, "repos/innovadis-dev/spec-manager/issues"));

        Assert.Equal(
            "innovadis-dev/spec-manager is worked as 'j-schepers_innobv', "
            + "and this machine has no credential for 'j-schepers_innobv'.",
            exception.Message);

        // Nothing left the machine at all — not as the wrong identity, not otherwise.
        Assert.Equal(0, handler.RequestCount);

        // And the repository bound to an account this machine does hold works: the
        // account's token, not the one left lying on the repository.
        await transport.SendAsync(HttpMethod.Get, "repos/JSdotNet/Backlog/issues");
        Assert.Equal("Bearer ghp_jsdotnet", handler.Request!.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task A_bound_account_with_nothing_pasted_into_it_says_which_account()
    {
        var settings = new GitHubSettings
        {
            Accounts = [new GitHubAccount("JSdotNet") { Credential = GitHubCredentialKind.PersonalAccessToken }],
            Repositories = [new GitHubRepositoryRef("backlog", "JSdotNet", "Backlog") { Account = "JSdotNet" }]
        };

        var exception = await Assert.ThrowsAsync<GitHubNotConfiguredException>(() =>
            Transport(settings).SendAsync(HttpMethod.Get, "repos/JSdotNet/Backlog"));

        Assert.Equal(
            "JSdotNet/Backlog is worked as 'JSdotNet', "
            + "and no personal access token has been pasted for 'JSdotNet'.",
            exception.Message);
    }

    /// <summary>An account may name its own API endpoint, which is how a login on a
    /// GitHub Enterprise Server host reaches its own API without the whole install
    /// moving there.</summary>
    [Fact]
    public async Task An_accounts_own_api_endpoint_wins_over_the_install_wide_one()
    {
        var settings = new GitHubSettings
        {
            Accounts =
            [
                TokenAccount("enterprise", "ghp_enterprise") with { ApiEndpoint = "https://ghe.example.internal/api/v3" }
            ],
            Repositories = [new GitHubRepositoryRef("tools", "acme", "tools") { Account = "enterprise" }]
        };

        var handler = new RecordingHandler();
        await Transport(settings, handler).SendAsync(HttpMethod.Get, "repos/acme/tools/issues");

        Assert.Equal(
            "https://ghe.example.internal/api/v3/repos/acme/tools/issues",
            handler.Request!.RequestUri!.ToString());
    }

    /// <summary>A CLI-backed account holds no token of its own, so the credential is
    /// fetched from <c>gh</c> when a call needs one — and the token that comes back is
    /// the one that travels.</summary>
    [Fact]
    public async Task A_cli_backed_account_sends_the_token_the_cli_hands_over()
    {
        var settings = new GitHubSettings
        {
            Accounts = [new GitHubAccount("j-schepers_innobv")],
            Repositories = [new GitHubRepositoryRef("spec", "innovadis-dev", "spec-manager") { Account = "j-schepers_innobv" }]
        };

        var accounts = new StubGhCliAccountSource { Tokens = { ["j-schepers_innobv"] = "gho_innobv" } };

        var handler = new RecordingHandler();
        var transport = new TokenTransport(
            new GitHubCredentialResolver(() => settings, accounts),
            () => GitHubSettings.DefaultApiEndpoint,
            new HttpClient(handler));

        await transport.SendAsync(HttpMethod.Get, "repos/innovadis-dev/spec-manager/issues");

        Assert.Equal("Bearer gho_innobv", handler.Request!.Headers.Authorization!.ToString());
        Assert.Equal(["j-schepers_innobv"], accounts.Asked);
    }

    /// <summary>And a CLI that cannot produce one fails naming the account rather than
    /// falling back to whoever <c>gh</c> is switched to.</summary>
    [Fact]
    public async Task A_cli_backed_account_the_cli_has_no_token_for_says_so()
    {
        var settings = new GitHubSettings
        {
            Accounts = [new GitHubAccount("j-schepers_innobv")],
            Repositories = [new GitHubRepositoryRef("spec", "innovadis-dev", "spec-manager") { Account = "j-schepers_innobv" }]
        };

        var handler = new RecordingHandler();
        var transport = new TokenTransport(
            new GitHubCredentialResolver(() => settings, new StubGhCliAccountSource()),
            () => GitHubSettings.DefaultApiEndpoint,
            new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<GitHubNotConfiguredException>(() =>
            transport.SendAsync(HttpMethod.Get, "repos/innovadis-dev/spec-manager/issues"));

        Assert.Equal(
            "innovadis-dev/spec-manager is worked as 'j-schepers_innobv', "
            + "and the GitHub CLI has no token for 'j-schepers_innobv'.",
            exception.Message);
        Assert.Equal(0, handler.RequestCount);
    }

    // --- Helpers --------------------------------------------------------------

    /// <summary>A transport over a fixed configuration, resolving through the real
    /// resolver — so what is being tested is the whole path from a settings value to
    /// the header that leaves.</summary>
    private static TokenTransport Transport(GitHubSettings settings, RecordingHandler? handler = null) =>
        new(new GitHubCredentialResolver(() => settings, new StubGhCliAccountSource()),
            () => GitHubSettings.DefaultApiEndpoint,
            new HttpClient(handler ?? new RecordingHandler()));

    private static GitHubSettings Configured(params GitHubRepositoryRef[] repositories) =>
        new() { Repositories = [.. repositories] };

    private static GitHubRepositoryRef Repository(string alias, string owner, string name, string? token = null) =>
        new(alias, owner, name) { Token = token };

    private static GitHubAccount TokenAccount(string login, string token) =>
        new(login) { Credential = GitHubCredentialKind.PersonalAccessToken, Token = token };

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            Request = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        }
    }
}
