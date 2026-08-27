namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The index carries the atlas's whole accessibility claim: the canvas beside it
/// is deliberately unreachable, so anything a reader cannot do here they cannot
/// do at all. These are the most load-bearing tests in the feature.
/// </summary>
public sealed class GraphAtlasIndexTests
{
    private static IReadOnlyList<GraphAtlasNode> Nodes() =>
    [
        new("flour", "Flour", "staple", "adopted", "active", "Pantry", 3, 0),
        new("roux", "Roux", "preparation", "hold", "blocked", "Preparations", 1, 2),
        new("gratin", "Gratin", "dish", "retired", "archived", "Dishes", 0, 2)
    ];

    private static IRenderedComponent<GraphAtlasIndex> Render(BunitContext context, string? selected = null, Action<string?>? onSelected = null) =>
        context.Render<GraphAtlasIndex>(parameters =>
        {
            parameters.Add(i => i.Nodes, Nodes());
            parameters.Add(i => i.SelectedId, selected);

            if (onSelected is not null)
            {
                parameters.Add(i => i.SelectedIdChanged, EventCallback.Factory.Create<string?>(new object(), onSelected));
            }
        });

    [Fact]
    public void Every_node_is_an_option()
    {
        using var context = new BunitContext();

        var index = Render(context);

        Assert.Equal("listbox", index.Find(".graph-atlas-index").GetAttribute("role"));
        Assert.Equal(3, index.FindAll("[role=\"option\"]").Count);
    }

    /// <summary>The order is the host's. A list that sorted for itself would
    /// disagree with the reading order the pager steps through.</summary>
    [Fact]
    public void The_order_is_the_order_it_was_given()
    {
        using var context = new BunitContext();

        var index = Render(context);
        var labels = index.FindAll(".graph-atlas-index__label").Select(row => row.TextContent.Trim()).ToArray();

        Assert.Equal(["Flour", "Roux", "Gratin"], labels);
    }

    /// <summary>Colour is never the sole carrier, so the status word is written
    /// out as text rather than left to the mark.</summary>
    [Fact]
    public void Each_row_writes_its_status_out()
    {
        using var context = new BunitContext();

        var index = Render(context);
        var statuses = index.FindAll(".graph-atlas-index__status").Select(row => row.TextContent.Trim()).ToArray();

        Assert.Equal(["adopted", "hold", "retired"], statuses);
    }

    /// <summary>Degree is what sizes a node on the map, so it is exactly the fact
    /// the list must not leave to the map.</summary>
    [Fact]
    public void Each_row_says_how_much_leans_on_it()
    {
        using var context = new BunitContext();

        var index = Render(context);
        var first = index.FindAll("[role=\"option\"]")[0];

        Assert.Equal("3", first.QuerySelector(".graph-atlas-index__degree-value")!.TextContent.Trim());
        Assert.Contains("3 dependents, 0 dependencies", first.TextContent);
    }

    /// <summary>One tab stop for the whole list, not one per node.</summary>
    [Fact]
    public void Exactly_one_row_is_in_the_tab_order()
    {
        using var context = new BunitContext();

        var index = Render(context);
        var stops = index.FindAll("[role=\"option\"]").Count(row => row.GetAttribute("tabindex") == "0");

        Assert.Equal(1, stops);
    }

    /// <summary>And it is the selected one, so tabbing back into the list lands
    /// where the reader left it.</summary>
    [Fact]
    public void The_tab_stop_follows_the_selection()
    {
        using var context = new BunitContext();

        var index = Render(context, selected: "gratin");
        var rows = index.FindAll("[role=\"option\"]");

        Assert.Equal("-1", rows[0].GetAttribute("tabindex"));
        Assert.Equal("0", rows[2].GetAttribute("tabindex"));
        Assert.Equal("true", rows[2].GetAttribute("aria-selected"));
    }

    [Theory]
    [InlineData("ArrowDown", "roux")]
    [InlineData("ArrowUp", "flour")]
    [InlineData("Home", "flour")]
    [InlineData("End", "gratin")]
    public void The_arrows_and_the_ends_move_the_selection(string key, string expected)
    {
        using var context = new BunitContext();
        string? selected = null;

        var index = Render(context, selected: "flour", onSelected: value => selected = value);
        index.Find(".graph-atlas-index").KeyDown(new KeyboardEventArgs { Key = key });

        Assert.Equal(expected, selected ?? "flour");
    }

    [Fact]
    public void Clicking_a_row_selects_it()
    {
        using var context = new BunitContext();
        string? selected = null;

        var index = Render(context, onSelected: value => selected = value);
        index.FindAll("[role=\"option\"]")[1].Click();

        Assert.Equal("roux", selected);
    }

    /// <summary>A status in no vocabulary is flagged rather than styled: the word
    /// is printed exactly as written, and the mark falls back to the unknown one
    /// instead of quietly borrowing another tone's.</summary>
    [Fact]
    public void A_status_in_no_vocabulary_is_printed_as_written_and_marked_unknown()
    {
        using var context = new BunitContext();

        var index = context.Render<GraphAtlasIndex>(parameters => parameters
            .Add(i => i.Nodes, [new GraphAtlasNode("x", "Mystery", "tone", "yolo", string.Empty, "Ladder")]));

        Assert.Equal("yolo", index.Find(".graph-atlas-index__status").TextContent.Trim());
        Assert.Equal("unknown", index.Find(".graph-atlas-index__mark").GetAttribute("data-tone"));
    }
}
