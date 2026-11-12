using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Backlog.UI.Components.UnitTests;

public sealed class PopoverButtonTests
{
    private static IRenderedComponent<PopoverButton> Render(BunitContext context, List<bool>? changes = null) =>
        context.Render<PopoverButton>(parameters => parameters
            .Add(p => p.AriaLabel, "Display options")
            .Add(p => p.TestId, "options")
            .Add(p => p.TriggerOpenCssClass, "is-open")
            .Add(p => p.OpenChanged, EventCallback.Factory.Create<bool>(new object(), open => changes?.Add(open)))
            .Add(p => p.Trigger, (RenderFragment)(builder => builder.AddContent(0, "⋯")))
            .Add(p => p.ChildContent, (RenderFragment)(builder =>
            {
                builder.OpenElement(0, "button");
                builder.AddAttribute(1, "data-testid", "inside");
                builder.AddAttribute(2, "onclick", EventCallback.Factory.Create<MouseEventArgs>(new object(), () => { }));
                builder.CloseElement();
            })));

    [Fact]
    public void The_trigger_is_a_named_disclosure_and_the_panel_is_closed_until_pressed()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var popover = Render(context);
        var trigger = popover.Find("[data-testid='options-button']");

        Assert.Equal("BUTTON", trigger.TagName);
        Assert.Equal("Display options", trigger.GetAttribute("aria-label"));
        Assert.Equal("Display options", trigger.GetAttribute("title"));
        Assert.Equal("false", trigger.GetAttribute("aria-expanded"));
        Assert.Null(trigger.GetAttribute("aria-haspopup"));
        Assert.NotNull(trigger.QuerySelector("[aria-hidden='true']"));
        Assert.Empty(popover.FindAll("[data-testid='options-panel']"));
    }

    [Fact]
    public void Pressing_the_trigger_opens_a_group_panel_it_controls()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var changes = new List<bool>();

        var popover = Render(context, changes);
        popover.Find("[data-testid='options-button']").Click();

        var trigger = popover.Find("[data-testid='options-button']");
        var panel = popover.Find("[data-testid='options-panel']");
        Assert.Equal("true", trigger.GetAttribute("aria-expanded"));
        Assert.Contains("is-open", trigger.ClassList);
        Assert.Equal(panel.Id, trigger.GetAttribute("aria-controls"));
        Assert.Equal("group", panel.GetAttribute("role"));
        Assert.Equal("Display options", panel.GetAttribute("aria-label"));
        Assert.NotNull(panel.QuerySelector("[data-testid='inside']"));
        Assert.Equal([true], changes);
    }

    [Fact]
    public void Escape_inside_the_panel_closes_it_and_returns_focus_to_the_trigger()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var popover = Render(context);
        popover.Find("[data-testid='options-button']").Click();
        popover.Find("[data-testid='inside']").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(popover.FindAll("[data-testid='options-panel']"));
        Assert.Equal("false", popover.Find("[data-testid='options-button']").GetAttribute("aria-expanded"));
        context.JSInterop.VerifyFocusAsyncInvoke();
    }

    [Fact]
    public void A_click_outside_closes_the_panel()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var changes = new List<bool>();

        var popover = Render(context, changes);
        popover.Find("[data-testid='options-button']").Click();
        popover.Find("[data-testid='options-backdrop']").Click();

        Assert.Empty(popover.FindAll("[data-testid='options-panel']"));
        Assert.Empty(popover.FindAll("[data-testid='options-backdrop']"));
        Assert.Equal([true, false], changes);
    }

    [Fact]
    public void The_panel_lines_up_with_the_trigger_start_unless_asked_for_the_end()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var popover = Render(context);
        popover.Find("[data-testid='options-button']").Click();
        Assert.DoesNotContain("popover-button__panel--end", popover.Find("[data-testid='options-panel']").ClassList);

        popover.Render(parameters => parameters.Add(p => p.AlignEnd, true));
        Assert.Contains("popover-button__panel--end", popover.Find("[data-testid='options-panel']").ClassList);
    }

    /// <summary>The backdrop is only window-sized while no ancestor is a containing
    /// block for it, so while the panel is open the window is watched as well, and
    /// the script's word that a click landed outside closes the panel. The watch goes
    /// with the panel.</summary>
    [Fact]
    public async Task While_open_the_window_is_watched_and_an_outside_click_closes_the_panel()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var changes = new List<bool>();

        var popover = Render(context, changes);
        Assert.Empty(context.JSInterop.Invocations["backlogPopoverOutside.watch"]);

        popover.Find("[data-testid='options-button']").Click();

        var watch = Assert.Single(context.JSInterop.Invocations["backlogPopoverOutside.watch"]);
        Assert.Equal(nameof(PopoverButton.CloseFromOutsideAsync), watch.Arguments[3]);

        await popover.InvokeAsync(popover.Instance.CloseFromOutsideAsync);

        Assert.Empty(popover.FindAll("[data-testid='options-panel']"));
        Assert.Equal("false", popover.Find("[data-testid='options-button']").GetAttribute("aria-expanded"));
        Assert.Equal([true, false], changes);
        var unwatch = Assert.Single(context.JSInterop.Invocations["backlogPopoverOutside.unwatch"]);
        Assert.Equal(watch.Arguments[0], unwatch.Arguments[0]);
    }

    /// <summary>A close that lands while the watch call is still on its way must take
    /// the watch down anyway — left up, it would swallow every outside click in the
    /// window. The host hears the opening before the close, never after it.</summary>
    [Fact]
    public async Task A_close_while_the_watch_call_is_in_flight_still_takes_the_watch_down()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var watch = context.JSInterop.SetupVoid("backlogPopoverOutside.watch", _ => true);
        var changes = new List<bool>();

        var popover = Render(context, changes);
        popover.Find("[data-testid='options-button']").Click();
        popover.Find("[data-testid='options-backdrop']").Click();

        await popover.InvokeAsync(watch.SetVoidResult);

        var unwatch = Assert.Single(context.JSInterop.Invocations["backlogPopoverOutside.unwatch"]);
        Assert.Equal(Assert.Single(context.JSInterop.Invocations["backlogPopoverOutside.watch"]).Arguments[0], unwatch.Arguments[0]);
        Assert.Empty(popover.FindAll("[data-testid='options-panel']"));
        Assert.Equal([true, false], changes);
    }

    /// <summary>A word from the script for a panel already shut means a watch is still
    /// up that should not be, and it is taken down.</summary>
    [Fact]
    public async Task An_outside_click_for_a_shut_panel_takes_the_stray_watch_down()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var changes = new List<bool>();

        var popover = Render(context, changes);
        await popover.InvokeAsync(popover.Instance.CloseFromOutsideAsync);

        Assert.Single(context.JSInterop.Invocations["backlogPopoverOutside.unwatch"]);
        Assert.Empty(changes);
    }

    /// <summary>
    /// Escape gives the focus back to the trigger before the host hears the panel
    /// closed: a host may take the whole component out of the page on that word —
    /// the Tasks bar's Filter menu does, redrawing the bar wide — and a focus sent
    /// after it would go to a trigger that is no longer there.
    /// </summary>
    [Fact]
    public void Escape_hands_the_focus_back_before_the_host_hears_the_panel_closed()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var order = new List<string>();
        context.JSInterop.SetupVoid("Blazor._internal.domWrapper.focus", _ =>
        {
            order.Add("focus");
            return true;
        }).SetVoidResult();

        var popover = context.Render<PopoverButton>(parameters => parameters
            .Add(p => p.AriaLabel, "Display options")
            .Add(p => p.TestId, "options")
            .Add(p => p.OpenChanged, EventCallback.Factory.Create<bool>(new object(), open => order.Add(open ? "opened" : "closed")))
            .Add(p => p.ChildContent, (RenderFragment)(builder =>
            {
                builder.OpenElement(0, "button");
                builder.AddAttribute(1, "data-testid", "inside");
                builder.CloseElement();
            })));

        popover.Find("[data-testid='options-button']").Click();
        popover.Find("[data-testid='inside']").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        popover.WaitForAssertion(() => Assert.Equal(["opened", "focus", "closed"], order));
    }

    [Fact]
    public async Task Disposing_an_open_popover_stops_watching_the_window()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var popover = Render(context);
        popover.Find("[data-testid='options-button']").Click();

        await context.DisposeComponentsAsync();

        Assert.Single(context.JSInterop.Invocations["backlogPopoverOutside.unwatch"]);
    }

    /// <summary>A trigger on the window's bottom edge opens its panel above itself,
    /// and the end alignment still applies on top of that.</summary>
    [Fact]
    public void The_panel_opens_below_the_trigger_unless_asked_to_open_upward()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var popover = Render(context);
        popover.Find("[data-testid='options-button']").Click();
        Assert.DoesNotContain("popover-button__panel--up", popover.Find("[data-testid='options-panel']").ClassList);

        popover.Render(parameters => parameters.Add(p => p.OpenUpward, true).Add(p => p.AlignEnd, true));
        var panel = popover.Find("[data-testid='options-panel']");
        Assert.Contains("popover-button__panel--up", panel.ClassList);
        Assert.Contains("popover-button__panel--end", panel.ClassList);
    }

    /// <summary>The upward panel's placement is the stylesheet's: its bottom edge on
    /// the trigger's top, the downward rule's top released.</summary>
    [Fact]
    public void The_upward_panel_sits_on_the_triggers_top_edge()
    {
        var css = File.ReadAllText(RepositoryRoot.File("src", "Core", "Backlog.UI.Components", "wwwroot", "components.css"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        var start = css.IndexOf(".popover-button__panel--up {", StringComparison.Ordinal);
        Assert.True(start >= 0, "components.css styles the upward panel.");
        var rule = css[start..css.IndexOf('}', start)];

        Assert.Contains("top: auto;", rule, StringComparison.Ordinal);
        Assert.Contains("bottom: calc(100% + var(--spacing-xs));", rule, StringComparison.Ordinal);
    }

    [Fact]
    public void A_click_inside_the_panel_leaves_it_open()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var popover = Render(context);
        popover.Find("[data-testid='options-button']").Click();
        popover.Find("[data-testid='inside']").Click();

        Assert.NotNull(popover.Find("[data-testid='options-panel']"));
    }
}
