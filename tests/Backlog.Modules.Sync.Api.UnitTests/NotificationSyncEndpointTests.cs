using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Api.Endpoints;

namespace Backlog.Modules.Sync.Api.UnitTests;

/// <summary>
/// The notification exchange over HTTP (local ADR 0038): an owner instance pushes its
/// notifications, every device of the owner pulls the feed, any of them posts a reply, and only
/// the owner instance lists and deletes the replies addressed to it — and nobody under another
/// account reaches any of it.
/// </summary>
public class NotificationSyncEndpointTests : IDisposable
{
    private static readonly Guid DesktopInstance = Guid.Parse("7f3a0000-0000-0000-0000-00000000000a");

    private readonly SyncServiceFactory _service = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _service.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task A_notification_the_owner_pushes_reaches_every_device_of_the_owner()
    {
        var (desktop, phone) = await PairedDevices();
        var id = Guid.CreateVersion7();

        var pushed = await desktop.PushNotifications(Change(id, NotificationStates.Open, "Run the migration?"));

        Assert.Equal(HttpStatusCode.OK, pushed.StatusCode);
        Assert.Equal(1, (await pushed.Content.ReadFromJsonAsync<PushNotificationsResponse>(Cancellation))!.Accepted);

        var feed = await phone.PullNotifications();
        var arrived = Assert.Single(feed.Notifications);
        Assert.Equal(id, arrived.Change.Id);
        Assert.Equal(DesktopInstance, arrived.Change.OwnerInstanceId);
        Assert.Equal(NotificationStates.Open, arrived.Change.State);
        Assert.Equal("Run the migration?", arrived.Change.Notification.GetProperty("title").GetString());
        Assert.False(feed.HasMore);
    }

    [Fact]
    public async Task A_state_change_from_the_owner_travels_on_the_feed()
    {
        var (desktop, phone) = await PairedDevices();
        var id = Guid.CreateVersion7();

        await desktop.PushNotifications(Change(id, NotificationStates.Open, "Run the migration?"));
        var since = (await phone.PullNotifications()).Since;

        await desktop.PushNotifications(Change(id, NotificationStates.Answered, "Run the migration?", DateTimeOffset.UtcNow.AddSeconds(5)));

        var arrived = Assert.Single((await phone.PullNotifications(since)).Notifications);
        Assert.Equal(NotificationStates.Answered, arrived.Change.State);
    }

    /// <summary>One writer per notification (ADR 0038 §1): a push naming another owner
    /// instance does not overwrite a notification, and says so without holding the rest of its
    /// batch back.</summary>
    [Fact]
    public async Task A_notification_held_under_another_owner_instance_is_not_overwritten()
    {
        var (desktop, laptop) = await PairedDevices();
        var id = Guid.CreateVersion7();
        var laptopsOwn = Guid.CreateVersion7();

        await desktop.PushNotifications(Change(id, NotificationStates.Open, "Mine."));

        var overwritten = await laptop.PushNotifications(
            Change(id, NotificationStates.Closed, "Taken over.", DateTimeOffset.UtcNow.AddMinutes(1)) with { OwnerInstanceId = Guid.CreateVersion7() },
            Change(laptopsOwn, NotificationStates.Open, "The laptop's own.") with { OwnerInstanceId = Guid.CreateVersion7() });

        Assert.Equal(HttpStatusCode.OK, overwritten.StatusCode);
        var response = (await overwritten.Content.ReadFromJsonAsync<PushNotificationsResponse>(Cancellation))!;
        Assert.Equal(1, response.Accepted);
        Assert.Equal([id], response.NotOwned);

        var held = (await desktop.PullNotifications()).Notifications.Single(record => record.Change.Id == id);
        Assert.Equal(NotificationStates.Open, held.Change.State);
        Assert.Equal(DesktopInstance, held.Change.OwnerInstanceId);
    }

    /// <summary>The owner is the installation, not the pairing (ADR 0038 §1): a desktop that
    /// pairs again pushes its open notifications under its new device and takes the replies
    /// addressed to it along, and the old device no longer reads them.</summary>
    [Fact]
    public async Task A_desktop_that_pairs_again_keeps_its_notifications_and_their_replies()
    {
        var (desktop, phone) = await PairedDevices();
        var id = Guid.CreateVersion7();
        await desktop.PushNotifications(Change(id, NotificationStates.Open, "Run the migration?"));
        var reply = Reply(id, "yes");
        await phone.PostReply(reply);

        var repaired = await PairAgain(phone, "Study desktop, paired again");
        var pushed = await repaired.PushNotifications(Change(id, NotificationStates.Open, "Run the migration?", DateTimeOffset.UtcNow.AddSeconds(1)));

        Assert.Empty((await pushed.Content.ReadFromJsonAsync<PushNotificationsResponse>(Cancellation))!.NotOwned);
        Assert.Equal(reply.Id, Assert.Single((await repaired.ListReplies(DesktopInstance)).Replies).Reply.Id);
        Assert.Empty((await desktop.ListReplies(DesktopInstance)).Replies);
    }

