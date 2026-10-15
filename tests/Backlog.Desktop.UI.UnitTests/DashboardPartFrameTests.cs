using Backlog.Modules.Dashboard.UI.Parts;
using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The frame every dashboard part wears, while its part refreshes: the refresh control
/// stays in the tab order with a spinner for its glyph and drops a second press, and the
/// part's content says it is busy (interaction-guidelines.md#responsiveness).
/// </summary>
public sealed class DashboardPartFrameTests
{
    [Fact]
    public void A_refreshing_part_keeps_its_refresh_focusable_and_marks_its_content_busy()
    {
        using var context = new BunitContext();
        var presses = 0;

        var frame = context.Render<PartFrame>(parameters => parameters
            .Add(p => p.Title, "Throughput")
            .Add(p => p.Busy, true)
            .Add(p => p.TestId, "part")
            .Add(p => p.OnRefresh, () => presses++)
            .AddChildContent("<p>figures</p>"));

        var refresh = frame.Find("[data-testid='part-refresh']");
        Assert.False(refresh.HasAttribute("disabled"));
        Assert.Equal("true", refresh.GetAttribute("aria-disabled"));
        Assert.Equal("true", refresh.GetAttribute("aria-busy"));
        Assert.NotNull(refresh.QuerySelector(".spinner__ring"));
        Assert.Equal("true", frame.Find(".dashboard-part__content").GetAttribute("aria-busy"));
        Assert.Contains("figures", frame.Find(".dashboard-part__content").TextContent, StringComparison.Ordinal);

        refresh.Click();
        Assert.Equal(0, presses);
    }

    [Fact]
    public void An_idle_part_offers_refresh_and_is_not_busy()
    {
        using var context = new BunitContext();
        var presses = 0;

        var frame = context.Render<PartFrame>(parameters => parameters
            .Add(p => p.Title, "Throughput")
            .Add(p => p.TestId, "part")
            .Add(p => p.OnRefresh, () => presses++));

        var refresh = frame.Find("[data-testid='part-refresh']");
        Assert.False(refresh.HasAttribute("aria-disabled"));
        Assert.Null(frame.Find(".dashboard-part__content").GetAttribute("aria-busy"));

        refresh.Click();
        Assert.Equal(1, presses);
    }
}
