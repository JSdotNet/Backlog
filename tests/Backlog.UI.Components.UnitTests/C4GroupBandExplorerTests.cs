namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// Which of the two C4 tabs draws group bands. The zoom tab asks the layout for them;
/// the C4 tab does not, and local ADR 0040 holds its picture still while the two stand
/// side by side — so the C4 tab draws a grouped model exactly as it drew it before
/// bands existed.
/// </summary>
public sealed class C4GroupBandExplorerTests : IDisposable
{
    private readonly BunitContext _context = new();

    public C4GroupBandExplorerTests()
    {
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.JSInterop.Setup<bool>("backlogC4Explorer.attach", _ => true).SetResult(true);
        _context.JSInterop.Setup<bool>("backlogC4Zoom.attach", _ => true).SetResult(true);
    }

    public void Dispose() => _context.Dispose();

    /// <summary>The C4 tab's landscape of the grouped model is the layout with no
    /// options, written with none: no band, no line to one, every member its own card.</summary>
    [Fact]
    public void The_C4_tab_draws_a_grouped_model_unchanged()
    {
        var explorer = _context.Render<C4Explorer>(parameters => parameters
            .Add(c => c.Workspace, C4LayoutGroupTests.Workspace)
            .Add(c => c.ViewKey, "landscape"));

        var markup = explorer.Find("svg.c4-diagram").OuterHtml;

        Assert.Empty(explorer.FindAll("svg.c4-diagram g.c4-diagram__group"));
        Assert.DoesNotContain("data-c4-group", markup, StringComparison.Ordinal);

        var view = C4LayoutGroupTests.Workspace.View("landscape")!;
        var unchanged = C4LayoutEngine.Build(C4LayoutGroupTests.Workspace, view);

        Assert.Equal(unchanged.Nodes.Count, explorer.FindAll("svg.c4-diagram g.c4-diagram__node").Count);
        Assert.Equal(unchanged.Edges.Count, explorer.FindAll("svg.c4-diagram g.c4-diagram__edge").Count);
    }

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
}