    [Fact]
    public async Task A_separate_owner_sees_nothing()
    {
        var mine = await _service.CreateClient().RegisteredDevice("Mine");
        var theirs = await _service.CreateClient().RegisteredDevice("Theirs");

        await mine.PushNotifications(Change(Guid.CreateVersion7(), NotificationStates.Open, "Private."));

        Assert.Empty((await theirs.PullNotifications()).Notifications);
    }

    [Fact]
    public async Task A_cursor_minted_for_another_owner_is_refused()
    {
        var mine = await _service.CreateClient().RegisteredDevice("Mine");
        var theirs = await _service.CreateClient().RegisteredDevice("Theirs");

        await mine.PushNotifications(Change(Guid.CreateVersion7(), NotificationStates.Open, "Private."));
        var myCursor = (await mine.PullNotifications()).Since;

        var replayed = await theirs.GetAsync(
            $"{SyncRoutes.Absolute(SyncRoutes.Notifications)}?since={Uri.EscapeDataString(myCursor)}", Cancellation);

        Assert.Equal(HttpStatusCode.Forbidden, replayed.StatusCode);
        Assert.Equal(SyncErrorCodes.SyncCursorNotYours, (await replayed.Content.ReadFromJsonAsync<ProblemBody>(Cancellation))?.Code);
    }

    [Fact]
    public async Task A_reply_from_another_device_of_the_owner_is_listed_to_the_owner_instance_only()
    {
        var (desktop, phone) = await PairedDevices();
        var id = Guid.CreateVersion7();
        await desktop.PushNotifications(Change(id, NotificationStates.Open, "Run the migration?"));

        var reply = Reply(id, "yes");
        var posted = await phone.PostReply(reply);

        Assert.Equal(HttpStatusCode.OK, posted.StatusCode);
        Assert.True((await posted.Content.ReadFromJsonAsync<PostNotificationReplyResponse>(Cancellation))!.Stored);

        var waiting = Assert.Single((await desktop.ListReplies(DesktopInstance)).Replies);
        Assert.Equal(reply.Id, waiting.Reply.Id);
        Assert.Equal(["yes"], waiting.Reply.ChoiceIds);
        Assert.Equal(await OwnerOf(desktop), waiting.OwnerId);
        Assert.Equal(await DeviceOf(phone), waiting.FromDeviceId);

        // The phone wrote the reply but is not the instance it is addressed to.
        Assert.Empty((await phone.ListReplies(DesktopInstance)).Replies);
    }

    /// <summary>The service's half of <c>reply.foreign_owner</c> (ADR 0038 §2): a device under
    /// another account finds no such notification in its own partition, and its reply is never
    /// stored where the owner instance would read it.</summary>
    [Fact]
    public async Task A_reply_from_a_device_under_another_owner_is_refused()
    {
        var (desktop, _) = await PairedDevices();
        var stranger = await _service.CreateClient().RegisteredDevice("Stranger");
        var id = Guid.CreateVersion7();
        await desktop.PushNotifications(Change(id, NotificationStates.Open, "Approve PR #1431?"));

        var posted = await stranger.PostReply(Reply(id, "yes"));

        Assert.Equal(HttpStatusCode.NotFound, posted.StatusCode);
        Assert.Equal(SyncErrorCodes.NotificationNotFound, (await posted.Content.ReadFromJsonAsync<ProblemBody>(Cancellation))?.Code);
        Assert.Empty((await desktop.ListReplies(DesktopInstance)).Replies);
    }

    [Fact]
    public async Task A_reply_addressed_to_another_owner_instance_is_refused()
    {
        var (desktop, phone) = await PairedDevices();
        var id = Guid.CreateVersion7();
        await desktop.PushNotifications(Change(id, NotificationStates.Open, "Run the migration?"));

        var posted = await phone.PostReply(Reply(id, "yes") with { OwnerInstanceId = Guid.CreateVersion7() });

        Assert.Equal(HttpStatusCode.NotFound, posted.StatusCode);
        Assert.Empty((await desktop.ListReplies(DesktopInstance)).Replies);
    }

