namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// A set of mutually exclusive choices that is one stop in the tab order, with the
/// arrow keys moving through it — the WAI-ARIA radio group pattern, held to the
/// same roving tabindex <see cref="TabsTests"/> holds the tab strip to.
/// </summary>
public sealed class RadioGroupTests
{
    private static readonly string[] Sizes = ["small", "medium", "large"];

    private static IRenderedComponent<RadioGroup<string>> Render(
        BunitContext context,
        string? value = "small",
        Action<string>? changed = null,
        Func<string, bool>? optionDisabled = null,
        bool disabled = false) =>
        context.Render<RadioGroup<string>>(parameters => parameters
            .Add(group => group.Options, Sizes)
            .Add(group => group.Value, value)
            .Add(group => group.ValueChanged, (string next) => changed?.Invoke(next))
            .Add(group => group.OptionLabel, size => size.ToUpperInvariant())
            .Add(group => group.OptionTestId, size => $"size-{size}")
            .Add(group => group.OptionDisabled, optionDisabled)
            .Add(group => group.Disabled, disabled)
            .Add(group => group.AriaLabel, "Size")
            .Add(group => group.TestId, "sizes"));

    private static string[] Checked(IRenderedComponent<RadioGroup<string>> group) =>
        [.. group.FindAll("[role='radio']").Select(radio => radio.GetAttribute("aria-checked") ?? string.Empty)];

    private static string[] TabIndexes(IRenderedComponent<RadioGroup<string>> group) =>
        [.. group.FindAll("[role='radio']").Select(radio => radio.GetAttribute("tabindex") ?? string.Empty)];

    [Fact]
    public void The_group_is_a_labelled_radiogroup_of_radios_that_say_which_is_checked()
    {
        using var context = new BunitContext();

        var group = Render(context, value: "medium");

        var root = group.Find("[data-testid='sizes']");
        Assert.Equal("radiogroup", root.GetAttribute("role"));
        Assert.Equal("Size", root.GetAttribute("aria-label"));
        Assert.Equal(3, group.FindAll("[role='radio']").Count);
        Assert.Equal(["false", "true", "false"], Checked(group));
        Assert.Equal("MEDIUM", group.Find("[data-testid='size-medium']").TextContent.Trim());
        Assert.All(group.FindAll("[role='radio']"), radio => Assert.Equal("button", radio.GetAttribute("type")));
    }

    [Fact]
    public void Exactly_one_choice_is_reachable_with_Tab_and_it_is_the_checked_one()
    {
        using var context = new BunitContext();

        var group = Render(context, value: "large");

        Assert.Equal(["-1", "-1", "0"], TabIndexes(group));
    }

    /// <summary>With nothing checked the group is still one stop, on its first
    /// choice the keyboard can reach.</summary>
    [Fact]
    public void With_nothing_checked_the_first_enabled_choice_is_the_tab_stop()
    {
        using var context = new BunitContext();

        var group = Render(context, value: null, optionDisabled: size => size == "small");

        Assert.Equal(["-1", "0", "-1"], TabIndexes(group));
        Assert.Equal(["false", "false", "false"], Checked(group));
    }

    [Theory]
    [InlineData("ArrowRight")]
    [InlineData("ArrowDown")]
    public void The_next_arrow_checks_the_next_choice_and_wraps(string key)
    {
        using var context = new BunitContext();
        var picked = new List<string>();

        var group = Render(context, value: "medium", changed: picked.Add);

        group.Find("[data-testid='size-medium']").KeyDown(new KeyboardEventArgs { Key = key });
        Assert.Equal(["false", "false", "true"], Checked(group));
        Assert.Equal(["-1", "-1", "0"], TabIndexes(group));

        group.Find("[data-testid='size-large']").KeyDown(new KeyboardEventArgs { Key = key });
        Assert.Equal(["true", "false", "false"], Checked(group));

        Assert.Equal(["large", "small"], picked);
    }

    [Theory]
    [InlineData("ArrowLeft")]
    [InlineData("ArrowUp")]
    public void The_previous_arrow_checks_the_previous_choice_and_wraps(string key)
    {
        using var context = new BunitContext();
        var picked = new List<string>();

        var group = Render(context, value: "small", changed: picked.Add);

        group.Find("[data-testid='size-small']").KeyDown(new KeyboardEventArgs { Key = key });
        Assert.Equal(["false", "false", "true"], Checked(group));

        group.Find("[data-testid='size-large']").KeyDown(new KeyboardEventArgs { Key = key });
        Assert.Equal(["false", "true", "false"], Checked(group));

        Assert.Equal(["large", "medium"], picked);
    }

