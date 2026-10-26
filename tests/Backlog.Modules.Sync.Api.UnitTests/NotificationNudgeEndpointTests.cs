using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Backlog.Infrastructure.WebPubSub;
using Backlog.Infrastructure.WebPubSub.Extensions;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Adapters;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Ports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backlog.Modules.Sync.Api.UnitTests;

/// <summary>
/// The service's half of the realtime nudge (local ADR 0039 §4): negotiate hands a paired
/// device a URL for its own owner's group, or none where the host has no hub, and a write that
/// landed — a pushed notification, a newly stored reply — publishes "pull now" to that owner and
/// nobody else. The hub itself is behind <see cref="INotificationNudgeChannel"/>, replaced here
/// by a recording one.
/// </summary>
public class NotificationNudgeEndpointTests : IDisposable
{
    private static readonly Uri HubUrl = new("wss://wps-test.webpubsub.azure.com/client/hubs/notifications?access_token=minted");

    private readonly RecordingNudgeChannel _channel = new();

    private readonly SyncServiceFactory _service;

    public NotificationNudgeEndpointTests()
    {
        _service = new SyncServiceFactory
        {
            TestServices = services =>
            {
                services.RemoveAll<INotificationNudgeChannel>();
                services.AddSingleton<INotificationNudgeChannel>(_channel);
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
    public async Task An_unpaired_caller_cannot_negotiate()
    {
        var anonymous = _service.CreateClient();

        var response = await anonymous.GetAsync(SyncRoutes.Absolute(SyncRoutes.NotificationNudge), Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_channel.Negotiated);
    }

    [Fact]
    public async Task Negotiate_hands_the_caller_a_url_for_its_own_owner_and_device()
    {
        var desktop = await _service.CreateClient().RegisteredDevice("Study desktop");
        var me = await desktop.GetFromJsonAsync<DeviceStatusResponse>(SyncRoutes.Absolute(SyncRoutes.DeviceStatus), Cancellation);

        var negotiated = await desktop.GetFromJsonAsync<NotificationNudgeNegotiateResponse>(
            SyncRoutes.Absolute(SyncRoutes.NotificationNudge), Cancellation);

        Assert.Equal(HubUrl, negotiated!.Url);
        var scope = Assert.Single(_channel.Negotiated);
        Assert.Equal(me!.OwnerId, scope.OwnerId.Value);
        Assert.Equal(me.DeviceId, scope.DeviceId.Value);
    }

    /// <summary>A hub that cannot mint a URL just now is a 503 under its own code, which the
    /// desktop retries soon — not the "no hub" answer it asks again only rarely.</summary>
    [Fact]
    public async Task A_hub_that_cannot_mint_a_url_answers_503_nudge_unavailable()
    {
        _channel.FailNegotiate = true;
        var desktop = await _service.CreateClient().RegisteredDevice("Study desktop");

        var response = await desktop.GetAsync(SyncRoutes.Absolute(SyncRoutes.NotificationNudge), Cancellation);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(SyncErrorCodes.NudgeUnavailable, (await response.Content.ReadFromJsonAsync<ProblemBody>(Cancellation))?.Code);
    }

    /// <summary>The Aspire run: no hub configured, so the module's no-op channel stands and the
    /// desktop is told to keep polling.</summary>
    [Fact]
    public async Task A_service_without_a_hub_answers_no_url()
    {
        using var bare = new SyncServiceFactory();
        var desktop = await bare.CreateClient().RegisteredDevice("Study desktop");

        var response = await desktop.GetAsync(SyncRoutes.Absolute(SyncRoutes.NotificationNudge), Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null((await response.Content.ReadFromJsonAsync<NotificationNudgeNegotiateResponse>(Cancellation))!.Url);
        Assert.IsType<NoNotificationNudgeChannel>(bare.Services.GetRequiredService<INotificationNudgeChannel>());
    }

    [Fact]
    public async Task A_push_that_landed_nudges_its_owner_with_the_kind_only()
    {
        var desktop = await _service.CreateClient().RegisteredDevice("Study desktop");
        var owner = (await desktop.GetFromJsonAsync<DeviceStatusResponse>(SyncRoutes.Absolute(SyncRoutes.DeviceStatus), Cancellation))!.OwnerId;

        var pushed = await desktop.PushNotifications(
            NotificationSyncEndpointTests.Change(Guid.CreateVersion7(), NotificationStates.Open, "Run the migration?"));

        Assert.Equal(HttpStatusCode.OK, pushed.StatusCode);
        var nudge = Assert.Single(_channel.Published);
        Assert.Equal(owner, nudge.Owner.Value);
        Assert.Equal(NotificationNudgeKinds.Notification, nudge.Kind);
    }

    /// <summary>A push the replica took none of — every notification held under another owner
    /// instance — changed nothing a desktop could pull, so it nudges nobody.</summary>
    [Fact]
    public async Task A_push_that_wrote_nothing_nudges_nobody()
    {
        var desktop = await _service.CreateClient().RegisteredDevice("Study desktop");
        var id = Guid.CreateVersion7();

        await desktop.PushNotifications(NotificationSyncEndpointTests.Change(id, NotificationStates.Open, "Mine."));
        _channel.Published.Clear();

        var refused = await desktop.PushNotifications(
            NotificationSyncEndpointTests.Change(id, NotificationStates.Closed, "Taken over.", DateTimeOffset.UtcNow.AddMinutes(1))
                with { OwnerInstanceId = Guid.CreateVersion7() });

        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
        Assert.Equal(0, (await refused.Content.ReadFromJsonAsync<PushNotificationsResponse>(Cancellation))!.Accepted);
        Assert.Empty(_channel.Published);
    }

    [Fact]
    public async Task A_reply_stored_for_the_first_time_nudges_its_owner_and_a_repeat_does_not()
    {
        var (desktop, phone) = await PairedDevices();
        var owner = (await desktop.GetFromJsonAsync<DeviceStatusResponse>(SyncRoutes.Absolute(SyncRoutes.DeviceStatus), Cancellation))!.OwnerId;
        var id = Guid.CreateVersion7();

        await desktop.PushNotifications(NotificationSyncEndpointTests.Change(id, NotificationStates.Open, "Run the migration?"));
        _channel.Published.Clear();

        var reply = NotificationSyncEndpointTests.Reply(id, "yes");
        var first = await phone.PostReply(reply);
        var again = await phone.PostReply(reply);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        var nudge = Assert.Single(_channel.Published);
        Assert.Equal(owner, nudge.Owner.Value);
        Assert.Equal(NotificationNudgeKinds.Reply, nudge.Kind);
    }

    [Fact]
    public async Task A_reply_to_no_notification_nudges_nobody()
    {
        var phone = await _service.CreateClient().RegisteredDevice("Phone");

        var posted = await phone.PostReply(NotificationSyncEndpointTests.Reply(Guid.CreateVersion7(), "yes"));

        Assert.Equal(HttpStatusCode.NotFound, posted.StatusCode);
        Assert.Empty(_channel.Published);
    }

    /// <summary>The group a token joins is the owner's id and nothing else — no device, no
    /// instance — so every device of the owner hears the same nudge and no other owner's.</summary>
    [Fact]
    public void The_group_is_named_for_the_owner_alone()
    {
        var owner = new OwnerId(Guid.Parse("0193a6f4-7b8c-7d2e-9f10-112233445566"));

        Assert.Equal("owner-0193a6f47b8c7d2e9f10112233445566", WebPubSubNudgeChannel.GroupFor(owner));
    }

    /// <summary>The deployed shape: Bicep sets the hub's endpoint, and the Web PubSub channel
    /// replaces the no-op one. Resolving it opens no connection.</summary>
    [Fact]
    public void A_configured_endpoint_puts_the_web_pubsub_channel_in_place()
    {
        using var deployed = new SyncServiceFactory
        {
            MoreConfiguration =
            [
                (NudgeChannelRegistration.EndpointKey, "https://wps-test.webpubsub.azure.com"),
                (NudgeChannelRegistration.HubKey, "notifications"),
            ],
        };

        Assert.IsType<WebPubSubNudgeChannel>(deployed.Services.GetRequiredService<INotificationNudgeChannel>());
    }

    [Fact]
    public void An_endpoint_that_is_not_https_refuses_to_start()
    {
        using var misconfigured = new SyncServiceFactory
        {
            MoreConfiguration = [(NudgeChannelRegistration.EndpointKey, "http://wps-test.webpubsub.azure.com")],
        };

        var thrown = Assert.ThrowsAny<Exception>(() => misconfigured.Services.GetRequiredService<INotificationNudgeChannel>());

        Assert.Contains(NudgeChannelRegistration.EndpointKey, Flatten(thrown), StringComparison.Ordinal);
    }

    private static string Flatten(Exception exception) =>
        exception.InnerException is null ? exception.Message : exception.Message + " " + Flatten(exception.InnerException);

    private async Task<(HttpClient Desktop, HttpClient Phone)> PairedDevices()
    {
        var desktop = await _service.CreateClient().RegisteredDevice("Study desktop");
        var code = await desktop.MintPairingCode();

        var phone = _service.CreateClient();
        var paired = await phone.PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.RedeemPairingCode),
            new RedeemPairingCodeRequest(code.Code, "Phone"),
            Cancellation);

        phone.Bearing(await phone.DeviceToken((await paired.Content.ReadFromJsonAsync<DeviceRegistrationResponse>(Cancellation))!));

        return (desktop, phone);
    }

    private sealed class RecordingNudgeChannel : INotificationNudgeChannel
    {
        public ConcurrentQueue<OwnerScope> Negotiated { get; } = new();

        public List<(OwnerId Owner, string Kind)> Published { get; } = [];

        public bool FailNegotiate { get; set; }

        public Task<Uri?> GetClientAccessUrlAsync(OwnerScope scope, CancellationToken cancellationToken = default)
        {
            Negotiated.Enqueue(scope);

            if (FailNegotiate)
            {
                throw new SyncReplicaException(SyncErrorCodes.NudgeUnavailable, "The hub did not answer.");
            }

            return Task.FromResult<Uri?>(HubUrl);
        }

        public Task PublishAsync(OwnerId owner, string kind, CancellationToken cancellationToken = default)
        {
            lock (Published) Published.Add((owner, kind));
            return Task.CompletedTask;
        }
    }
}