    /// <summary>The phone's outbox may send the same reply again after a dropped answer.</summary>
    [Fact]
    public async Task Posting_the_same_reply_twice_stores_it_once()
    {
        var (desktop, phone) = await PairedDevices();
        var id = Guid.CreateVersion7();
        await desktop.PushNotifications(Change(id, NotificationStates.Open, "Run the migration?"));
        var reply = Reply(id, "yes");

        await phone.PostReply(reply);
        var again = await phone.PostReply(reply);

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.False((await again.Content.ReadFromJsonAsync<PostNotificationReplyResponse>(Cancellation))!.Stored);
        Assert.Single((await desktop.ListReplies(DesktopInstance)).Replies);
    }

    [Fact]
    public async Task Only_the_owner_instance_deletes_a_reply()
    {
        var (desktop, phone) = await PairedDevices();
        var id = Guid.CreateVersion7();
        await desktop.PushNotifications(Change(id, NotificationStates.Open, "Run the migration?"));
        var reply = Reply(id, "yes");
        await phone.PostReply(reply);

        var byPhone = await phone.DeleteAsync(SyncRoutes.NotificationReplyFor(reply.Id), Cancellation);
        Assert.Equal(HttpStatusCode.NotFound, byPhone.StatusCode);
        Assert.Single((await desktop.ListReplies(DesktopInstance)).Replies);

        var byOwner = await desktop.DeleteAsync(SyncRoutes.NotificationReplyFor(reply.Id), Cancellation);
        Assert.Equal(HttpStatusCode.NoContent, byOwner.StatusCode);
        Assert.Empty((await desktop.ListReplies(DesktopInstance)).Replies);

        var twice = await desktop.DeleteAsync(SyncRoutes.NotificationReplyFor(reply.Id), Cancellation);
        Assert.Equal(HttpStatusCode.NotFound, twice.StatusCode);
    }

    [Fact]
    public async Task Replies_are_listed_in_the_order_the_service_took_them()
    {
        var (desktop, phone) = await PairedDevices();
        var id = Guid.CreateVersion7();
        await desktop.PushNotifications(Change(id, NotificationStates.Open, "Run the migration?"));

        var first = Reply(id, "yes");
        var second = Reply(id, "no");
        await phone.PostReply(first);
        await phone.PostReply(second);

        Assert.Equal([first.Id, second.Id], (await desktop.ListReplies(DesktopInstance)).Replies.Select(waiting => waiting.Reply.Id));
    }

