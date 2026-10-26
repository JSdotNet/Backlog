using System.Text.Json;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Sync.UnitTests;

/// <summary>
/// The push preview is held to the notification's own bounds at the edge — title 80, summary
/// 180, a known kind and severity — so a phone's tray never shows more than the owner's
/// notification could hold (<c>.devbook/domain/notifications/domain.md</c>).
/// </summary>
public class NotificationPushPreviewLimitsTests
{
    [Fact]
    public void A_preview_within_the_bounds_is_stored()
    {
        Assert.Null(NotificationRecordLimits.WhyRefused(Change(Preview())));
    }

    [Fact]
    public void A_change_without_a_preview_is_still_stored()
    {
        Assert.Null(NotificationRecordLimits.WhyRefused(Change(preview: null)));
    }

    [Fact]
    public void A_title_longer_than_80_characters_is_refused()
    {
        Assert.NotNull(NotificationRecordLimits.WhyRefused(Change(Preview() with { Title = new string('t', 81) })));
    }

    [Fact]
    public void A_blank_title_is_refused()
    {
        Assert.NotNull(NotificationRecordLimits.WhyRefused(Change(Preview() with { Title = " " })));
    }

    [Fact]
    public void A_summary_longer_than_180_characters_is_refused()
    {
        Assert.NotNull(NotificationRecordLimits.WhyRefused(Change(Preview() with { Summary = new string('s', 181) })));
    }

    [Fact]
    public void An_unknown_kind_is_refused()
    {
        Assert.NotNull(NotificationRecordLimits.WhyRefused(Change(Preview() with { Kind = "reminder" })));
    }

    [Fact]
    public void An_unknown_severity_is_refused()
    {
        Assert.NotNull(NotificationRecordLimits.WhyRefused(Change(Preview() with { Severity = "Warning" })));
    }

    private static NotificationPushPreview Preview() =>
        new("question", "warning", new string('t', 80), new string('s', 180), IsPrivate: false);

    private static NotificationChange Change(NotificationPushPreview? preview) =>
        new(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            NotificationStates.Open,
            DateTimeOffset.UtcNow,
            JsonSerializer.SerializeToElement(new { title = "Run the migration?" }),
            preview);
}
