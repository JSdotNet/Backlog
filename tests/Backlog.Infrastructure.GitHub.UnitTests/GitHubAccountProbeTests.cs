using System.Net;
using System.Text;

using Backlog.Infrastructure.GitHub;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// The "Test this account" button on a GitHub account card: the card's own
/// credential is resolved the way a bound call resolves it, sent to <c>GET user</c>,
/// and the login GitHub answers with is held against the login on the card. That is
/// the one check the card cannot do by looking at itself - a token is a token
/// until GitHub says whose it is.
/// </summary>
public sealed class GitHubAccountProbeTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "github-account-probe-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task A_token_that_authenticates_as_the_card_passes_and_says_so()
    {
        using var gh = new GhStub().Fails();
        var handler = new AnsweringHandler("""{"login":"octocat"}""");
        var transport = new ResolvingGitHubTransport(Store(), gh.Transport(), BoundToken(handler, "octocat"));

        var check = await transport.CheckAccountAsync(
            new GitHubAccount("octocat") { Credential = GitHubCredentialKind.PersonalAccessToken, Token = "ghp_bound" },
            TestContext.Current.CancellationToken);

        Assert.True(check.Passed);
        Assert.Equal("GitHub recognises the token as octocat at https://api.github.com.", check.Summary);
        Assert.Equal("Bearer ghp_bound", handler.Request!.Headers.Authorization!.ToString());
        Assert.EndsWith("/user", handler.Request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
    }

    /// <summary>The one wrong answer that would otherwise pass every other check: a
    /// valid token that belongs to somebody else.</summary>
    [Fact]
    public async Task A_token_for_a_different_login_fails_naming_both()
    {
        using var gh = new GhStub().Fails();
        var handler = new AnsweringHandler("""{"login":"hubot"}""");
        var transport = new ResolvingGitHubTransport(Store(), gh.Transport(), BoundToken(handler, "octocat"));

        var check = await transport.CheckAccountAsync(
            new GitHubAccount("octocat") { Credential = GitHubCredentialKind.PersonalAccessToken, Token = "ghp_bound" },
            TestContext.Current.CancellationToken);

        Assert.False(check.Passed);
        Assert.Equal("The credential for octocat authenticates as hubot, not octocat. Calls for octocat would leave as the wrong person.", check.Summary);
    }

    [Fact]
    public async Task A_rejected_token_fails_with_githubs_reason()
    {
        using var gh = new GhStub().Fails();
        var handler = new AnsweringHandler("""{"message":"Bad credentials"}""", HttpStatusCode.Unauthorized);
        var transport = new ResolvingGitHubTransport(Store(), gh.Transport(), BoundToken(handler, "octocat"));

        var check = await transport.CheckAccountAsync(
            new GitHubAccount("octocat") { Credential = GitHubCredentialKind.PersonalAccessToken, Token = "ghp_bound" },
            TestContext.Current.CancellationToken);

        Assert.False(check.Passed);
        Assert.Contains("rejected", check.Summary, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A binding this machine cannot satisfy is the resolver's sentence, not a
    /// network failure: nothing was sent.</summary>
    [Fact]
    public async Task An_account_with_no_credential_on_this_machine_fails_before_any_call()
    {
        using var gh = new GhStub().Fails();
        var handler = new AnsweringHandler("{}");
        var store = Store();
        Assert.Null(store.SetAccounts([new GitHubAccount("octocat") { Credential = GitHubCredentialKind.PersonalAccessToken }]));
        var transport = new ResolvingGitHubTransport(store, gh.Transport(), token: null, accounts: new StubGhCliAccountSource());

        var check = await transport.CheckAccountAsync(store.Current.Accounts[0], TestContext.Current.CancellationToken);

        Assert.False(check.Passed);
        Assert.Equal(0, handler.RequestCount);
        Assert.Contains("octocat", check.Summary, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private GitHubSettingsStore Store() => new(Path.Combine(_root, "github.json"));

    private static TokenTransport BoundToken(AnsweringHandler handler, string account) =>
        new(StubCredentialResolver.Bound(account), () => GitHubSettings.DefaultApiEndpoint, new HttpClient(handler));

    private sealed class AnsweringHandler(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            Request = request;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
