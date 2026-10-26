using Backlog.Infrastructure.Sync.Notifications;
using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// The owner desktop writes the Android push preview on the envelope beside the document (local
/// ADR 0038 §4), so the sync service never reads the document to buzz a phone — and a private
/// notification's summary never leaves the desktop at all.
/// </summary>
public class NotificationPushPreviewTests
{
    private static readonly DateTimeOffset Created = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_change_carries_the_kind_severity_title_and_summary_as_the_preview()
    {
        var change = NotificationPayloads.ToChange(Snapshot(isPrivate: false), [], Created);

        Assert.Equal(
            new NotificationPushPreview("question", "warning", "Run the migration?", "Three tables move.", IsPrivate: false),
            change.Preview);
    }

    [Fact]
    public void A_private_notification_sends_no_summary_in_its_preview()
    {
        var change = NotificationPayloads.ToChange(Snapshot(isPrivate: true), [], Created);

        Assert.NotNull(change.Preview);
        Assert.Null(change.Preview.Summary);
        Assert.True(change.Preview.IsPrivate);
        Assert.Equal("Run the migration?", change.Preview.Title);
    }

    [Fact]
    public void The_preview_holds_to_the_services_bounds()
    {
        var change = NotificationPayloads.ToChange(Snapshot(isPrivate: false), [], Created);

        Assert.Null(NotificationRecordLimits.WhyRefused(change));
    }

    private static NotificationSnapshot Snapshot(bool isPrivate) =>
        new(
            Guid.Parse("0193a6f4-7b8c-7d2e-9f10-112233445566"),
            Guid.Parse("7f3a0000-0000-0000-0000-00000000000a"),
            new NotificationSource("sessions", "migration"),
            new NotificationSubject("sessions", "session", "sess-91"),
            NotificationKind.Question,
            NotificationSeverity.Warning,
            "Run the migration?",
            "Three tables move.",
            "The **body** stays home.",
            new AnswerSpec([new AnswerChoice("yes", "Yes"), new AnswerChoice("no", "No")]),
            DeepLink: null,
            isPrivate,
            Created,
            NotificationState.Open,
            SettledAt: null,
            CloseReason: null,
            Answer: null);
}
