using System.Net;
using System.Net.Http.Json;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Sync.Api.UnitTests;

/// <summary>
/// The owner's device list and revoking a device over HTTP: what the issuing desktop reads
/// to see the device its pairing code paired, and what its person's Refuse does to it.
/// </summary>
public sealed class DeviceRosterEndpointTests : IDisposable
{
    private readonly SyncServiceFactory _service = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose() => _service.Dispose();

    [Fact]
    public async Task The_device_list_names_the_caller_and_the_device_whose_code_paired_each()
    {
        var (desktop, registration) = await Desktop();
        var phone = await Pair(desktop, "Pixel 8");

        var listed = (await desktop.GetFromJsonAsync<DeviceListResponse>(SyncRoutes.Absolute(SyncRoutes.Devices), Cancellation))!;

        Assert.Equal(registration.DeviceId, listed.CallerDeviceId);
        Assert.Equal([registration.DeviceId, phone.DeviceId], listed.Devices.Select(device => device.DeviceId));
        Assert.Null(listed.Devices[0].PairedBy);
        Assert.Equal(registration.DeviceId, listed.Devices[1].PairedBy);
        Assert.Equal("Pixel 8", listed.Devices[1].Name);
    }

    [Fact]
    public async Task A_revoked_device_can_no_longer_mint_a_token_and_leaves_the_list()
    {
        var (desktop, _) = await Desktop();
        var phone = await Pair(desktop, "Pixel 8");

        var revoked = await desktop.DeleteAsync(SyncRoutes.DeviceFor(phone.DeviceId), Cancellation);

        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);

        var token = await _service.CreateClient().PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.DeviceToken),
            new DeviceTokenRequest(phone.DeviceId, phone.Credential),
            Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, token.StatusCode);

        var listed = (await desktop.GetFromJsonAsync<DeviceListResponse>(SyncRoutes.Absolute(SyncRoutes.Devices), Cancellation))!;
        Assert.DoesNotContain(listed.Devices, device => device.DeviceId == phone.DeviceId);

        var again = await desktop.DeleteAsync(SyncRoutes.DeviceFor(phone.DeviceId), Cancellation);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.EndsWith(SyncErrorCodes.DeviceNotFound, (await again.Content.ReadFromJsonAsync<ProblemBody>(Cancellation))?.Type);
    }

    [Fact]
    public async Task A_device_cannot_revoke_itself_or_another_owners_device()
    {
        var (desktop, registration) = await Desktop();
        var (_, stranger) = await Desktop();

        var self = await desktop.DeleteAsync(SyncRoutes.DeviceFor(registration.DeviceId), Cancellation);
        var foreign = await desktop.DeleteAsync(SyncRoutes.DeviceFor(stranger.DeviceId), Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);
        Assert.EndsWith(SyncErrorCodes.DeviceRevokeSelf, (await self.Content.ReadFromJsonAsync<ProblemBody>(Cancellation))?.Type);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);

        // The stranger's device is still there to mint tokens.
        Assert.False(string.IsNullOrEmpty(await _service.CreateClient().DeviceToken(stranger)));
    }

    [Fact]
    public async Task Only_the_device_whose_code_paired_another_may_revoke_it()
    {
        var (desktop, registration) = await Desktop();
        var phone = await Pair(desktop, "Pixel 8");
        var laptop = await Pair(desktop, "Laptop");
        var phoneClient = _service.CreateClient();
        phoneClient.Bearing(await phoneClient.DeviceToken(phone));

        var issuer = await phoneClient.DeleteAsync(SyncRoutes.DeviceFor(registration.DeviceId), Cancellation);
        var sibling = await phoneClient.DeleteAsync(SyncRoutes.DeviceFor(laptop.DeviceId), Cancellation);

        Assert.Equal(HttpStatusCode.Forbidden, issuer.StatusCode);
        Assert.EndsWith(SyncErrorCodes.DeviceRevokeNotPairer, (await issuer.Content.ReadFromJsonAsync<ProblemBody>(Cancellation))?.Type);
        Assert.Equal(HttpStatusCode.Forbidden, sibling.StatusCode);

        // The desktop that is deciding still can.
        Assert.False(string.IsNullOrEmpty(await _service.CreateClient().DeviceToken(registration)));
    }

    [Fact]
    public async Task Listing_and_revoking_need_a_paired_device()
    {
        var anonymous = _service.CreateClient();

        var listed = await anonymous.GetAsync(SyncRoutes.Absolute(SyncRoutes.Devices), Cancellation);
        var revoked = await anonymous.DeleteAsync(SyncRoutes.DeviceFor(Guid.NewGuid()), Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, listed.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
    }

    private async Task<(HttpClient Client, DeviceRegistrationResponse Registration)> Desktop()
    {
        var desktop = _service.CreateClient();
        var registration = await desktop.RegisterDevice("Study desktop");
        desktop.Bearing(await desktop.DeviceToken(registration));
        return (desktop, registration);
    }

    private async Task<DeviceRegistrationResponse> Pair(HttpClient issuer, string name)
    {
        var code = await issuer.MintPairingCode();

        var paired = await _service.CreateClient().PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.RedeemPairingCode),
            new RedeemPairingCodeRequest(code.Code, name),
            Cancellation);

        paired.EnsureSuccessStatusCode();
        return (await paired.Content.ReadFromJsonAsync<DeviceRegistrationResponse>(Cancellation))!;
    }
}
