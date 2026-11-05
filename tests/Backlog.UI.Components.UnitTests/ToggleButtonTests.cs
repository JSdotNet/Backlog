namespace Backlog.UI.Components.UnitTests;

public sealed class ToggleButtonTests
{
    [Fact]
    public void Aria_pressed_reflects_the_pressed_parameter()
    {
        using var context = new BunitContext();

        var off = context.Render<ToggleButton>();
        var on = context.Render<ToggleButton>(parameters => parameters.Add(b => b.Pressed, true));

        Assert.Equal("false", off.Find("button").GetAttribute("aria-pressed"));
        Assert.Equal("true", on.Find("button").GetAttribute("aria-pressed"));
        Assert.Contains("btn--pressed", on.Find("button").ClassList);
    }

    [Fact]
    public void Clicking_flips_the_state_and_reports_it()
    {
        using var context = new BunitContext();
        bool? reported = null;

        var button = context.Render<ToggleButton>(parameters => parameters
            .Add(b => b.PressedChanged, (bool pressed) => reported = pressed));

        button.Find("button").Click();

        Assert.True(reported);
        Assert.Equal("true", button.Find("button").GetAttribute("aria-pressed"));
    }

    /// <summary>
    /// Busy is AppButton's Busy: the button keeps its tab stop, so focus survives the
    /// act it belongs to, and the press is dropped rather than flipping the state.
    /// </summary>
    [Fact]
    public void A_busy_button_stays_focusable_and_ignores_the_press()
    {
        using var context = new BunitContext();
        bool? reported = null;
        var clicked = false;

        var button = context.Render<ToggleButton>(parameters => parameters
            .Add(b => b.Busy, true)
            .Add(b => b.PressedChanged, (bool pressed) => reported = pressed)
            .Add(b => b.OnClick, (MouseEventArgs _) => clicked = true));

        var element = button.Find("button");
        Assert.Equal("true", element.GetAttribute("aria-disabled"));
        Assert.False(element.HasAttribute("disabled"));

        element.Click();

        Assert.Null(reported);
        Assert.False(clicked);
        Assert.Equal("false", button.Find("button").GetAttribute("aria-pressed"));
    }

    [Fact]
    public void Disabled_stays_the_native_attribute_and_wins_over_busy()
    {
        using var context = new BunitContext();

        var disabled = context.Render<ToggleButton>(parameters => parameters.Add(b => b.Disabled, true));
        var both = context.Render<ToggleButton>(parameters => parameters
            .Add(b => b.Disabled, true)
            .Add(b => b.Busy, true));
        var idle = context.Render<ToggleButton>();

        Assert.True(disabled.Find("button").HasAttribute("disabled"));
        Assert.False(disabled.Find("button").HasAttribute("aria-disabled"));
        Assert.True(both.Find("button").HasAttribute("disabled"));
        Assert.False(both.Find("button").HasAttribute("aria-disabled"));
        Assert.False(idle.Find("button").HasAttribute("aria-disabled"));
    }

    /// <summary>
    /// A host whose strip means something different with a modifier held — one of
    /// many on a plain press, one more on Ctrl — needs the event, not just the new
    /// state. It arrives beside <c>PressedChanged</c> rather than instead of it, so
    /// a host that only wants the bool keeps getting it.
    /// </summary>
    [Fact]
    public void The_click_reaches_the_host_with_its_modifiers()
    {
        using var context = new BunitContext();
        MouseEventArgs? received = null;
        bool? reported = null;

        var button = context.Render<ToggleButton>(parameters => parameters
            .Add(b => b.PressedChanged, (bool pressed) => reported = pressed)
            .Add(b => b.OnClick, (MouseEventArgs args) => received = args));

        button.Find("button").Click(new MouseEventArgs { CtrlKey = true });

        Assert.True(reported);
        Assert.NotNull(received);
        Assert.True(received.CtrlKey);
    }
}
