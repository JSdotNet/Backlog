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
}
