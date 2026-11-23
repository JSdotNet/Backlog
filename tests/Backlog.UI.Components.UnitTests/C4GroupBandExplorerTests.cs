namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The zoom navigation draws a grouped model's outside systems as group bands.
/// </summary>
public sealed class C4GroupBandExplorerTests : IDisposable
{
    private readonly BunitContext _context = new();

    public C4GroupBandExplorerTests()
    {
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.JSInterop.Setup<bool>("backlogC4Zoom.attach", _ => true).SetResult(true);
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public void The_zoom_tab_draws_the_groups_as_bands()
    {
        var explorer = _context.Render<C4ZoomExplorer>(parameters => parameters
            .Add(c => c.Workspace, C4LayoutGroupTests.Workspace)
            .Add(c => c.HintDismissed, true));

        // The zoom tab opens on the owned system's context, whose outside systems are
        // the same three groups the landscape draws.
        Assert.Equal("context-backlog", explorer.Instance.ViewKey);

        var names = explorer.FindAll("svg.c4-diagram g.c4-diagram__group")
            .Select(band => band.GetAttribute("data-c4-group"))
            .ToList();

        Assert.Equal(["Capture sources", "Work tracking", "Operations"], names);
    }

    /// <summary>As the design's landscape draws them: one column of bands to the right
    /// of the system, and a band's line says how many relationships it stands for.</summary>
    [Fact]
    public void The_zoom_tab_draws_the_bands_beside_the_system_and_counts_a_merged_line()
    {
        var explorer = _context.Render<C4ZoomExplorer>(parameters => parameters
            .Add(c => c.Workspace, C4LayoutGroupTests.Workspace)
            .Add(c => c.HintDismissed, true));

        var system = explorer.Find("svg.c4-diagram g.c4-diagram__node[id$='-backlog'] rect");
        var right = Number(system.GetAttribute("x")) + Number(system.GetAttribute("width"));

        var bands = explorer.FindAll("svg.c4-diagram g.c4-diagram__group > rect");
        Assert.Equal(3, bands.Count);
        Assert.All(bands, band => Assert.True(Number(band.GetAttribute("x")) >= right));

        Assert.Contains(
            explorer.FindAll("svg.c4-diagram g.c4-diagram__edge-label"),
            label => label.TextContent.Contains("3 relationships", StringComparison.Ordinal));
    }

    private static double Number(string? value) =>
        double.Parse(value ?? "NaN", System.Globalization.CultureInfo.InvariantCulture);
}
