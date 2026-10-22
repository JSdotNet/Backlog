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

    /// <summary>The arrows and the ends move the keyboard to another row — its
    /// focus, the tab stop and the active row a host spotlights — and choose
    /// nothing. A selection is what opens a host's sheet, and a sheet that opened
    /// on every arrow press would leave no way to browse the list.</summary>
    [Theory]
    [InlineData("ArrowDown", "roux")]
    [InlineData("ArrowRight", "roux")]
    [InlineData("ArrowUp", "flour")]
    [InlineData("ArrowLeft", "flour")]
    [InlineData("Home", "flour")]
    [InlineData("End", "gratin")]
    public void The_arrows_and_the_ends_move_the_active_row_and_leave_the_selection(string key, string expected)
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var selections = new List<string?>();
        string? active = null;

        var index = context.Render<GraphAtlasIndex>(parameters => parameters
            .Add(i => i.Nodes, Nodes())
            .Add(i => i.SelectedId, "flour")
            .Add(i => i.SelectedIdChanged, EventCallback.Factory.Create<string?>(new object(), selections.Add))
            .Add(i => i.ActiveIdChanged, EventCallback.Factory.Create<string?>(new object(), value => active = value)));

        index.Find(".graph-atlas-index").KeyDown(new KeyboardEventArgs { Key = key });

        Assert.Empty(selections);
        Assert.Equal(expected, active ?? "flour");

        var stop = Assert.Single(index.FindAll("[role=\"option\"]"), row => row.GetAttribute("tabindex") == "0");
        Assert.Equal(expected, stop.GetAttribute("data-node-id"));
        Assert.Equal("true", index.Find("[data-node-id=\"flour\"]").GetAttribute("aria-selected"));
    }

    /// <summary>Focus follows the arrow to the row it reached, so a screen reader
    /// reads that row out — the sheet is not what speaks. With nothing selected
    /// the focus starts on the first row, the tab stop, so the first Down goes to
    /// the second.</summary>
    [Fact]
    public void An_arrow_moves_the_focus_to_the_row_it_reached()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var index = Render(context);
        index.Find(".graph-atlas-index").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        index.WaitForAssertion(() => Assert.Single(context.JSInterop.Invocations, invocation => invocation.Identifier == "Blazor._internal.domWrapper.focus"));
        Assert.Equal("0", index.Find("[data-node-id=\"roux\"]").GetAttribute("tabindex"));
        Assert.Equal("-1", index.Find("[data-node-id=\"flour\"]").GetAttribute("tabindex"));
    }

    /// <summary>Enter or Space chooses the row the keyboard is on — the active one,
    /// not the one selected before the reader started arrowing.</summary>
    [Theory]
    [InlineData("Enter")]
    [InlineData(" ")]
    public void Enter_or_space_selects_the_row_the_keyboard_is_on(string key)
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var selections = new List<string?>();

        var index = Render(context, selected: "flour", onSelected: selections.Add);
        index.Find(".graph-atlas-index").KeyDown(new KeyboardEventArgs { Key = "End" });
        Assert.Empty(selections);

        index.Find(".graph-atlas-index").KeyDown(new KeyboardEventArgs { Key = key });

        Assert.Equal(["gratin"], selections);
    }

    /// <summary>With nothing selected and nothing arrowed to, the keyboard is on
    /// the first row — the tab stop — and Enter chooses it.</summary>
    [Fact]
    public void Enter_with_nothing_selected_chooses_the_tab_stop()
    {
        using var context = new BunitContext();
        string? selected = null;

        var index = Render(context, onSelected: value => selected = value);
        index.Find(".graph-atlas-index").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal("flour", selected);
    }

    /// <summary>A host that hands the active row in is believed: the tab stop is
    /// on it, whatever is selected.</summary>
    [Fact]
    public void The_tab_stop_follows_an_active_row_the_host_hands_in()
    {
        using var context = new BunitContext();

        var index = context.Render<GraphAtlasIndex>(parameters => parameters
            .Add(i => i.Nodes, Nodes())
            .Add(i => i.SelectedId, "flour")
            .Add(i => i.ActiveId, "roux"));

        Assert.Equal("0", index.Find("[data-node-id=\"roux\"]").GetAttribute("tabindex"));
        Assert.Equal("-1", index.Find("[data-node-id=\"flour\"]").GetAttribute("tabindex"));
    }

    [Fact]
    public void Clicking_a_row_selects_it()
    {
        using var context = new BunitContext();
        string? selected = null;
        string? active = null;

        var index = context.Render<GraphAtlasIndex>(parameters => parameters
            .Add(i => i.Nodes, Nodes())
            .Add(i => i.SelectedIdChanged, EventCallback.Factory.Create<string?>(new object(), value => selected = value))
            .Add(i => i.ActiveIdChanged, EventCallback.Factory.Create<string?>(new object(), value => active = value)));
        index.FindAll("[role=\"option\"]")[1].Click();

        Assert.Equal("roux", selected);
        Assert.Equal("roux", active);
    }

    /// <summary>The half bUnit cannot render: the arrows and Space would also
    /// scroll the index, which scrolls itself, unless their default is refused
    /// at the keydown — a state-bound <c>@onkeydown:preventDefault</c> is one key
    /// late. Only the default: the key still bubbles to the index's handler.</summary>
    [Fact]
    public void Components_js_refuses_the_index_keys_default_at_the_keydown_and_lets_them_bubble()
    {
        var script = File.ReadAllText(RepositoryRoot.File("src", "Core", "Backlog.UI.Components", "wwwroot", "components.js"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        var start = script.IndexOf("const GRAPH_ATLAS_INDEX_SWALLOWED_KEYS", StringComparison.Ordinal);
        Assert.True(start >= 0, "components.js no longer refuses the index keys.");

        var listener = script[start..script.IndexOf("\n    });", start, StringComparison.Ordinal)];

        Assert.Contains("new Set(['ArrowDown', 'ArrowUp', 'ArrowLeft', 'ArrowRight', 'Home', 'End', ' '])", listener, StringComparison.Ordinal);
        Assert.Contains(".graph-atlas-index__option", listener, StringComparison.Ordinal);
        Assert.Contains("event.preventDefault()", listener, StringComparison.Ordinal);
        Assert.DoesNotContain("stopPropagation", listener, StringComparison.Ordinal);
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
