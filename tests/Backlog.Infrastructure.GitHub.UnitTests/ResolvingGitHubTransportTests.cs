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

    /// <summary>
    /// The CLI's identity comes first on purpose: it means the common case needs no
    /// secret stored in this app at all.
    /// <para>
    /// Rewritten deliberately. This pinned "two <c>gh</c> calls, nothing over HTTP"
    /// while every unbound call started its own <c>gh api</c> process; it now pins
    /// the meaning that survives the move — the CLI identity wins over a repository's
    /// own token — with the call carried over HTTP on the token the CLI hands over.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_cli_is_preferred_over_a_configured_token()
    {
        using var gh = new GhStub().Answers("""{"login":"octocat"}""");
        var handler = new RecordingHandler();
        var transport = Resolving(Store(), gh.Transport(), Token(handler), accounts: CliToken("octocat", "gho_cli"));

        await transport.SendAsync(HttpMethod.Get, "repos/octo/demo", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, handler.RequestCount);
        Assert.Equal("Bearer gho_cli", handler.Request!.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task An_unbound_call_with_the_cli_signed_in_goes_out_over_http_and_starts_no_gh_api_for_its_path()
    {
        using var gh = new GhStub().Answers("""{"login":"octocat"}""");
        var handler = new RecordingHandler();
        var transport = Resolving(Store(), gh.Transport(), NoToken(handler), accounts: CliToken("octocat", "gho_cli"));

        await transport.SendAsync(HttpMethod.Get, "repos/octo/demo", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, handler.RequestCount);
        Assert.Equal("https://api.github.com/repos/octo/demo", handler.Request!.RequestUri!.ToString());
        Assert.Equal("Bearer gho_cli", handler.Request.Headers.Authorization!.ToString());
        Assert.DoesNotContain(gh.Calls, call => call.Contains("repos/octo/demo"));
    }

    /// <summary>A screen full of calls costs one probe and one token lookup, not a
    /// process per call: the token is reused for its lifetime.</summary>
    [Fact]
    public async Task Repeated_unbound_calls_start_one_cli_probe_and_one_token_lookup()
    {
        using var gh = new GhStub().Answers("""{"login":"octocat"}""").AnswersTo("auth", "gho_cli");
        var handler = new RecordingHandler();
        var transport = Resolving(Store(), gh.Transport(), NoToken(handler), accounts: gh.Source());

        for (var i = 0; i < 5; i++)
        {
            await transport.SendAsync(HttpMethod.Get, "repos/octo/demo", cancellationToken: TestContext.Current.CancellationToken);
        }

        Assert.Equal(5, handler.RequestCount);
        Assert.Collection(
            gh.Calls,
            call => Assert.Equal(["api", "user"], call),
            call => Assert.Equal(["auth", "token", "--user", "octocat", "--hostname", "github.com"], call));
    }

    /// <summary>
    /// The token is asked for on github.com by name, not on whatever host gh calls its
    /// default. A gh signed in only to an Enterprise host answers <c>gh api user</c>
    /// from there, and holds no github.com token for that login — so nothing is
    /// borrowed and the call stays with <c>gh api</c>, rather than an Enterprise token
    /// going to api.github.com.
    /// </summary>
    [Fact]
    public async Task A_cli_with_no_github_com_token_for_its_login_keeps_the_call_on_gh_api()
    {
        using var gh = new GhStub().Answers("""{"login":"octocat"}""").FailsTo("auth");
        var handler = new RecordingHandler();
        var transport = Resolving(Store(), gh.Transport(), NoToken(handler), accounts: gh.Source());

        await transport.SendAsync(HttpMethod.Get, "repos/octo/demo", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, handler.RequestCount);
        Assert.Contains(gh.Calls, call => call.SequenceEqual(["auth", "token", "--user", "octocat", "--hostname", "github.com"]));
        Assert.Contains(gh.Calls, call => call.Contains("repos/octo/demo"));
    }

    /// <summary>Built without an account source, the transport asks the same
    /// executable it probes for the token — never the real <c>gh</c> on the
    /// machine running the tests.</summary>
    [Fact]
    public async Task The_default_account_source_asks_the_same_cli_the_transport_probes()
    {
        using var gh = new GhStub().Answers("""{"login":"octocat"}""").AnswersTo("auth", "gho_from_the_stub");
        var handler = new RecordingHandler();
        var transport = Resolving(Store(), gh.Transport(), NoToken(handler));

        await transport.SendAsync(HttpMethod.Get, "repos/octo/demo", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("Bearer gho_from_the_stub", handler.Request!.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task Without_a_token_from_the_cli_an_unbound_call_goes_through_gh_api_as_before()
    {
        using var gh = new GhStub().Answers("""{"login":"octocat"}""");
        var handler = new RecordingHandler();
        var transport = Resolving(Store(), gh.Transport(), Token(handler), accounts: new StubGhCliAccountSource());

        await transport.SendAsync(HttpMethod.Get, "repos/octo/demo", cancellationToken: TestContext.Current.CancellationToken);

        // The availability probe, then the call itself — and nothing over HTTP.
        Assert.Equal(2, gh.Calls.Count);
        Assert.Equal("repos/octo/demo", gh.Calls[1][3]);
        Assert.Equal(0, handler.RequestCount);
    }

    /// <summary><c>gh api user</c> that names nobody leaves no login to ask a token
    /// for, so the call stays with <c>gh api</c>.</summary>
    [Fact]
    public async Task A_cli_that_names_no_login_keeps_the_call_on_gh_api()
    {
        using var gh = new GhStub().Answers("{}");
        var handler = new RecordingHandler();
        var accounts = CliToken("octocat", "gho_cli");
        var transport = Resolving(Store(), gh.Transport(), NoToken(handler), accounts: accounts);

        await transport.SendAsync(HttpMethod.Get, "repos/octo/demo", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, handler.RequestCount);
        Assert.Contains(gh.Calls, call => call.Contains("repos/octo/demo"));
    }

    /// <summary>
    /// <c>gh api</c> goes to gh's own default host and ignores the install-wide
    /// endpoint, so an Enterprise endpoint never received unbound calls. Putting the
    /// CLI's github.com token on it would send it to the wrong host; the call keeps
    /// going where it always went.
    /// </summary>
    [Fact]
    public async Task A_non_default_api_endpoint_keeps_unbound_calls_on_gh_api()
    {
        var store = Store();
        Assert.Null(store.SetApiEndpoint("https://github.example.com/api/v3"));

        using var gh = new GhStub().Answers("""{"login":"octocat"}""");
        var handler = new RecordingHandler();
        var transport = Resolving(store, gh.Transport(), NoToken(handler), accounts: CliToken("octocat", "gho_cli"));

        await transport.SendAsync(HttpMethod.Get, "repos/octo/demo", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, handler.RequestCount);
        Assert.Contains(gh.Calls, call => call.Contains("repos/octo/demo"));
    }

    /// <summary>Either variable moves the host <c>gh</c> talks to, so its default
    /// token is no longer a github.com token.</summary>
    [Theory]
    [InlineData("GH_HOST")]
    [InlineData("GH_ENTERPRISE_TOKEN")]
    public async Task A_gh_host_override_in_the_environment_keeps_unbound_calls_on_gh_api(string variable)
    {
        using var gh = new GhStub().Answers("""{"login":"octocat"}""");
        var handler = new RecordingHandler();
        var transport = new ResolvingGitHubTransport(Store(), gh.Transport(), NoToken(handler), accounts: CliToken("octocat", "gho_cli"))
        {
            EnvironmentVariable = name => name == variable ? "github.example.com" : null
        };

        await transport.SendAsync(HttpMethod.Get, "repos/octo/demo", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, handler.RequestCount);
        Assert.Contains(gh.Calls, call => call.Contains("repos/octo/demo"));
    }

    /// <summary>
    /// A refused borrowed token surfaces as the 401 it is and is not sent again —
    /// guideline 0015 resends nothing after an authentication failure, a write least
    /// of all. What changes is the next call: the token and the CLI's remembered
    /// availability are both forgotten, so it re-probes <c>gh</c> and borrows afresh,
    /// or falls back if <c>gh</c> itself is no longer signed in.
    /// </summary>
    [Fact]
    public async Task A_rejected_cli_token_is_forgotten_and_the_refusal_surfaces_without_a_resend()
    {
        using var gh = new GhStub().Answers("""{"login":"octocat"}""");
        var handler = new RecordingHandler { Status = System.Net.HttpStatusCode.Unauthorized };
        var accounts = CliToken("octocat", "gho_revoked");
        var transport = Resolving(Store(), gh.Transport(), NoToken(handler), accounts: accounts);

        var exception = await Assert.ThrowsAsync<GitHubException>(() =>
            transport.SendAsync(HttpMethod.Post, "repos/octo/demo/issues", new { title = "t" }, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, exception.Status);
        Assert.Equal(1, handler.RequestCount);
        Assert.Equal(1, accounts.Invalidations);
        Assert.DoesNotContain(gh.Calls, call => call.Contains("repos/octo/demo/issues"));

        // The CLI's availability is asked again rather than taken from memory.
        Assert.True(await transport.IsAvailableAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, gh.Calls.Count(call => call.SequenceEqual(["api", "user"])));
    }

    /// <summary>Callers branch on <c>Status</c>, never on wording, so a refusal
    /// other than a 401 surfaces with the same status either route would give it —
    /// and is not sent a second time.</summary>
    [Theory]
    [InlineData(System.Net.HttpStatusCode.Forbidden)]
    [InlineData(System.Net.HttpStatusCode.NotFound)]
    public async Task A_refusal_on_the_cli_token_carries_its_status_and_is_not_retried(System.Net.HttpStatusCode status)
    {
        using var gh = new GhStub().Answers("""{"login":"octocat"}""");
        var handler = new RecordingHandler { Status = status };
        var transport = Resolving(Store(), gh.Transport(), NoToken(handler), accounts: CliToken("octocat", "gho_cli"));

        var exception = await Assert.ThrowsAsync<GitHubException>(() =>
            transport.SendAsync(HttpMethod.Get, "repos/octo/demo", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(status, exception.Status);
        Assert.Equal(1, handler.RequestCount);
        Assert.DoesNotContain(gh.Calls, call => call.Contains("repos/octo/demo"));
    }

    [Fact]
    public async Task The_token_answers_when_the_cli_cannot()
    {
        using var gh = new GhStub().Fails();
        var handler = new RecordingHandler();
        var transport = Resolving(Store(), gh.Transport(), Token(handler));

        await transport.SendAsync(HttpMethod.Get, "repos/octo/demo", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, handler.RequestCount);
        Assert.Equal("Bearer ghp_example", handler.Request!.Headers.Authorization!.ToString());
    }

    /// <summary>The token route it builds for itself asks the host's factory for a
    /// client by name on every send, not once when it is built, so the factory's
    /// handler rotation reaches a transport that lives as long as the process.</summary>
    [Fact]
    public async Task The_token_route_it_builds_asks_the_factory_for_a_client_on_every_send()
    {
        using var gh = new GhStub().Fails();
        var handler = new RecordingHandler();
        var factory = new RecordingHttpClientFactory(handler);
        var transport = Resolving(
            Store(),
            gh.Transport(),
            credentials: StubCredentialResolver.WithToken(),
            httpClients: factory);

        Assert.Empty(factory.Names);

        await transport.SendAsync(HttpMethod.Get, "repos/octo/demo", cancellationToken: TestContext.Current.CancellationToken);
        await transport.SendAsync(HttpMethod.Get, "repos/octo/demo", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal([TokenTransport.HttpClientName, TokenTransport.HttpClientName], factory.Names);
        Assert.Equal(2, handler.RequestCount);
        Assert.Equal("Bearer ghp_example", handler.Request!.Headers.Authorization!.ToString());
    }

    /// <summary>A settings problem rather than a failure, so it carries the two things
    /// that would fix it.</summary>
    [Fact]
    public async Task Neither_of_them_is_a_configuration_problem_stated_as_one()
    {
        using var gh = new GhStub().Fails();
        var transport = Resolving(Store(), gh.Transport(), NoToken());

        var exception = await Assert.ThrowsAsync<GitHubNotConfiguredException>(() =>
            transport.SendAsync(HttpMethod.Get, "repos/octo/demo", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(
            "No way to reach GitHub. Sign in with `gh auth login`, or add a personal access token in repository settings.",
            exception.Message);
    }

    [Fact]
    public async Task Availability_is_true_when_either_one_can_answer_and_false_when_neither_can()
    {
        using var cli = new GhStub().Answers("""{"login":"octocat"}""");
        using var noCli = new GhStub().Fails();

        Assert.True(await Resolving(Store(), cli.Transport(), NoToken()).IsAvailableAsync(TestContext.Current.CancellationToken));
        Assert.True(await Resolving(Store(), noCli.Transport(), Token(new RecordingHandler())).IsAvailableAsync(TestContext.Current.CancellationToken));
        Assert.False(await Resolving(Store(), noCli.Transport(), NoToken()).IsAvailableAsync(TestContext.Current.CancellationToken));
    }

    // --- The sentence Settings shows ------------------------------------------

    [Fact]
    public async Task A_cli_connection_is_described_with_the_account_it_is_signed_in_as()
    {
        using var gh = new GhStub().Answers("""{"login":"octocat"}""");

        var connection = await Resolving(Store(), gh.Transport(), NoToken()).DescribeAsync(TestContext.Current.CancellationToken);

        Assert.True(connection.IsConnected);
        Assert.Equal("Connected through the GitHub CLI as octocat.", connection.Summary);
        Assert.Equal("octocat", connection.Account);
    }

    [Fact]
    public async Task A_cli_connection_with_no_login_to_report_is_described_without_one()
    {
        using var gh = new GhStub().Answers("{}");

        var connection = await Resolving(Store(), gh.Transport(), NoToken()).DescribeAsync(TestContext.Current.CancellationToken);

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

        var connection = await Resolving(Store(), gh.Transport(), Token(new RecordingHandler()))
            .DescribeAsync(TestContext.Current.CancellationToken);

        Assert.True(connection.IsConnected);
        Assert.Equal("Connected with a repository personal access token.", connection.Summary);
        Assert.Null(connection.Account);
    }

    [Fact]
    public async Task Nothing_to_reach_github_with_is_described_as_what_to_do_about_it()
    {
        using var gh = new GhStub().Fails();

        var connection = await Resolving(Store(), gh.Transport(), NoToken()).DescribeAsync(TestContext.Current.CancellationToken);

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
        var transport = Resolving(Store(), gh.Transport(), NoToken());

        Assert.Equal("Connected through the GitHub CLI as octocat.", (await transport.DescribeAsync(TestContext.Current.CancellationToken)).Summary);

        // Signed out underneath it. Without invalidating, the remembered answer stands.
        gh.Fails();
        Assert.Equal("Connected through the GitHub CLI as octocat.", (await transport.DescribeAsync(TestContext.Current.CancellationToken)).Summary);

        transport.Invalidate();
        Assert.Equal(NotConnected, (await transport.DescribeAsync(TestContext.Current.CancellationToken)).Summary);
    }

    [Fact]
    public void The_transport_describes_itself_as_either_route()
    {
        using var gh = new GhStub();

        Assert.Equal(
            "GitHub CLI, or a personal access token",
            Resolving(Store(), gh.Transport(), NoToken()).Description);
    }

    // --- The defect that was pinned here, now fixed ---------------------------

    /// <summary>
    /// Stage 0 pinned this the other way round, as a known defect: the token lookup
    /// was a method group bound to the <c>GitHubSettings</c> instance that existed
    /// when the transport was constructed, while the API endpoint beside it was a
    /// lambda reading <c>Current</c> per call. Every mutator replaces <c>Current</c>
    /// with a new instance, so a token configured after construction — or a workspace
    /// move, which is what <c>Reload()</c> is wired to — was visible to the endpoint
    /// half and invisible to the token half.
    /// <para>
    /// Stage 2 replaced both delegates with a resolver that reads the settings per
    /// call, so the pin is rewritten rather than removed and now asserts the
    /// opposite.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_token_configured_after_construction_is_visible_to_the_transport()
    {
        var store = Store();
        Assert.Null(store.SetRepositories([new GitHubRepositoryRef("backlog", "octo", "demo")]));

        using var gh = new GhStub().Fails();
        var transport = Resolving(store, gh.Transport());

        Assert.False(await transport.IsAvailableAsync(TestContext.Current.CancellationToken));

        Assert.Null(store.SetRepositoryToken("backlog", "ghp_added_after_construction"));

        Assert.True(await transport.IsAvailableAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The other half of the same fix, and the one the workspace move needs: the
    /// registry follows the backlog folder, so <c>Reload()</c> replaces
    /// <c>Current</c> wholesale and the credential lookup has to follow it.
    /// </summary>
    [Fact]
    public async Task Moving_the_workspace_moves_the_credential_lookup_with_it()
    {
        const string path = "repos/innovadis-dev/spec-manager/issues";

        var root = Path.Combine(_root, "workspace-one");
        var store = new GitHubSettingsStore(Path.Combine(_root, "github.json"), () => root);

        // Deliberately nothing machine-local, so this install has no overlay row to
        // carry into the workspace it moves to.
        Assert.Null(store.SetRepositories([new GitHubRepositoryRef("spec", "innovadis-dev", "spec-manager")]));

        using var gh = new GhStub().Fails();
        var transport = Resolving(store, gh.Transport());

        // Unbound in this workspace: nothing to reach GitHub with, said the way it
        // has always been said.
        Assert.Equal(
            "No way to reach GitHub. Sign in with `gh auth login`, or add a personal access token in repository settings.",
            (await Assert.ThrowsAsync<GitHubNotConfiguredException>(() =>
                transport.SendAsync(HttpMethod.Get, path, cancellationToken: TestContext.Current.CancellationToken))).Message);

        // The same repository, bound in the workspace being moved to.
        var moved = Path.Combine(_root, "workspace-two");
        Directory.CreateDirectory(Path.Combine(moved, "config"));
        File.WriteAllText(
            Path.Combine(moved, "config", "repos.json"),
            """
            {
              "repositories": [
                { "id": "innovadis-dev/spec-manager", "alias": "spec", "account": "j-schepers_innobv" }
              ]
            }
            """);

        root = moved;
        store.Reload();

        // The binding is picked up, which it could not be if the lookup were still
        // reading the settings snapshot it was constructed with.
        Assert.Equal(
            "innovadis-dev/spec-manager is worked as 'j-schepers_innobv', "
            + "and this machine has no credential for 'j-schepers_innobv'.",
            (await Assert.ThrowsAsync<GitHubNotConfiguredException>(() =>
                transport.SendAsync(HttpMethod.Get, path, cancellationToken: TestContext.Current.CancellationToken))).Message);
    }

    // --- A bound repository never leaves as the process-wide identity ----------

    /// <summary>
    /// The fix, stated as the routing rule it is. <c>gh api</c> has no per-call
    /// account selector, so the CLI can only ever speak as whoever <c>gh</c> is
    /// currently switched to — and letting it answer for a repository bound to a
    /// different account is exactly how a call left as the wrong identity and came
    /// back a 404.
    /// </summary>
    [Fact]
    public async Task A_bound_path_goes_out_over_http_even_when_the_cli_is_signed_in()
    {
        using var gh = new GhStub().Answers("""{"login":"JSdotNet"}""");
        var handler = new RecordingHandler();
        var transport = Resolving(Store(), gh.Transport(), BoundToken(handler));

        await transport.SendAsync(HttpMethod.Get, "repos/innovadis-dev/spec-manager/issues", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, handler.RequestCount);
        Assert.Equal("Bearer ghp_bound", handler.Request!.Headers.Authorization!.ToString());

        // And the CLI carried nothing. Its availability probe may or may not have
        // run; what matters is that no `gh api` call for this path was made.
        Assert.DoesNotContain(gh.Calls, call => call.Contains("repos/innovadis-dev/spec-manager/issues"));
    }

    /// <summary>Settings' "Check the connection" button is the only way a rotated or
    /// revoked credential is ever noticed, so invalidation has to reach the token the
    /// CLI handed over as well as the CLI's own remembered answer.</summary>
    [Fact]
    public async Task Invalidate_also_forgets_the_tokens_extracted_from_the_cli()
    {
        using var gh = new GhStub().Answers("""{"login":"octocat"}""");
        var accounts = new StubGhCliAccountSource();
        var transport = Resolving(Store(), gh.Transport(), NoToken(), accounts: accounts);

        Assert.True(await transport.IsAvailableAsync(TestContext.Current.CancellationToken));

        transport.Invalidate();

        Assert.Equal(1, accounts.Invalidations);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private GitHubSettingsStore Store() => new(Path.Combine(_root, "github.json"));

    /// <summary>The transport under test, blind to this machine's environment: a
    /// <c>GH_HOST</c> or <c>GH_ENTERPRISE_TOKEN</c> on a developer machine or a runner
    /// would otherwise move unbound calls onto <c>gh api</c> and fail tests that are
    /// not about the environment at all.</summary>
    private static ResolvingGitHubTransport Resolving(
        GitHubSettingsStore settings,
        GhCliTransport? cli = null,
        TokenTransport? token = null,
        IGitHubCredentialResolver? credentials = null,
        IGhCliAccountSource? accounts = null,
        IHttpClientFactory? httpClients = null) =>
        new(settings, cli, token, credentials, accounts, httpClients) { EnvironmentVariable = _ => null };

    /// <summary>A token route that can answer any path, with nothing naming an
    /// identity — the shape of a machine that has a repository token and no
    /// accounts.</summary>
    private static TokenTransport Token(RecordingHandler handler) =>
        new(StubCredentialResolver.WithToken(), () => GitHubSettings.DefaultApiEndpoint, new HttpClient(handler));

    private static TokenTransport NoToken() => NoToken(new RecordingHandler());

    /// <summary>No repository token, over a handler the test reads — the route a
    /// call borrowing the CLI's token goes out on.</summary>
    private static TokenTransport NoToken(RecordingHandler handler) =>
        new(StubCredentialResolver.None(), () => GitHubSettings.DefaultApiEndpoint, new HttpClient(handler));

    /// <summary>A <c>gh</c> that hands over <paramref name="token"/> for
    /// <paramref name="login"/>.</summary>
    private static StubGhCliAccountSource CliToken(string login, string token)
    {
        var accounts = new StubGhCliAccountSource();
        accounts.Tokens[login] = token;
        return accounts;
    }

    /// <summary>A token route every path resolves to <em>by name</em>, which is the
    /// case the CLI may never answer for.</summary>
    private static TokenTransport BoundToken(RecordingHandler handler, string account = "j-schepers_innobv") =>
        new(StubCredentialResolver.Bound(account), () => GitHubSettings.DefaultApiEndpoint, new HttpClient(handler));

    private sealed class RecordingHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public List<string> Names { get; } = [];

        public HttpClient CreateClient(string name)
        {
            Names.Add(name);
            return new HttpClient(handler, disposeHandler: false);
        }
    }
}
