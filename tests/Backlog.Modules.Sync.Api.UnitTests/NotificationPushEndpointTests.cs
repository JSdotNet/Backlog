using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backlog.Infrastructure.NotificationHubs;
using Backlog.Infrastructure.NotificationHubs.Extensions;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Adapters;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Ports;
using Microsoft.Azure.NotificationHubs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backlog.Modules.Sync.Api.UnitTests;

/// <summary>
/// The service's half of the Android push (local ADR 0038 §4): a paired phone registers its FCM
/// token and gets one installation tagged with its owner and device, both from its device token;
/// a notification that is published or moves state is pushed to the owner, and nothing else is;
/// a hub that fails never fails the push; and a revoked phone's installation goes. The hub itself
/// is behind <see cref="INotificationPushChannel"/>, replaced here by a recording one.
/// </summary>
public class NotificationPushEndpointTests : IDisposable
{
    private const string SendConnectionString =
        "Endpoint=sb://ntfns-test.servicebus.windows.net/;SharedAccessKeyName=sync-send;SharedAccessKey=c2VuZC1vbmx5LWtleQ==";

    private const string ManageConnectionString =
        "Endpoint=sb://ntfns-test.servicebus.windows.net/;SharedAccessKeyName=sync-manage;SharedAccessKey=bWFuYWdlLWtleQ==";

    private readonly RecordingPushChannel _channel = new();

    private readonly SyncServiceFactory _service;

    public NotificationPushEndpointTests()
    {
        _service = new SyncServiceFactory
        {
            TestServices = services =>
            {
                services.RemoveAll<INotificationPushChannel>();
                services.AddSingleton<INotificationPushChannel>(_channel);
            },
        };
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _service.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task An_unpaired_caller_cannot_register()
    {
        var anonymous = _service.CreateClient();

        var response = await anonymous.RegisterPush("fcm-token-1");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_channel.Registered);
    }

