namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The room the layout leaves between cards. Larger views were crowded: labels piled up
/// on the lines round the system in the middle, and a frame full of components left
/// them no gap to sit in. The gaps are the zoom navigation's wider ones, which every
/// view is laid out with since the C4 tab is retired (local ADR 0046).
/// </summary>
public sealed class C4LayoutSpacingTests
{
    private static readonly C4Workspace Workspace = C4LayoutGroupTests.Workspace;

    private static C4Diagram Layout(string key) =>
        C4LayoutEngine.Build(Workspace, Workspace.View(key)!);

    private static C4PlacedNode Card(C4Diagram diagram, string name) =>
        Assert.Single(diagram.Nodes, node => node.Node.Name == name);

    /// <summary>Two cards in one frame sit a column gap apart.</summary>
    [Fact]
    public void Cards_in_a_frame_sit_a_column_gap_apart()
    {
        var diagram = Layout("containers-backlog");

        var gap = Card(diagram, "Cloud Service").X - (Card(diagram, "Desktop App").X + Card(diagram, "Desktop App").Width);

        Assert.Equal(80, gap, 3);
    }

    /// <summary>Each row sits at least a row gap below the one above it.</summary>
    [Fact]
    public void Each_row_sits_a_row_gap_below_the_one_above()
    {
        var diagram = Layout("landscape");

        var drop = Card(diagram, "Prompt Backlog").Y - (Card(diagram, "ME").Y + Card(diagram, "ME").Height);

        Assert.True(drop >= 144);
    }

    [Fact]
    public void Cards_never_overlap()
    {
        foreach (var key in new[] { "landscape", "context-backlog", "containers-backlog" })
        {
            var nodes = Layout(key).Nodes;

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
}
