using Backlog.Infrastructure.Sync;
using Backlog.Mobile.UI.Notifications;
using Backlog.Mobile.UI.Outbox;
using Backlog.Modules.Notifications.Abstractions;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// An answer given from the tray (local ADR 0039 §4): the action the person tapped, or the text
/// typed into the inline reply, becomes the same <see cref="ReplyOutboxKind"/> entry a reply
/// made in the app does, through <see cref="NotificationViewProjection.ReplyAsync"/> — and a
/// question the phone may no longer answer queues nothing.
/// </summary>
public sealed class PushReplyTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 9, 41, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly ScriptedNotificationService _service = new();
    private readonly DeviceOutbox _outbox;
    private readonly NotificationViewProjection _projection;

    public PushReplyTests()
    {
        var sync = new CloudSyncClient(new HttpClient(new Handler(_service)) { BaseAddress = new Uri("https://sync.test") });
        _outbox = new DeviceOutbox(new InMemoryDeviceStore(), [new ReplyOutboxKind(sync)], _clock);
        _projection = new NotificationViewProjection(new InMemoryNotificationViewStore(), _outbox, sync, TestDevices.Paired(), _clock);
    }

    private async Task<NotificationSnapshot> HoldAsync(NotificationSnapshot notification)
    {
        _service.Publish(notification, Now.AddMinutes(-1));
        await _projection.PullAsync(TestContext.Current.CancellationToken);

        // From here on the replies stay queued, where the test can read them.
        _service.State = InboxServiceState.Unreachable;
        return notification;
    }

    [Fact]
    public async Task A_tapped_choice_queues_a_reply_with_that_choice()
    {
        var question = await HoldAsync(TestNotifications.Question("Run the migration against staging?", Now.AddMinutes(-2)));

        var reply = await PushActions.ReplyAsync(_projection, question.Id, "choice:yes", text: null, TestContext.Current.CancellationToken);

        var entry = Assert.Single(_outbox.Entries);
        var queued = ReplyOutboxKind.Read(entry);
        Assert.Equal(ReplyOutboxKind.Token, entry.Kind);
        Assert.Equal(reply!.Id, queued.Id);
        Assert.Equal((question.Id, TestNotifications.Owner, (string?)null), (queued.NotificationId, queued.OwnerInstanceId, queued.Text));
        Assert.Equal(["yes"], queued.ChoiceIds);
    }

    [Fact]
    public async Task An_inline_reply_queues_the_typed_text()
    {
        var question = await HoldAsync(TestNotifications.Question("What goes in the release note?", Now.AddMinutes(-2), TestNotifications.ReleaseNote));

        await PushActions.ReplyAsync(_projection, question.Id, PushActions.ReplyKey, "Pairing is faster.", TestContext.Current.CancellationToken);

        var queued = ReplyOutboxKind.Read(Assert.Single(_outbox.Entries));
        Assert.Empty(queued.ChoiceIds);
        Assert.Equal("Pairing is faster.", queued.Text);
    }

    [Fact]
    public async Task A_choice_that_takes_text_is_sent_from_the_app_with_its_words()
    {
        var question = await HoldAsync(TestNotifications.Question("Approve the validation of run-58?", Now.AddMinutes(-2), TestNotifications.Validation()));

        Assert.Null(await _projection.ReplyAsync(question.Id, ["revise"], "  ", TestContext.Current.CancellationToken));
        Assert.Empty(_outbox.Entries);

        await _projection.ReplyAsync(question.Id, ["revise"], " The toast overlaps the header ", TestContext.Current.CancellationToken);

        var queued = ReplyOutboxKind.Read(Assert.Single(_outbox.Entries));
        Assert.Equal(["revise"], queued.ChoiceIds);
        Assert.Equal("The toast overlaps the header", queued.Text);
    }

    [Fact]
    public async Task A_notification_the_phone_does_not_hold_queues_nothing()
    {
        await HoldAsync(TestNotifications.Question("Run the migration against staging?", Now.AddMinutes(-2)));

        var reply = await PushActions.ReplyAsync(_projection, Guid.NewGuid(), "choice:yes", null, TestContext.Current.CancellationToken);

        Assert.Null(reply);
        Assert.Empty(_outbox.Entries);
    }

    [Fact]
    public async Task A_question_settled_before_the_tap_queues_nothing()
    {
        var question = TestNotifications.Question("Run the migration against staging?", Now.AddMinutes(-2));
        await HoldAsync(TestNotifications.Answered(question, Guid.NewGuid(), Guid.NewGuid(), Now.AddMinutes(-1), "no"));

        var reply = await PushActions.ReplyAsync(_projection, question.Id, "choice:yes", null, TestContext.Current.CancellationToken);

        Assert.Null(reply);
        Assert.Empty(_outbox.Entries);
    }

    [Fact]
    public async Task A_choice_the_question_does_not_offer_queues_nothing()
    {
        var question = await HoldAsync(TestNotifications.Question("Run the migration against staging?", Now.AddMinutes(-2)));

        Assert.Null(await PushActions.ReplyAsync(_projection, question.Id, "choice:maybe", null, TestContext.Current.CancellationToken));
        Assert.Empty(_outbox.Entries);
    }

    public void Dispose()
    {
        _projection.Dispose();
        _outbox.Dispose();
    }

    private sealed class Handler(ScriptedNotificationService service) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            service.AnswerAsync(request, cancellationToken);
    }
}
