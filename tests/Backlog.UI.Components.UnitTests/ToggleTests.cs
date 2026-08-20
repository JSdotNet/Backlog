namespace Backlog.UI.Components.UnitTests;

public sealed class ToggleTests
{
    [Fact]
    public void Default_shape_is_a_switch_with_a_labelled_track()
    {
        using var context = new BunitContext();

        var toggle = context.Render<Toggle>(parameters => parameters
            .Add(t => t.Label, "Show archived")
            .Add(t => t.Description, "Includes finished entries"));

        var control = toggle.Find("[role='switch']");

        Assert.Equal("false", control.GetAttribute("aria-checked"));
        Assert.Equal("Show archived", toggle.Find(".toggle__label").TextContent);
        Assert.Equal(control.GetAttribute("aria-labelledby"), toggle.Find(".toggle__label").Id);
        Assert.Equal(control.GetAttribute("aria-describedby"), toggle.Find(".toggle__description").Id);
        Assert.Empty(toggle.FindAll("input[type='checkbox']"));
    }

    [Fact]
    public void Native_checkbox_mode_renders_a_real_checkbox_inside_a_label()
    {
        using var context = new BunitContext();

        var toggle = context.Render<Toggle>(parameters => parameters
            .Add(t => t.NativeCheckbox, true)
            .Add(t => t.Label, "Show archived"));

        Assert.Empty(toggle.FindAll("[role='switch']"));
        Assert.Equal("label", toggle.Find(".toggle").NodeName.ToLowerInvariant());
        Assert.NotNull(toggle.Find("label > input[type='checkbox']"));
    }

    /// <summary>The suffix wrapper is conditional, and this is the half of that
    /// worth protecting: the text block is a grid, so a wrapper rendered
    /// unconditionally would put every existing caller's label inside a box it
    /// did not have before.</summary>
    [Fact]
    public void A_toggle_with_no_label_suffix_keeps_the_markup_it_had()
    {
        using var context = new BunitContext();

        var toggle = context.Render<Toggle>(parameters => parameters
            .Add(t => t.Label, "Show archived"));

        Assert.Empty(toggle.FindAll(".toggle__label-row"));
        Assert.Equal("toggle__text", toggle.Find(".toggle__label").ParentElement!.GetAttribute("class"));
    }

    [Fact]
    public void A_label_suffix_shares_the_label_line_without_joining_the_name()
    {
        using var context = new BunitContext();

        var toggle = context.Render<Toggle>(parameters => parameters
            .Add(t => t.Label, "Inbox pane")
            .Add(t => t.LabelSuffix, builder =>
            {
                builder.OpenElement(0, "span");
                builder.AddAttribute(1, "data-testid", "suffix");
                builder.AddContent(2, "DEV");
                builder.CloseElement();
            }));

        var row = toggle.Find(".toggle__label-row");
        var label = toggle.Find(".toggle__label");

        // Same line as the label...
        Assert.Equal(row, toggle.Find("[data-testid='suffix']").ParentElement);
        Assert.Equal(row, label.ParentElement);

        // ...and outside the span the control is named by, so the switch is still
        // called "Inbox pane" rather than "Inbox pane DEV".
        Assert.Equal("Inbox pane", label.TextContent);
        Assert.Equal(label.Id, toggle.Find("[role='switch']").GetAttribute("aria-labelledby"));
    }

    [Fact]
    public void The_on_modifier_belongs_to_the_switch_only()
    {
        // A real checkbox already carries `:checked` in the DOM, so mirroring the
        // state into a class there would be a second source of truth.
        using var context = new BunitContext();

        var @switch = context.Render<Toggle>(parameters => parameters.Add(t => t.Checked, true));
        var native = context.Render<Toggle>(parameters => parameters
            .Add(t => t.Checked, true)
            .Add(t => t.NativeCheckbox, true));

        Assert.Contains("toggle--on", @switch.Find(".toggle").ClassList);
        Assert.DoesNotContain("toggle--on", native.Find(".toggle").ClassList);
    }

    [Fact]
    public void Both_shapes_report_the_new_state()
    {
        using var context = new BunitContext();
        bool? fromSwitch = null;
        bool? fromCheckbox = null;

        var @switch = context.Render<Toggle>(parameters => parameters
            .Add(t => t.CheckedChanged, (bool value) => fromSwitch = value));
        var native = context.Render<Toggle>(parameters => parameters
            .Add(t => t.NativeCheckbox, true)
            .Add(t => t.CheckedChanged, (bool value) => fromCheckbox = value));

        @switch.Find("[role='switch']").Click();
        native.Find("input[type='checkbox']").Change(true);

        Assert.True(fromSwitch);
        Assert.True(fromCheckbox);
    }

    [Fact]
    public void A_disabled_switch_does_not_report_a_change()
    {
        using var context = new BunitContext();
        var changes = 0;

        var toggle = context.Render<Toggle>(parameters => parameters
            .Add(t => t.Disabled, true)
            .Add(t => t.CheckedChanged, (bool _) => changes++));

        toggle.Find("[role='switch']").Click();

        Assert.Equal(0, changes);
    }

    [Fact]
    public void Part_classes_land_on_the_element_each_one_names()
    {
        // These hooks exist so the app could keep its own DOM byte for byte while
        // handing the behaviour over to the library.
        using var context = new BunitContext();

        var toggle = context.Render<Toggle>(parameters => parameters
            .Add(t => t.Label, "Show archived")
            .Add(t => t.Description, "Includes finished entries")
            .Add(t => t.BaseClass, "setting-row")
            .Add(t => t.TextCssClass, "setting-row__text")
            .Add(t => t.LabelCssClass, "setting-row__label")
            .Add(t => t.DescriptionCssClass, "setting-row__hint")
            .Add(t => t.ControlCssClass, "setting-row__switch"));

        Assert.Equal("setting-row", toggle.Find("div").GetAttribute("class"));
        Assert.Equal("setting-row__switch", toggle.Find("[role='switch']").GetAttribute("class"));
        Assert.NotNull(toggle.Find(".setting-row__text .setting-row__label"));
        Assert.NotNull(toggle.Find(".setting-row__text .setting-row__hint"));
        Assert.Empty(toggle.FindAll(".toggle__track"));
    }
}
