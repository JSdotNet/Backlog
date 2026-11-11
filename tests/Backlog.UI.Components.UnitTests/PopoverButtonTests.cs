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

    [Fact]
    public async Task The_host_can_close_the_panel_and_closing_it_again_does_nothing()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var changes = new List<bool>();

        var popover = Render(context, changes);
        popover.Find("[data-testid='options-button']").Click();

        await popover.InvokeAsync(popover.Instance.CloseAsync);
        await popover.InvokeAsync(popover.Instance.CloseAsync);

        Assert.Empty(popover.FindAll("[data-testid='options-panel']"));
        Assert.Equal("false", popover.Find("[data-testid='options-button']").GetAttribute("aria-expanded"));
        Assert.Equal([true, false], changes);
    }

    [Fact]
    public async Task The_host_can_put_the_focus_on_the_trigger_and_the_panel_stays_open()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var changes = new List<bool>();

        var popover = Render(context, changes);
        popover.Find("[data-testid='options-button']").Click();

        await popover.InvokeAsync(popover.Instance.FocusAsync);

        context.JSInterop.VerifyFocusAsyncInvoke();
        Assert.NotNull(popover.Find("[data-testid='options-panel']"));
        Assert.Equal([true], changes);
    }
}