    [Fact]
    public async Task A_push_of_more_notifications_than_the_cap_is_refused_whole()
    {
        var device = await _service.CreateClient().RegisteredDevice("Study desktop");

        var response = await device.PushNotifications(
            [.. Enumerable.Range(0, SyncRequestLimits.MaximumPushNotifications + 1)
                .Select(index => Change(Guid.CreateVersion7(), NotificationStates.Open, $"Notification {index}"))]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(SyncErrorCodes.NotificationBatchTooLarge, (await response.Content.ReadFromJsonAsync<ProblemBody>(Cancellation))?.Code);
        Assert.Empty((await device.PullNotifications()).Notifications);
    }

    [Fact]
    public async Task A_notification_in_an_unknown_state_is_refused_with_the_whole_batch()
    {
        var device = await _service.CreateClient().RegisteredDevice("Study desktop");

        var response = await device.PushNotifications(
            Change(Guid.CreateVersion7(), NotificationStates.Open, "Fine."),
            Change(Guid.CreateVersion7(), "snoozed", "Not a state."));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(SyncErrorCodes.NotificationInvalid, (await response.Content.ReadFromJsonAsync<ProblemBody>(Cancellation))?.Code);
        Assert.Empty((await device.PullNotifications()).Notifications);
    }

    [Fact]
    public async Task A_reply_past_its_bounds_is_refused()
    {
        var (desktop, phone) = await PairedDevices();
        var id = Guid.CreateVersion7();
        await desktop.PushNotifications(Change(id, NotificationStates.Open, "What should the release note say?"));

        var posted = await phone.PostReply(Reply(id) with { Text = new string('x', NotificationRecordLimits.ReplyTextLength + 1) });

        Assert.Equal(HttpStatusCode.BadRequest, posted.StatusCode);
        Assert.Equal(SyncErrorCodes.NotificationInvalid, (await posted.Content.ReadFromJsonAsync<ProblemBody>(Cancellation))?.Code);
    }

    [Theory]
    [InlineData("GET", SyncRoutes.Notifications)]
    [InlineData("POST", SyncRoutes.Notifications)]
    [InlineData("GET", SyncRoutes.NotificationReplies + "?instance=7f3a0000-0000-0000-0000-00000000000a")]
    [InlineData("POST", SyncRoutes.NotificationReplies)]
    [InlineData("DELETE", "/notifications/replies/0193a6f4-7b8c-7d2e-9f10-112233445566")]
    public async Task An_unpaired_caller_is_refused(string method, string route)
    {
        var anonymous = _service.CreateClient();

        using var request = new HttpRequestMessage(new HttpMethod(method), SyncRoutes.Absolute(route));
        if (method == "POST") request.Content = JsonContent.Create(new { });

        var response = await anonymous.SendAsync(request, Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    internal static NotificationChange Change(Guid id, string state, string title, DateTimeOffset? updatedAt = null) =>
        new(id, DesktopInstance, state, updatedAt ?? DateTimeOffset.UtcNow, JsonSerializer.SerializeToElement(new { title }));

    internal static NotificationReplyPost Reply(Guid notificationId, params string[] choiceIds) =>
        new(Guid.CreateVersion7(), notificationId, DesktopInstance, choiceIds, Text: null, DateTimeOffset.UtcNow);

    private static async Task<Guid> OwnerOf(HttpClient device) =>
        (await device.GetFromJsonAsync<DeviceStatusResponse>(SyncRoutes.Absolute(SyncRoutes.DeviceStatus), Cancellation))!.OwnerId;

    private static async Task<Guid> DeviceOf(HttpClient device) =>
        (await device.GetFromJsonAsync<DeviceStatusResponse>(SyncRoutes.Absolute(SyncRoutes.DeviceStatus), Cancellation))!.DeviceId;

    private async Task<HttpClient> PairAgain(HttpClient paired, string name)
    {
        var code = await paired.MintPairingCode();
        var device = _service.CreateClient();
        var redeemed = await device.PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.RedeemPairingCode),
            new RedeemPairingCodeRequest(code.Code, name),
            Cancellation);

        device.Bearing(await device.DeviceToken((await redeemed.Content.ReadFromJsonAsync<DeviceRegistrationResponse>(Cancellation))!));
        return device;
    }

    private async Task<(HttpClient Desktop, HttpClient Phone)> PairedDevices()
    {
        var desktop = _service.CreateClient();
        var registration = await desktop.RegisterDevice("Study desktop");
        desktop.Bearing(await desktop.DeviceToken(registration));

        var code = await desktop.MintPairingCode();

        var phone = _service.CreateClient();
        var paired = await phone.PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.RedeemPairingCode),
            new RedeemPairingCodeRequest(code.Code, "Phone"),
            Cancellation);

        phone.Bearing(await phone.DeviceToken((await paired.Content.ReadFromJsonAsync<DeviceRegistrationResponse>(Cancellation))!));

        return (desktop, phone);
    }
}

internal static class NotificationSyncClientExtensions
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    internal static Task<HttpResponseMessage> PushNotifications(this HttpClient client, params NotificationChange[] changes) =>
        client.PostAsJsonAsync(SyncRoutes.Absolute(SyncRoutes.Notifications), new PushNotificationsRequest(changes), Cancellation);

    internal static async Task<PullNotificationsResponse> PullNotifications(this HttpClient client, string? since = null)
    {
        var route = SyncRoutes.Absolute(SyncRoutes.Notifications);
        var response = await client.GetAsync(since is null ? route : $"{route}?since={Uri.EscapeDataString(since)}", Cancellation);

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<PullNotificationsResponse>(Cancellation))!;
    }

    internal static Task<HttpResponseMessage> PostReply(this HttpClient client, NotificationReplyPost reply) =>
        client.PostAsJsonAsync(SyncRoutes.Absolute(SyncRoutes.NotificationReplies), reply, Cancellation);

    internal static async Task<PullNotificationRepliesResponse> ListReplies(this HttpClient client, Guid ownerInstanceId)
    {
        var response = await client.GetAsync($"{SyncRoutes.Absolute(SyncRoutes.NotificationReplies)}?instance={ownerInstanceId:D}", Cancellation);

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<PullNotificationRepliesResponse>(Cancellation))!;
    }
}
