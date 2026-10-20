namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The C4 zoom tab as a reader meets it: the view it opens on, the first-open hint, the
/// zoom bar, and every way of moving.
/// <para>
/// The gestures on the picture — a double-click, the Zoom in chip, Enter, Backspace —
/// arrive from the viewer in <c>components.js</c> as <c>ZoomIn</c>, <c>ZoomOut</c> and
/// <c>NodeSelected</c>. Those are called here directly, the way the viewer calls them,
/// because bUnit runs no script; that the viewer listens on the diagram's frame and
/// nowhere else is a property of the script, checked in the browser.
/// </para>
/// </summary>
public sealed class C4ZoomExplorerTests : IDisposable
{
    private const string Attach = "backlogC4Zoom.attach";
    private const string Select = "backlogC4Zoom.select";

    private readonly BunitContext _context = new();

    public C4ZoomExplorerTests()
    {
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.JSInterop.Setup<bool>(Attach, _ => true).SetResult(true);
    }

    public void Dispose() => _context.Dispose();

    private static C4Workspace Workspace => C4ZoomNavigationTests.Workspace;

    private static string Alias(string elementId) => C4MermaidWriter.AliasOf(elementId);

    private IRenderedComponent<C4ZoomExplorer> Render(bool hintDismissed = false, Action? dismissed = null) =>
        _context.Render<C4ZoomExplorer>(parameters =>
        {
            parameters
                .Add(c => c.Workspace, Workspace)
                .Add(c => c.HintDismissed, hintDismissed);

            if (dismissed is not null)
            {
                parameters.Add(c => c.OnHintDismissed, EventCallback.Factory.Create(this, dismissed));
            }
        });

    private static string Key(IRenderedComponent<C4ZoomExplorer> explorer) =>
        explorer.Find("[data-testid='c4-zoom-key']").TextContent;

    private static IReadOnlyList<AngleSharp.Dom.IElement> Steps(IRenderedComponent<C4ZoomExplorer> explorer) =>
        explorer.FindAll("[data-testid='c4-zoom-step']");

    private static AngleSharp.Dom.IElement Step(IRenderedComponent<C4ZoomExplorer> explorer, int level) =>
        Steps(explorer).Single(step => step.GetAttribute("data-level") == level.ToString(System.Globalization.CultureInfo.InvariantCulture));

    // ---- opening -----------------------------------------------------------------

    [Fact]
    public void It_opens_on_the_system_context_view_with_the_hint_naming_the_ways_to_move()
    {
        var explorer = Render();

        Assert.Equal("context-backlog", Key(explorer));

        var hint = explorer.Find("[data-testid='c4-zoom-hint']").TextContent;
        Assert.Contains("Zoom in:", hint, StringComparison.Ordinal);
        Assert.Contains("Zoom out:", hint, StringComparison.Ordinal);
        Assert.Contains("Jump:", hint, StringComparison.Ordinal);
        Assert.Contains("Sideways:", hint, StringComparison.Ordinal);
    }

    [Fact]
    public void Dismissing_the_hint_hides_it_and_tells_the_host()
    {
        var told = 0;
        var explorer = Render(dismissed: () => told++);

        explorer.Find("[data-testid='c4-zoom-hint-dismiss']").Click();

        Assert.Empty(explorer.FindAll("[data-testid='c4-zoom-hint']"));
        Assert.Equal(1, told);
    }

    [Fact]
    public void A_reader_who_dismissed_the_hint_sees_the_view_alone()
    {
        var explorer = Render(hintDismissed: true);

        Assert.Equal("context-backlog", Key(explorer));
        Assert.Empty(explorer.FindAll("[data-testid='c4-zoom-hint']"));
    }

    // ---- the zoom bar ------------------------------------------------------------

