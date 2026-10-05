using System.Collections.Specialized;
using System.Net;
using System.Web;

using Backlog.Infrastructure.SpecManager.OAuth;

namespace Backlog.Infrastructure.SpecManager.UnitTests;

/// <summary>
/// The sign-in end to end against the stub installation: discovery, the one client
/// registration, the authorization request with its S256 challenge, the browser
/// coming back to the loopback listener — driven here by an HTTP GET where a person
/// would have clicked — and the code exchanged for a token pair.
/// </summary>
public sealed class SpecManagerSignInTests : IDisposable
{
    private const string TokenPath = "/oauth/token";

    private readonly HttpClient _browser = new(new SocketsHttpHandler { UseProxy = false });

    public void Dispose() => _browser.Dispose();

    [Fact]
    public async Task Signing_in_registers_the_app_and_exchanges_the_code_for_a_token_pair()
    {
        using var scenario = new ConnectorScenario();
        var changed = 0;
        scenario.Connector.AccountChanged += () => changed++;
        var visit = ComeBack(scenario, (_, state) => $"code=the-code&state={state}");

        var error = await scenario.Connector.SignInAsync(TestContext.Current.CancellationToken);

        Assert.Null(error);

        // Registered as Backlog, for the loopback redirect on any port.
        var registration = Assert.Single(scenario.Server.To("/oauth/registreren"));
        Assert.Contains("\"client_name\":\"Backlog\"", registration.Body, StringComparison.Ordinal);
        Assert.Contains("\"redirect_uris\":[\"http://127.0.0.1/callback\"]", registration.Body, StringComparison.Ordinal);

        // The authorization request.
        var authorize = visit.Authorize!;
        Assert.Equal("https://spec.test/oauth/autoriseren", authorize.GetLeftPart(UriPartial.Path));
        var query = HttpUtility.ParseQueryString(authorize.Query);
        Assert.Equal(ConnectorScenario.ClientId, query["client_id"]);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.False(string.IsNullOrWhiteSpace(query["state"]));
        Assert.Matches(@"^http://127\.0\.0\.1:\d+/callback$", query["redirect_uri"]);

        // The browser was told it could close the tab.
        using (var page = await visit.Answer!)
        {
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            Assert.Contains("close this tab", await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        }

        // The code exchange: a form, with the verifier whose S256 is the challenge
        // and the very redirect URI the code was issued for.
        var exchange = Form(Assert.Single(scenario.Server.To(TokenPath)));
        Assert.Equal("authorization_code", exchange["grant_type"]);
        Assert.Equal("the-code", exchange["code"]);
        Assert.Equal(ConnectorScenario.ClientId, exchange["client_id"]);
        Assert.Equal(query["redirect_uri"], exchange["redirect_uri"]);
        Assert.Equal(query["code_challenge"], Pkce.Challenge(exchange["code_verifier"]!));

        // Kept, and shown.
        var kept = scenario.Store.Get(scenario.Options.Root)!;
        Assert.Equal("sma_first-access", kept.AccessToken);
        Assert.Equal("smr_first-refresh", kept.RefreshToken);
        Assert.Equal(scenario.Time.GetUtcNow().AddHours(1), kept.AccessTokenExpiresAt);
        Assert.Equal("spec-manager", scenario.Connector.Account?.DisplayName);
        Assert.Equal(scenario.Time.GetUtcNow(), scenario.Connector.Account?.SignedInAt);
        Assert.True(changed > 0);
    }

    [Fact]
    public async Task A_signed_in_account_takes_the_name_the_installation_gives_it()
    {
        using var scenario = new ConnectorScenario();
        scenario.Server.Json(HttpMethod.Get, $"/api/producten/{StubSpecManager.Product}/backlogleden", "leden.json");
        ComeBack(scenario, (_, state) => $"code=the-code&state={state}");

        Assert.Null(await scenario.Connector.SignInAsync(TestContext.Current.CancellationToken));

        Assert.Equal("Jip Jansen", scenario.Connector.Account?.DisplayName);
    }

    [Fact]
    public async Task A_second_sign_in_discovers_and_registers_nothing_new()
    {
        using var scenario = new ConnectorScenario();

        ComeBack(scenario, (_, state) => $"code=first&state={state}");
        Assert.Null(await scenario.Connector.SignInAsync(TestContext.Current.CancellationToken));
        await scenario.Connector.SignOutAsync(TestContext.Current.CancellationToken);
        ComeBack(scenario, (_, state) => $"code=second&state={state}");
        Assert.Null(await scenario.Connector.SignInAsync(TestContext.Current.CancellationToken));

        Assert.Single(scenario.Server.To("/oauth/registreren"));
        Assert.Single(scenario.Server.To("/.well-known/oauth-authorization-server"));
        Assert.Equal(2, scenario.Server.To(TokenPath).Count);
    }

    [Fact]
    public async Task An_answer_carrying_another_state_is_ignored_and_the_sign_in_finishes_with_the_right_one()
    {
        using var scenario = new ConnectorScenario();
        var visit = new BrowserVisit();
        scenario.Browser = (authorize, _) =>
        {
            visit.Authorize = authorize;
            var query = HttpUtility.ParseQueryString(authorize.Query);
            var redirect = query["redirect_uri"]!;
            var state = Uri.EscapeDataString(query["state"]!);
            visit.Answer = Task.Run(async () =>
            {
                using (var forged = await _browser.GetAsync(new Uri($"{redirect}?code=stolen&state=forged"), TestContext.Current.CancellationToken))
                {
                    Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
                }

                return await _browser.GetAsync(new Uri($"{redirect}?code=the-code&state={state}"), TestContext.Current.CancellationToken);
            });
            return Task.CompletedTask;
        };

        Assert.Null(await scenario.Connector.SignInAsync(TestContext.Current.CancellationToken));

        using (await visit.Answer!)
        {
        }

        Assert.Equal("the-code", Form(Assert.Single(scenario.Server.To(TokenPath)))["code"]);
    }

    [Fact]
    public async Task A_code_carrying_another_state_is_never_exchanged()
    {
        using var scenario = new ConnectorScenario();
        Task<HttpResponseMessage>? forged = null;
        scenario.Browser = (authorize, _) =>
        {
            var redirect = HttpUtility.ParseQueryString(authorize.Query)["redirect_uri"]!;
            forged = Task.Run(async () =>
            {
                var answer = await _browser.GetAsync(new Uri($"{redirect}?code=stolen&state=forged"), TestContext.Current.CancellationToken);
                // Nobody comes back with the right one.
                scenario.Time.Advance(TimeSpan.FromMinutes(6));
                return answer;
            });
            return Task.CompletedTask;
        };

        var error = await scenario.Connector.SignInAsync(TestContext.Current.CancellationToken);

        using (var page = await forged!)
        {
            Assert.Equal(HttpStatusCode.BadRequest, page.StatusCode);
        }

        Assert.Contains("within 5 minutes", error, StringComparison.Ordinal);
        Assert.Empty(scenario.Server.To(TokenPath));
        Assert.Null(scenario.Connector.Account);
    }

    [Fact]
    public async Task A_registration_the_installation_has_forgotten_is_dropped_on_the_code_exchange()
    {
        using var scenario = new ConnectorScenario();
        scenario.Registered();
        scenario.Server.Route(HttpMethod.Post, TokenPath, (_, _) =>
            StubSpecManager.Respond(HttpStatusCode.BadRequest, """{"error":"invalid_client","error_description":"Onbekende client."}"""));
        ComeBack(scenario, (_, state) => $"code=the-code&state={state}");

        var error = await scenario.Connector.SignInAsync(TestContext.Current.CancellationToken);

        Assert.Contains("Sign in again", error, StringComparison.Ordinal);
        Assert.Null(scenario.Store.Get(scenario.Options.Root));

        // The next attempt registers afresh.
        scenario.Server.Json(HttpMethod.Post, TokenPath, "token.json");
        ComeBack(scenario, (_, state) => $"code=the-code&state={state}");
        Assert.Null(await scenario.Connector.SignInAsync(TestContext.Current.CancellationToken));
        Assert.Single(scenario.Server.To("/oauth/registreren"));
    }

    [Fact]
    public async Task A_sign_in_that_times_out_on_a_kept_registration_drops_it_so_the_next_registers_afresh()
    {
        using var scenario = new ConnectorScenario();
        scenario.Registered();
        // An installation that forgot the client shows an error page and never
        // sends the browser back.
        scenario.Browser = (_, _) =>
        {
            scenario.Time.Advance(TimeSpan.FromMinutes(6));
            return Task.CompletedTask;
        };

        Assert.NotNull(await scenario.Connector.SignInAsync(TestContext.Current.CancellationToken));
        Assert.Null(scenario.Store.Get(scenario.Options.Root));
        Assert.Empty(scenario.Server.To("/oauth/registreren"));

        ComeBack(scenario, (_, state) => $"code=the-code&state={state}");
        Assert.Null(await scenario.Connector.SignInAsync(TestContext.Current.CancellationToken));
        Assert.Single(scenario.Server.To("/oauth/registreren"));
    }

    [Fact]
    public async Task A_sign_in_that_times_out_while_signed_in_keeps_the_sign_in()
    {
        using var scenario = new ConnectorScenario();
        scenario.SignedIn();
        scenario.Browser = (_, _) =>
        {
            scenario.Time.Advance(TimeSpan.FromMinutes(6));
            return Task.CompletedTask;
        };

        Assert.NotNull(await scenario.Connector.SignInAsync(TestContext.Current.CancellationToken));

        Assert.NotNull(scenario.Connector.Account);
    }

    [Fact]
    public async Task A_pipeline_that_gives_up_reads_as_an_installation_out_of_reach()
    {
        using var scenario = new ConnectorScenario();
        scenario.Server.Route(HttpMethod.Get, "/.well-known/oauth-authorization-server", (_, _) => throw new Polly.Timeout.TimeoutRejectedException());

        var error = await scenario.Connector.SignInAsync(TestContext.Current.CancellationToken);

        Assert.Contains("could not be reached", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Failing_to_name_the_account_does_not_fail_the_sign_in()
    {
        using var scenario = new ConnectorScenario();
        scenario.Server.Route(HttpMethod.Get, $"/api/producten/{StubSpecManager.Product}/backlogleden", (_, _) => throw new Polly.CircuitBreaker.BrokenCircuitException());
        ComeBack(scenario, (_, state) => $"code=the-code&state={state}");

        Assert.Null(await scenario.Connector.SignInAsync(TestContext.Current.CancellationToken));

        Assert.Equal("spec-manager", scenario.Connector.Account?.DisplayName);
    }

    [Fact]
    public async Task A_rotated_token_that_cannot_be_kept_is_still_used_for_the_rest_of_the_run()
    {
        using var scenario = new ConnectorScenario();
        scenario.SignedIn(validFor: TimeSpan.Zero);
        AnswerRefreshWith(scenario, "sma_second", "smr_second");
        scenario.Store.FailSaves = true;

        var first = await scenario.SignIn.GetAccessTokenAsync(TestContext.Current.CancellationToken);
        var second = await scenario.SignIn.GetAccessTokenAsync(TestContext.Current.CancellationToken);

        Assert.Equal("sma_second", first);
        Assert.Equal("sma_second", second);
        Assert.Single(scenario.Server.To(TokenPath));
        Assert.NotNull(scenario.Connector.Account);

        // The next refresh presents the rotated token, not the dead one on disk.
        scenario.Time.Advance(TimeSpan.FromHours(1));
        scenario.Store.FailSaves = false;
        AnswerRefreshWith(scenario, "sma_third", "smr_third");
        Assert.Equal("sma_third", await scenario.SignIn.GetAccessTokenAsync(TestContext.Current.CancellationToken));
        Assert.Equal("smr_second", Form(scenario.Server.To(TokenPath)[1])["refresh_token"]);
        Assert.Equal("smr_third", scenario.Store.Get(scenario.Options.Root)!.RefreshToken);
    }

    [Fact]
    public async Task A_sign_in_turned_down_in_the_browser_says_so()
    {
        using var scenario = new ConnectorScenario();
        ComeBack(scenario, (_, state) => $"error=access_denied&state={state}");

        var error = await scenario.Connector.SignInAsync(TestContext.Current.CancellationToken);

        Assert.Equal("The sign-in was turned down in spec-manager.", error);
        Assert.Empty(scenario.Server.To(TokenPath));
    }

    [Fact]
    public async Task A_sign_in_the_browser_never_finishes_gives_up_after_the_timeout()
    {
        using var scenario = new ConnectorScenario();
        scenario.Browser = (_, _) =>
        {
            scenario.Time.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
            return Task.CompletedTask;
        };

        var error = await scenario.Connector.SignInAsync(TestContext.Current.CancellationToken);

        Assert.Contains("within 5 minutes", error, StringComparison.Ordinal);
        Assert.Null(scenario.Connector.Account);
    }

    [Fact]
    public async Task An_installation_that_cannot_be_reached_is_said_to_the_person()
    {
        using var scenario = new ConnectorScenario();
        scenario.Server.Route(HttpMethod.Get, "/.well-known/oauth-authorization-server", (_, _) => StubSpecManager.Respond(HttpStatusCode.BadGateway, "{}"));

        var error = await scenario.Connector.SignInAsync(TestContext.Current.CancellationToken);

        Assert.Contains("could not be reached", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_access_token_about_to_expire_is_refreshed_first_and_the_rotated_refresh_token_kept()
    {
        using var scenario = new ConnectorScenario();
        scenario.SignedIn(validFor: TimeSpan.FromSeconds(30));
        AnswerRefreshWith(scenario, "sma_second", "smr_second");

        await scenario.Client.GetStatusesAsync(StubSpecManager.Product, TestContext.Current.CancellationToken);

        var refresh = Form(Assert.Single(scenario.Server.To(TokenPath)));
        Assert.Equal("refresh_token", refresh["grant_type"]);
        Assert.Equal("smr_current", refresh["refresh_token"]);
        Assert.Equal(ConnectorScenario.ClientId, refresh["client_id"]);

        Assert.Equal("sma_second", Assert.Single(scenario.Server.To($"/api/producten/{StubSpecManager.Product}/backlogstatussen")).Bearer);
        var kept = scenario.Store.Get(scenario.Options.Root)!;
        Assert.Equal("smr_second", kept.RefreshToken);
        Assert.Equal("sma_second", kept.AccessToken);
    }

    [Fact]
    public async Task A_token_with_time_left_is_used_without_a_refresh()
    {
        using var scenario = new ConnectorScenario();
        scenario.SignedIn(validFor: TimeSpan.FromMinutes(2));

        await scenario.Client.GetStatusesAsync(StubSpecManager.Product, TestContext.Current.CancellationToken);

        Assert.Empty(scenario.Server.To(TokenPath));
    }

    [Fact]
    public async Task Callers_needing_a_refresh_at_once_share_one()
    {
        using var scenario = new ConnectorScenario();
        scenario.SignedIn(validFor: TimeSpan.Zero);
        AnswerRefreshWith(scenario, "sma_second", "smr_second");

        var tokens = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            Task.Run(() => scenario.SignIn.GetAccessTokenAsync(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken)));

        Assert.All(tokens, token => Assert.Equal("sma_second", token));
        Assert.Single(scenario.Server.To(TokenPath));
    }

    [Fact]
    public async Task A_refused_token_is_refreshed_once_and_the_call_sent_again()
    {
        using var scenario = new ConnectorScenario();
        scenario.SignedIn();
        AnswerRefreshWith(scenario, "sma_second", "smr_second");
        var path = $"/api/producten/{StubSpecManager.Product}/backlogstatussen";
        scenario.Server.Route(HttpMethod.Get, path, (request, _) =>
            request.Headers.Authorization?.Parameter == "sma_second"
                ? StubSpecManager.Respond(HttpStatusCode.OK, Fixtures.Read("statuses.json"))
                : StubSpecManager.Respond(HttpStatusCode.Unauthorized, "{}"));

        var statuses = await scenario.Client.GetStatusesAsync(StubSpecManager.Product, TestContext.Current.CancellationToken);

        Assert.Equal(5, statuses.Count);
        Assert.Equal(["sma_current", "sma_second"], scenario.Server.To(path).Select(request => request.Bearer));
        Assert.Single(scenario.Server.To(TokenPath));
        Assert.Equal("smr_second", scenario.Store.Get(scenario.Options.Root)!.RefreshToken);
    }

    [Fact]
    public async Task A_second_refusal_fails_the_call_without_a_third_attempt()
    {
        using var scenario = new ConnectorScenario();
        scenario.SignedIn();
        var path = $"/api/producten/{StubSpecManager.Product}/backlogstatussen";
        scenario.Server.Route(HttpMethod.Get, path, (_, _) => StubSpecManager.Respond(HttpStatusCode.Unauthorized, "{}"));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            scenario.Client.GetStatusesAsync(StubSpecManager.Product, TestContext.Current.CancellationToken));

        Assert.Equal(2, scenario.Server.To(path).Count);
        Assert.Single(scenario.Server.To(TokenPath));
    }

    [Fact]
    public async Task A_refresh_the_installation_no_longer_honours_signs_the_person_out()
    {
        using var scenario = new ConnectorScenario();
        scenario.SignedIn(validFor: TimeSpan.Zero);
        scenario.Server.Route(HttpMethod.Post, TokenPath, (_, _) =>
            StubSpecManager.Respond(HttpStatusCode.BadRequest, """{"error":"invalid_grant","error_description":"Onbekend vernieuwtoken."}"""));
        var changed = 0;
        scenario.Connector.AccountChanged += () => changed++;

        await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            scenario.Client.GetStatusesAsync(StubSpecManager.Product, TestContext.Current.CancellationToken));

        Assert.Null(scenario.Connector.Account);
        Assert.Equal(1, changed);
        var kept = scenario.Store.Get(scenario.Options.Root)!;
        Assert.False(kept.IsSignedIn);
        Assert.Equal(ConnectorScenario.ClientId, kept.ClientId);
    }

    [Fact]
    public async Task A_refresh_that_fails_for_another_reason_keeps_the_sign_in()
    {
        using var scenario = new ConnectorScenario();
        scenario.SignedIn(validFor: TimeSpan.Zero);
        scenario.Server.Route(HttpMethod.Post, TokenPath, (_, _) => StubSpecManager.Respond(HttpStatusCode.ServiceUnavailable, "<html>down</html>"));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            scenario.Client.GetStatusesAsync(StubSpecManager.Product, TestContext.Current.CancellationToken));

        Assert.NotNull(scenario.Connector.Account);
        Assert.Equal("smr_current", scenario.Store.Get(scenario.Options.Root)!.RefreshToken);
    }

    [Fact]
    public async Task Signing_out_forgets_the_tokens_and_keeps_the_registration()
    {
        using var scenario = new ConnectorScenario();
        scenario.SignedIn();
        var changed = 0;
        scenario.Connector.AccountChanged += () => changed++;

        await scenario.Connector.SignOutAsync(TestContext.Current.CancellationToken);
        await scenario.Connector.SignOutAsync(TestContext.Current.CancellationToken);

        Assert.Null(scenario.Connector.Account);
        Assert.Equal(1, changed);
        var kept = scenario.Store.Get(scenario.Options.Root)!;
        Assert.Null(kept.AccessToken);
        Assert.Null(kept.RefreshToken);
        Assert.Equal(ConnectorScenario.ClientId, kept.ClientId);
    }

    /// <summary>Makes the "browser" follow the authorization request back to the
    /// loopback listener with the query <paramref name="answer"/> builds from the
    /// redirect URI and the state it was sent.</summary>
    private BrowserVisit ComeBack(ConnectorScenario scenario, Func<Uri, string, string> answer)
    {
        var visit = new BrowserVisit();
        scenario.Browser = (authorize, _) =>
        {
            visit.Authorize = authorize;
            var query = HttpUtility.ParseQueryString(authorize.Query);
            var redirect = new Uri(query["redirect_uri"]!);
            var callback = new Uri($"{redirect}?{answer(redirect, Uri.EscapeDataString(query["state"]!))}");

            // Not awaited here: the listener only answers once the sign-in waits on
            // it, which is after the browser has been opened.
            visit.Answer = Task.Run(() => _browser.GetAsync(callback, TestContext.Current.CancellationToken));
            return Task.CompletedTask;
        };

        return visit;
    }

    private static void AnswerRefreshWith(ConnectorScenario scenario, string accessToken, string refreshToken) =>
        scenario.Server.Route(HttpMethod.Post, TokenPath, (_, _) => StubSpecManager.Respond(
            HttpStatusCode.OK,
            $$"""{"access_token":"{{accessToken}}","refresh_token":"{{refreshToken}}","token_type":"Bearer","expires_in":3600}"""));

    private static NameValueCollection Form(RecordedRequest request) => HttpUtility.ParseQueryString(request.Body!);

    private sealed class BrowserVisit
    {
        public Uri? Authorize { get; set; }

        public Task<HttpResponseMessage>? Answer { get; set; }
    }
}
