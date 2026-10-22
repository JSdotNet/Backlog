using Microsoft.JSInterop;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The picture is drawn by JS, which bUnit does not run. What is worth pinning
/// here is the contract around it — and, more than for the explorer, the
/// direction the selection flows in.
///
/// <para>The atlas has three surfaces that all show the same selection: a
/// canvas, a list, and a sheet. They agree because exactly one of them decides,
/// and that is C#. Several of the facts below exist only to keep it that
/// way.</para>
/// </summary>
public sealed class GraphAtlasTests
{
    private static object Model() => new
    {
        nodes = new[] { new { id = "a", label = "A" } },
        edges = Array.Empty<object>()
    };

    private static IReadOnlyList<GraphAtlasNode> Nodes() =>
    [
        new("a", "Alpha", "staple", "adopted", "active", "Pantry", 3, 0),
        new("b", "Beta", "dish", "candidate", "ready", "Dishes", 0, 2)
    ];

    [Fact]
    public void The_model_is_handed_to_the_atlas_renderer_by_default()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var data = Model();

        context.Render<GraphAtlas>(parameters => parameters.Add(a => a.Data, data));

        var invocation = Assert.Single(context.JSInterop.Invocations["backlogGraphAtlas.render"]);

        Assert.Same(data, invocation.Arguments[2]);
    }

    [Fact]
    public void A_caller_can_point_the_atlas_at_a_renderer_of_its_own()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var data = Model();

        context.Render<GraphAtlas>(parameters => parameters
            .Add(a => a.Data, data)
            .Add(a => a.JsFunction, "backlogDiagrams.renderTechnologyAtlas"));

        var invocation = Assert.Single(context.JSInterop.Invocations["backlogDiagrams.renderTechnologyAtlas"]);

        Assert.Same(data, invocation.Arguments[2]);
        Assert.Empty(context.JSInterop.Invocations["backlogGraphAtlas.render"]);
    }

    /// <summary>The renderer needs a way back to report a pick, and the model has
    /// to stay at index two or every assertion above it moves.</summary>
    [Fact]
    public void The_renderer_is_given_a_way_to_report_back()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        context.Render<GraphAtlas>(parameters => parameters.Add(a => a.Data, Model()));

        var invocation = Assert.Single(context.JSInterop.Invocations["backlogGraphAtlas.render"]);

        Assert.IsType<DotNetObjectReference<GraphAtlas>>(invocation.Arguments[3]);
    }

    [Fact]
    public void The_same_model_is_not_rendered_twice()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var data = Model();

        var atlas = context.Render<GraphAtlas>(parameters => parameters.Add(a => a.Data, data));
        atlas.Render();

        Assert.Single(context.JSInterop.Invocations["backlogGraphAtlas.render"]);
    }

    [Fact]
    public void A_new_model_is_rendered_again()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var atlas = context.Render<GraphAtlas>(parameters => parameters.Add(a => a.Data, Model()));
        atlas.Render(parameters => parameters.Add(a => a.Data, Model()));

        Assert.Equal(2, context.JSInterop.Invocations["backlogGraphAtlas.render"].Count);
    }

    [Fact]
    public async Task A_pick_on_the_canvas_becomes_the_selection()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        string? selected = null;

        var atlas = context.Render<GraphAtlas>(parameters => parameters
            .Add(a => a.Data, Model())
            .Add(a => a.Nodes, Nodes())
            .Add(a => a.SelectedIdChanged, EventCallback.Factory.Create<string?>(this, value => selected = value)));

        await atlas.Instance.NodePicked("b");

        Assert.Equal("b", selected);
    }

    /// <summary>The canvas is told what is selected and reports what was picked.
    /// Without this, telling it echoes straight back as a pick and the two sides
    /// bounce a selection between them.</summary>
    [Fact]
    public async Task A_pick_on_what_is_already_selected_is_not_reported_again()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var raised = 0;

        var atlas = context.Render<GraphAtlas>(parameters => parameters
            .Add(a => a.Data, Model())
            .Add(a => a.Nodes, Nodes())
            .Add(a => a.SelectedId, "b")
            .Add(a => a.SelectedIdChanged, EventCallback.Factory.Create<string?>(this, _ => raised++)));

        await atlas.Instance.NodePicked("b");

        Assert.Equal(0, raised);
    }

    /// <summary>Moving the selection is a camera move, not a redraw. Re-rendering
    /// the scene every time a reader arrowed down the list would repaint sixty
    /// nodes per keystroke.</summary>
    [Fact]
    public void A_selection_change_moves_the_camera_rather_than_redrawing_the_scene()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var data = Model();

        var atlas = context.Render<GraphAtlas>(parameters => parameters
            .Add(a => a.Data, data)
            .Add(a => a.Nodes, Nodes()));

        atlas.Render(parameters => parameters
            .Add(a => a.Data, data)
            .Add(a => a.Nodes, Nodes())
            .Add(a => a.SelectedId, "a"));

        Assert.Single(context.JSInterop.Invocations["backlogGraphAtlas.render"]);
        Assert.Contains(context.JSInterop.Invocations["backlogGraphAtlas.select"],
            invocation => Equals(invocation.Arguments[1], "a"));
    }

    /// <summary>Arrowing down the index spotlights the row on the picture and
    /// selects nothing, so the host's sheet stays shut while the reader browses;
    /// Enter then selects the row the spotlight is on.</summary>
    [Fact]
    public void Arrowing_the_index_spotlights_the_row_on_the_picture_and_enter_selects_it()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var selections = new List<string?>();

        var atlas = context.Render<GraphAtlas>(parameters => parameters
            .Add(a => a.Data, Model())
            .Add(a => a.Nodes, Nodes())
            .Add(a => a.SelectedIdChanged, EventCallback.Factory.Create<string?>(this, selections.Add)));

        atlas.Find(".graph-atlas-index").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        Assert.Empty(selections);
        atlas.WaitForAssertion(() => Assert.Equal("b", context.JSInterop.Invocations["backlogGraphAtlas.select"][^1].Arguments[1]));
        Assert.Equal("ready", atlas.Find(".graph-atlas__canvas").GetAttribute("data-tone"));

        atlas.Find(".graph-atlas-index").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal(["b"], selections);
    }

    /// <summary>A pick on the canvas moves the list's tab stop with it, so the
    /// keyboard carries on from the node the pointer chose.</summary>
    [Fact]
    public async Task A_pick_on_the_canvas_moves_the_lists_tab_stop_to_it()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var atlas = context.Render<GraphAtlas>(parameters => parameters
            .Add(a => a.Data, Model())
            .Add(a => a.Nodes, Nodes()));

        await atlas.Instance.NodePicked("b");

        atlas.WaitForAssertion(() => Assert.Equal("0", atlas.Find("[data-node-id=\"b\"]").GetAttribute("tabindex")));
    }

    /// <summary>Clearing the selection — closing the host's sheet — leaves the
    /// keyboard on the row it was on. The sheet gives the focus back to that row,
    /// and the list has to still be on it, so the tab stop stays; the spotlight
    /// goes, as it did before the keyboard had a spotlight of its own.</summary>
    [Fact]
    public void Clearing_the_selection_leaves_the_tab_stop_on_the_row_and_clears_the_spotlight()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var data = Model();

        var atlas = context.Render<GraphAtlas>(parameters => parameters
            .Add(a => a.Data, data)
            .Add(a => a.Nodes, Nodes())
            .Add(a => a.SelectedId, "b"));

        atlas.Render(parameters => parameters
            .Add(a => a.Data, data)
            .Add(a => a.Nodes, Nodes())
            .Add(a => a.SelectedId, null));

        Assert.Equal("0", atlas.Find("[data-node-id=\"b\"]").GetAttribute("tabindex"));
        Assert.Equal("false", atlas.Find("[data-node-id=\"b\"]").GetAttribute("aria-selected"));
        Assert.Null(context.JSInterop.Invocations["backlogGraphAtlas.select"][^1].Arguments[1]);
        Assert.Equal(string.Empty, atlas.Find(".graph-atlas__canvas").GetAttribute("data-tone"));
    }

    /// <summary>A pick on empty canvas clears the spotlight, whichever row the
    /// keyboard last moved it to.</summary>
    [Fact]
    public async Task A_pick_on_empty_canvas_clears_a_spotlight_the_keyboard_set()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var atlas = context.Render<GraphAtlas>(parameters => parameters
            .Add(a => a.Data, Model())
            .Add(a => a.Nodes, Nodes())
            .Add(a => a.SelectedId, "a"));

        atlas.Find(".graph-atlas-index").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        atlas.WaitForAssertion(() => Assert.Equal("b", context.JSInterop.Invocations["backlogGraphAtlas.select"][^1].Arguments[1]));

        await atlas.Instance.NodePicked(null);

        atlas.WaitForAssertion(() => Assert.Null(context.JSInterop.Invocations["backlogGraphAtlas.select"][^1].Arguments[1]));
    }

    /// <summary>An active row the nodes no longer hold is forgotten, so a later
    /// set of nodes that brings the id back puts the tab stop where the selection
    /// is, not on the old row.</summary>
    [Fact]
    public void An_active_row_the_nodes_no_longer_hold_is_forgotten()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var data = Model();

        var atlas = context.Render<GraphAtlas>(parameters => parameters
            .Add(a => a.Data, data)
            .Add(a => a.Nodes, Nodes()));

        atlas.Find(".graph-atlas-index").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        atlas.Render(parameters => parameters.Add(a => a.Nodes, [Nodes()[0]]));
        atlas.Render(parameters => parameters.Add(a => a.Nodes, Nodes()));

        Assert.Equal("0", atlas.Find("[data-node-id=\"a\"]").GetAttribute("tabindex"));
        Assert.Equal("-1", atlas.Find("[data-node-id=\"b\"]").GetAttribute("tabindex"));
    }

    /// <summary>A canvas has no accessibility tree, so it is kept out of the one
    /// the page has rather than given a label that would announce the whole graph
    /// as a single unnavigable node.</summary>
    [Fact]
    public void The_picture_is_not_on_the_accessibility_tree_and_the_region_around_it_is()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var atlas = context.Render<GraphAtlas>(parameters => parameters
            .Add(a => a.Data, Model())
            .Add(a => a.Nodes, Nodes())
            .Add(a => a.AriaLabel, "Kitchen atlas")
            .Add(a => a.StatusText, "Rendering kitchen..."));

        var canvas = atlas.Find(".graph-atlas__canvas");

        Assert.Equal("region", canvas.GetAttribute("role"));
        Assert.Equal("Kitchen atlas", canvas.GetAttribute("aria-label"));
        Assert.Equal("Rendering kitchen...", atlas.Find(".graph-atlas__status").TextContent.Trim());
    }

    /// <summary>The list is rendered by Blazor and is the surface a keyboard
    /// reaches, so it has to be there whether or not the renderer ever ran.</summary>
    [Fact]
    public void The_nodes_are_listed_beside_the_picture()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var atlas = context.Render<GraphAtlas>(parameters => parameters
            .Add(a => a.Data, Model())
            .Add(a => a.Nodes, Nodes()));

        Assert.Equal(2, atlas.FindAll("[data-testid=\"graph-atlas-index-option\"]").Count);
    }

    /// <summary>The surface takes its cast from the selected node's tone. Nothing
    /// selected is no cast, not a default one.</summary>
    [Fact]
    public void The_stage_wears_the_selected_nodes_tone()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var atlas = context.Render<GraphAtlas>(parameters => parameters
            .Add(a => a.Data, Model())
            .Add(a => a.Nodes, Nodes()));

        Assert.Equal(string.Empty, atlas.Find(".graph-atlas__canvas").GetAttribute("data-tone"));

        atlas.Render(parameters => parameters.Add(a => a.SelectedId, "a"));

        Assert.Equal("active", atlas.Find(".graph-atlas__canvas").GetAttribute("data-tone"));
    }

    /// <summary>The sentence is the host's, and it is announced politely rather
    /// than interrupting — a reader arrowing down a list is generating these
    /// faster than a screen reader can be interrupted usefully.</summary>
    [Fact]
    public void The_hosts_announcement_is_read_out_politely()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var atlas = context.Render<GraphAtlas>(parameters => parameters
            .Add(a => a.Data, Model())
            .Add(a => a.Nodes, Nodes())
            .Add(a => a.Announcement, "Alpha, adopted, Pantry. 1 of 2."));

        var live = atlas.Find("[aria-live=\"polite\"]");

        Assert.Equal("polite", live.GetAttribute("aria-live"));
        Assert.Equal("Alpha, adopted, Pantry. 1 of 2.", live.TextContent.Trim());
    }

    /// <summary>Rendered whether or not anything is selected: a sheet that only
    /// exists while it is open cannot animate out.</summary>
    [Fact]
    public void The_detail_fragment_is_rendered_with_nothing_selected()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var atlas = context.Render<GraphAtlas>(parameters => parameters
            .Add(a => a.Data, Model())
            .Add(a => a.Nodes, Nodes())
            .Add(a => a.Detail, (RenderFragment)(builder =>
            {
                builder.OpenElement(0, "p");
                builder.AddAttribute(1, "data-testid", "detail");
                builder.CloseElement();
            })));

        Assert.NotNull(atlas.Find("[data-testid=\"detail\"]"));
    }

    /// <summary>An empty title drops the header and moves the accessible name onto
    /// the section, for a host whose own surface already names the atlas.</summary>
    [Fact]
    public void An_empty_title_drops_the_header_and_names_the_section_instead()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var atlas = context.Render<GraphAtlas>(parameters => parameters
            .Add(a => a.Data, Model())
            .Add(a => a.Title, string.Empty)
            .Add(a => a.AriaLabel, "Kitchen atlas"));

        var section = atlas.Find("section.graph-atlas");

        Assert.Empty(atlas.FindAll(".graph-atlas__header"));
        Assert.Null(section.GetAttribute("aria-labelledby"));
        Assert.Equal("Kitchen atlas", section.GetAttribute("aria-label"));
    }
}
