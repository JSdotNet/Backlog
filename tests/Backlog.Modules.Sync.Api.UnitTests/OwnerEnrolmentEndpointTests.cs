using System.Net;
using System.Net.Http.Json;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Backlog.Modules.Sync.Api.UnitTests;

/// <summary>
/// Local ADR 0058 §1 over HTTP: a first device enrols only through the configured
/// owner's GitHub sign-in, the code it gets back works once and only with its PKCE
/// verifier, and the same sign-in from a new device recovers the owner by revoking
/// every other device. GitHub itself is a stand-in that answers a user id per code.
/// </summary>
public sealed class OwnerEnrolmentEndpointTests : IDisposable
{
    private const long OwnerGitHubId = StubGitHubOwnerSignIn.OwnerGitHubId;
    private const string OwnerCode = StubGitHubOwnerSignIn.OwnerCode;
    private const string StrangerCode = StubGitHubOwnerSignIn.StrangerCode;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private readonly SyncServiceFactory _service = new()
    {
        MoreConfiguration = SyncServiceFactory.ConfiguredOwner,
        TestServices = services =>
        {
            services.RemoveAll<IGitHubOwnerSignIn>();
            services.AddSingleton<IGitHubOwnerSignIn>(new StubGitHubOwnerSignIn());
        },
    };

    public void Dispose() => _service.Dispose();

    [Fact]
    public async Task The_owners_sign_in_enrols_the_first_device_under_a_stable_owner()
    {
        var first = await Enrol("Study desktop");
        var again = await Enrol("Laptop");

        Assert.Equal(first.OwnerId, again.OwnerId);
        Assert.Equal(0, first.RevokedDeviceCount);
    }

    [Fact]
    public async Task The_start_sends_the_browser_to_GitHub_and_keeps_the_device_state_away_from_it()
    {
        var signIn = await Client().StartEnrolment(EnrolmentPkce.CreateVerifier(), state: "device-state");

        Assert.Equal("github.test", signIn.Host);
        Assert.NotNull(SyncClientExtensions.QueryValue(signIn, "state"));
        Assert.NotEqual("device-state", SyncClientExtensions.QueryValue(signIn, "state"));
    }

    [Fact]
    public async Task A_GitHub_user_other_than_the_owner_is_refused_and_gets_no_code()
    {
        var signIn = await Client().StartEnrolment(EnrolmentPkce.CreateVerifier());

        var loopback = await Callback(StrangerCode, SyncClientExtensions.QueryValue(signIn, "state"));

        Assert.StartsWith(SyncClientExtensions.Loopback.AbsoluteUri, loopback.AbsoluteUri, StringComparison.Ordinal);
        Assert.Equal("access_denied", SyncClientExtensions.QueryValue(loopback, "error"));
        Assert.Equal("device-state", SyncClientExtensions.QueryValue(loopback, "state"));
        Assert.Null(SyncClientExtensions.QueryValue(loopback, "code"));
    }

    [Fact]
    public async Task A_sign_in_GitHub_did_not_confirm_is_refused_and_gets_no_code()
    {
        var signIn = await Client().StartEnrolment(EnrolmentPkce.CreateVerifier());

        var loopback = await Callback("a-code-github-rejects", SyncClientExtensions.QueryValue(signIn, "state"));

        Assert.Equal("access_denied", SyncClientExtensions.QueryValue(loopback, "error"));
        Assert.Null(SyncClientExtensions.QueryValue(loopback, "code"));
    }

