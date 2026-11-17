namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The flag glyph the Domain view's Features board leads a flag chip with, and
/// the properties every glyph of the library holds: an outline on the
/// iconography chapter's 24x24 grid, its ink from what it sits in, its size from
/// an explicit width and height, and hidden from assistive technology unless a
/// caller names it. See <see cref="ChevronUpIconTests"/> for why each is a rule
/// rather than a preference.
/// </summary>
public sealed class FlagIconTests
{
    [Fact]
    public void The_glyph_is_an_outline_on_the_icon_grid()
    {
        using var context = new BunitContext();

        var svg = context.Render<FlagIcon>().Find("svg");

        Assert.Equal("0 0 24 24", svg.GetAttribute("viewBox"));
        Assert.Equal("none", svg.GetAttribute("fill"));
        Assert.Equal("currentColor", svg.GetAttribute("stroke"));
        Assert.Equal("2", svg.GetAttribute("stroke-width"));
        Assert.Equal("round", svg.GetAttribute("stroke-linecap"));
        Assert.Equal("round", svg.GetAttribute("stroke-linejoin"));

        var shapes = svg.QuerySelectorAll("path");
        Assert.NotEmpty(shapes);
        Assert.Equal(shapes.Length, svg.Children.Length);
        foreach (var shape in shapes)
        {
            Assert.Null(shape.GetAttribute("fill"));
            Assert.Null(shape.GetAttribute("stroke"));
        }
    }

    [Fact]
    public void Size_is_an_explicit_width_and_height_and_never_a_type_size()
    {
        using var context = new BunitContext();

        var byDefault = context.Render<FlagIcon>().Find("svg");
        Assert.Equal("16", byDefault.GetAttribute("width"));
        Assert.Equal("16", byDefault.GetAttribute("height"));

        var small = context.Render<FlagIcon>(parameters => parameters.Add(icon => icon.Size, "12")).Find("svg");
        Assert.Equal("12", small.GetAttribute("width"));
        Assert.Equal("12", small.GetAttribute("height"));
        Assert.DoesNotContain("font-size", small.OuterHtml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_glyph_is_hidden_unless_the_caller_takes_the_name_on_itself()
    {
        using var context = new BunitContext();

        var hidden = context.Render<FlagIcon>().Find("svg");
        Assert.Equal("true", hidden.GetAttribute("aria-hidden"));
        Assert.Equal("false", hidden.GetAttribute("focusable"));

        var named = context
            .Render<FlagIcon>(parameters => parameters
                .AddUnmatched("aria-hidden", "false")
                .AddUnmatched("role", "img")
                .AddUnmatched("aria-label", "Flag"))
            .Find("svg");

        Assert.Equal("false", named.GetAttribute("aria-hidden"));
        Assert.Equal("img", named.GetAttribute("role"));
        Assert.Equal("Flag", named.GetAttribute("aria-label"));
    }

    [Fact]
    public void The_class_and_the_test_id_are_hooks_a_host_can_reach()
    {
        using var context = new BunitContext();

        var svg = context
            .Render<FlagIcon>(parameters => parameters
                .Add(icon => icon.CssClass, "domain-gate-chip__glyph")
                .Add(icon => icon.TestId, "flag-icon-12"))
            .Find("svg");

        Assert.Contains("flag-icon", svg.ClassList);
        Assert.Contains("domain-gate-chip__glyph", svg.ClassList);
        Assert.Equal("flag-icon-12", svg.GetAttribute("data-testid"));
    }
}
