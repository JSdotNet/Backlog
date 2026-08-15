namespace Backlog.UI.Components.UnitTests;

public sealed class SplitPaneTests
{
    [Fact]
    public void The_separator_is_a_focusable_separator_that_reports_its_range()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var pane = context.Render<SplitPane>(parameters => parameters
            .Add(p => p.StartWidthRem, 36)
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
    public void Left_and_right_arrows_resize_the_start_pane()
    {
        // Every drag in this product owes the keyboard an equivalent, and this is
        // the only route to a resize for anyone who cannot hold a pointer down.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var widths = new List<double>();

        var pane = context.Render<SplitPane>(parameters => parameters
            .Add(p => p.StartWidthRem, 36)
            .Add(p => p.StartWidthRemChanged, (double width) => widths.Add(width)));

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
            .Add(p => p.StartWidthRem, 30)
            .Add(p => p.MinRem, 24)
            .Add(p => p.MaxRem, 32)
            .Add(p => p.StartWidthRemChanged, (double width) => widths.Add(width)));

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

    [Fact]
    public async Task The_js_side_can_push_a_settled_width_back_into_the_component()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        double? width = null;

        var pane = context.Render<SplitPane>(parameters => parameters
            .Add(p => p.MaxRem, 60)
            .Add(p => p.StartWidthRemChanged, (double value) => width = value));

        await pane.InvokeAsync(() => pane.Instance.SetSidePaneWidthAsync(48));

        Assert.Equal(48d, width);
    }
}