    [Fact]
    public async Task The_callback_state_works_once()
    {
        var signIn = await Client().StartEnrolment(EnrolmentPkce.CreateVerifier());
        var state = SyncClientExtensions.QueryValue(signIn, "state");
        await Callback(OwnerCode, state);

        using var replay = await NoRedirects().GetAsync(CallbackAddress(OwnerCode, state), Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        Assert.Equal(SyncErrorCodes.EnrolmentSignInUnknown, (await replay.Content.ReadFromJsonAsync<ProblemBody>(Cancellation))?.Code);
    }

    [Fact]
    public async Task The_enrolment_code_registers_one_device_only()
    {
        var request = await SignedInRequest("Study desktop");
        var client = Client();

        using var first = await client.PostAsJsonAsync(SyncRoutes.Absolute(SyncRoutes.RegisterDevice), request, Cancellation);
        using var second = await client.PostAsJsonAsync(SyncRoutes.Absolute(SyncRoutes.RegisterDevice), request with { DeviceName = "Intruder" }, Cancellation);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(SyncErrorCodes.EnrolmentCodeUsed, (await second.Content.ReadFromJsonAsync<ProblemBody>(Cancellation))?.Code);
    }

    [Fact]
    public async Task A_code_without_its_verifier_is_refused_and_is_not_spent()
    {
        var request = await SignedInRequest("Study desktop");
        var client = Client();

        using var stolen = await client.PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.RegisterDevice),
            request with { CodeVerifier = EnrolmentPkce.CreateVerifier() },
            Cancellation);
        using var genuine = await client.PostAsJsonAsync(SyncRoutes.Absolute(SyncRoutes.RegisterDevice), request, Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, stolen.StatusCode);
        Assert.Equal(SyncErrorCodes.EnrolmentVerifierMismatch, (await stolen.Content.ReadFromJsonAsync<ProblemBody>(Cancellation))?.Code);
        Assert.Equal(HttpStatusCode.Created, genuine.StatusCode);
    }

    [Fact]
    public async Task Registering_without_an_enrolment_code_creates_no_owner()
    {
        using var response = await Client().PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.RegisterDevice), new RegisterDeviceRequest("Anonymous"), Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(SyncErrorCodes.EnrolmentCodeRequired, (await response.Content.ReadFromJsonAsync<ProblemBody>(Cancellation))?.Code);
    }

    [Fact]
    public async Task An_unknown_enrolment_code_is_refused()
    {
        var request = await SignedInRequest("Study desktop");

        using var response = await Client().PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.RegisterDevice), request with { EnrolmentCode = "never-issued" }, Cancellation);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(SyncErrorCodes.EnrolmentCodeNotFound, (await response.Content.ReadFromJsonAsync<ProblemBody>(Cancellation))?.Code);
    }

    [Theory]
    [InlineData("https://127.0.0.1:49152/enrolment/")]
    [InlineData("http://example.com:49152/enrolment/")]
    [InlineData("http://127.0.0.1/enrolment/")]
    [InlineData("http://localhost:49152/enrolment/")]
    public async Task A_redirect_that_is_not_a_loopback_with_a_port_is_refused(string redirect)
    {
        var verifier = EnrolmentPkce.CreateVerifier();

        using var response = await Client().PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.EnrolmentStart),
            new StartEnrolmentRequest(new Uri(redirect), EnrolmentPkce.Challenge(verifier), EnrolmentPkce.Method, "s"),
            Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(SyncErrorCodes.EnrolmentRequestInvalid, (await response.Content.ReadFromJsonAsync<ProblemBody>(Cancellation))?.Code);
    }

    [Fact]
    public async Task A_plain_challenge_is_refused()
    {
        var verifier = EnrolmentPkce.CreateVerifier();

        using var response = await Client().PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.EnrolmentStart),
            new StartEnrolmentRequest(SyncClientExtensions.Loopback, verifier, "plain", "s"),
            Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Recovery_enrols_the_new_device_and_revokes_every_other_one()
    {
        var desktop = Client();
        var lost = await RegisterSignedIn(desktop, "Study desktop");
        desktop.Bearing(await desktop.DeviceToken(lost));

        // A phone paired by code, the way later devices still join.
        var issued = await desktop.PostAsync(SyncRoutes.Absolute(SyncRoutes.PairingCodes), content: null, Cancellation);
        var code = (await issued.Content.ReadFromJsonAsync<PairingCodeResponse>(Cancellation))!;
        var phoneClient = Client();
        var redeemed = await phoneClient.PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.RedeemPairingCode), new RedeemPairingCodeRequest(code.Code, "Phone"), Cancellation);
        Assert.Equal(HttpStatusCode.Created, redeemed.StatusCode);
        var phone = (await redeemed.Content.ReadFromJsonAsync<DeviceRegistrationResponse>(Cancellation))!;

        var recovered = Client();
        var replacement = await RegisterSignedIn(recovered, "New laptop");

        Assert.Equal(lost.OwnerId, replacement.OwnerId);
        Assert.Equal(2, replacement.RevokedDeviceCount);

        // Neither the lost desktop nor the phone can mint another token.
        foreach (var revoked in new[] { lost, phone })
        {
            using var token = await Client().PostAsJsonAsync(
                SyncRoutes.Absolute(SyncRoutes.DeviceToken),
                new DeviceTokenRequest(revoked.DeviceId, revoked.Credential),
                Cancellation);
            Assert.Equal(HttpStatusCode.Unauthorized, token.StatusCode);
        }

        recovered.Bearing(await recovered.DeviceToken(replacement));
        var list = await recovered.GetFromJsonAsync<DeviceListResponse>(SyncRoutes.Absolute(SyncRoutes.Devices), Cancellation);
        Assert.Equal([replacement.DeviceId], list!.Devices.Select(d => d.DeviceId));
    }

    [Fact]
    public void An_owner_without_the_OAuth_App_stops_the_start()
    {
        using var misconfigured = new SyncServiceFactory
        {
            MoreConfiguration = [("Owner:GitHubUserId", "7655166")],
        };

        var failure = Assert.ThrowsAny<OptionsValidationException>(() => misconfigured.CreateClient());
        Assert.Contains("GitHub:ClientSecret", failure.Message, StringComparison.Ordinal);
    }

    private HttpClient Client() => _service.CreateClient();

    private HttpClient NoRedirects() => _service.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private Task<DeviceRegistrationResponse> Enrol(string name) => RegisterSignedIn(Client(), name);

    /// <summary>The owner's sign-in end to end, registering through <paramref name="client"/>.</summary>
    private async Task<DeviceRegistrationResponse> RegisterSignedIn(HttpClient client, string name)
    {
        using var response = await client.PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.RegisterDevice), await SignedInRequest(name), Cancellation);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DeviceRegistrationResponse>(Cancellation))!;
    }

    /// <summary>The whole sign-in as the owner, ending in the request a device sends.</summary>
    private async Task<RegisterDeviceRequest> SignedInRequest(string name)
    {
        var verifier = EnrolmentPkce.CreateVerifier();
        var signIn = await Client().StartEnrolment(verifier);
        var loopback = await Callback(OwnerCode, SyncClientExtensions.QueryValue(signIn, "state"));

        return new RegisterDeviceRequest(name, SyncClientExtensions.QueryValue(loopback, "code"), verifier);
    }

    /// <summary>GitHub's redirect to the callback, answered with where the service sends the browser.</summary>
    private async Task<Uri> Callback(string code, string? state)
    {
        using var response = await NoRedirects().GetAsync(CallbackAddress(code, state), Cancellation);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return response.Headers.Location!;
    }

    private static string CallbackAddress(string code, string? state) =>
        $"{SyncRoutes.GitHubCallback}?code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(state ?? string.Empty)}";
}
