using System.Net;
using System.Net.Http.Json;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Sync.Api.UnitTests;

/// <summary>
/// What the sync service does with a caller it cannot place. The fallback
/// policy denies anything that has not opened itself, so these are about the
/// three ways a bearer token fails: absent, signed by somebody else, and out
/// of date.
/// </summary>
public class SyncEndpointAuthenticationTests : IDisposable
{
    private readonly SyncServiceFactory _service = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _service.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task The_inbox_is_closed_to_a_caller_with_no_token()
    {
        var response = await _service.CreateClient().GetAsync(SyncRoutes.Absolute(SyncRoutes.Inbox), Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<ProblemBody>(Cancellation);
        Assert.Equal(401, problem?.Status);
        Assert.False(string.IsNullOrWhiteSpace(problem?.TraceId));
    }

    [Fact]
    public async Task A_token_signed_by_somebody_else_is_refused()
    {
        var client = _service.CreateClient().Bearing(TestTokens.SignedWith(SyncServiceFactory.OtherSigningKey));

        var response = await client.GetAsync(SyncRoutes.Absolute(SyncRoutes.Inbox), Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task An_expired_token_is_refused()
    {
        var expired = TestTokens.SignedWith(SyncServiceFactory.SigningKey, TimeSpan.FromMinutes(-5));
        var client = _service.CreateClient().Bearing(expired);

        var response = await client.GetAsync(SyncRoutes.Absolute(SyncRoutes.Inbox), Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// A token another service minted, correct in every other respect. Issuer
    /// validation is what stops a token that is perfectly valid somewhere else
    /// from being valid here, and it is only worth having if it is checked.
    /// </summary>
    [Fact]
    public async Task A_token_from_another_issuer_is_refused()
    {
        var foreign = TestTokens.SignedWith(SyncServiceFactory.SigningKey, issuer: "https://someone-else.example/sts");
        var client = _service.CreateClient().Bearing(foreign);

        var response = await client.GetAsync(SyncRoutes.Absolute(SyncRoutes.Inbox), Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>
    /// A token this service issued for somewhere else. Without audience
    /// validation, a token minted for one relying party is a token for all of
    /// them.
    /// </summary>
    [Fact]
    public async Task A_token_for_another_audience_is_refused()
    {
        var elsewhere = TestTokens.SignedWith(SyncServiceFactory.SigningKey, audience: "somebody-elses-api");
        var client = _service.CreateClient().Bearing(elsewhere);

        var response = await client.GetAsync(SyncRoutes.Absolute(SyncRoutes.Inbox), Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>
    /// The right key, the right issuer, the right audience — and the wrong
    /// algorithm. The validator pins HS256, so this is refused on the algorithm
    /// alone. Nothing else in this file would catch a validator that accepted
    /// whatever the token's own header asked for, which is how algorithm
    /// confusion gets in.
    /// </summary>
    [Fact]
    public async Task A_token_signed_with_an_algorithm_the_service_does_not_issue_is_refused()
    {
        var hs512 = TestTokens.SignedWith(
            SyncServiceFactory.SigningKey,
            algorithm: Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha512);

        var client = _service.CreateClient().Bearing(hs512);

        var response = await client.GetAsync(SyncRoutes.Absolute(SyncRoutes.Inbox), Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Minting_a_pairing_code_needs_a_token_too()
    {
        var response = await _service.CreateClient()
            .PostAsync(SyncRoutes.Absolute(SyncRoutes.PairingCodes), content: null, Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_service_says_what_it_is_without_a_token()
    {
        var response = await _service.CreateClient().GetAsync("/", Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_wrong_credential_gets_no_token_and_says_why()
    {
        var client = _service.CreateClient();
        var device = await client.RegisterDevice("Study desktop");

        var response = await client.PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.DeviceToken),
            new DeviceTokenRequest(device.DeviceId, "not-the-credential"),
            Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemBody>(Cancellation);
        Assert.EndsWith(SyncErrorCodes.DeviceCredentialInvalid, problem?.Type);
        Assert.Equal(SyncErrorCodes.DeviceCredentialInvalid, problem?.Code);
    }

    [Fact]
    public void Anywhere_but_development_a_missing_signing_key_stops_the_start()
    {
        using var misconfigured = new SyncServiceFactory { ConfiguredSigningKey = null };

        // Inherited ADR 0018: bind, validate, fail fast. A service that started
        // anyway would sign tokens with nothing and only say so on the first
        // sync.
        Assert.ThrowsAny<Microsoft.Extensions.Options.OptionsValidationException>(
            () => misconfigured.CreateClient());
    }

    [Fact]
    public async Task A_development_run_starts_without_a_configured_signing_key()
    {
        using var development = new SyncServiceFactory { Environment = "Development", ConfiguredSigningKey = null };

        var client = development.CreateClient();
        var device = await client.RegisterDevice("Study desktop");
        client.Bearing(await client.DeviceToken(device));

        // The generated key is real enough to sign a token this same process
        // then validates; what it is not is stable across a restart.
        var response = await client.GetAsync(SyncRoutes.Absolute(SyncRoutes.Inbox), Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
