using System.Net;
using System.Net.Http.Json;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Tests;

namespace Backlog.Modules.Sync.Api.UnitTests;

/// <summary>
/// A device bound to a non-exportable key, over HTTP (local ADR 0052 §2): it
/// registers or pairs with its public key, signs a <c>private_key_jwt</c> for its
/// 30-minute token, and a credential device binds its key once on a token fetch.
/// </summary>
public sealed class DeviceKeyEndpointTests : IDisposable
{
    private readonly SyncServiceFactory _service = new();
    private readonly DeviceSigningKey _key = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _key.Dispose();
        _service.Dispose();
    }

    [Fact]
    public async Task A_key_bound_device_signs_for_a_thirty_minute_token_that_syncs()
    {
        var client = _service.CreateClient();
        var device = await RegisterKeyBound(client);

        Assert.Null(device.Credential);

        var response = await RequestToken(client, Signed(device.DeviceId, _key.Assertion(device.DeviceId, DateTimeOffset.UtcNow)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var token = (await response.Content.ReadFromJsonAsync<DeviceTokenResponse>(Cancellation))!;
        Assert.InRange(token.ExpiresAt - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(29), TimeSpan.FromMinutes(31));

        client.Bearing(token.AccessToken);
        var status = await client.GetFromJsonAsync<DeviceStatusResponse>(SyncRoutes.Absolute(SyncRoutes.DeviceStatus), Cancellation);
        Assert.Equal(device.DeviceId, status!.DeviceId);
    }

    [Fact]
    public async Task A_replayed_assertion_is_unauthorized()
    {
        var client = _service.CreateClient();
        var device = await RegisterKeyBound(client);
        var request = Signed(device.DeviceId, _key.Assertion(device.DeviceId, DateTimeOffset.UtcNow));

        Assert.Equal(HttpStatusCode.OK, (await RequestToken(client, request)).StatusCode);

        await AssertUnauthorized(await RequestToken(client, request));
    }

    [Fact]
    public async Task An_assertion_signed_by_another_key_is_unauthorized()
    {
        var client = _service.CreateClient();
        var device = await RegisterKeyBound(client);
        using var other = new DeviceSigningKey();

        await AssertUnauthorized(await RequestToken(client, Signed(device.DeviceId, other.Assertion(device.DeviceId, DateTimeOffset.UtcNow))));
    }

    [Fact]
    public async Task A_key_bound_device_has_no_credential_path()
    {
        var client = _service.CreateClient();
        var device = await RegisterKeyBound(client);

        await AssertUnauthorized(await RequestToken(client, new DeviceTokenRequest(device.DeviceId, "any-credential")));
    }

    [Fact]
    public async Task A_credential_device_binds_its_key_once_and_then_signs()
    {
        var client = _service.CreateClient();
        var device = await client.RegisterDevice("Study desktop");

        var migrated = await RequestToken(client, new DeviceTokenRequest(device.DeviceId, device.Credential, PublicKey: _key.PublicKey));
        Assert.Equal(HttpStatusCode.OK, migrated.StatusCode);

        await AssertUnauthorized(await RequestToken(client, new DeviceTokenRequest(device.DeviceId, device.Credential)));

        var signed = await RequestToken(client, Signed(device.DeviceId, _key.Assertion(device.DeviceId, DateTimeOffset.UtcNow)));
        Assert.Equal(HttpStatusCode.OK, signed.StatusCode);
    }

    [Fact]
    public async Task A_migration_with_a_key_that_is_not_one_is_a_bad_request()
    {
        var client = _service.CreateClient();
        var device = await client.RegisterDevice("Study desktop");

        var response = await RequestToken(client, new DeviceTokenRequest(device.DeviceId, device.Credential, PublicKey: "not-a-key"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemBody>(Cancellation);
        Assert.EndsWith(SyncErrorCodes.DevicePublicKeyInvalid, problem?.Type);
    }

    [Fact]
    public async Task A_device_pairs_with_its_public_key_and_signs_for_its_token()
    {
        var desktop = _service.CreateClient();
        desktop.Bearing(await desktop.DeviceToken(await desktop.RegisterDevice("Study desktop")));
        var code = await desktop.MintPairingCode();

        var phone = _service.CreateClient();
        var pairResponse = await phone.PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.RedeemPairingCode),
            new RedeemPairingCodeRequest(code.Code, "Laptop", _key.PublicKey),
            Cancellation);

        Assert.Equal(HttpStatusCode.Created, pairResponse.StatusCode);
        var paired = (await pairResponse.Content.ReadFromJsonAsync<DeviceRegistrationResponse>(Cancellation))!;
        Assert.Null(paired.Credential);

        var token = await RequestToken(phone, Signed(paired.DeviceId, _key.Assertion(paired.DeviceId, DateTimeOffset.UtcNow)));
        Assert.Equal(HttpStatusCode.OK, token.StatusCode);
    }

    [Fact]
    public async Task Registering_with_a_key_that_is_not_one_is_a_bad_request()
    {
        var client = _service.CreateClient();
        var request = await client.EnrolmentRequest("Study desktop") with { PublicKey = "AAAA" };

        var response = await client.PostAsJsonAsync(SyncRoutes.Absolute(SyncRoutes.RegisterDevice), request, Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemBody>(Cancellation);
        Assert.EndsWith(SyncErrorCodes.DevicePublicKeyInvalid, problem?.Type);
    }

    private async Task<DeviceRegistrationResponse> RegisterKeyBound(HttpClient client)
    {
        var request = await client.EnrolmentRequest("Study desktop") with { PublicKey = _key.PublicKey };
        var response = await client.PostAsJsonAsync(SyncRoutes.Absolute(SyncRoutes.RegisterDevice), request, Cancellation);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DeviceRegistrationResponse>(Cancellation))!;
    }

    private static DeviceTokenRequest Signed(Guid deviceId, string assertion) =>
        new(deviceId, ClientAssertionType: SyncAssertion.ClientAssertionType, ClientAssertion: assertion);

    private static Task<HttpResponseMessage> RequestToken(HttpClient client, DeviceTokenRequest request) =>
        client.PostAsJsonAsync(SyncRoutes.Absolute(SyncRoutes.DeviceToken), request, Cancellation);

    private static async Task AssertUnauthorized(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemBody>(Cancellation);
        Assert.EndsWith(SyncErrorCodes.DeviceCredentialInvalid, problem?.Type);
    }
}
