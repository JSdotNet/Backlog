namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// A marker at a field's trailing edge used to mean the host hand-rolling its own
/// input, because the component had nowhere to put one. The hook is here instead.
/// <para>
/// What these pin is the price of adding it: a field given no suffix renders the
/// markup it always did, down to the input being the wrapper's own child. Every
/// form in the product is laid out against that markup, and a wrapper that
/// appeared unconditionally would move all of them at once.
/// </para>
/// </summary>
public sealed class TextFieldSuffixTests
{
    private const string Marker = """<span class="host-marker">ok</span>""";

    [Fact]
    public void A_field_with_no_suffix_keeps_the_input_as_the_wrappers_own_child()
    {
        using var context = new BunitContext();

        var field = context.Render<TextField>(parameters => parameters.Add(f => f.Label, "Folder"));

        Assert.Empty(field.FindAll(".field__control"));
        Assert.Empty(field.FindAll(".field__suffix"));
        Assert.Equal(["LABEL", "INPUT"], field.Find(".field").Children.Select(child => child.TagName));
    }

    [Fact]
    public void A_bare_field_with_no_suffix_emits_the_parts_as_siblings()
    {
        using var context = new BunitContext();

        var field = context.Render<TextField>(parameters => parameters
            .Add(f => f.Bare, true)
            .Add(f => f.Label, "Folder"));

        Assert.Empty(field.FindAll(".field__control"));
        Assert.Equal(["LABEL", "INPUT"], field.Nodes.OfType<AngleSharp.Dom.IElement>().Select(node => node.TagName));
    }

    [Fact]
    public void A_suffix_sits_beside_the_input_inside_the_control()
    {
        using var context = new BunitContext();

        var field = context.Render<TextField>(parameters => parameters
            .Add(f => f.Label, "Folder")
            .Add(f => f.Suffix, Marker));

        var control = field.Find(".field__control");
        Assert.Equal(["INPUT", "SPAN"], control.Children.Select(child => child.TagName));

        // The host's own element, untouched: the library positions the box around
        // it rather than dictating what goes in it.
        Assert.Equal("ok", control.QuerySelector(".field__suffix > .host-marker")!.TextContent);
    }

    /// <summary>Bare is how a settings row uses this field, so the suffix has to
    /// arrive without the wrapper the row has no room for - and the test id has to
    /// stay on the input, which is still the one element automation types into.</summary>
    [Fact]
    public void A_bare_field_takes_a_suffix_without_regrowing_the_field_wrapper()
    {
        using var context = new BunitContext();

        var field = context.Render<TextField>(parameters => parameters
            .Add(f => f.Bare, true)
            .Add(f => f.InputCssClass, "setting__input")
            .Add(f => f.TestId, "knowledge-folder-path-input")
            .Add(f => f.Suffix, Marker));

        Assert.Empty(field.FindAll(".field"));
        Assert.Equal("setting__input", field.Find(".field__control > input").GetAttribute("class"));

        var found = Assert.Single(field.FindAll("[data-testid=\"knowledge-folder-path-input\"]"));
        Assert.Equal("INPUT", found.TagName);
    }

    [Fact]
    public void A_host_can_replace_the_suffix_class_with_its_own()
    {
        using var context = new BunitContext();

        var field = context.Render<TextField>(parameters => parameters
            .Add(f => f.SuffixCssClass, "setting__marker")
            .Add(f => f.Suffix, Marker));

        Assert.Empty(field.FindAll(".field__suffix"));
        Assert.Equal("setting__marker", field.Find(".field__control > span").GetAttribute("class"));
    }
}
