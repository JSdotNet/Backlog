using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Adapters;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Ports;
using Backlog.Modules.Sync.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Backlog.Modules.Sync.Api.UnitTests;

/// <summary>
/// Local ADR 0058 §8 over HTTP: the remote connector's authorization server — RFC 8414
/// metadata, an authorize that refuses a bad client or redirect in place, the owner's GitHub
/// sign-in through the shared callback, a consent page, and a token endpoint that rotates
/// refresh tokens and answers RFC 6749 errors. GitHub is a stand-in that answers a user id
/// per code; claude.ai is this test.
/// </summary>
public sealed class ConnectorAuthorizationEndpointTests : IDisposable
{
    private const long OwnerGitHubId = StubGitHubOwnerSignIn.OwnerGitHubId;
    private const string OwnerCode = StubGitHubOwnerSignIn.OwnerCode;
    private const string StrangerCode = StubGitHubOwnerSignIn.StrangerCode;
    private const string Issuer = "https://sync.test";
    private const string Resource = "https://sync.test/mcp";
    private const string ClientId = "claude-connector";
    private const string Redirect = "https://claude.ai/api/mcp/auth_callback";
    private const string ClaudeState = "claude-state";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private readonly SyncServiceFactory _service = new()
    {
        MoreConfiguration =
        [
            .. SyncServiceFactory.ConfiguredOwner,
            ("Modules:Sync:Connector:Issuer", Issuer),
            ("Modules:Sync:Connector:Resource", Resource),
        ],
        TestServices = services =>
        {
            services.RemoveAll<IGitHubOwnerSignIn>();
            services.AddSingleton<IGitHubOwnerSignIn>(new StubGitHubOwnerSignIn());
            services.RemoveAll<IConnectorClientStore>();
            services.AddSingleton<BusySwitch>();
            services.AddSingleton<InMemoryConnectorClientStore>();
            services.AddSingleton<IConnectorClientStore, SwitchableClientStore>();
        },
    };

    public void Dispose() => _service.Dispose();

