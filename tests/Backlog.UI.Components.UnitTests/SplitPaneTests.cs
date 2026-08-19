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
}
