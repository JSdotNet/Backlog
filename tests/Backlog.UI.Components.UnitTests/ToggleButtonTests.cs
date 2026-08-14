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
}