    [Fact]
    public async Task The_metadata_document_describes_the_authorization_server_anonymously()
    {
        using var response = await NoRedirects().GetAsync(SyncRoutes.AuthorizationServerMetadata, Cancellation);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation));
        var metadata = document.RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Issuer, metadata.GetProperty("issuer").GetString());
        Assert.Equal("https://sync.test/oauth/authorize", metadata.GetProperty("authorization_endpoint").GetString());
        Assert.Equal("https://sync.test/oauth/token", metadata.GetProperty("token_endpoint").GetString());
        Assert.Equal(["code"], Strings(metadata, "response_types_supported"));
        Assert.Equal(["authorization_code", "refresh_token"], Strings(metadata, "grant_types_supported"));
        Assert.Equal(["S256"], Strings(metadata, "code_challenge_methods_supported"));
        Assert.Equal(["client_secret_basic", "client_secret_post"], Strings(metadata, "token_endpoint_auth_methods_supported"));
        Assert.Equal(["inbox:capture"], Strings(metadata, "scopes_supported"));
        Assert.False(metadata.TryGetProperty("registration_endpoint", out _));
    }

    [Theory]
    [InlineData("https://claude.com/api/mcp/auth_callback")]
    [InlineData("https://claude.ai/api/mcp/auth_callback/")]
    [InlineData("http://claude.ai/api/mcp/auth_callback")]
    [InlineData("https://evil.example/api/mcp/auth_callback")]
    public async Task A_redirect_other_than_the_registered_one_is_refused_in_place(string redirect)
    {
        await OwnerDesktopWithSecret();

        using var response = await NoRedirects().GetAsync(AuthorizeAddress(EnrolmentPkce.CreateVerifier(), redirect: redirect), Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task An_unknown_client_is_refused_in_place()
    {
        await OwnerDesktopWithSecret();

        using var response = await NoRedirects().GetAsync(AuthorizeAddress(EnrolmentPkce.CreateVerifier(), clientId: "someone-else"), Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Theory]
    [InlineData("plain")]
    [InlineData(null)]
    public async Task A_challenge_that_is_not_S256_goes_back_to_claude_as_invalid_request(string? method)
    {
        await OwnerDesktopWithSecret();
        var verifier = EnrolmentPkce.CreateVerifier();

        var back = await Redirected(AuthorizeAddress(verifier, challenge: method is null ? null : verifier, method: method));

        Assert.StartsWith(Redirect + "?", back.AbsoluteUri, StringComparison.Ordinal);
        Assert.Equal("invalid_request", SyncClientExtensions.QueryValue(back, "error"));
        Assert.Equal(ClaudeState, SyncClientExtensions.QueryValue(back, "state"));
    }

    [Fact]
    public async Task A_GitHub_user_other_than_the_owner_goes_back_to_claude_denied_without_a_code()
    {
        await OwnerDesktopWithSecret();
        var gitHub = await Redirected(AuthorizeAddress(EnrolmentPkce.CreateVerifier()));

        var back = await Redirected(CallbackAddress(StrangerCode, SyncClientExtensions.QueryValue(gitHub, "state")));

        Assert.StartsWith(Redirect + "?", back.AbsoluteUri, StringComparison.Ordinal);
        Assert.Equal("access_denied", SyncClientExtensions.QueryValue(back, "error"));
        Assert.Equal(ClaudeState, SyncClientExtensions.QueryValue(back, "state"));
        Assert.Null(SyncClientExtensions.QueryValue(back, "code"));
    }

    [Fact]
    public async Task The_consent_page_names_the_client_and_scope_and_cannot_be_framed_or_cached()
    {
        await OwnerDesktopWithSecret();
        var consent = await ConsentAddress(EnrolmentPkce.CreateVerifier());

        using var page = await NoRedirects().GetAsync(consent, Cancellation);
        var html = await page.Content.ReadAsStringAsync(Cancellation);

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal("text/html", page.Content.Headers.ContentType?.MediaType);
        Assert.Contains("frame-ancestors 'none'", page.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        Assert.True(page.Headers.CacheControl?.NoStore);
        Assert.Contains("Claude", html, StringComparison.Ordinal);
        Assert.Contains("Add items to your Backlog Inbox.", html, StringComparison.Ordinal);
        Assert.Contains("name=\"ticket\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deny_goes_back_to_claude_as_access_denied()
    {
        await OwnerDesktopWithSecret();
        var consent = await ConsentAddress(EnrolmentPkce.CreateVerifier());

        var back = await Decide(consent, "deny");

        Assert.Equal("access_denied", SyncClientExtensions.QueryValue(back, "error"));
        Assert.Null(SyncClientExtensions.QueryValue(back, "code"));
    }

    [Fact]
    public async Task The_access_token_is_for_the_mcp_resource_for_fifteen_minutes_and_not_cached()
    {
        var secret = await OwnerDesktopWithSecret();
        var verifier = EnrolmentPkce.CreateVerifier();
        var code = await AuthorizationCode(verifier);

        using var response = await Token(Basic(secret), CodeGrant(code, verifier));
        var tokens = (await response.Content.ReadFromJsonAsync<ConnectorTokenResponse>(Cancellation))!;
        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(tokens.AccessToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("Bearer", tokens.TokenType);
        Assert.Equal(900, tokens.ExpiresIn);
        Assert.Equal("inbox:capture", tokens.Scope);
        Assert.Equal([Resource], jwt.Audiences);
        Assert.Equal(Issuer, jwt.Issuer);
        Assert.Equal("inbox:capture", jwt.GetClaim("scope").Value);
        Assert.Equal(ClientId, jwt.GetClaim("client_id").Value);
        Assert.False(string.IsNullOrEmpty(jwt.Id));
        Assert.True(Guid.TryParse(jwt.Subject, out _));
        Assert.Equal(TimeSpan.FromMinutes(15), jwt.ValidTo - jwt.IssuedAt);
        Assert.Equal(SecurityAlgorithms.HmacSha256, jwt.Alg);
    }

    [Fact]
    public async Task The_access_token_is_not_signed_with_the_device_token_key()
    {
        var secret = await OwnerDesktopWithSecret();
        var tokens = await Connected(secret);

        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(
            tokens.AccessToken,
            new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(SyncServiceFactory.SigningKey)),
            });

        Assert.False(validation.IsValid);
    }

    [Fact]
    public async Task The_sync_API_refuses_a_connector_token()
    {
        var secret = await OwnerDesktopWithSecret();
        var tokens = await Connected(secret);

        using var response = await Client().Bearing(tokens.AccessToken)
            .GetAsync(SyncRoutes.Absolute(SyncRoutes.DeviceStatus), Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_wrong_verifier_is_an_invalid_grant()
    {
        var secret = await OwnerDesktopWithSecret();
        var code = await AuthorizationCode(EnrolmentPkce.CreateVerifier());

        using var response = await Token(Basic(secret), CodeGrant(code, EnrolmentPkce.CreateVerifier()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_grant", await OAuthError(response));
    }

    [Fact]
    public async Task A_code_used_twice_is_an_invalid_grant()
    {
        var secret = await OwnerDesktopWithSecret();
        var verifier = EnrolmentPkce.CreateVerifier();
        var code = await AuthorizationCode(verifier);

        using var first = await Token(Basic(secret), CodeGrant(code, verifier));
        using var second = await Token(Basic(secret), CodeGrant(code, verifier));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("invalid_grant", await OAuthError(second));
    }

    [Fact]
    public async Task A_wrong_secret_in_the_Basic_header_is_a_401_invalid_client_with_a_Basic_challenge()
    {
        await OwnerDesktopWithSecret();
        var verifier = EnrolmentPkce.CreateVerifier();
        var code = await AuthorizationCode(verifier);

        using var response = await Token(Basic("not-the-secret"), CodeGrant(code, verifier));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Basic", response.Headers.WwwAuthenticate.Single().Scheme);
        Assert.Equal("invalid_client", await OAuthError(response));
    }

    [Fact]
    public async Task The_secret_may_also_come_in_the_form()
    {
        var secret = await OwnerDesktopWithSecret();
        var verifier = EnrolmentPkce.CreateVerifier();
        var code = await AuthorizationCode(verifier);

        using var response = await Token(
            authorization: null,
            [.. CodeGrant(code, verifier), new("client_id", ClientId), new("client_secret", secret)]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_missing_secret_is_a_401_invalid_client()
    {
        await OwnerDesktopWithSecret();
        var verifier = EnrolmentPkce.CreateVerifier();
        var code = await AuthorizationCode(verifier);

        using var response = await Token(authorization: null, [.. CodeGrant(code, verifier), new("client_id", ClientId)]);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("invalid_client", await OAuthError(response));
    }

    [Fact]
    public async Task A_refresh_rotates_and_replaying_the_old_token_revokes_the_family()
    {
        var secret = await OwnerDesktopWithSecret();
        var first = await Connected(secret);

        using var rotated = await Token(Basic(secret), RefreshGrant(first.RefreshToken));
        var second = (await rotated.Content.ReadFromJsonAsync<ConnectorTokenResponse>(Cancellation))!;
        using var replay = await Token(Basic(secret), RefreshGrant(first.RefreshToken));
        using var victim = await Token(Basic(secret), RefreshGrant(second.RefreshToken));

        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        Assert.NotEqual(first.RefreshToken, second.RefreshToken);
        Assert.Equal("invalid_grant", await OAuthError(replay));
        Assert.Equal("invalid_grant", await OAuthError(victim));
    }

    [Fact]
    public async Task Revoke_cuts_off_the_secret_every_refresh_token_and_new_authorizations()
    {
        var desktop = await OwnerDesktop();
        var secret = await CreateSecret(desktop);
        var tokens = await Connected(secret);

        using var revoked = await desktop.DeleteAsync(SyncRoutes.Absolute(SyncRoutes.ConnectorSecret), Cancellation);
        using var withOldSecret = await Token(Basic(secret), RefreshGrant(tokens.RefreshToken));
        var fresh = await CreateSecret(desktop);
        using var withNewSecret = await Token(Basic(fresh), RefreshGrant(tokens.RefreshToken));
        await desktop.DeleteAsync(SyncRoutes.Absolute(SyncRoutes.ConnectorSecret), Cancellation);
        var authorize = await Redirected(AuthorizeAddress(EnrolmentPkce.CreateVerifier()));

        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        Assert.Equal("invalid_client", await OAuthError(withOldSecret));
        Assert.Equal("invalid_grant", await OAuthError(withNewSecret));
        Assert.Equal("unauthorized_client", SyncClientExtensions.QueryValue(authorize, "error"));
    }

    [Fact]
    public async Task The_desktop_sees_the_secret_once_and_then_only_whether_it_exists()
    {
        var desktop = await OwnerDesktop();

        using var created = await desktop.PostAsync(SyncRoutes.Absolute(SyncRoutes.ConnectorSecret), content: null, Cancellation);
        var secret = (await created.Content.ReadFromJsonAsync<ConnectorSecretResponse>(Cancellation))!;
        var statusJson = await desktop.GetStringAsync(SyncRoutes.Absolute(SyncRoutes.Connector), Cancellation);
        var status = JsonSerializer.Deserialize<ConnectorStatusResponse>(statusJson, JsonSerializerOptions.Web)!;

        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.Equal(43, secret.ClientSecret.Length);
        Assert.True(status.HasSecret);
        Assert.Equal(ClientId, status.ClientId);
        Assert.Equal(new Uri(Redirect), status.RedirectUri);
        Assert.Null(status.LastUsedAt);
        Assert.DoesNotContain(secret.ClientSecret, statusJson, StringComparison.Ordinal);
    }

    /// <summary>A store that is throttled answers the desktop 429 with its own code, the
    /// way every other replica route does, never a bare 500.</summary>
    [Fact]
    public async Task A_busy_store_is_refused_with_its_own_code()
    {
        var desktop = await OwnerDesktop();
        _service.Services.GetRequiredService<BusySwitch>().On = true;

        using var response = await desktop.PostAsync(SyncRoutes.Absolute(SyncRoutes.ConnectorSecret), content: null, Cancellation);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(
            SyncErrorCodes.ReplicaBusy,
            (await response.Content.ReadFromJsonAsync<ProblemBody>(Cancellation))?.Code);
    }

    [Theory]
    [InlineData("GET", SyncRoutes.Connector)]
    [InlineData("POST", SyncRoutes.ConnectorSecret)]
    [InlineData("DELETE", SyncRoutes.ConnectorSecret)]
    public async Task The_owner_endpoints_need_a_device_token(string method, string route)
    {
        using var response = await Client().SendAsync(new HttpRequestMessage(new HttpMethod(method), SyncRoutes.Absolute(route)), Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("GET", SyncRoutes.Connector)]
    [InlineData("POST", SyncRoutes.ConnectorSecret)]
    [InlineData("DELETE", SyncRoutes.ConnectorSecret)]
    public async Task A_device_of_another_owner_may_not_manage_the_connector(string method, string route)
    {
        var stranger = Client().Bearing(TestTokens.SignedWith(SyncServiceFactory.SigningKey));

        using var response = await stranger.SendAsync(new HttpRequestMessage(new HttpMethod(method), SyncRoutes.Absolute(route)), Cancellation);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(SyncErrorCodes.ConnectorNotOwner, (await response.Content.ReadFromJsonAsync<ProblemBody>(Cancellation))?.Code);
    }

    [Fact]
    public void An_issuer_with_a_path_stops_the_start()
    {
        using var misconfigured = new SyncServiceFactory
        {
            MoreConfiguration = [("Modules:Sync:Connector:Issuer", "https://sync.test/sync")],
        };

        var failure = Assert.ThrowsAny<OptionsValidationException>(() => misconfigured.CreateClient());
        Assert.Contains("Modules:Sync:Connector:Issuer", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Anywhere_but_development_a_missing_issuer_and_resource_stop_the_start()
    {
        // The deployed shape with everything but the connector; its other refusals (the
        // vault) arrive in the same aggregate, so the case reads the message.
        using var misconfigured = new SyncServiceFactory
        {
            Environment = "Production",
            Configuration = ("Sync:Cosmos:AccountEndpoint", "https://localhost:8081/"),
            MoreConfiguration =
            [
                ("ConnectionStrings:attachments", "Endpoint=https://localhost:10000/devstoreaccount1;ContainerName=attachments"),
                .. SyncServiceFactory.ConfiguredOwner,
            ],
        };

        var failure = Assert.ThrowsAny<Exception>(() => misconfigured.CreateClient());
        Assert.Contains("Modules:Sync:Connector:Issuer", failure.Message, StringComparison.Ordinal);
        Assert.Contains("Modules:Sync:Connector:Resource", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("scope", "inbox:capture")]
    [InlineData("resource", Resource)]
    [InlineData("code_challenge_method", "S256")]
    public async Task A_repeated_parameter_goes_back_to_claude_as_invalid_request(string name, string value)
    {
        await OwnerDesktopWithSecret();

        var back = await Redirected($"{AuthorizeAddress(EnrolmentPkce.CreateVerifier())}&{name}={Uri.EscapeDataString(value)}");

        Assert.StartsWith(Redirect + "?", back.AbsoluteUri, StringComparison.Ordinal);
        Assert.Equal("invalid_request", SyncClientExtensions.QueryValue(back, "error"));
        Assert.Equal(ClaudeState, SyncClientExtensions.QueryValue(back, "state"));
    }

    [Fact]
    public async Task A_repeated_state_goes_back_to_claude_as_invalid_request_without_a_state()
    {
        await OwnerDesktopWithSecret();

        var back = await Redirected($"{AuthorizeAddress(EnrolmentPkce.CreateVerifier())}&state=another");

        Assert.StartsWith(Redirect + "?", back.AbsoluteUri, StringComparison.Ordinal);
        Assert.Equal("invalid_request", SyncClientExtensions.QueryValue(back, "error"));
        Assert.Null(SyncClientExtensions.QueryValue(back, "state"));
    }

    [Theory]
    [InlineData("client_id", ClientId)]
    [InlineData("redirect_uri", Redirect)]
    public async Task A_repeated_client_or_redirect_is_refused_in_place(string name, string value)
    {
        await OwnerDesktopWithSecret();

        using var response = await NoRedirects().GetAsync(
            $"{AuthorizeAddress(EnrolmentPkce.CreateVerifier())}&{name}={Uri.EscapeDataString(value)}", Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task A_token_request_with_a_repeated_parameter_is_an_invalid_request()
    {
        var secret = await OwnerDesktopWithSecret();
        var first = await Connected(secret);

        using var refresh = await Token(Basic(secret), [.. RefreshGrant(first.RefreshToken), new("scope", "inbox:capture"), new("scope", "inbox:capture")]);
        using var stillLive = await Token(Basic(secret), RefreshGrant(first.RefreshToken));

        Assert.Equal(HttpStatusCode.BadRequest, refresh.StatusCode);
        Assert.Equal("invalid_request", await OAuthError(refresh));
        Assert.Equal(HttpStatusCode.OK, stillLive.StatusCode);
    }

    private HttpClient Client() => _service.CreateClient();

    private HttpClient NoRedirects() => _service.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>A desktop enrolled through the owner's sign-in, holding its device token.</summary>
    private async Task<HttpClient> OwnerDesktop()
    {
        var verifier = EnrolmentPkce.CreateVerifier();
        var client = Client();
        var signIn = await client.StartEnrolment(verifier);
        var loopback = await Redirected(CallbackAddress(OwnerCode, SyncClientExtensions.QueryValue(signIn, "state")));

        using var registered = await client.PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.RegisterDevice),
            new RegisterDeviceRequest("Study desktop", SyncClientExtensions.QueryValue(loopback, "code"), verifier),
            Cancellation);
        var device = (await registered.Content.ReadFromJsonAsync<DeviceRegistrationResponse>(Cancellation))!;

        return client.Bearing(await client.DeviceToken(device));
    }

    private async Task<string> CreateSecret(HttpClient desktop)
    {
        using var created = await desktop.PostAsync(SyncRoutes.Absolute(SyncRoutes.ConnectorSecret), content: null, Cancellation);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        return (await created.Content.ReadFromJsonAsync<ConnectorSecretResponse>(Cancellation))!.ClientSecret;
    }

    private async Task<string> OwnerDesktopWithSecret() => await CreateSecret(await OwnerDesktop());

    private static string AuthorizeAddress(
        string verifier,
        string clientId = ClientId,
        string redirect = Redirect,
        string? challenge = "",
        string? method = "S256")
    {
        var parameters = new List<(string, string?)>
        {
            ("response_type", "code"),
            ("client_id", clientId),
            ("redirect_uri", redirect),
            ("code_challenge", challenge == string.Empty ? EnrolmentPkce.Challenge(verifier) : challenge),
            ("code_challenge_method", method),
            ("scope", "inbox:capture"),
            ("state", ClaudeState),
            ("resource", Resource),
        };

        return SyncRoutes.ConnectorAuthorize + "?" + string.Join('&', parameters
            .Where(p => p.Item2 is not null)
            .Select(p => $"{p.Item1}={Uri.EscapeDataString(p.Item2!)}"));
    }

    private static string CallbackAddress(string code, string? state) =>
        $"{SyncRoutes.GitHubCallback}?code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(state ?? string.Empty)}";

    /// <summary>A GET answered with a redirect, and where it sends the browser.</summary>
    private async Task<Uri> Redirected(string address)
    {
        using var response = await NoRedirects().GetAsync(address, Cancellation);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return response.Headers.Location!;
    }

    /// <summary>claude.ai's authorize and the owner's GitHub sign-in, up to the consent page's address.</summary>
    private async Task<string> ConsentAddress(string verifier)
    {
        var gitHub = await Redirected(AuthorizeAddress(verifier));
        var consent = await Redirected(CallbackAddress(OwnerCode, SyncClientExtensions.QueryValue(gitHub, "state")));

        Assert.StartsWith(SyncRoutes.ConnectorConsent + "?", consent.OriginalString, StringComparison.Ordinal);
        return consent.OriginalString;
    }

    /// <summary>The owner's answer on the consent page, and where it sends the browser.</summary>
    private async Task<Uri> Decide(string consentAddress, string decision)
    {
        var ticket = SyncClientExtensions.QueryValue(new Uri(new Uri("https://sync.test"), consentAddress), "ticket")!;
        using var form = new FormUrlEncodedContent([new("ticket", ticket), new("decision", decision)]);

        using var response = await NoRedirects().PostAsync(SyncRoutes.ConnectorConsent, form, Cancellation);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return response.Headers.Location!;
    }

    private async Task<string> AuthorizationCode(string verifier)
    {
        var back = await Decide(await ConsentAddress(verifier), "allow");

        Assert.Equal(ClaudeState, SyncClientExtensions.QueryValue(back, "state"));
        return SyncClientExtensions.QueryValue(back, "code")!;
    }

    private async Task<ConnectorTokenResponse> Connected(string secret)
    {
        var verifier = EnrolmentPkce.CreateVerifier();
        using var response = await Token(Basic(secret), CodeGrant(await AuthorizationCode(verifier), verifier));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ConnectorTokenResponse>(Cancellation))!;
    }

    private static AuthenticationHeaderValue Basic(string secret) =>
        new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ClientId}:{Uri.EscapeDataString(secret)}")));

    private static KeyValuePair<string, string>[] CodeGrant(string code, string verifier) =>
    [
        new("grant_type", "authorization_code"),
        new("code", code),
        new("redirect_uri", Redirect),
        new("code_verifier", verifier),
    ];

    private static KeyValuePair<string, string>[] RefreshGrant(string refreshToken) =>
    [
        new("grant_type", "refresh_token"),
        new("refresh_token", refreshToken),
    ];

    private async Task<HttpResponseMessage> Token(AuthenticationHeaderValue? authorization, KeyValuePair<string, string>[] form)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, SyncRoutes.ConnectorToken)
        {
            Content = new FormUrlEncodedContent(form),
        };
        request.Headers.Authorization = authorization;

        return await NoRedirects().SendAsync(request, Cancellation);
    }

    private static async Task<string?> OAuthError(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation));
        return body.RootElement.GetProperty("error").GetString();
    }

    private static string?[] Strings(JsonElement element, string name) =>
        [.. element.GetProperty(name).EnumerateArray().Select(e => e.GetString())];

    /// <summary>Turns the client store's throttling on for one test.</summary>
    private sealed class BusySwitch
    {
        public bool On { get; set; }
    }

    /// <summary>The in-memory client store, answering as a throttled replica while the switch is on.</summary>
    private sealed class SwitchableClientStore(InMemoryConnectorClientStore inner, BusySwitch busy) : IConnectorClientStore
    {
        public Task<ConnectorClient?> Find(string clientId, CancellationToken cancellationToken = default) =>
            busy.On ? throw Busy() : inner.Find(clientId, cancellationToken);

        public Task Save(ConnectorClient client, CancellationToken cancellationToken = default) =>
            busy.On ? throw Busy() : inner.Save(client, cancellationToken);

        public Task StampLastUsed(string clientId, int generation, DateTimeOffset at, CancellationToken cancellationToken = default) =>
            inner.StampLastUsed(clientId, generation, at, cancellationToken);

        private static SyncReplicaException Busy() => new(SyncErrorCodes.ReplicaBusy, "The connector store is busy.");
    }
}
