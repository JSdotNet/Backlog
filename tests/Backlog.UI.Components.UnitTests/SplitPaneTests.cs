namespace Backlog.UI.Components.UnitTests;

public sealed class SplitPaneTests
{
    [Fact]
    public void The_separator_is_a_focusable_separator_that_reports_its_range()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var pane = context.Render<SplitPane>(parameters => parameters
            .Add(p => p.FixedWidthRem, 36)
            .Add(p => p.MinRem, 24)
            .Add(p => p.MaxRem, 80));

        var separator = pane.Find("[role='separator']");

        Assert.Equal("0", separator.GetAttribute("tabindex"));
        Assert.Equal("vertical", separator.GetAttribute("aria-orientation"));
        Assert.Equal("24", separator.GetAttribute("aria-valuemin"));
        Assert.Equal("80", separator.GetAttribute("aria-valuemax"));
        Assert.Equal("36", separator.GetAttribute("aria-valuenow"));
    }

    [Fact]
    public void Left_and_right_arrows_resize_the_fixed_pane()
    {
        // Every drag in this product owes the keyboard an equivalent, and this is
        // the only route to a resize for anyone who cannot hold a pointer down.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var widths = new List<double>();

        var pane = context.Render<SplitPane>(parameters => parameters
            .Add(p => p.FixedWidthRem, 36)
            .Add(p => p.FixedWidthRemChanged, (double width) => widths.Add(width)));

        pane.Find("[role='separator']").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        pane.Find("[role='separator']").KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });

        Assert.Equal([38d, 36d], widths);
        Assert.Equal("36", pane.Find("[role='separator']").GetAttribute("aria-valuenow"));
    }

    [Fact]
    public void Home_and_End_go_to_the_ends_and_the_range_is_never_left()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var widths = new List<double>();

        var pane = context.Render<SplitPane>(parameters => parameters
            .Add(p => p.FixedWidthRem, 30)
            .Add(p => p.MinRem, 24)
            .Add(p => p.MaxRem, 32)
            .Add(p => p.FixedWidthRemChanged, (double width) => widths.Add(width)));

        pane.Find("[role='separator']").KeyDown(new KeyboardEventArgs { Key = "Home" });
        pane.Find("[role='separator']").KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });
        pane.Find("[role='separator']").KeyDown(new KeyboardEventArgs { Key = "End" });

        Assert.Equal([24d, 32d], widths);
    }

    [Fact]
    public void The_pointer_drag_is_handed_to_the_js_resizer_unless_the_host_owns_it()
    {
        // A per-frame interop call would be a per-frame render, so the drag lives
        // in JS and only the settled width comes back.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        context.Render<SplitPane>();
        Assert.Single(context.JSInterop.Invocations["backlogPaneResizer.initialize"]);

        context.Render<SplitPane>(parameters => parameters.Add(p => p.UseJsResizer, false));
        Assert.Single(context.JSInterop.Invocations["backlogPaneResizer.initialize"]);
    }

    /// <summary>
    /// The anchor flips the grid and the resizer's attribute together.
    /// <para>
    /// One decision with two readings, and they have to move as one: a grid whose
    /// fixed column is last while <c>data-pane-anchor</c> still says "start" is a drag
    /// that runs backwards, which is the exact bug the attribute was introduced to
    /// fix. The grid template itself lives in <c>components.css</c>, so the class is
    /// what is asserted here — the class is the only part of it a stylesheet can key
    /// off.
    /// </para>
    /// </summary>
    [Fact]
    public void The_anchor_moves_the_grid_and_the_resizer_hint_together()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var start = context.Render<SplitPane>(parameters => parameters
            .Add(p => p.TestId, "split"));

        var startRoot = start.Find("[data-testid='split']");
        Assert.Equal("start", startRoot.GetAttribute("data-pane-anchor"));
        Assert.DoesNotContain("split-pane--end", startRoot.GetAttribute("class") ?? string.Empty, StringComparison.Ordinal);

        var end = context.Render<SplitPane>(parameters => parameters
            .Add(p => p.Anchor, SplitPaneAnchor.End)
            .Add(p => p.TestId, "split"));

        var endRoot = end.Find("[data-testid='split']");
        Assert.Equal("end", endRoot.GetAttribute("data-pane-anchor"));
        Assert.Contains("split-pane--end", endRoot.GetAttribute("class") ?? string.Empty, StringComparison.Ordinal);
    }

    /// <summary>
    /// The keyboard resize works in both anchors, and the arrows keep describing the
    /// separator rather than the number.
    /// <para>
    /// Left drags the separator left. That narrows a pane fixed to the left edge and
    /// widens one fixed to the right, so the same key means "more" in one anchor and
    /// "less" in the other — because what the reader is doing is the same either way.
    /// </para>
    /// </summary>
    [Fact]
    public void Arrows_follow_the_separator_rather_than_the_number()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var widths = new List<double>();

        var pane = context.Render<SplitPane>(parameters => parameters
            .Add(p => p.Anchor, SplitPaneAnchor.End)
            .Add(p => p.FixedWidthRem, 24)
            .Add(p => p.FixedWidthRemChanged, (double width) => widths.Add(width)));

        pane.Find("[role='separator']").KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });
        pane.Find("[role='separator']").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });

        Assert.Equal([26d, 24d], widths);
    }

    /// <summary>Home and End are about the value's ends rather than about the
    /// screen's, so they mean the same thing whichever edge the pane is fixed
    /// to.</summary>
    [Fact]
    public void Home_and_End_mean_the_same_thing_in_both_anchors()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var widths = new List<double>();

        var pane = context.Render<SplitPane>(parameters => parameters
            .Add(p => p.Anchor, SplitPaneAnchor.End)
            .Add(p => p.FixedWidthRem, 30)
            .Add(p => p.MinRem, 24)
            .Add(p => p.MaxRem, 40)
            .Add(p => p.FixedWidthRemChanged, (double width) => widths.Add(width)));

        pane.Find("[role='separator']").KeyDown(new KeyboardEventArgs { Key = "Home" });
        pane.Find("[role='separator']").KeyDown(new KeyboardEventArgs { Key = "End" });

        Assert.Equal([24d, 40d], widths);
    }

    /// <summary>
    /// A stacked split has a horizontal separator, and says so.
    /// <para>
    /// <c>aria-orientation</c> describes the separator, not the layout: panes side by
    /// side are divided by a vertical line, panes one above the other by a horizontal
    /// one. A screen reader announces the orientation so the reader knows which arrows
    /// move it; announcing "vertical" over a stacked split would send them to Left and
    /// Right, which do nothing there.
    /// </para>
    /// </summary>
    [Fact]
    public void A_stacked_split_announces_a_horizontal_separator_and_names_the_height()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var pane = context.Render<SplitPane>(parameters => parameters
            .Add(p => p.Orientation, SplitPaneOrientation.Vertical)
            .Add(p => p.FixedWidthRem, 14)
            .Add(p => p.MinRem, 8)
            .Add(p => p.MaxRem, 30));

        var separator = pane.Find("[role='separator']");

        Assert.Equal("horizontal", separator.GetAttribute("aria-orientation"));
        Assert.Equal("Pane height", separator.GetAttribute("aria-label"));
        Assert.Equal("0", separator.GetAttribute("tabindex"));
        Assert.Equal("8", separator.GetAttribute("aria-valuemin"));
        Assert.Equal("30", separator.GetAttribute("aria-valuemax"));
        Assert.Equal("14", separator.GetAttribute("aria-valuenow"));
    }

    /// <summary>The side-by-side split keeps the name it always had when the host
    /// gives none, and a name the host gives wins in either orientation.</summary>
    [Fact]
    public void The_default_name_follows_the_axis_and_a_host_name_wins()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var beside = context.Render<SplitPane>();
        var named = context.Render<SplitPane>(parameters => parameters
            .Add(p => p.Orientation, SplitPaneOrientation.Vertical)
            .Add(p => p.AriaLabel, "Map height"));

        Assert.Equal("Pane width", beside.Find("[role='separator']").GetAttribute("aria-label"));
        Assert.Equal("Map height", named.Find("[role='separator']").GetAttribute("aria-label"));
    }

    /// <summary>
    /// The orientation reaches the stylesheet and the resizer as one decision.
    /// <para>
    /// The class turns the grid's tracks into rows; the attribute tells the JS drag to
    /// read the pointer's Y and measure the layout's height. A split whose grid stacked
    /// while the drag still read X would resize the map by moving sideways. The floor
    /// goes out on the stacked split only: the side-by-side drag keeps its own.
    /// </para>
    /// </summary>
    [Fact]
    public void The_orientation_moves_the_grid_and_the_resizer_axis_together()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var beside = context.Render<SplitPane>(parameters => parameters
            .Add(p => p.TestId, "split"));

        var besideRoot = beside.Find("[data-testid='split']");
        Assert.Equal("horizontal", besideRoot.GetAttribute("data-pane-orientation"));
        Assert.Null(besideRoot.GetAttribute("data-pane-min"));
        Assert.DoesNotContain("split-pane--vertical", besideRoot.GetAttribute("class") ?? string.Empty, StringComparison.Ordinal);

        var stacked = context.Render<SplitPane>(parameters => parameters
            .Add(p => p.Orientation, SplitPaneOrientation.Vertical)
            .Add(p => p.MinRem, 8.5)
            .Add(p => p.TestId, "split"));

        var stackedRoot = stacked.Find("[data-testid='split']");
        Assert.Equal("vertical", stackedRoot.GetAttribute("data-pane-orientation"));
        Assert.Equal("8.5", stackedRoot.GetAttribute("data-pane-min"));
        Assert.Contains("split-pane--vertical", stackedRoot.GetAttribute("class") ?? string.Empty, StringComparison.Ordinal);
    }

    /// <summary>
    /// Up and Down resize a stacked split, and Left and Right leave it alone.
    /// <para>
    /// Up lifts the separator, so it shortens a pane fixed to the top; the arrows
    /// describe the separator's travel here exactly as they do beside.
    /// </para>
    /// </summary>
    [Fact]
    public void Up_and_down_arrows_resize_a_stacked_split_and_left_and_right_do_not()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var heights = new List<double>();

        var pane = context.Render<SplitPane>(parameters => parameters
            .Add(p => p.Orientation, SplitPaneOrientation.Vertical)
            .Add(p => p.FixedWidthRem, 14)
            .Add(p => p.MinRem, 8)
            .Add(p => p.FixedWidthRemChanged, (double height) => heights.Add(height)));

        pane.Find("[role='separator']").KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });
        pane.Find("[role='separator']").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        pane.Find("[role='separator']").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        pane.Find("[role='separator']").KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });
        pane.Find("[role='separator']").KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });

        Assert.Equal([16d, 14d, 12d], heights);
        Assert.Equal("12", pane.Find("[role='separator']").GetAttribute("aria-valuenow"));
    }

    /// <summary>The side-by-side split is untouched by the new pair: Up and Down do
    /// nothing there, as they never did.</summary>
    [Fact]
    public void Up_and_down_do_nothing_beside()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var widths = new List<double>();

        var pane = context.Render<SplitPane>(parameters => parameters
            .Add(p => p.FixedWidthRem, 36)
            .Add(p => p.FixedWidthRemChanged, (double width) => widths.Add(width)));

        pane.Find("[role='separator']").KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });
        pane.Find("[role='separator']").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        Assert.Empty(widths);
    }

    /// <summary>
    /// A pane fixed to the bottom grows when the separator goes up.
    /// <para>
    /// The stacked reading of <see cref="Arrows_follow_the_separator_rather_than_the_number"/>:
    /// Up means "more" for a bottom-anchored pane and "less" for a top-anchored one.
    /// Home and End still mean the value's ends.
    /// </para>
    /// </summary>
    [Fact]
    public void A_bottom_anchored_stack_grows_when_the_separator_goes_up_and_home_and_end_keep_their_meaning()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var heights = new List<double>();

        var pane = context.Render<SplitPane>(parameters => parameters
            .Add(p => p.Orientation, SplitPaneOrientation.Vertical)
            .Add(p => p.Anchor, SplitPaneAnchor.End)
            .Add(p => p.FixedWidthRem, 14)
            .Add(p => p.MinRem, 8)
            .Add(p => p.MaxRem, 20)
            .Add(p => p.FixedWidthRemChanged, (double height) => heights.Add(height)));

        pane.Find("[role='separator']").KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });
        pane.Find("[role='separator']").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        pane.Find("[role='separator']").KeyDown(new KeyboardEventArgs { Key = "Home" });
        pane.Find("[role='separator']").KeyDown(new KeyboardEventArgs { Key = "End" });

        Assert.Equal([16d, 14d, 8d, 20d], heights);
    }

    /// <summary>
    /// The separator never refuses every key's default.
    /// <para>
    /// An unconditional <c>@onkeydown:preventDefault</c> cancelled Tab and Shift+Tab
    /// too, so a keyboard user who tabbed onto the separator could not tab off it — a
    /// keyboard trap. The keys it handles have their default refused in
    /// <c>components.js</c> instead (<c>SplitPaneKeyScriptTests</c>), and the rest pass.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(SplitPaneOrientation.Horizontal)]
    [InlineData(SplitPaneOrientation.Vertical)]
    public void The_separator_does_not_refuse_every_keys_default(SplitPaneOrientation orientation)
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var pane = context.Render<SplitPane>(parameters => parameters
            .Add(p => p.Orientation, orientation));

        Assert.Null(pane.Find("[role='separator']").GetAttribute("blazor:onkeydown:preventdefault"));
    }

    [Fact]
    public async Task The_js_side_can_push_a_settled_width_back_into_the_component()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        double? width = null;

        var pane = context.Render<SplitPane>(parameters => parameters
            .Add(p => p.MaxRem, 60)
            .Add(p => p.FixedWidthRemChanged, (double value) => width = value));

        await pane.InvokeAsync(() => pane.Instance.SetSidePaneWidthAsync(48));

        Assert.Equal(48d, width);
    }

    /// <summary>
    /// The pane draws the width it says it has.
    /// <para>
    /// The room a pane may take is measured in the browser and pushed back through
    /// <c>SetSidePaneMaxWidthAsync</c>, and a drag has always been clamped to it. The
    /// render was not: the host's stored width went out to the grid and to
    /// <c>aria-valuenow</c> whatever the measurement said, so a pane that had been
    /// dragged wide on a large window came back on a small one asking for a width
    /// that was not there — and the grid gave it what it could while the separator
    /// went on announcing the number nobody could see.
    /// </para>
    /// <para>
    /// Clamped for the render only. The parameter keeps the reader's number, so a
    /// window that grows gives the width back rather than having quietly lost it.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_measured_room_clamps_the_width_the_pane_draws()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var pane = context.Render<SplitPane>(parameters => parameters
            .Add(p => p.FixedWidthRem, 40)
            .Add(p => p.MinRem, 24));

        await pane.InvokeAsync(() => pane.Instance.SetSidePaneMaxWidthAsync(30));

        Assert.Contains(
            "--split-pane-fixed: 30rem",
            pane.Find("[data-pane-split]").GetAttribute("style") ?? string.Empty,
            StringComparison.Ordinal);

        var separator = pane.Find("[role='separator']");

        Assert.Equal("30", separator.GetAttribute("aria-valuenow"));
        Assert.Equal("30", separator.GetAttribute("aria-valuemax"));
    }
}
