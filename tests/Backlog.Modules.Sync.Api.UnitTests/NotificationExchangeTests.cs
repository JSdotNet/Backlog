using System.Net.Http.Json;

using Backlog.Infrastructure.Sqlite.Notifications;
using Backlog.Infrastructure.Sync;
using Backlog.Infrastructure.Sync.Notifications;
using Backlog.Modules.Notifications;
using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Notifications.Extensions;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.SharedKernel;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Sync.Api.UnitTests;

/// <summary>
/// The owner desktop's notification exchange end to end (local ADR 0039,
/// <c>.devbook/domain/notifications/requirements.md#answering-from-another-device</c>): the
/// real Notifications module over its own <c>backlog.db</c>, the real
/// <see cref="NotificationSyncSession"/> and a real sync service, with a phone and a second
/// desktop of the same owner replying through the API. What it proves is the owner's half —
/// the first valid reply wins, every other one is rejected with its reason and the viewers can
/// read that reason off the feed, and every processed reply is deleted.
/// </summary>
public sealed class NotificationExchangeTests : IDisposable
{
    private static readonly Guid DesktopInstance = Guid.Parse("7f3a0000-0000-0000-0000-0000000000d1");

    private readonly SyncServiceFactory _service = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "notification-sync-" + Guid.NewGuid().ToString("N"));
    private ServiceProvider? _desktop;
    private ServiceProvider? _secondDesktop;
    private Guid _ownerId;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _desktop?.Dispose();
        _secondDesktop?.Dispose();
        _service.Dispose();

        if (Directory.Exists(_root))
        {
            try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public async Task A_question_published_on_the_desktop_reaches_the_phone()
    {
        var owner = await OwnerWithViewersAsync();
        var id = await owner.AskAsync();

        var synced = await owner.Session.SyncAsync(Cancellation);

        Assert.True(synced.IsSuccess, synced.IsFailure ? synced.Error.Message : null);
        Assert.Equal(1, synced.Value.Pushed);

        var arrived = Assert.Single((await owner.Phone.PullNotifications()).Notifications);
        var payload = NotificationPayloads.Read(arrived.Change);
        Assert.NotNull(payload);
        Assert.Equal(id, payload.Notification.Id);
        Assert.Equal(NotificationState.Open, payload.Notification.State);
        Assert.Equal("Run the migration against staging?", payload.Notification.Title);
        Assert.Equal(["yes", "no"], payload.Notification.AnswerSpec!.Choices.Select(choice => choice.Id));
    }

    /// <summary>
    /// <c>requirements.md</c> — "Two replies race": the phone replies Yes and the second desktop
    /// No, and the owner processes the phone's first. The answer is Yes, the laptop's reply is
    /// rejected as already answered, both are deleted, and the feed tells both viewers.
    /// </summary>
    [Fact]
    public async Task Two_replies_racing_settle_on_the_first_and_the_second_is_rejected_as_already_answered()
    {
        var owner = await OwnerWithViewersAsync();
        var id = await owner.AskAsync();
        await owner.Session.SyncAsync(Cancellation);

        var fromPhone = await owner.ReplyAsync(owner.Phone, id, "yes");
        var fromLaptop = await owner.ReplyAsync(owner.Laptop, id, "no");

        var synced = await owner.Session.SyncAsync(Cancellation);

        Assert.True(synced.IsSuccess, synced.IsFailure ? synced.Error.Message : null);
        Assert.True(synced.Value.PolledReplies);
        Assert.Equal(1, synced.Value.Accepted);
        Assert.Equal(1, synced.Value.Rejected);

        var answer = (await owner.Notifications.GetAnswerAsync(id, Cancellation)).Value;
        Assert.NotNull(answer);
        Assert.Equal(fromPhone.Id, answer.ReplyId);
        Assert.Equal(["yes"], answer.ChoiceIds);
        Assert.Equal(owner.PhoneDevice, answer.FromDeviceId);

        var payload = await owner.LatestOnTheFeedAsync(id);
        Assert.Equal(NotificationState.Answered, payload.Notification.State);
        Assert.Equal(fromPhone.Id, payload.Notification.Answer!.ReplyId);
        var rejected = Assert.Single(payload.RejectedReplies);
        Assert.Equal(fromLaptop.Id, rejected.ReplyId);
        Assert.Equal(NotificationReplyReasons.AlreadyAnswered, rejected.Reason);
        Assert.Equal(owner.LaptopDevice, rejected.FromDeviceId);

        Assert.Empty((await owner.Desktop.ListReplies(DesktopInstance)).Replies);
    }

    [Fact]
    public async Task A_reply_after_the_publisher_closed_the_question_is_rejected_as_closed()
    {
        var owner = await OwnerWithViewersAsync();
        var id = await owner.AskAsync();
        await owner.Session.SyncAsync(Cancellation);

        Assert.True((await owner.Notifications.CloseAsync(id, "Session archived", Cancellation)).IsSuccess);
        await owner.ReplyAsync(owner.Phone, id, "yes");

        // A second open question keeps the reply poll running.
        await owner.AskAsync();
        var synced = await owner.Session.SyncAsync(Cancellation);

        Assert.Equal(0, synced.Value.Accepted);
        Assert.Equal(1, synced.Value.Rejected);

        var payload = await owner.LatestOnTheFeedAsync(id);
        Assert.Equal(NotificationState.Closed, payload.Notification.State);
        Assert.Equal(NotificationReplyReasons.Closed, Assert.Single(payload.RejectedReplies).Reason);
        Assert.Empty((await owner.Desktop.ListReplies(DesktopInstance)).Replies);
    }

    /// <summary><c>requirements.md</c> — "An invalid reply can be sent again".</summary>
    [Fact]
    public async Task A_reply_that_does_not_fit_is_rejected_as_invalid_and_the_viewer_can_reply_again()
    {
        var owner = await OwnerWithViewersAsync();
        var id = await owner.AskAsync();
        await owner.Session.SyncAsync(Cancellation);

        var wrong = await owner.ReplyAsync(owner.Phone, id, "maybe");
        await owner.Session.SyncAsync(Cancellation);

        var payload = await owner.LatestOnTheFeedAsync(id);
        Assert.Equal(NotificationState.Open, payload.Notification.State);
        var rejected = Assert.Single(payload.RejectedReplies);
        Assert.Equal(wrong.Id, rejected.ReplyId);
        Assert.Equal(NotificationReplyReasons.Invalid, rejected.Reason);
        Assert.Equal("reply.invalid", rejected.Code);

        var right = await owner.ReplyAsync(owner.Phone, id, "no");
        var synced = await owner.Session.SyncAsync(Cancellation);

        Assert.Equal(1, synced.Value.Accepted);
        Assert.Equal(right.Id, (await owner.Notifications.GetAnswerAsync(id, Cancellation)).Value!.ReplyId);
    }

    [Fact]
    public async Task An_owner_only_question_rejects_the_phones_reply_as_invalid()
    {
        var owner = await OwnerWithViewersAsync();
        var id = await owner.AskAsync(AnswerOn.Owner);
        await owner.Session.SyncAsync(Cancellation);

        await owner.ReplyAsync(owner.Phone, id, "yes");
        await owner.Session.SyncAsync(Cancellation);

        var payload = await owner.LatestOnTheFeedAsync(id);
        Assert.Equal(NotificationState.Open, payload.Notification.State);
        var rejected = Assert.Single(payload.RejectedReplies);
        Assert.Equal(NotificationReplyReasons.Invalid, rejected.Reason);
        Assert.Equal("reply.owner_only", rejected.Code);
    }

    [Fact]
    public async Task A_question_answered_on_the_desktop_itself_travels_as_answered()
    {
        var owner = await OwnerWithViewersAsync();
        var id = await owner.AskAsync();
        await owner.Session.SyncAsync(Cancellation);

        var local = await owner.Replies.AcceptReplyAsync(
            new NotificationReply(Guid.CreateVersion7(), id, DesktopInstance, DesktopInstance, ["no"], null, DateTimeOffset.UtcNow),
            Cancellation);
        Assert.True(local.IsSuccess);

        var synced = await owner.Session.SyncAsync(Cancellation);

        Assert.Equal(1, synced.Value.Pushed);

        // The cycle that settles the last question lists the replies once more.
        Assert.True(synced.Value.PolledReplies);
        Assert.Equal(NotificationState.Answered, (await owner.LatestOnTheFeedAsync(id)).Notification.State);
    }

    /// <summary>Idle while no question is open: an alert is pushed once, and no cycle after it
    /// lists the replies.</summary>
    [Fact]
    public async Task With_no_open_question_the_exchange_never_asks_for_replies()
    {
        var owner = await OwnerWithViewersAsync();
        await owner.Notifications.PublishAsync(
            new NotificationDraft(
                new NotificationSource("devbook", "remark-1"),
                Subject: null,
                NotificationKind.Alert,
                NotificationSeverity.Info,
                "Remark left on Storage and sync",
                "Does the reply container need its own TTL?",
                string.Empty),
            Cancellation);

        var first = await owner.Session.SyncAsync(Cancellation);
        var second = await owner.Session.SyncAsync(Cancellation);

        Assert.Equal(1, first.Value.Pushed);
        Assert.False(first.Value.PolledReplies);
        Assert.Equal(0, second.Value.Pushed);
        Assert.False(second.Value.PolledReplies);
    }

    /// <summary>
    /// A source that names its notification by a derived id — one spec-manager question read on
    /// two signed-in desktops — publishes the same id on both. The replica keeps the desktop that
    /// put it there first, and the second closes its own copy rather than ask the person a
    /// question whose answer its owner would never see.
    /// </summary>
    [Fact]
    public async Task A_notification_another_desktop_put_on_the_replica_first_closes_on_the_second()
    {
        var owner = await OwnerWithViewersAsync();
        var shared = Guid.Parse("5a5a0000-0000-5000-8000-000000000001");
        var draft = new NotificationDraft(
            new NotificationSource("spec-manager", "billing/q-1"),
            new NotificationSubject("spec-manager", "question", "q-1"),
            NotificationKind.Question,
            NotificationSeverity.Info,
            "Which rounding rule applies?",
            "#7 Rounding",
            string.Empty,
            new AnswerSpec([new AnswerChoice("1", "Half even"), new AnswerChoice("2", "Half up")]),
            Id: shared);
        await owner.Notifications.PublishAsync(draft, Cancellation);
        Assert.True((await owner.Session.SyncAsync(Cancellation)).IsSuccess);

        var (second, notifications) = SecondDesktop(owner);
        await notifications.PublishAsync(draft, Cancellation);
        Assert.True((await second.SyncAsync(Cancellation)).IsSuccess);

        var status = await _secondDesktop!.GetRequiredService<INotificationReplication>().GetAsync(shared, Cancellation);
        Assert.Equal(NotificationState.Closed, status!.State);
        Assert.Equal(NotificationSyncSession.OwnedElsewhereReason, status.CloseReason);

        var onTheFeed = await owner.LatestOnTheFeedAsync(shared);
        Assert.Equal(DesktopInstance, onTheFeed.Notification.OwnerInstanceId);
        Assert.Equal(NotificationState.Open, onTheFeed.Notification.State);

        // Closed and marked pushed, so the next exchange offers nothing of it again.
        Assert.Equal(0, (await second.SyncAsync(Cancellation)).Value.Pushed);
    }

    /// <summary>The laptop as a second desktop of the same owner: its own instance, its own
    /// notifications, its own exchange.</summary>
    private (NotificationSyncSession Session, INotifications Notifications) SecondDesktop(Owner owner)
    {
        var root = Path.Combine(_root, "laptop");
        Directory.CreateDirectory(root);
        var identity = new FixedIdentity(new DeviceIdentity(Guid.Parse("7f3a0000-0000-0000-0000-0000000000d2"), "LAPTOP"));

        var services = new ServiceCollection();
        services.AddSingleton<IDeviceIdentitySource>(identity);
        services.AddSingleton<INotificationRepository>(new SqliteNotificationRepository(root));
        services.AddNotificationsModule();
        _secondDesktop = services.BuildServiceProvider();

        var notifications = _secondDesktop.GetRequiredService<INotifications>();
        var session = new NotificationSyncSession(
            new NotificationSyncClient(owner.Laptop),
            _secondDesktop.GetRequiredService<INotificationReplication>(),
            _secondDesktop.GetRequiredService<INotificationReplies>(),
            new FileNotificationSyncStateStore(Path.Combine(root, "notification-sync-state.json")),
            new InMemoryDeviceCredentialStore(new DeviceCredential(_ownerId, owner.LaptopDevice, "Laptop", "unused")),
            identity,
            TimeProvider.System,
            owner: notifications);

        return (session, notifications);
    }

    // --- The owner and its viewers ------------------------------------------------------

    private async Task<Owner> OwnerWithViewersAsync()
    {
        var desktopHttp = _service.CreateClient();
        var registration = await desktopHttp.RegisterDevice("Study desktop");
        desktopHttp.Bearing(await desktopHttp.DeviceToken(registration));
        _ownerId = registration.OwnerId;

        var (phone, phoneDevice) = await PairAsync(desktopHttp, "Phone");
        var (laptop, laptopDevice) = await PairAsync(desktopHttp, "Laptop");

        Directory.CreateDirectory(_root);
        var identity = new FixedIdentity(new DeviceIdentity(DesktopInstance, "STUDY-DESKTOP"));

        var services = new ServiceCollection();
        services.AddSingleton<IDeviceIdentitySource>(identity);
        services.AddSingleton<INotificationRepository>(new SqliteNotificationRepository(_root));
        services.AddNotificationsModule();
        _desktop = services.BuildServiceProvider();

        var session = new NotificationSyncSession(
            new NotificationSyncClient(desktopHttp),
            _desktop.GetRequiredService<INotificationReplication>(),
            _desktop.GetRequiredService<INotificationReplies>(),
            new FileNotificationSyncStateStore(Path.Combine(_root, "notification-sync-state.json")),
            new InMemoryDeviceCredentialStore(new DeviceCredential(registration.OwnerId, registration.DeviceId, "Study desktop", registration.Credential)),
            identity,
            TimeProvider.System);

        return new Owner(
            desktopHttp,
            phone,
            phoneDevice,
            laptop,
            laptopDevice,
            session,
            _desktop.GetRequiredService<INotifications>(),
            _desktop.GetRequiredService<INotificationReplies>());
    }

    private async Task<(HttpClient Client, Guid DeviceId)> PairAsync(HttpClient owner, string name)
    {
        var code = await owner.MintPairingCode();
        var client = _service.CreateClient();
        var paired = await client.PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.RedeemPairingCode),
            new RedeemPairingCodeRequest(code.Code, name),
            Cancellation);
        var registration = (await paired.Content.ReadFromJsonAsync<DeviceRegistrationResponse>(Cancellation))!;
        client.Bearing(await client.DeviceToken(registration));

        return (client, registration.DeviceId);
    }

    private sealed record Owner(
        HttpClient Desktop,
        HttpClient Phone,
        Guid PhoneDevice,
        HttpClient Laptop,
        Guid LaptopDevice,
        NotificationSyncSession Session,
        INotifications Notifications,
        INotificationReplies Replies)
    {
        public async Task<Guid> AskAsync(AnswerOn answerOn = AnswerOn.AnyInstance)
        {
            var published = await Notifications.PublishAsync(
                new NotificationDraft(
                    new NotificationSource("mcp", "ask-17"),
                    new NotificationSubject("sessions", "session", "sess-91"),
                    NotificationKind.Question,
                    NotificationSeverity.Warning,
                    "Run the migration against staging?",
                    "0042_add_device_kind",
                    "3 tables changed",
                    new AnswerSpec([new AnswerChoice("yes", "Yes, run it"), new AnswerChoice("no", "Not now")], AnswerOn: answerOn)),
                Cancellation);

            Assert.True(published.IsSuccess, published.IsFailure ? published.Error.Message : null);
            return published.Value;
        }

        public async Task<NotificationReplyPost> ReplyAsync(HttpClient viewer, Guid notificationId, params string[] choiceIds)
        {
            var reply = new NotificationReplyPost(Guid.CreateVersion7(), notificationId, DesktopInstance, choiceIds, null, DateTimeOffset.UtcNow);
            var posted = await viewer.PostReply(reply);
            posted.EnsureSuccessStatusCode();
            return reply;
        }

        /// <summary>The last version of one notification the feed holds, as a viewer reads it.</summary>
        public async Task<NotificationPayload> LatestOnTheFeedAsync(Guid id)
        {
            var feed = await Phone.PullNotifications();
            var latest = feed.Notifications.Last(record => record.Change.Id == id);
            return NotificationPayloads.Read(latest.Change)!;
        }
    }

    private sealed class FixedIdentity(DeviceIdentity identity) : IDeviceIdentitySource
    {
        public DeviceIdentity Current { get; } = identity;
    }
}