    [Fact]
    public async Task A_paired_phone_registers_an_installation_under_its_own_owner_and_device()
    {
        var phone = await _service.CreateClient().RegisteredDevice("Pixel 8");
        var me = await phone.GetFromJsonAsync<DeviceStatusResponse>(SyncRoutes.Absolute(SyncRoutes.DeviceStatus), Cancellation);

        var response = await phone.RegisterPush("fcm-token-1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<RegisterPushInstallationResponse>(Cancellation))!.Registered);
        var registered = Assert.Single(_channel.Registered);
        Assert.Equal(me!.OwnerId, registered.Scope.OwnerId.Value);
        Assert.Equal(me.DeviceId, registered.Scope.DeviceId.Value);
        Assert.Equal("fcm-token-1", registered.Token);
    }

    /// <summary>Nothing in the body can choose whose phone this is: an owner or a device named
    /// there is not read.</summary>
    [Fact]
    public async Task The_body_cannot_name_another_owner_or_device()
    {
        var phone = await _service.CreateClient().RegisteredDevice("Pixel 8");
        var me = await phone.GetFromJsonAsync<DeviceStatusResponse>(SyncRoutes.Absolute(SyncRoutes.DeviceStatus), Cancellation);

        var response = await phone.PutAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.NotificationPush),
            new { fcmToken = "fcm-token-1", ownerId = Guid.CreateVersion7(), deviceId = Guid.CreateVersion7() },
            Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var registered = Assert.Single(_channel.Registered);
        Assert.Equal(me!.OwnerId, registered.Scope.OwnerId.Value);
        Assert.Equal(me.DeviceId, registered.Scope.DeviceId.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task A_blank_token_is_refused_as_push_token_invalid(string? token)
    {
        var phone = await _service.CreateClient().RegisteredDevice("Pixel 8");

        var response = await phone.RegisterPush(token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(SyncErrorCodes.PushTokenInvalid, (await response.Content.ReadFromJsonAsync<ProblemBody>(Cancellation))?.Code);
        Assert.Empty(_channel.Registered);
    }

    [Fact]
    public async Task An_oversized_token_is_refused_as_push_token_invalid()
    {
        var phone = await _service.CreateClient().RegisteredDevice("Pixel 8");

        var response = await phone.RegisterPush(new string('x', NotificationPushLimits.FcmTokenLength + 1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(SyncErrorCodes.PushTokenInvalid, (await response.Content.ReadFromJsonAsync<ProblemBody>(Cancellation))?.Code);
        Assert.Empty(_channel.Registered);
    }

    /// <summary>The Aspire run: no hub, so the module's no-op channel stands, and the phone is
    /// told it is not registered rather than to try again.</summary>
    [Fact]
    public async Task A_service_without_a_hub_answers_not_registered()
    {
        using var bare = new SyncServiceFactory();
        var phone = await bare.CreateClient().RegisteredDevice("Pixel 8");

        var response = await phone.RegisterPush("fcm-token-1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False((await response.Content.ReadFromJsonAsync<RegisterPushInstallationResponse>(Cancellation))!.Registered);
        Assert.IsType<NoNotificationPushChannel>(bare.Services.GetRequiredService<INotificationPushChannel>());
    }

    /// <summary>A hub that exists but did not take the installation just now is a 503 under its
    /// own code, so the phone tries again later.</summary>
    [Fact]
    public async Task A_hub_that_cannot_register_answers_503_push_unavailable()
    {
        _channel.FailRegister = true;
        var phone = await _service.CreateClient().RegisteredDevice("Pixel 8");

        var response = await phone.RegisterPush("fcm-token-1");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(SyncErrorCodes.PushUnavailable, (await response.Content.ReadFromJsonAsync<ProblemBody>(Cancellation))?.Code);
    }

    [Fact]
    public async Task A_published_notification_is_pushed_to_its_owner_once()
    {
        var desktop = await _service.CreateClient().RegisteredDevice("Study desktop");
        var owner = (await desktop.GetFromJsonAsync<DeviceStatusResponse>(SyncRoutes.Absolute(SyncRoutes.DeviceStatus), Cancellation))!.OwnerId;
        var id = Guid.CreateVersion7();

        var pushed = await desktop.PushNotifications(Previewed(id, NotificationStates.Open, DateTimeOffset.UtcNow));

        Assert.Equal(HttpStatusCode.OK, pushed.StatusCode);
        var sent = Assert.Single(_channel.Sent);
        Assert.Equal(owner, sent.Owner.Value);
        Assert.Equal(id.ToString("D"), sent.Message.Data[NotificationPushData.Id]);
        Assert.Equal("Run the migration?", sent.Message.Data[NotificationPushData.Title]);
    }

    /// <summary>The owner's ten-minute presence refresh re-pushes every open notification; the
    /// phone hears about none of them again, and hears the state change that follows.</summary>
    [Fact]
    public async Task A_re_push_in_the_same_state_sends_nothing_and_a_state_change_sends_one()
    {
        var desktop = await _service.CreateClient().RegisteredDevice("Study desktop");
        var id = Guid.CreateVersion7();
        var published = DateTimeOffset.UtcNow;

        await desktop.PushNotifications(Previewed(id, NotificationStates.Open, published));
        _channel.Sent.Clear();

        var refreshed = await desktop.PushNotifications(Previewed(id, NotificationStates.Open, published.AddMinutes(10)));

        Assert.Equal(1, (await refreshed.Content.ReadFromJsonAsync<PushNotificationsResponse>(Cancellation))!.Accepted);
        Assert.Empty(_channel.Sent);

        await desktop.PushNotifications(Previewed(id, NotificationStates.Answered, published.AddMinutes(11)));

        var sent = Assert.Single(_channel.Sent);
        Assert.Equal(NotificationStates.Answered, sent.Message.Data[NotificationPushData.State]);
    }

    [Fact]
    public async Task A_change_held_under_another_owner_instance_sends_nothing()
    {
        var desktop = await _service.CreateClient().RegisteredDevice("Study desktop");
        var id = Guid.CreateVersion7();

        await desktop.PushNotifications(Previewed(id, NotificationStates.Open, DateTimeOffset.UtcNow));
        _channel.Sent.Clear();

        await desktop.PushNotifications(
            Previewed(id, NotificationStates.Closed, DateTimeOffset.UtcNow.AddMinutes(1)) with { OwnerInstanceId = Guid.CreateVersion7() });

        Assert.Empty(_channel.Sent);
    }

    /// <summary>The write has landed by the time the hub is asked, so a hub that fails costs the
    /// phone its buzz and never costs the owner its push.</summary>
    [Fact]
    public async Task A_hub_that_fails_to_send_does_not_fail_the_push()
    {
        _channel.FailSend = true;
        var desktop = await _service.CreateClient().RegisteredDevice("Study desktop");

        var pushed = await desktop.PushNotifications(
            Previewed(Guid.CreateVersion7(), NotificationStates.Open, DateTimeOffset.UtcNow),
            Previewed(Guid.CreateVersion7(), NotificationStates.Open, DateTimeOffset.UtcNow));

        Assert.Equal(HttpStatusCode.OK, pushed.StatusCode);
        Assert.Equal(2, (await pushed.Content.ReadFromJsonAsync<PushNotificationsResponse>(Cancellation))!.Accepted);
    }

    [Fact]
    public async Task A_preview_past_its_bounds_is_refused_as_notification_invalid()
    {
        var desktop = await _service.CreateClient().RegisteredDevice("Study desktop");
        var change = Previewed(Guid.CreateVersion7(), NotificationStates.Open, DateTimeOffset.UtcNow);

        var pushed = await desktop.PushNotifications(change with { Preview = change.Preview! with { Title = new string('t', 81) } });

        Assert.Equal(HttpStatusCode.BadRequest, pushed.StatusCode);
        Assert.Equal(SyncErrorCodes.NotificationInvalid, (await pushed.Content.ReadFromJsonAsync<ProblemBody>(Cancellation))?.Code);
    }

    [Fact]
    public async Task Revoking_a_phone_removes_its_installation()
    {
        var (desktop, phone) = await DesktopAndPairedPhone();

        var revoked = await desktop.DeleteAsync(SyncRoutes.DeviceFor(phone.DeviceId), Cancellation);

        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        Assert.Equal(phone.DeviceId, Assert.Single(_channel.Removed).Value);
    }

    [Fact]
    public async Task A_hub_that_fails_to_remove_the_installation_still_revokes_the_phone()
    {
        _channel.FailRemove = true;
        var (desktop, phone) = await DesktopAndPairedPhone();

        var revoked = await desktop.DeleteAsync(SyncRoutes.DeviceFor(phone.DeviceId), Cancellation);

        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        var listed = (await desktop.GetFromJsonAsync<DeviceListResponse>(SyncRoutes.Absolute(SyncRoutes.Devices), Cancellation))!;
        Assert.DoesNotContain(listed.Devices, device => device.DeviceId == phone.DeviceId);
    }

    [Fact]
    public async Task A_refused_revoke_removes_no_installation()
    {
        var desktop = await _service.CreateClient().RegisteredDevice("Study desktop");

        var revoked = await desktop.DeleteAsync(SyncRoutes.DeviceFor(Guid.CreateVersion7()), Cancellation);

        Assert.Equal(HttpStatusCode.NotFound, revoked.StatusCode);
        Assert.Empty(_channel.Removed);
    }

    /// <summary>FCM v1's native shape, data-only — no <c>notification</c> key, so the app's
    /// service runs while it is in the background — at high priority, every value a string.</summary>
    [Fact]
    public void The_hub_is_sent_a_data_only_fcm_v1_message_at_high_priority()
    {
        var message = new NotificationPushMessage(
            Guid.Parse("0193a6f4-7b8c-7d2e-9f10-112233445566"),
            new Dictionary<string, string> { ["id"] = "0193a6f4-7b8c-7d2e-9f10-112233445566", ["state"] = "open", ["title"] = "Run it?" });

        using var payload = JsonDocument.Parse(NotificationHubsPushChannel.FcmPayload(message));

        var root = payload.RootElement.GetProperty("message");
        Assert.Equal("high", root.GetProperty("android").GetProperty("priority").GetString());
        Assert.False(root.TryGetProperty("notification", out _));
        var data = root.GetProperty("data");
        Assert.Equal(
            ["id:0193a6f4-7b8c-7d2e-9f10-112233445566", "state:open", "title:Run it?"],
            data.EnumerateObject().Select(field => $"{field.Name}:{field.Value.GetString()}").Order(StringComparer.Ordinal));
        Assert.All(data.EnumerateObject(), field => Assert.Equal(JsonValueKind.String, field.Value.ValueKind));
    }

    [Fact]
    public void A_push_goes_to_the_owners_tag()
    {
        var owner = new OwnerId(Guid.Parse("0193a6f4-7b8c-7d2e-9f10-112233445566"));

        Assert.Equal("owner:0193a6f47b8c7d2e9f10112233445566", NotificationHubsPushChannel.OwnerTag(owner));
    }

    [Fact]
    public void The_installation_is_the_device_tagged_with_its_owner_and_itself()
    {
        var scope = new OwnerScope(
            new OwnerId(Guid.Parse("0193a6f4-7b8c-7d2e-9f10-112233445566")),
            new DeviceId(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001")));

        var installation = NotificationHubsPushChannel.InstallationFor(scope, "fcm-token-1");

        Assert.Equal("aaaaaaaa-0000-0000-0000-000000000001", installation.InstallationId);
        Assert.Equal(NotificationPlatform.FcmV1, installation.Platform);
        Assert.Equal("fcm-token-1", installation.PushChannel);
        Assert.Equal(
            ["device:aaaaaaaa000000000000000000000001", "owner:0193a6f47b8c7d2e9f10112233445566"],
            installation.Tags.Order(StringComparer.Ordinal));
    }

    /// <summary>The deployed shape: Bicep sets both connection strings, and the Notification Hubs
    /// channel replaces the no-op one. Resolving it opens no connection.</summary>
    [Fact]
    public void Configured_connection_strings_put_the_notification_hubs_channel_in_place()
    {
        using var deployed = new SyncServiceFactory
        {
            MoreConfiguration =
            [
                (PushChannelRegistration.SendConnectionStringKey, SendConnectionString),
                (PushChannelRegistration.ManageConnectionStringKey, ManageConnectionString),
                (PushChannelRegistration.HubKey, "backlog"),
            ],
        };

        Assert.IsType<NotificationHubsPushChannel>(deployed.Services.GetRequiredService<INotificationPushChannel>());
    }

    [Fact]
    public void A_send_connection_string_without_a_manage_one_refuses_to_start()
    {
        using var misconfigured = new SyncServiceFactory
        {
            MoreConfiguration = [(PushChannelRegistration.SendConnectionStringKey, SendConnectionString)],
        };

        var thrown = Assert.ThrowsAny<Exception>(() => misconfigured.Services.GetRequiredService<INotificationPushChannel>());

        Assert.Contains(PushChannelRegistration.ManageConnectionStringKey, Flatten(thrown), StringComparison.Ordinal);
    }

    /// <summary>Guideline ADR 0018: a connection string that is not one fails the start, naming
    /// its key — rather than passing it and failing every owner push the first time the channel
    /// is resolved.</summary>
    [Theory]
    [InlineData(PushChannelRegistration.SendConnectionStringKey)]
    [InlineData(PushChannelRegistration.ManageConnectionStringKey)]
    public void A_malformed_connection_string_refuses_to_start_naming_its_key(string key)
    {
        using var misconfigured = new SyncServiceFactory
        {
            MoreConfiguration =
            [
                (PushChannelRegistration.SendConnectionStringKey, key == PushChannelRegistration.SendConnectionStringKey ? "not-a-connection-string" : SendConnectionString),
                (PushChannelRegistration.ManageConnectionStringKey, key == PushChannelRegistration.ManageConnectionStringKey ? "Endpoint=sb://ntfns-test.servicebus.windows.net/" : ManageConnectionString),
            ],
        };

        var thrown = Assert.ThrowsAny<Exception>(() => misconfigured.Services.GetRequiredService<INotificationPushChannel>());

        Assert.Contains(key, Flatten(thrown), StringComparison.Ordinal);
    }

    internal static NotificationChange Previewed(Guid id, string state, DateTimeOffset updatedAt) =>
        NotificationSyncEndpointTests.Change(id, state, "Run the migration?", updatedAt) with
        {
            Preview = new NotificationPushPreview(
                NotificationKinds.Question, NotificationSeverities.Warning, "Run the migration?", "Three tables move.", IsPrivate: false),
        };

    private static string Flatten(Exception exception) =>
        exception.InnerException is null ? exception.Message : exception.Message + " " + Flatten(exception.InnerException);

    private async Task<(HttpClient Desktop, DeviceRegistrationResponse Phone)> DesktopAndPairedPhone()
    {
        var desktop = await _service.CreateClient().RegisteredDevice("Study desktop");
        var code = await desktop.MintPairingCode();

        var paired = await _service.CreateClient().PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.RedeemPairingCode),
            new RedeemPairingCodeRequest(code.Code, "Pixel 8"),
            Cancellation);

        return (desktop, (await paired.Content.ReadFromJsonAsync<DeviceRegistrationResponse>(Cancellation))!);
    }

    private sealed class RecordingPushChannel : INotificationPushChannel
    {
        public List<(OwnerScope Scope, string Token)> Registered { get; } = [];

        public List<DeviceId> Removed { get; } = [];

        public List<(OwnerId Owner, NotificationPushMessage Message)> Sent { get; } = [];

        public bool FailRegister { get; set; }

        public bool FailRemove { get; set; }

        public bool FailSend { get; set; }

        public Task<bool> RegisterAsync(OwnerScope scope, string fcmToken, CancellationToken cancellationToken = default)
        {
            if (FailRegister)
            {
                throw new SyncReplicaException(SyncErrorCodes.PushUnavailable, "The hub did not answer.");
            }

            lock (Registered) Registered.Add((scope, fcmToken));
            return Task.FromResult(true);
        }

        public Task RemoveAsync(DeviceId device, CancellationToken cancellationToken = default)
        {
            if (FailRemove)
            {
                throw new InvalidOperationException("The hub did not answer.");
            }

            lock (Removed) Removed.Add(device);
            return Task.CompletedTask;
        }

        public Task SendAsync(OwnerId owner, NotificationPushMessage message, CancellationToken cancellationToken = default)
        {
            if (FailSend)
            {
                throw new InvalidOperationException("The hub did not answer.");
            }

            lock (Sent) Sent.Add((owner, message));
            return Task.CompletedTask;
        }
    }
}

internal static class NotificationPushClientExtensions
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    internal static Task<HttpResponseMessage> RegisterPush(this HttpClient client, string? fcmToken) =>
        client.PutAsJsonAsync(SyncRoutes.Absolute(SyncRoutes.NotificationPush), new RegisterPushInstallationRequest(fcmToken), Cancellation);
}
