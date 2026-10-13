namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// How often a window resize reports the pane bounds to .NET.
/// <para>
/// A drag of the window edge fires <c>resize</c> dozens of times a second, and each
/// report is two interop calls and a forced style read per registered layout. The
/// listener used to be the reporter itself, so every event paid all of that for a
/// width the next event replaced before anything painted. One report per animation
/// frame is as current as the screen can show.
/// </para>
/// <para>
/// Only the resize listener is coalesced. <c>initialize</c> and <c>refresh</c> still
/// report at once, because their callers act on the answer in the same render;
/// <c>GlobalPaneMarkupTests</c> in the desktop suite pins those.
/// </para>
/// </summary>
public sealed class PaneBoundsResizeTests
{
    [Fact]
    public void A_resize_burst_reports_the_pane_bounds_once_per_frame()
    {
        var wiring = ComponentsScript.Region("    window.backlogPaneResizer = {", "    document.addEventListener('pointerdown',");

        Assert.DoesNotContain("window.addEventListener('resize', backlogReportPaneBounds)", wiring, StringComparison.Ordinal);
        Assert.Contains("window.addEventListener('resize', backlogSchedulePaneBounds)", wiring, StringComparison.Ordinal);

        var scheduler = ComponentsScript.Region("    function backlogSchedulePaneBounds() {", "    window.addEventListener('resize',");
        Assert.Contains("requestAnimationFrame(", scheduler, StringComparison.Ordinal);
        Assert.Contains("backlogReportPaneBounds();", scheduler, StringComparison.Ordinal);
    }
}
