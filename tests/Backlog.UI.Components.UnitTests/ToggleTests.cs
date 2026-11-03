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

    /// <summary>Extra text in the text block describes the control as much as the
    /// description does, so a screen reader must hear it too: the control names an
    /// element holding it, beside the description.</summary>
    [Fact]
    public void The_switch_is_described_by_its_description_and_its_extra_content()
    {
        using var context = new BunitContext();

        var toggle = context.Render<Toggle>(parameters => parameters
            .Add(t => t.Label, "Only questions assigned to me")
            .Add(t => t.Description, "Off also brings the rest.")
            .Add(t => t.TextContent, Hint("Switch off the phone's own push.")));

        var ids = DescribedBy(toggle.Find("[role='switch']"));

        Assert.Equal(2, ids.Length);
        Assert.Equal(toggle.Find(".toggle__description").Id, ids[0]);
        Assert.Contains("Switch off the phone's own push.", toggle.Find($"#{ids[1]}").TextContent);
    }

    [Fact]
    public void Extra_content_alone_still_describes_the_switch()
    {
        using var context = new BunitContext();

        var toggle = context.Render<Toggle>(parameters => parameters
            .Add(t => t.Label, "Backlog")
            .Add(t => t.TextContent, Hint("Always available")));

        var ids = DescribedBy(toggle.Find("[role='switch']"));

        Assert.Single(ids);
        Assert.Contains("Always available", toggle.Find($"#{ids[0]}").TextContent);
    }

    [Fact]
    public void The_native_checkbox_is_described_by_its_extra_content_too()
    {
        using var context = new BunitContext();

        var toggle = context.Render<Toggle>(parameters => parameters
            .Add(t => t.NativeCheckbox, true)
            .Add(t => t.Label, "Show archived")
            .Add(t => t.Description, "Includes finished entries")
            .Add(t => t.TextContent, Hint("Kept for 30 days")));

        var ids = DescribedBy(toggle.Find("input[type='checkbox']"));

        Assert.Equal(2, ids.Length);
        Assert.Equal(toggle.Find(".toggle__description").Id, ids[0]);
        Assert.Contains("Kept for 30 days", toggle.Find($"#{ids[1]}").TextContent);
    }

    /// <summary>The content wrapper is conditional, like the label row: a toggle
    /// with no extra content renders what it did, and one with neither a
    /// description nor content names nothing.</summary>
    [Fact]
    public void A_toggle_with_no_extra_content_keeps_the_markup_it_had()
    {
        using var context = new BunitContext();

        var bare = context.Render<Toggle>(parameters => parameters.Add(t => t.Label, "Show archived"));
        var native = context.Render<Toggle>(parameters => parameters
            .Add(t => t.NativeCheckbox, true)
            .Add(t => t.Label, "Show archived"));

        Assert.Null(bare.Find("[role='switch']").GetAttribute("aria-describedby"));
        Assert.Null(native.Find("input[type='checkbox']").GetAttribute("aria-describedby"));
        Assert.Empty(bare.FindAll(".toggle__content"));
        Assert.Empty(native.FindAll(".toggle__content"));
    }

    private static string[] DescribedBy(AngleSharp.Dom.IElement control) =>
        (control.GetAttribute("aria-describedby") ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static RenderFragment Hint(string text) => builder =>
    {
        builder.OpenElement(0, "span");
        builder.AddContent(1, text);
        builder.CloseElement();
    };

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
