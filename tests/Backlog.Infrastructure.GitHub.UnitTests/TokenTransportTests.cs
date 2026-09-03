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
            _ => "ghp_example",
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
        var transport = new TokenTransport(_ => "ghp_example", () => "not-a-url", new HttpClient(handler));

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
        var transport = new TokenTransport(_ => "ghp_example", http: new HttpClient(handler));

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
        var transport = new TokenTransport(_ => "ghp_example", http: new HttpClient(handler));

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
        var transport = new TokenTransport(_ => "ghp_example", http: new HttpClient(handler));

        await transport.SendAsync(HttpMethod.Get, "orgs/acme/copilot/billing/seats", apiVersion: "2026-03-10");

        Assert.Single(handler.Request!.Headers.GetValues("X-GitHub-Api-Version"));
    }

    // --- Which token a path resolves to ---------------------------------------
    //
    // GitHubSettings.TokenForPath is the delegate this transport is constructed with in
    // both hosts, so its semantics are this transport's semantics and are pinned beside
    // it rather than off in a settings test.
    //
    // Characterization: what follows states today's behaviour, including the parts of
    // it that are wrong. The "first repository with any token" fallback is the bug the
    // multi-account work exists to fix — it is why a call for one owner's repository
    // can leave carrying another owner's credential and come back a 404. Stage 2
    // removes it. Until then it is written down, so its removal arrives as an edited
    // test with a reason attached rather than as a silent change of behaviour.

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

    /// <summary>Characterization of a known defect, removed in stage 2 of the
    /// multi-account work: a repository that has no token of its own is handed the
    /// first token in the list, which belongs to somebody else.</summary>
    [Fact]
    public void A_repository_with_no_token_of_its_own_borrows_the_first_one_in_the_list()
    {
        var settings = Configured(
            Repository("tools", "acme", "tools", "ghp_tools"),
            Repository("backlog", "octo", "demo"));

        Assert.Equal("ghp_tools", settings.TokenForPath("repos/octo/demo/issues"));
    }

    /// <summary>Characterization of the same known defect on the other route: a path
    /// this lookup cannot read as a repository at all — the organization and user
    /// shapes the Copilot and billing clients send — is handed the first token too.
    /// Removed in stage 2.</summary>
    [Fact]
    public void A_path_that_names_no_configured_repository_borrows_the_first_one_too()
    {
        var settings = Configured(
            Repository("tools", "acme", "tools", "ghp_tools"),
            Repository("backlog", "octo", "demo", "ghp_demo"));

        Assert.Equal("ghp_tools", settings.TokenForPath("orgs/octo/copilot/billing/seats"));
        Assert.Equal("ghp_tools", settings.TokenForPath("users/octocat/settings/billing/usage"));
        Assert.Equal("ghp_tools", settings.TokenForPath("repos/octo"));
    }

    /// <summary>The one place the fallback is load bearing rather than a defect:
    /// <see cref="TokenTransport.IsAvailableAsync"/> asks with no path at all, meaning
    /// "is there any token anywhere". Whatever stage 2 replaces the fallback with still
    /// has to answer this question.</summary>
    [Fact]
    public async Task The_availability_probe_asks_with_no_path_and_means_any_token_anywhere()
    {
        var settings = Configured(Repository("backlog", "octo", "demo", "ghp_demo"));

        Assert.Equal("ghp_demo", settings.TokenForPath(null));
        Assert.True(await new TokenTransport(settings.TokenForPath).IsAvailableAsync());

        var none = Configured(Repository("backlog", "octo", "demo"));

        Assert.Null(none.TokenForPath(null));
        Assert.False(await new TokenTransport(none.TokenForPath).IsAvailableAsync());
    }

    [Fact]
    public void No_token_anywhere_is_no_token()
    {
        var settings = Configured(Repository("backlog", "octo", "demo"));

        Assert.Null(settings.TokenForPath("repos/octo/demo/issues"));
        Assert.Null(new GitHubSettings().TokenForPath("repos/octo/demo/issues"));
    }

    /// <summary>And the resolved token is the one that actually travels, which is what
    /// makes the fallback above a wrong-identity call rather than a curiosity.</summary>
    [Fact]
    public async Task The_token_the_path_resolved_to_is_the_one_that_is_sent()
    {
        var settings = Configured(
            Repository("tools", "acme", "tools", "ghp_tools"),
            Repository("backlog", "octo", "demo", "ghp_demo"));

        var handler = new RecordingHandler();
        var transport = new TokenTransport(settings.TokenForPath, http: new HttpClient(handler));

        await transport.SendAsync(HttpMethod.Get, "repos/octo/demo/issues");

        Assert.Equal("Bearer ghp_demo", handler.Request!.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task A_path_with_no_token_to_resolve_to_is_a_configuration_problem()
    {
        var handler = new RecordingHandler();
        var transport = new TokenTransport(new GitHubSettings().TokenForPath, http: new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<GitHubNotConfiguredException>(() =>
            transport.SendAsync(HttpMethod.Get, "repos/octo/demo/issues"));

        Assert.Equal("No GitHub token is configured.", exception.Message);
        Assert.Equal(0, handler.RequestCount);
    }

    private static GitHubSettings Configured(params GitHubRepositoryRef[] repositories) =>
        new() { Repositories = [.. repositories] };

    private static GitHubRepositoryRef Repository(string alias, string owner, string name, string? token = null) =>
        new(alias, owner, name) { Token = token };

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
