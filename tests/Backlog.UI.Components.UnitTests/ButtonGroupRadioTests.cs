namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The radio-group mode of <see cref="ButtonGroup"/>: a strip whose buttons are one
/// choice, announced as one control with one tab stop, where the arrows move and
/// select — the idiom <c>Tabs</c> and <c>ChangeScopePicker</c> already use.
/// </summary>
public sealed class ButtonGroupRadioTests
{
    [Fact]
    public void A_radio_group_announces_one_choice_rather_than_separate_switches()
    {
        using var context = new BunitContext();

        var harness = context.Render<RadioGroupHarness>(parameters => parameters.Add(h => h.Selected, "8"));

        Assert.Equal("radiogroup", harness.Find("[data-testid='radio']").GetAttribute("role"));

        var radios = harness.FindAll("[data-testid='radio'] button");
        Assert.All(radios, radio => Assert.Equal("radio", radio.GetAttribute("role")));
        Assert.All(radios, radio => Assert.False(radio.HasAttribute("aria-pressed")));
        Assert.Equal(["false", "true", "false", "false"], radios.Select(radio => radio.GetAttribute("aria-checked")));
    }

    [Fact]
    public void Only_the_checked_radio_is_a_tab_stop()
    {
        using var context = new BunitContext();

        var harness = context.Render<RadioGroupHarness>(parameters => parameters.Add(h => h.Selected, "12"));

        var stops = harness.FindAll("[data-testid='radio'] button").Select(radio => radio.GetAttribute("tabindex"));
        Assert.Equal(["-1", "-1", "0", "-1"], stops);
    }

    [Fact]
    public void With_nothing_checked_the_first_radio_is_the_tab_stop()
    {
        using var context = new BunitContext();

        var harness = context.Render<RadioGroupHarness>();

        var stops = harness.FindAll("[data-testid='radio'] button").Select(radio => radio.GetAttribute("tabindex"));
        Assert.Equal(["0", "-1", "-1", "-1"], stops);
    }

    [Fact]
    public void An_arrow_moves_to_the_next_radio_and_selects_it()
    {
        using var context = new BunitContext();
        context.JSInterop.SetupVoid("Blazor._internal.domWrapper.focus", _ => true).SetVoidResult();

        var harness = context.Render<RadioGroupHarness>(parameters => parameters.Add(h => h.Selected, "4"));

        harness.Find("[data-testid='radio-4']").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });

        harness.WaitForAssertion(() =>
            Assert.Equal("true", harness.Find("[data-testid='radio-8']").GetAttribute("aria-checked")));
        Assert.Equal("0", harness.Find("[data-testid='radio-8']").GetAttribute("tabindex"));
        Assert.Equal("-1", harness.Find("[data-testid='radio-4']").GetAttribute("tabindex"));
        Assert.Single(context.JSInterop.Invocations["Blazor._internal.domWrapper.focus"]);
    }

    [Fact]
    public void The_arrows_wrap_and_Home_and_End_jump_to_the_ends()
    {
        using var context = new BunitContext();
        context.JSInterop.SetupVoid("Blazor._internal.domWrapper.focus", _ => true).SetVoidResult();

        var harness = context.Render<RadioGroupHarness>(parameters => parameters.Add(h => h.Selected, "4"));

        // Each step waits for the strip to have drawn the new choice before the next key
        // goes to it: a key dispatched to the element drawn before that render would
        // reach a handler the render has already replaced.
        void Press(string from, string key, string lands)
        {
            harness.Find($"[data-testid='radio-{from}']").KeyDown(new KeyboardEventArgs { Key = key });
            harness.WaitForAssertion(() =>
                Assert.Equal("true", harness.Find($"[data-testid='radio-{lands}']").GetAttribute("aria-checked")));
        }

        Press("4", "ArrowLeft", "26");
        Press("26", "ArrowDown", "4");
        Press("4", "End", "26");
        Press("26", "Home", "4");
        Press("4", "ArrowUp", "26");
    }

    [Fact]
    public void A_disabled_radio_is_skipped_by_the_arrows()
    {
        using var context = new BunitContext();
        context.JSInterop.SetupVoid("Blazor._internal.domWrapper.focus", _ => true).SetVoidResult();

        var harness = context.Render<RadioGroupHarness>(parameters => parameters
            .Add(h => h.Selected, "4")
            .Add(h => h.DisabledOption, "8"));

        harness.Find("[data-testid='radio-4']").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });

        harness.WaitForAssertion(() => Assert.Equal("12", harness.Instance.Selected));
    }

    [Fact]
    public void Pressing_the_checked_radio_keeps_it_checked()
    {
        using var context = new BunitContext();

        var harness = context.Render<RadioGroupHarness>(parameters => parameters.Add(h => h.Selected, "8"));

        harness.Find("[data-testid='radio-8']").Click();

        Assert.Equal([("8", true)], harness.Instance.Reports);
        Assert.Equal("true", harness.Find("[data-testid='radio-8']").GetAttribute("aria-checked"));
    }

    [Fact]
    public void A_key_that_is_not_navigation_selects_nothing()
    {
        using var context = new BunitContext();

        var harness = context.Render<RadioGroupHarness>(parameters => parameters.Add(h => h.Selected, "8"));

        harness.Find("[data-testid='radio-8']").KeyDown(new KeyboardEventArgs { Key = "a" });

        Assert.Empty(harness.Instance.Reports);
    }

    /// <summary>The mode is opt-in: every existing strip keeps its separate
    /// <c>aria-pressed</c> switches and the tab stops it had.</summary>
    [Fact]
    public void Without_the_radio_mode_a_group_keeps_its_pressed_toggles()
    {
        using var context = new BunitContext();

        var group = context.Render<ButtonGroup>(parameters => parameters
            .Add(g => g.AriaLabel, "View")
            .AddChildContent<ToggleButton>(child => child.Add(b => b.Pressed, true)));

        Assert.Equal("group", group.Find(".btn-group").GetAttribute("role"));
        var button = group.Find("button");
        Assert.Equal("true", button.GetAttribute("aria-pressed"));
        Assert.False(button.HasAttribute("role"));
        Assert.False(button.HasAttribute("aria-checked"));
        Assert.False(button.HasAttribute("tabindex"));
    }
}