    [Fact]
    public void The_bar_rings_the_current_step_and_greys_the_steps_waiting_on_a_selection()
    {
        var explorer = Render();

        Assert.Equal(4, Steps(explorer).Count);

        var context = Step(explorer, 1);
        Assert.Contains("c4-zoom-bar__step--current", context.ClassList);
        Assert.Equal("step", context.GetAttribute("aria-current"));
        Assert.Contains("Prompt Backlog", context.TextContent, StringComparison.Ordinal);

        Assert.False(Step(explorer, 2).HasAttribute("disabled"));
        Assert.Contains("Prompt Backlog", Step(explorer, 2).TextContent, StringComparison.Ordinal);

        Assert.Contains("c4-zoom-bar__step--waiting", Step(explorer, 3).ClassList);
        Assert.True(Step(explorer, 3).HasAttribute("disabled"));
        Assert.Contains("pick a container", Step(explorer, 3).TextContent, StringComparison.Ordinal);

        Assert.True(Step(explorer, 4).HasAttribute("disabled"));
        Assert.Contains("pick a component", Step(explorer, 4).TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_also_links_open_the_landscape_dynamic_and_deployment_views()
    {
        var explorer = Render();

        var links = explorer.FindAll("[data-testid='c4-zoom-also-link']");
        Assert.Equal(["Landscape", "Dynamic", "Deployment"], links.Select(link => link.TextContent.Trim()));

        links[0].Click();
        Assert.Equal("landscape", Key(explorer));

        explorer.FindAll("[data-testid='c4-zoom-also-link']")[2].Click();
        Assert.Equal("deployment-local", Key(explorer));
    }

    // ---- zooming in --------------------------------------------------------------

    /// <summary>Only a card with a deeper view carries the chip. On the context view
    /// that is the system itself, never a person or an external system.</summary>
    [Fact]
    public void Only_cards_with_a_deeper_view_carry_a_zoom_in_chip()
    {
        var explorer = Render();
        var markup = explorer.Find("[data-testid='c4-zoom-diagram']").InnerHtml;

        Assert.Equal(1, markup.Split("data-c4-zoom-in", StringSplitOptions.None).Length - 1);
        Assert.Contains($"-{Alias("backlog")}\"", markup[..markup.IndexOf("data-c4-zoom-in", StringComparison.Ordinal)], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Zooming_into_the_system_opens_its_containers()
    {
        var explorer = Render();

        await explorer.InvokeAsync(() => explorer.Instance.ZoomIn(Alias("backlog"), "context-backlog"));

        Assert.Equal("containers-backlog", Key(explorer));
        Assert.Contains("c4-zoom-bar__step--current", Step(explorer, 2).ClassList);
    }

    [Fact]
    public async Task Selecting_a_container_fills_step_three_which_then_opens_its_components()
    {
        var explorer = Render();
        await explorer.InvokeAsync(() => explorer.Instance.ZoomIn(Alias("backlog"), "context-backlog"));

        await explorer.InvokeAsync(() => explorer.Instance.NodeSelected(Alias("backlog.desktop"), "containers-backlog"));

        var components = Step(explorer, 3);
        Assert.Contains("Desktop App", components.TextContent, StringComparison.Ordinal);
        Assert.False(components.HasAttribute("disabled"));
        Assert.Contains("pick a component", Step(explorer, 4).TextContent, StringComparison.Ordinal);

        await components.ClickAsync(new());

        Assert.Equal("components-desktop", Key(explorer));
    }

    [Fact]
    public async Task Zooming_into_an_element_with_nothing_deeper_selects_it_and_opens_nothing()
    {
        var explorer = Render();
        await explorer.InvokeAsync(() => explorer.Instance.ZoomIn(Alias("backlog"), "context-backlog"));

        await explorer.InvokeAsync(() => explorer.Instance.ZoomIn(Alias("backlog.cloud"), "containers-backlog"));

        Assert.Equal("containers-backlog", Key(explorer));
        Assert.Equal("backlog.cloud", explorer.Instance.SelectedElementId);
        Assert.Contains("Cloud Database", Step(explorer, 3).TextContent, StringComparison.Ordinal);
        Assert.True(Step(explorer, 3).HasAttribute("disabled"));
    }

    /// <summary>A press on the picture that was on screen before a move is about that
    /// picture, and is dropped rather than applied to the one showing now.</summary>
    [Fact]
    public async Task A_press_reported_for_a_view_no_longer_open_is_dropped()
    {
        var explorer = Render();
        await explorer.InvokeAsync(() => explorer.Instance.ZoomIn(Alias("backlog"), "context-backlog"));

        await explorer.InvokeAsync(() => explorer.Instance.ZoomIn(Alias("backlog.desktop"), "context-backlog"));
        await explorer.InvokeAsync(() => explorer.Instance.ZoomOut("context-backlog"));

        Assert.Equal("containers-backlog", Key(explorer));
    }

    // ---- zooming out -------------------------------------------------------------

    [Fact]
    public async Task Backspace_out_of_the_components_opens_the_containers_with_that_container_selected()
    {
        var explorer = Render();
        await explorer.InvokeAsync(() => explorer.Instance.ZoomIn(Alias("backlog"), "context-backlog"));
        await explorer.InvokeAsync(() => explorer.Instance.ZoomIn(Alias("backlog.desktop"), "containers-backlog"));
        Assert.Equal("components-desktop", Key(explorer));

        await explorer.InvokeAsync(() => explorer.Instance.ZoomOut("components-desktop"));

        Assert.Equal("containers-backlog", Key(explorer));
        Assert.Equal("backlog.desktop", explorer.Instance.SelectedElementId);
        Assert.Contains("Desktop App", Step(explorer, 3).TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_earlier_step_zooms_out_with_the_element_zoomed_out_of_selected()
    {
        var explorer = Render();
        await explorer.InvokeAsync(() => explorer.Instance.ZoomIn(Alias("backlog"), "context-backlog"));
        await explorer.InvokeAsync(() => explorer.Instance.ZoomIn(Alias("backlog.desktop"), "containers-backlog"));

        await Step(explorer, 1).ClickAsync(new());

        Assert.Equal("context-backlog", Key(explorer));
        Assert.Equal("backlog", explorer.Instance.SelectedElementId);
    }

    // ---- the viewer --------------------------------------------------------------

    [Fact]
    public async Task Each_new_view_is_handed_to_the_viewer_with_its_selection()
    {
        var explorer = Render();
        await explorer.InvokeAsync(() => explorer.Instance.ZoomIn(Alias("backlog"), "context-backlog"));
        await explorer.InvokeAsync(() => explorer.Instance.ZoomIn(Alias("backlog.desktop"), "containers-backlog"));
        await explorer.InvokeAsync(() => explorer.Instance.ZoomOut("components-desktop"));

        var attaches = _context.JSInterop.Invocations[Attach];
        Assert.Equal(["context-backlog", "containers-backlog", "components-desktop", "containers-backlog"], attaches.Select(call => call.Arguments[2] as string));
        Assert.Equal(Alias("backlog.desktop"), attaches[^1].Arguments[3]);

        // A move made on the picture hands the keyboard back to the picture; the
        // first opening does not take it.
        Assert.Equal([false, true, true, true], attaches.Select(call => (bool)call.Arguments[4]!));
    }

    /// <summary>A move made from the bar leaves the keyboard on the bar.</summary>
    [Fact]
    public async Task A_move_from_the_bar_does_not_pull_the_focus_into_the_picture()
    {
        var explorer = Render();

        await Step(explorer, 2).ClickAsync(new());

        var attaches = _context.JSInterop.Invocations[Attach];
        Assert.Equal("containers-backlog", attaches[^1].Arguments[2]);
        Assert.False((bool)attaches[^1].Arguments[4]!);
    }

    /// <summary>The viewer names each card by its full name, which the card itself
    /// may draw cut short.</summary>
    [Fact]
    public void The_viewer_is_handed_every_card_s_full_name()
    {
        Render();

        var names = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(_context.JSInterop.Invocations[Attach][0].Arguments[5]);
        Assert.Equal("Prompt Backlog", names[Alias("backlog")]);
    }

    /// <summary>A selection made from the bar on the same picture moves the ring
    /// without a new picture.</summary>
    [Fact]
    public async Task A_selection_on_the_same_picture_moves_the_ring_through_the_viewer()
    {
        var explorer = Render();
        await explorer.InvokeAsync(() => explorer.Instance.ZoomIn(Alias("backlog"), "context-backlog"));
        await explorer.InvokeAsync(() => explorer.Instance.ZoomIn(Alias("backlog.cloud"), "containers-backlog"));

        var selects = _context.JSInterop.Invocations[Select];
        Assert.Equal(Alias("backlog.cloud"), selects[^1].Arguments[1]);
    }
}