    [Fact]
    public void Home_and_End_jump_to_the_ends_of_the_group()
    {
        using var context = new BunitContext();
        var picked = new List<string>();

        var group = Render(context, value: "medium", changed: picked.Add);

        group.Find("[data-testid='size-medium']").KeyDown(new KeyboardEventArgs { Key = "End" });
        group.Find("[data-testid='size-large']").KeyDown(new KeyboardEventArgs { Key = "Home" });

        Assert.Equal(["large", "small"], picked);
        Assert.Equal(["true", "false", "false"], Checked(group));
    }

    /// <summary>The ring goes with the selection, for the reason
    /// <see cref="TabsTests.An_arrow_key_takes_focus_with_it"/> gives: the choice
    /// that was checked is at <c>tabindex="-1"</c> the moment the reader arrows off
    /// it, so focus left there makes the group one-way.</summary>
    [Fact]
    public void An_arrow_key_takes_focus_with_it()
    {
        using var context = new BunitContext();
        context.JSInterop.SetupVoid("Blazor._internal.domWrapper.focus", _ => true);

        var group = Render(context, value: "small");

        group.Find("[data-testid='size-small']").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        group.Find("[data-testid='size-medium']").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });

        var focused = context.JSInterop.Invocations["Blazor._internal.domWrapper.focus"];

        Assert.Equal(2, focused.Count);
        Assert.NotEqual(
            ((ElementReference)focused[0].Arguments[0]!).Id,
            ((ElementReference)focused[1].Arguments[0]!).Id);
    }

    [Fact]
    public void Clicking_a_choice_checks_it_and_leaves_the_focus_where_the_reader_put_it()
    {
        using var context = new BunitContext();
        context.JSInterop.SetupVoid("Blazor._internal.domWrapper.focus", _ => true);
        var picked = new List<string>();

        var group = Render(context, value: "small", changed: picked.Add);

        group.Find("[data-testid='size-large']").Click();

        Assert.Equal(["large"], picked);
        Assert.Equal(["false", "false", "true"], Checked(group));
        Assert.Empty(context.JSInterop.Invocations["Blazor._internal.domWrapper.focus"]);
    }

    [Fact]
    public void Other_keys_change_nothing()
    {
        using var context = new BunitContext();
        var picked = new List<string>();

        var group = Render(context, value: "small", changed: picked.Add);

        group.Find("[data-testid='size-small']").KeyDown(new KeyboardEventArgs { Key = "a" });
        group.Find("[data-testid='size-small']").KeyDown(new KeyboardEventArgs { Key = "Tab" });

        Assert.Empty(picked);
        Assert.Equal(["true", "false", "false"], Checked(group));
    }

    /// <summary>A choice that cannot be taken is drawn but stepped over: the arrows
    /// skip it, as Tabs' arrows skip a disabled tab.</summary>
    [Fact]
    public void The_arrows_step_over_a_disabled_choice()
    {
        using var context = new BunitContext();
        var picked = new List<string>();

        var group = Render(context, value: "small", changed: picked.Add, optionDisabled: size => size == "medium");

        Assert.True(group.Find("[data-testid='size-medium']").HasAttribute("disabled"));

        group.Find("[data-testid='size-small']").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        group.Find("[data-testid='size-large']").KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });

        Assert.Equal(["large", "small"], picked);
    }

    [Fact]
    public void A_disabled_group_disables_every_choice_and_takes_no_key()
    {
        using var context = new BunitContext();
        var picked = new List<string>();

        var group = Render(context, value: "small", changed: picked.Add, disabled: true);

        Assert.All(group.FindAll("[role='radio']"), radio => Assert.True(radio.HasAttribute("disabled")));
        Assert.Equal("true", group.Find("[data-testid='sizes']").GetAttribute("aria-disabled"));

        group.Find("[data-testid='size-small']").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });

        Assert.Empty(picked);
        Assert.Equal(["true", "false", "false"], Checked(group));
    }

    /// <summary>A host moving the value itself is followed: the checked choice and
    /// the tab stop both move with it.</summary>
    [Fact]
    public void A_value_set_by_the_host_moves_the_checked_choice_and_the_tab_stop()
    {
        using var context = new BunitContext();

        var group = Render(context, value: "small");

        group.Render(parameters => parameters.Add(radio => radio.Value, "large"));

        Assert.Equal(["false", "false", "true"], Checked(group));
        Assert.Equal(["-1", "-1", "0"], TabIndexes(group));
    }

    [Fact]
    public void The_defaults_draw_toggle_buttons_in_a_radio_group()
    {
        using var context = new BunitContext();

        var group = Render(context, value: "small");

        Assert.Equal("radio-group", group.Find("[data-testid='sizes']").ClassName);
        Assert.Equal(
            "btn btn--toggle btn--small btn--pressed radio-group__option radio-group__option--checked",
            group.Find("[data-testid='size-small']").ClassName);
        Assert.Equal(
            "btn btn--toggle btn--small radio-group__option",
            group.Find("[data-testid='size-medium']").ClassName);
    }

    /// <summary>A host hands over its own class names, and they replace the
    /// library's rather than sitting beside them; the button stem stays the
    /// library's, with the variant the host picks.</summary>
    [Fact]
    public void The_class_hooks_replace_the_defaults_after_a_button_stem_in_the_variant_asked_for()
    {
        using var context = new BunitContext();

        var group = context.Render<RadioGroup<string>>(parameters => parameters
            .Add(radio => radio.Options, Sizes)
            .Add(radio => radio.Value, "small")
            .Add(radio => radio.BaseClass, "host__choices")
            .Add(radio => radio.CssClass, "host__choices--wide")
            .Add(radio => radio.OptionVariantClass, "btn--secondary")
            .Add(radio => radio.OptionCssClass, "host__choice")
            .Add(radio => radio.CheckedOptionCssClass, "host__choice host__choice--on")
            .Add(radio => radio.TestId, "sizes"));

        Assert.Equal("host__choices host__choices--wide", group.Find("[data-testid='sizes']").ClassName);
        Assert.Equal(
            [
                "btn btn--secondary btn--small host__choice host__choice--on",
                "btn btn--secondary btn--small host__choice",
                "btn btn--secondary btn--small host__choice",
            ],
            group.FindAll("[role='radio']").Select(radio => radio.ClassName).ToArray());
    }

    /// <summary>
    /// The half the render tests cannot reach: the listener that refuses the
    /// browser default of the keys the group moves with, so they do not also
    /// scroll the pane. Matched on role, because a host replaces the classes;
    /// never Space or Enter, which check a choice through its native click; and
    /// the event is never stopped.
    /// </summary>
    [Fact]
    public void Components_js_refuses_the_radio_keys_default_at_the_keydown_and_lets_them_bubble()
    {
        var script = File.ReadAllText(RepositoryRoot.File("src", "Core", "Backlog.UI.Components", "wwwroot", "components.js"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        var start = script.IndexOf("const RADIO_SWALLOWED_KEYS", StringComparison.Ordinal);
        Assert.True(start >= 0, "components.js no longer refuses the radio keys.");

        var listener = script[start..script.IndexOf("\n    });", start, StringComparison.Ordinal)];

        Assert.Contains("new Set(['ArrowDown', 'ArrowUp', 'ArrowLeft', 'ArrowRight', 'Home', 'End'])", listener, StringComparison.Ordinal);
        Assert.Contains("target.matches('[role=\"radiogroup\"] > [role=\"radio\"]')", listener, StringComparison.Ordinal);
        Assert.Contains("event.preventDefault()", listener, StringComparison.Ordinal);
        Assert.DoesNotContain("' '", listener, StringComparison.Ordinal);
        Assert.DoesNotContain("'Enter'", listener, StringComparison.Ordinal);
        Assert.DoesNotContain("stopPropagation", listener, StringComparison.Ordinal);
    }

    /// <summary>With no label function an option says what it is, and a template
    /// wins over both.</summary>
    [Fact]
    public void An_option_reads_its_template_else_its_label_else_itself()
    {
        using var context = new BunitContext();

        var plain = context.Render<RadioGroup<string>>(parameters => parameters
            .Add(radio => radio.Options, Sizes));
        var templated = context.Render<RadioGroup<string>>(parameters => parameters
            .Add(radio => radio.Options, Sizes)
            .Add(radio => radio.OptionLabel, size => "label")
            .Add(radio => radio.OptionContent, size => builder => builder.AddMarkupContent(0, $"<em>{size}</em>")));

        Assert.Equal("small", plain.FindAll("[role='radio']")[0].TextContent.Trim());
        Assert.Equal("small", templated.FindAll("[role='radio'] em")[0].TextContent);
    }
}
