using Backlog.Infrastructure.Sync.Notifications;
using Backlog.Mobile.UI.Notifications;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary><c>notification_view</c> survives the app closing: the notifications, the cursor
/// and the replies this phone made come back from the file as they went in, beside the outbox
/// in the same file — so a reply made here is still known as this phone's after a restart.</summary>
public sealed class SqliteNotificationViewStoreTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 21, 9, 41, 0, TimeSpan.Zero);

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"notification-view-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public async Task Notifications_the_cursor_and_the_replies_made_here_survive_a_new_store_over_the_same_file()
    {
        var ct = TestContext.Current.CancellationToken;
        var question = TestNotifications.Question("Run the migration against staging?", T0, body: "```mermaid\nflowchart LR\n A --> B\n```");
        var change = NotificationPayloads.ToChange(question, [], T0, "WORKSTATION-01");
        var reply = new NotificationReplyPost(Guid.CreateVersion7(), question.Id, question.OwnerInstanceId, ["yes"], null, T0.AddMinutes(1));

        var store = new SqliteNotificationViewStore(_path);
        await store.SaveAsync([new NotificationChangeRecord(change, Guid.NewGuid(), 7)], "pos:1", ct);
        await store.AddSentReplyAsync(reply, ct);
        await store.AddSentReplyAsync(reply, ct);

        _ = new Outbox.SqliteDeviceStore(_path);
        var reopened = new SqliteNotificationViewStore(_path);

        Assert.Equal("pos:1", reopened.ReadCursor());
        var row = Assert.Single(reopened.ReadRows());
        Assert.Equal(7, row.ServerTimestamp);
        var payload = NotificationPayloads.Read(row.Change);
        Assert.NotNull(payload);
        Assert.Equal("WORKSTATION-01", payload.OwnerName);
        Assert.Equal(question.BodyMarkdown, payload.Notification.BodyMarkdown);
        Assert.Equal(["yes", "no"], payload.Notification.AnswerSpec!.Choices.Select(choice => choice.Id));

        var sent = Assert.Single(reopened.ReadSentReplies());
        Assert.Equal(reply.Id, sent.Id);
        Assert.Equal(["yes"], sent.ChoiceIds);

        await reopened.ResetCursorAsync(ct);
        Assert.Null(reopened.ReadCursor());
        Assert.Single(reopened.ReadRows());
    }
}
