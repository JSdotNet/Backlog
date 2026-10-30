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

    // ---- the Code level ------------------------------------------------------------

    private const string Devbook = "backlog.desktop.devbook";

    /// <summary>The explorer with a code source in which only the Devbook component has
    /// code folders, as a building block naming them would make it. Every opening is
    /// counted, so a re-read is visible.</summary>
    private IRenderedComponent<C4ZoomExplorer> RenderWithCode(List<string>? reads = null) =>
        _context.Render<C4ZoomExplorer>(parameters => parameters
            .Add(c => c.Workspace, Workspace)
            .Add(c => c.HintDismissed, true)
            .Add(c => c.Code, new C4ZoomCode(
                elementId => string.Equals(elementId, Devbook, StringComparison.OrdinalIgnoreCase),
                (elementId, _) =>
                {
                    reads?.Add(elementId);
                    return Task.FromResult(C4CodeViewTests.Level);
                })));

    private static async Task<IRenderedComponent<C4ZoomExplorer>> OnComponentsAsync(IRenderedComponent<C4ZoomExplorer> explorer)
    {
        await explorer.InvokeAsync(() => explorer.Instance.ZoomIn(Alias("backlog"), "context-backlog"));
        await explorer.InvokeAsync(() => explorer.Instance.ZoomIn(Alias("backlog.desktop"), "containers-backlog"));
        Assert.Equal("components-desktop", Key(explorer));
        return explorer;
    }

    /// <summary>Requirement "Code is drawn from the source on demand", scenario "A
    /// component with a code link": zooming into Devbook opens its Code level.</summary>
    [Fact]
    public async Task Zooming_into_a_component_with_code_opens_its_code_level()
    {
        var reads = new List<string>();
        var explorer = await OnComponentsAsync(RenderWithCode(reads));

        await explorer.InvokeAsync(() => explorer.Instance.ZoomIn(Alias(Devbook), "components-desktop"));

        Assert.Equal(Devbook, explorer.Instance.CodeComponentId);
        Assert.Equal([Devbook], reads);
        Assert.Contains("Code — Devbook component", explorer.Find("[data-testid='c4-code-title']").TextContent, StringComparison.Ordinal);
        Assert.Equal(3, explorer.FindAll("[data-testid='c4-code-type']").Count);
        Assert.Empty(explorer.FindAll("[data-testid='c4-zoom-diagram']"));

        var code = Step(explorer, 4);
        Assert.Contains("c4-zoom-bar__step--current", code.ClassList);
        Assert.Contains("Devbook", code.TextContent, StringComparison.Ordinal);
    }

    /// <summary>Only Devbook carries a chip on the components view: Shell has no code
    /// folder and nothing deeper.</summary>
    [Fact]
    public async Task Only_a_component_with_code_carries_a_zoom_in_chip()
    {
        var explorer = await OnComponentsAsync(RenderWithCode());
        var markup = explorer.Find("[data-testid='c4-zoom-diagram']").InnerHtml;

        Assert.Equal(1, markup.Split("data-c4-zoom-in", StringSplitOptions.None).Length - 1);
        Assert.Contains($"-{Alias(Devbook)}\"", markup[..markup.IndexOf("data-c4-zoom-in", StringComparison.Ordinal)], StringComparison.Ordinal);
    }

    /// <summary>Scenario "A component with no code link": step 4 names it and stays grey,
    /// and zooming into it only selects it.</summary>
    [Fact]
    public async Task A_component_without_code_keeps_step_four_grey()
    {
        var explorer = await OnComponentsAsync(RenderWithCode());

        await explorer.InvokeAsync(() => explorer.Instance.ZoomIn(Alias("backlog.desktop.shell"), "components-desktop"));

        Assert.Null(explorer.Instance.CodeComponentId);
        Assert.Equal("backlog.desktop.shell", explorer.Instance.SelectedElementId);
        Assert.True(Step(explorer, 4).HasAttribute("disabled"));
        Assert.Contains("Shell", Step(explorer, 4).TextContent, StringComparison.Ordinal);
        Assert.Equal("Shell has no code folder: no building block names one in code", Step(explorer, 4).GetAttribute("title"));
    }

    [Fact]
    public async Task Step_four_opens_the_selected_components_code()
    {
        var explorer = await OnComponentsAsync(RenderWithCode());
        await explorer.InvokeAsync(() => explorer.Instance.NodeSelected(Alias(Devbook), "components-desktop"));

        Assert.False(Step(explorer, 4).HasAttribute("disabled"));
        await Step(explorer, 4).ClickAsync(new());

        Assert.Equal(Devbook, explorer.Instance.CodeComponentId);
        Assert.Single(explorer.FindAll("[data-testid='c4-zoom-code']"));
    }

    /// <summary>Backspace on the Code level is one level up: the components view, with
    /// the component still selected and the picture handed back to the viewer.</summary>
    [Fact]
    public async Task Backspace_on_the_code_level_goes_back_to_the_components_with_it_selected()
    {
        var explorer = await OnComponentsAsync(RenderWithCode());
        await explorer.InvokeAsync(() => explorer.Instance.ZoomIn(Alias(Devbook), "components-desktop"));
        var attaches = _context.JSInterop.Invocations[Attach].Count;

        explorer.Find("[data-testid='c4-zoom-code']").KeyDown(new KeyboardEventArgs { Key = "Backspace" });

        Assert.Null(explorer.Instance.CodeComponentId);
        Assert.Equal("components-desktop", Key(explorer));
        Assert.Equal(Devbook, explorer.Instance.SelectedElementId);
        Assert.Contains("c4-zoom-bar__step--current", Step(explorer, 3).ClassList);
        Assert.True(_context.JSInterop.Invocations[Attach].Count > attaches);
    }

    /// <summary>Scenario "Zoom out from the bar": from the Code view of Devbook, step 2
    /// opens the containers with Desktop App selected.</summary>
    [Fact]
    public async Task Step_two_from_the_code_level_opens_the_containers_with_the_container_selected()
    {
        var explorer = await OnComponentsAsync(RenderWithCode());
        await explorer.InvokeAsync(() => explorer.Instance.ZoomIn(Alias(Devbook), "components-desktop"));

        await Step(explorer, 2).ClickAsync(new());

        Assert.Null(explorer.Instance.CodeComponentId);
        Assert.Equal("containers-backlog", Key(explorer));
        Assert.Equal("backlog.desktop", explorer.Instance.SelectedElementId);
    }

    /// <summary>Scenario "The source changed": each opening reads the level again.</summary>
    [Fact]
    public async Task Opening_the_code_level_again_reads_it_again()
    {
        var reads = new List<string>();
        var explorer = await OnComponentsAsync(RenderWithCode(reads));

        await explorer.InvokeAsync(() => explorer.Instance.ZoomIn(Alias(Devbook), "components-desktop"));
        explorer.Find("[data-testid='c4-zoom-code']").KeyDown(new KeyboardEventArgs { Key = "Backspace" });
        await explorer.InvokeAsync(() => explorer.Instance.ZoomIn(Alias(Devbook), "components-desktop"));

        Assert.Equal([Devbook, Devbook], reads);
    }

    /// <summary>A read that fails is shown as the problem it was.</summary>
    [Fact]
    public async Task A_code_level_that_cannot_be_read_says_why()
    {
        var explorer = _context.Render<C4ZoomExplorer>(parameters => parameters
            .Add(c => c.Workspace, Workspace)
            .Add(c => c.HintDismissed, true)
            .Add(c => c.Code, new C4ZoomCode(_ => true, (_, _) => throw new IOException("The disk went away."))));
        await OnComponentsAsync(explorer);

        await explorer.InvokeAsync(() => explorer.Instance.ZoomIn(Alias(Devbook), "components-desktop"));

        Assert.Contains("The disk went away.", explorer.Find("[data-testid='c4-code-problem']").TextContent, StringComparison.Ordinal);
    }

    /// <summary>A read that lands after the reader left the level is dropped: the
    /// level shown is the one opened last, and a closed level stays closed.</summary>
    [Fact]
    public async Task A_read_that_lands_after_the_reader_moved_on_is_dropped()
    {
        var pending = new Queue<TaskCompletionSource<C4CodeLevel>>();
        var explorer = _context.Render<C4ZoomExplorer>(parameters => parameters
            .Add(c => c.Workspace, Workspace)
            .Add(c => c.HintDismissed, true)
            .Add(c => c.Code, new C4ZoomCode(_ => true, (_, _) =>
            {
                var read = new TaskCompletionSource<C4CodeLevel>(TaskCreationOptions.RunContinuationsAsynchronously);
                pending.Enqueue(read);
                return read.Task;
            })));
        await OnComponentsAsync(explorer);

        await explorer.InvokeAsync(() => explorer.Instance.ZoomIn(Alias(Devbook), "components-desktop"));
        var first = pending.Dequeue();
        explorer.Find("[data-testid='c4-zoom-code']").KeyDown(new KeyboardEventArgs { Key = "Backspace" });
        await explorer.InvokeAsync(() => explorer.Instance.ZoomIn(Alias(Devbook), "components-desktop"));
        var second = pending.Dequeue();

        first.SetResult(C4CodeViewTests.Level with { Component = "Stale" });
        explorer.WaitForAssertion(() => Assert.Single(explorer.FindAll("[data-testid='c4-code-loading']")));

        second.SetResult(C4CodeViewTests.Level);
        explorer.WaitForAssertion(() => Assert.Equal(3, explorer.FindAll("[data-testid='c4-code-type']").Count));
        Assert.DoesNotContain("Stale", explorer.Find("[data-testid='c4-code-title']").TextContent, StringComparison.Ordinal);

        // Closed, a read still out comes to nothing.
        explorer.Find("[data-testid='c4-zoom-code']").KeyDown(new KeyboardEventArgs { Key = "Backspace" });
        Assert.Null(explorer.Instance.CodeComponentId);
        Assert.Empty(explorer.FindAll("[data-testid='c4-zoom-code']"));
    }

    /// <summary>Opened from the picture, the level takes the keyboard the card had, so
    /// Backspace works at once; opened from the bar, the focus stays on the bar.</summary>
    [Fact]
    public async Task The_code_level_takes_the_focus_when_opened_from_the_picture_and_not_from_the_bar()
    {
        const string Focus = "Blazor._internal.domWrapper.focus";
        var fromBar = await OnComponentsAsync(RenderWithCode());
        await fromBar.InvokeAsync(() => fromBar.Instance.NodeSelected(Alias(Devbook), "components-desktop"));
        await Step(fromBar, 4).ClickAsync(new());

        Assert.Single(fromBar.FindAll("[data-testid='c4-zoom-code']"));
        Assert.Empty(_context.JSInterop.Invocations[Focus]);

        fromBar.Find("[data-testid='c4-zoom-code']").KeyDown(new KeyboardEventArgs { Key = "Backspace" });
        await fromBar.InvokeAsync(() => fromBar.Instance.ZoomIn(Alias(Devbook), "components-desktop"));

        Assert.Single(_context.JSInterop.Invocations[Focus]);
    }

    /// <summary>With no code source every component stops at level 3.</summary>
    [Fact]
    public async Task Without_a_code_source_nothing_opens_a_code_level()
    {
        var explorer = await OnComponentsAsync(Render(hintDismissed: true));

        await explorer.InvokeAsync(() => explorer.Instance.ZoomIn(Alias(Devbook), "components-desktop"));

        Assert.Null(explorer.Instance.CodeComponentId);
        Assert.True(Step(explorer, 4).HasAttribute("disabled"));
    }
}
