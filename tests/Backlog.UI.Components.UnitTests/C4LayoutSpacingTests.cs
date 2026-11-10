namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The room the C4 zoom tab asks the layout for between cards. Larger views were
/// crowded: labels piled up on the lines round the system in the middle, and a frame
/// full of components left them no gap to sit in. The zoom tab widens the gaps; the C4
/// tab asks for nothing and keeps the layout it always had.
/// </summary>
public sealed class C4LayoutSpacingTests
{
    private static readonly C4Workspace Workspace = C4LayoutGroupTests.Workspace;

    private static readonly C4LayoutOptions Roomy = new() { ColumnGap = 80, RowGap = 140 };

    private static C4Diagram Layout(string key, C4LayoutOptions? options = null) =>
        C4LayoutEngine.Build(Workspace, Workspace.View(key)!, options);

    private static C4PlacedNode Card(C4Diagram diagram, string name) =>
        Assert.Single(diagram.Nodes, node => node.Node.Name == name);

    [Fact]
    public void Without_gaps_every_card_is_where_it_was()
    {
        foreach (var key in new[] { "landscape", "context-backlog", "containers-backlog" })
        {
            var before = Layout(key);
            var after = Layout(key, new C4LayoutOptions());

            Assert.Equal(
                before.Nodes.Select(node => (node.Alias, node.X, node.Y)),
                after.Nodes.Select(node => (node.Alias, node.X, node.Y)));
            Assert.Equal(before.Edges.Select(edge => edge.Path), after.Edges.Select(edge => edge.Path));
            Assert.Equal((before.Width, before.Height), (after.Width, after.Height));
        }
    }

    /// <summary>A wider column gap moves two cards in one frame further apart.</summary>
    [Fact]
    public void A_wider_column_gap_sets_cards_in_a_frame_further_apart()
    {
        var tight = Layout("containers-backlog");
        var roomy = Layout("containers-backlog", Roomy);

        static double Gap(C4Diagram diagram) =>
            Card(diagram, "Cloud Service").X - (Card(diagram, "Desktop App").X + Card(diagram, "Desktop App").Width);

        Assert.Equal(80, Gap(roomy), 3);
        Assert.True(Gap(roomy) > Gap(tight));
    }

    /// <summary>A wider row gap moves each row further below the one above it.</summary>
    [Fact]
    public void A_wider_row_gap_sets_the_rows_further_apart()
    {
        var tight = Layout("landscape");
        var roomy = Layout("landscape", Roomy);

        static double Drop(C4Diagram diagram) =>
            Card(diagram, "Prompt Backlog").Y - (Card(diagram, "ME").Y + Card(diagram, "ME").Height);

        Assert.True(Drop(roomy) >= 140);
        Assert.True(Drop(roomy) > Drop(tight));
    }

    [Fact]
    public void Roomier_cards_still_never_overlap()
    {
        foreach (var key in new[] { "landscape", "context-backlog", "containers-backlog" })
        {
            var nodes = Layout(key, Roomy).Nodes;

            for (var i = 0; i < nodes.Count; i++)
            {
                for (var j = i + 1; j < nodes.Count; j++)
                {
                    var apart = nodes[i].X + nodes[i].Width <= nodes[j].X || nodes[j].X + nodes[j].Width <= nodes[i].X
                        || nodes[i].Y + nodes[i].Height <= nodes[j].Y || nodes[j].Y + nodes[j].Height <= nodes[i].Y;

                    Assert.True(apart, $"{key}: {nodes[i].Alias} overlaps {nodes[j].Alias}");
                }
            }
        }
    }

    /// <summary>The zoom tab asks for the roomier layout; the C4 tab, which passes no
    /// options, keeps its own.</summary>
    [Fact]
    public void The_zoom_tab_lays_its_views_out_roomier_than_the_C4_tab()
    {
        var zoom = C4ZoomExplorer.Layout;

        Assert.True(zoom.GroupBands && zoom.BandsBeside && zoom.NestedDeploymentNodes);

        var classic = Layout("containers-backlog");
        var roomy = Layout("containers-backlog", zoom);

        static double Gap(C4Diagram diagram) =>
            Card(diagram, "Cloud Service").X - (Card(diagram, "Desktop App").X + Card(diagram, "Desktop App").Width);

        Assert.True(Gap(roomy) > Gap(classic));
    }
}
