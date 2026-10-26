using System.Text.Json;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Features.PushNotifications;

namespace Backlog.Modules.Sync.UnitTests;

/// <summary>
/// What a phone is sent when a notification is published or changes state (local ADR 0039 §4):
/// a data-only message whose values are all strings, carrying the envelope and the owner's push
/// preview — and never the body, which stays in the opaque document the service does not read.
/// </summary>
public class NotificationPushMessagesTests
{
    private static readonly Guid Id = Guid.Parse("0193a6f4-7b8c-7d2e-9f10-112233445566");

    private static readonly Guid Instance = Guid.Parse("7f3a0000-0000-0000-0000-00000000000a");

    [Fact]
    public void The_message_carries_the_id_kind_severity_state_title_and_summary_as_strings()
    {
        var change = Change(NotificationStates.Open, new NotificationPushPreview(
            NotificationKinds.Question, NotificationSeverities.Warning, "Run the migration?", "Three tables move.", IsPrivate: false));

        var message = NotificationPushMessages.For(change);

        Assert.Equal(Id, message.NotificationId);
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["id"] = "0193a6f4-7b8c-7d2e-9f10-112233445566",
                ["kind"] = "question",
                ["severity"] = "warning",
                ["state"] = "open",
                ["title"] = "Run the migration?",
                ["summary"] = "Three tables move.",
            },
            message.Data);
    }

    [Fact]
    public void The_body_never_travels_even_when_the_document_holds_one()
    {
        var change = Change(NotificationStates.Open, new NotificationPushPreview(
            NotificationKinds.Alert, NotificationSeverities.Info, "Build finished", "All green.", IsPrivate: false));

        var message = NotificationPushMessages.For(change);

        Assert.DoesNotContain(message.Data.Keys, key => key.Contains("body", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(message.Data.Values, value => value.Contains("SECRET BODY", StringComparison.Ordinal));
    }

    [Fact]
    public void A_private_notification_says_only_that_backlog_has_a_question()
    {
        var change = Change(NotificationStates.Open, new NotificationPushPreview(
            NotificationKinds.Question, NotificationSeverities.Critical, "Approve the payout?", "To account 1234.", IsPrivate: true));

        var message = NotificationPushMessages.For(change);

        Assert.Equal("Backlog has a question for you", message.Data["summary"]);
        Assert.Equal("Approve the payout?", message.Data["title"]);
    }

    /// <summary>An owner that predates the preview pushes none; the phone still learns which
    /// notification moved and where it stands, and pulls the rest.</summary>
    [Fact]
    public void A_change_without_a_preview_sends_only_its_id_and_state()
    {
        var message = NotificationPushMessages.For(Change(NotificationStates.Answered, preview: null));

        Assert.Equal(
            new Dictionary<string, string>
            {
                ["id"] = "0193a6f4-7b8c-7d2e-9f10-112233445566",
                ["state"] = "answered",
            },
            message.Data);
    }

    private static NotificationChange Change(string state, NotificationPushPreview? preview) =>
        new(
            Id,
            Instance,
            state,
            new DateTimeOffset(2026, 10, 26, 9, 0, 0, TimeSpan.Zero),
            JsonSerializer.SerializeToElement(new { title = "ignored", bodyMarkdown = "SECRET BODY" }),
            preview);
}
