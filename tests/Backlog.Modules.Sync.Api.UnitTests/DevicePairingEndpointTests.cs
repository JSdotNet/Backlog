using System.Net;
using System.Net.Http.Json;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Sync.Api.UnitTests;

/// <summary>
/// The pairing exchange over HTTP, and the isolation it is there to produce:
/// two owners on one service, neither able to see the other.
/// </summary>
public class DevicePairingEndpointTests : IDisposable
{
    private readonly SyncServiceFactory _service = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _service.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Register_pair_and_sync()
    {
        var desktop = _service.CreateClient();
        var registration = await desktop.RegisterDevice("Study desktop");
        desktop.Bearing(await desktop.DeviceToken(registration));

        var codeResponse = await desktop.PostAsync(
            SyncRoutes.Absolute(SyncRoutes.PairingCodes), content: null, Cancellation);

        Assert.Equal(HttpStatusCode.Created, codeResponse.StatusCode);
        var code = (await codeResponse.Content.ReadFromJsonAsync<PairingCodeResponse>(Cancellation))!;
        Assert.True(PairingCodeFormat.IsWellFormed(code.Code));

        var phone = _service.CreateClient();
        var pairResponse = await phone.PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.RedeemPairingCode),
            new RedeemPairingCodeRequest(PairingCodeFormat.Display(code.Code), "Phone"),
            Cancellation);

        Assert.Equal(HttpStatusCode.Created, pairResponse.StatusCode);
        var paired = (await pairResponse.Content.ReadFromJsonAsync<DeviceRegistrationResponse>(Cancellation))!;
        Assert.Equal(registration.OwnerId, paired.OwnerId);
        Assert.NotEqual(registration.DeviceId, paired.DeviceId);

        phone.Bearing(await phone.DeviceToken(paired));

        await phone.PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.Inbox), new CaptureRequest("Call the dentist", "phone"), Cancellation);

        var inbox = await desktop.GetFromJsonAsync<List<InboxItem>>(
            SyncRoutes.Absolute(SyncRoutes.Inbox), Cancellation);

        // The capture crossed devices because both tokens name one owner.
        Assert.Equal("Call the dentist", Assert.Single(inbox!).Title);
    }

    [Fact]
    public async Task Describing_the_owner_reports_both_devices_after_a_pair()
    {
        var desktop = _service.CreateClient();
        var registration = await desktop.RegisterDevice("Study desktop");
        desktop.Bearing(await desktop.DeviceToken(registration));

        var code = await desktop.MintPairingCode();

        await _service.CreateClient().PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.RedeemPairingCode),
            new RedeemPairingCodeRequest(code.Code, "Phone"),
            Cancellation);

        var status = await desktop.GetFromJsonAsync<DeviceStatusResponse>(
            SyncRoutes.Absolute(SyncRoutes.DeviceStatus), Cancellation);

        Assert.Equal(registration.OwnerId, status!.OwnerId);
        Assert.Equal(registration.DeviceId, status.DeviceId);
        Assert.Equal("Study desktop", status.DeviceName);
        Assert.Equal(2, status.PairedDeviceCount);
    }

    [Fact]
    public async Task A_code_nobody_issued_is_not_found()
    {
        var response = await _service.CreateClient().PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.RedeemPairingCode),
            new RedeemPairingCodeRequest("23456789", "Phone"),
            Cancellation);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemBody>(Cancellation);
        Assert.EndsWith(SyncErrorCodes.PairingCodeNotFound, problem?.Type);
        Assert.Equal(404, problem?.Status);
    }

    [Fact]
    public async Task A_code_that_is_not_a_code_is_a_bad_request()
    {
        var response = await _service.CreateClient().PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.RedeemPairingCode),
            new RedeemPairingCodeRequest("nope", "Phone"),
            Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemBody>(Cancellation);
        Assert.EndsWith(SyncErrorCodes.PairingCodeMalformed, problem?.Type);
    }

    [Fact]
    public async Task The_same_code_cannot_pair_two_devices()
    {
        var desktop = _service.CreateClient();
        desktop.Bearing(await desktop.DeviceToken(await desktop.RegisterDevice("Study desktop")));

        var code = await desktop.MintPairingCode();

        var first = await _service.CreateClient().PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.RedeemPairingCode),
            new RedeemPairingCodeRequest(code.Code, "Phone"),
            Cancellation);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await _service.CreateClient().PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.RedeemPairingCode),
            new RedeemPairingCodeRequest(code.Code, "Tablet"),
            Cancellation);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var problem = await second.Content.ReadFromJsonAsync<ProblemBody>(Cancellation);
        Assert.EndsWith(SyncErrorCodes.PairingCodeUsed, problem?.Type);
    }

    [Fact]
    public async Task A_device_only_ever_sees_its_own_owners_captures()
    {
        var mine = await _service.CreateClient().RegisteredDevice("Mine");
        var theirs = await _service.CreateClient().RegisteredDevice("Theirs");

        var captured = await mine.PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.Inbox), new CaptureRequest("My secret", "desktop"), Cancellation);

        var item = (await captured.Content.ReadFromJsonAsync<InboxItem>(Cancellation))!;

        var theirInbox = await theirs.GetFromJsonAsync<List<InboxItem>>(
            SyncRoutes.Absolute(SyncRoutes.Inbox), Cancellation);

        Assert.Empty(theirInbox!);

        // Knowing the id is not enough: the lookup starts from the owner in the
        // token, so the item is not there to be found.
        var acknowledged = await theirs.PostAsync(
            SyncRoutes.AcknowledgeInboxItemFor(item.Id), content: null, Cancellation);

        Assert.Equal(HttpStatusCode.NotFound, acknowledged.StatusCode);

        var mineStill = await mine.GetFromJsonAsync<List<InboxItem>>(
            SyncRoutes.Absolute(SyncRoutes.Inbox), Cancellation);

        Assert.Single(mineStill!);
    }

    [Fact]
    public async Task An_owner_acknowledges_its_own_capture()
    {
        var device = await _service.CreateClient().RegisteredDevice("Study desktop");

        var captured = await device.PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.Inbox), new CaptureRequest("Call the dentist", "desktop"), Cancellation);

        Assert.Equal(HttpStatusCode.Created, captured.StatusCode);
        var item = (await captured.Content.ReadFromJsonAsync<InboxItem>(Cancellation))!;

        var acknowledged = await device.PostAsync(
            SyncRoutes.AcknowledgeInboxItemFor(item.Id), content: null, Cancellation);

        Assert.Equal(HttpStatusCode.NoContent, acknowledged.StatusCode);
        Assert.Empty((await device.GetFromJsonAsync<List<InboxItem>>(
            SyncRoutes.Absolute(SyncRoutes.Inbox), Cancellation))!);
    }
}

internal static class PairingCodeClientExtensions
{
    internal static async Task<PairingCodeResponse> MintPairingCode(this HttpClient client)
    {
        var response = await client.PostAsync(
            SyncRoutes.Absolute(SyncRoutes.PairingCodes), content: null, TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<PairingCodeResponse>(
            TestContext.Current.CancellationToken))!;
    }
}
