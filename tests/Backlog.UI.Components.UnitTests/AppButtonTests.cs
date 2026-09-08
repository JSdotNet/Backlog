namespace Backlog.UI.Components.UnitTests;

public sealed class AppButtonTests
{
    [Fact]
    public void Variant_and_size_are_emitted_as_modifier_classes()
    {
        using var context = new BunitContext();

        var button = context.Render<AppButton>(parameters => parameters
            .Add(b => b.Variant, ButtonVariant.Primary)
            .Add(b => b.Size, ButtonSize.Small));

        var classes = button.Find("button").ClassList;

        Assert.Contains("btn", classes);
        Assert.Contains("btn--primary", classes);
        Assert.Contains("btn--small", classes);
    }

    [Fact]
    public void None_variant_and_default_size_emit_no_modifier_at_all()
    {
        // A host that already dresses plain `.btn` would be restyled by a
        // modifier it never asked for, so both have to stay silent.
        using var context = new BunitContext();

        var button = context.Render<AppButton>(parameters => parameters
            .Add(b => b.Variant, ButtonVariant.None)
            .Add(b => b.Size, ButtonSize.Default));

        Assert.Equal("btn", button.Find("button").GetAttribute("class"));
    }

    [Fact]
    public void The_default_size_is_the_one_that_emits_nothing()
    {
        // There is no third size. `.btn` carries the default metrics, so the only
        // size modifier that exists is the small one.
        using var context = new BunitContext();

        var button = context.Render<AppButton>(parameters => parameters
            .Add(b => b.Variant, ButtonVariant.None));

        Assert.Equal("btn", button.Find("button").GetAttribute("class"));
        Assert.Equal([ButtonSize.Default, ButtonSize.Small], Enum.GetValues<ButtonSize>());
    }

    [Fact]
    public void Busy_marks_the_button_busy_and_blocks_the_click()
    {
        using var context = new BunitContext();

        var button = context.Render<AppButton>(parameters => parameters.Add(b => b.Busy, true));
        var element = button.Find("button");

        Assert.Equal("true", element.GetAttribute("aria-busy"));
        Assert.True(element.HasAttribute("disabled"));
    }

    [Fact]
    public void Not_busy_leaves_aria_busy_off_the_element()
    {
        using var context = new BunitContext();

        var button = context.Render<AppButton>();

        Assert.False(button.Find("button").HasAttribute("aria-busy"));
    }

    [Fact]
    public void Click_reaches_the_callback()
    {
        using var context = new BunitContext();
        var clicks = 0;

        var button = context.Render<AppButton>(parameters => parameters
            .Add(b => b.OnClick, () => clicks++));

        button.Find("button").Click();

        Assert.Equal(1, clicks);
    }

    [Fact]
    public void Only_submit_survives_as_a_type_so_a_stray_value_cannot_submit_a_form()
    {
        using var context = new BunitContext();

        var submit = context.Render<AppButton>(parameters => parameters.Add(b => b.Type, "submit"));
        var nonsense = context.Render<AppButton>(parameters => parameters.Add(b => b.Type, "reset"));

        Assert.Equal("submit", submit.Find("button").GetAttribute("type"));
        Assert.Equal("button", nonsense.Find("button").GetAttribute("type"));
    }
    // --- Where the icon-alone marker may come from --------------------------

    /// <summary>
    /// The marker is a parameter and not a rule this component works out, and
    /// both of the rules it could have worked it out from are recorded here
    /// because both were reached for.
    ///
    /// <para><c>btn--icon</c> is neither necessary nor sufficient:
    /// <see cref="IconButton"/> owns that stem and emits no <c>.btn__icon</c> at
    /// all, while the overflow trigger in <c>IntegrationActionBar</c> is an
    /// icon-only button that never wears the stem.</para>
    /// </summary>
    [Fact]
    public void The_icon_stem_says_nothing_about_whether_a_word_follows_the_glyph()
    {
        using var context = new BunitContext();

        // The stem's own component does not emit the class the marker keys on,
        // so a rule reading `btn--icon` would never reach the buttons that need
        // it and would reach ones that have no gap to close.
        var icon = context.Render<IconButton>(parameters => parameters
            .Add(b => b.AriaLabel, "Copy")
            .AddChildContent("<svg />"));

        Assert.Contains("btn--icon", icon.Find("button").ClassList);
        Assert.Empty(icon.FindAll(".btn__icon"));

        // And the stem on an AppButton is a class a host hands over, which says
        // nothing about the content: labelled, it still needs the gap.
        var labelled = context.Render<AppButton>(parameters => parameters
            .Add(b => b.BaseClass, "btn btn--icon")
            .Add(b => b.Icon, (RenderFragment)(builder => builder.AddMarkupContent(0, "<svg />")))
            .AddChildContent("Copy"));

        Assert.DoesNotContain("btn__icon--alone", labelled.Find(".btn__icon").ClassList);
    }

    /// <summary>
    /// The second rejected rule. <c>ChildContent is null</c> looks like it would
    /// find an icon-only button, but a caller may pass a fragment that renders
    /// nothing — which is exactly what <c>IntegrationAction</c>'s caption does at
    /// Compact density — so the component holding the fragment is the only thing
    /// that knows whether a word came out of it.
    /// </summary>
    [Fact]
    public void A_caption_that_renders_nothing_is_still_a_caption()
    {
        using var context = new BunitContext();

        var button = context.Render<AppButton>(parameters => parameters
            .Add(b => b.Icon, (RenderFragment)(builder => builder.AddMarkupContent(0, "<svg />")))
            .Add(b => b.ChildContent, (RenderFragment)(_ => { })));

        // Non-null, and yet the button reads as empty: the two cases are
        // indistinguishable from in here.
        Assert.Equal(string.Empty, button.Find("button").TextContent.Trim());
        Assert.DoesNotContain("btn__icon--alone", button.Find(".btn__icon").ClassList);

        // The marker is what tells them apart, and it comes from the caller.
        var alone = context.Render<AppButton>(parameters => parameters
            .Add(b => b.Icon, (RenderFragment)(builder => builder.AddMarkupContent(0, "<svg />")))
            .Add(b => b.ChildContent, (RenderFragment)(_ => { }))
            .Add(b => b.IconAlone, true));

        Assert.Contains("btn__icon--alone", alone.Find(".btn__icon").ClassList);
    }
}
