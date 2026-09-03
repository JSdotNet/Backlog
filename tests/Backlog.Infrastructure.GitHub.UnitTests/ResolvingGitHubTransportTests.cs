using System.Net;
using System.Text;

using Backlog.Infrastructure.GitHub;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// Which way a call actually leaves the machine, and the sentence Settings shows about
/// it.
/// <para>
/// Characterization: this states today's behaviour so the multi-account work has
/// something to change deliberately. The order is the whole subject — the CLI first
/// because the common case then needs no secret in this app at all, the token only when
/// the CLI cannot answer — and it is the order that makes every call leave as one
/// process-wide identity, which is the defect the later stages fix. Pinning it is what
/// proves a stage changed nothing it did not mean to.
/// </para>
/// <para>
/// Both collaborators are optional constructor parameters, but they are concrete sealed
/// types, so the CLI half is driven through the stand-in executable here too. See
/// <see cref="GhStub"/>.
/// </para>
/// </summary>
public sealed class ResolvingGitHubTransportTests : IDisposable
{
    private const string NotConnected =
        "Not connected. Sign in with `gh auth login`, or paste a personal access token in repository settings.";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "resolving-github-transport-tests-" + Guid.NewGuid().ToString("N"));

    // --- Which transport answers ----------------------------------------------

    /// <summary>The CLI comes first on purpose: it means the common case needs no
    /// secret in this app at all.</summary>
    [Fact]
    public async Task The_cli_is_preferred_over_a_configured_token()
    {
        using var gh = new GhStub().Answers("""{"login":"octocat"}""");
        var handler = new RecordingHandler();
        var transport = new ResolvingGitHubTransport(Store(), gh.Transport(), Token(handler));

        await transport.SendAsync(HttpMethod.Get, "repos/octo/demo");

        // The availability probe, then the call itself — and nothing over HTTP.
        Assert.Equal(2, gh.Calls.Count);
        Assert.Equal("repos/octo/demo", gh.Calls[1][3]);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task The_token_answers_when_the_cli_cannot()
    {
        using var gh = new GhStub().Fails();
        var handler = new RecordingHandler();
        var transport = new ResolvingGitHubTransport(Store(), gh.Transport(), Token(handler));

        await transport.SendAsync(HttpMethod.Get, "repos/octo/demo");

        Assert.Equal(1, handler.RequestCount);
        Assert.Equal("Bearer ghp_example", handler.Request!.Headers.Authorization!.ToString());
    }

    /// <summary>A settings problem rather than a failure, so it carries the two things
    /// that would fix it.</summary>
    [Fact]
    public async Task Neither_of_them_is_a_configuration_problem_stated_as_one()
    {
        using var gh = new GhStub().Fails();
        var transport = new ResolvingGitHubTransport(Store(), gh.Transport(), NoToken());

        var exception = await Assert.ThrowsAsync<GitHubNotConfiguredException>(() =>
            transport.SendAsync(HttpMethod.Get, "repos/octo/demo"));

        Assert.Equal(
            "No way to reach GitHub. Sign in with `gh auth login`, or add a personal access token in repository settings.",
            exception.Message);
    }

    [Fact]
    public async Task Availability_is_true_when_either_one_can_answer_and_false_when_neither_can()
    {
        using var cli = new GhStub().Answers("""{"login":"octocat"}""");
        using var noCli = new GhStub().Fails();

        Assert.True(await new ResolvingGitHubTransport(Store(), cli.Transport(), NoToken()).IsAvailableAsync());
        Assert.True(await new ResolvingGitHubTransport(Store(), noCli.Transport(), Token(new RecordingHandler())).IsAvailableAsync());
        Assert.False(await new ResolvingGitHubTransport(Store(), noCli.Transport(), NoToken()).IsAvailableAsync());
    }

    // --- The sentence Settings shows ------------------------------------------

    [Fact]
    public async Task A_cli_connection_is_described_with_the_account_it_is_signed_in_as()
    {
        using var gh = new GhStub().Answers("""{"login":"octocat"}""");

        var connection = await new ResolvingGitHubTransport(Store(), gh.Transport(), NoToken()).DescribeAsync();

        Assert.True(connection.IsConnected);
        Assert.Equal("Connected through the GitHub CLI as octocat.", connection.Summary);
        Assert.Equal("octocat", connection.Account);
    }

    [Fact]
    public async Task A_cli_connection_with_no_login_to_report_is_described_without_one()
    {
        using var gh = new GhStub().Answers("{}");

        var connection = await new ResolvingGitHubTransport(Store(), gh.Transport(), NoToken()).DescribeAsync();

        Assert.True(connection.IsConnected);
        Assert.Equal("Connected through the GitHub CLI.", connection.Summary);
        Assert.Null(connection.Account);
    }

    /// <summary>No account on the token route: nothing has asked GitHub who the token
    /// belongs to.</summary>
    [Fact]
    public async Task A_token_connection_is_described_as_a_repository_token_and_names_nobody()
    {
        using var gh = new GhStub().Fails();

        var connection = await new ResolvingGitHubTransport(Store(), gh.Transport(), Token(new RecordingHandler()))
            .DescribeAsync();

        Assert.True(connection.IsConnected);
        Assert.Equal("Connected with a repository personal access token.", connection.Summary);
        Assert.Null(connection.Account);
    }

    [Fact]
    public async Task Nothing_to_reach_github_with_is_described_as_what_to_do_about_it()
    {
        using var gh = new GhStub().Fails();

        var connection = await new ResolvingGitHubTransport(Store(), gh.Transport(), NoToken()).DescribeAsync();

        Assert.False(connection.IsConnected);
        Assert.Equal(NotConnected, connection.Summary);
        Assert.Null(connection.Account);
    }

    /// <summary>Settings' "Check the connection" button is the only way a
    /// <c>gh auth login</c> in another window is ever noticed, so the invalidation has
    /// to reach the CLI's remembered answer.</summary>
    [Fact]
    public async Task Invalidate_reaches_the_cli()
    {
        using var gh = new GhStub().Answers("""{"login":"octocat"}""");
        var transport = new ResolvingGitHubTransport(Store(), gh.Transport(), NoToken());

        Assert.Equal("Connected through the GitHub CLI as octocat.", (await transport.DescribeAsync()).Summary);

        // Signed out underneath it. Without invalidating, the remembered answer stands.
        gh.Fails();
        Assert.Equal("Connected through the GitHub CLI as octocat.", (await transport.DescribeAsync()).Summary);

        transport.Invalidate();
        Assert.Equal(NotConnected, (await transport.DescribeAsync()).Summary);
    }

    [Fact]
    public void The_transport_describes_itself_as_either_route()
    {
        using var gh = new GhStub();

        Assert.Equal(
            "GitHub CLI, or a personal access token",
            new ResolvingGitHubTransport(Store(), gh.Transport(), NoToken()).Description);
    }

    // --- Known defect, pinned as it stands ------------------------------------

    /// <summary>
    /// Characterization of a known defect, not of intended behaviour.
    /// <para>
    /// The token lookup is a method group bound to the <c>GitHubSettings</c> instance
    /// that existed when the transport was constructed
    /// (<c>ResolvingGitHubTransport.cs:36</c>), while the API endpoint beside it is a
    /// lambda that reads <c>Current</c> per call. Every mutator replaces
    /// <c>Current</c> with a new instance, so a token configured after construction —
    /// or a workspace move, which is what <c>Reload()</c> is wired to — is invisible to
    /// the token half and visible to the endpoint half.
    /// </para>
    /// <para>
    /// Stage 2 of the multi-account work replaces both delegates with a resolver that
    /// reads the settings per call, at which point this test is expected to fail and
    /// must be rewritten to assert the opposite, with that stage named as the reason.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_token_configured_after_construction_is_invisible_to_the_transport()
    {
        var store = Store();
        Assert.Null(store.SetRepositories([new GitHubRepositoryRef("backlog", "octo", "demo")]));

        using var gh = new GhStub().Fails();
        var transport = new ResolvingGitHubTransport(store, gh.Transport());

        Assert.Null(store.SetRepositoryToken("backlog", "ghp_added_after_construction"));

        Assert.False(await transport.IsAvailableAsync());

        // The settings themselves are fine — a transport built now sees the token. It
        // is the binding that is stale, not the store.
        Assert.True(await new ResolvingGitHubTransport(store, gh.Transport()).IsAvailableAsync());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private GitHubSettingsStore Store() => new(Path.Combine(_root, "github.json"));

    private static TokenTransport Token(RecordingHandler handler) =>
        new(_ => "ghp_example", () => GitHubSettings.DefaultApiEndpoint, new HttpClient(handler));

    private static TokenTransport NoToken() =>
        new(_ => null, () => GitHubSettings.DefaultApiEndpoint, new HttpClient(new RecordingHandler()));

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
