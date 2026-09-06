using AngleSharp.Dom;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The chevron glyph, and the four properties it exists to hold.
///
/// <para>Four rather than one drawing test, because none of them is about the
/// shape: the grid it is drawn on, where its ink comes from, where its size comes
/// from, and whether assistive technology hears it are the contract every caller
/// depends on, and every one of them is a rule the iconography chapter states
/// rather than a preference this file settled on.</para>
///
/// <para>It is an outline on the iconography chapter's own grid — 24x24, stroked
/// at 2, round caps and joins, filled nowhere — because that chapter names Lucide
/// as the library and allows a drawing of our own only on those terms. It takes
/// its ink from whatever it is sitting in, and its size from an explicit width and
/// height, which are the two rules the chapter states as prohibitions: never a
/// hard-coded stroke, never a font-size scale. It is hidden from assistive
/// technology unless a caller says otherwise, because the control that holds a
/// glyph is where the name belongs. And it ships no state parameter: which state a
/// direction stands for is the host's reading — the desktop rail turns the drawing
/// over for pinned in its own stylesheet — so nothing here knows there is a pin at
/// all.</para>
/// </summary>
public sealed class ChevronUpIconTests
{
    [Fact]
    public void The_glyph_is_an_outline_on_the_icon_grid()
    {
        using var context = new BunitContext();

        var svg = context.Render<ChevronUpIcon>().Find("svg");

        Assert.Equal("0 0 24 24", svg.GetAttribute("viewBox"));
        Assert.Equal("none", svg.GetAttribute("fill"));
        Assert.Equal("currentColor", svg.GetAttribute("stroke"));
        Assert.Equal("2", svg.GetAttribute("stroke-width"));
        Assert.Equal("round", svg.GetAttribute("stroke-linecap"));
        Assert.Equal("round", svg.GetAttribute("stroke-linejoin"));

        // Paths only, and none of them painting itself: a shape that carried its own
        // fill or stroke would be a colour this component chose rather than one it
        // inherited, which is the thing the chapter rules out.
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

        // Sixteen unasked-for, which is the scale's inline-with-text step and the
        // size the library's other drawn glyphs default to.
        var byDefault = context.Render<ChevronUpIcon>().Find("svg");
        Assert.Equal("16", byDefault.GetAttribute("width"));
        Assert.Equal("16", byDefault.GetAttribute("height"));

        var small = context
            .Render<ChevronUpIcon>(parameters => parameters.Add(icon => icon.Size, "12"))
            .Find("svg");

        Assert.Equal("12", small.GetAttribute("width"));
        Assert.Equal("12", small.GetAttribute("height"));

        // Nothing about the glyph is left for type to decide, at either size.
        Assert.DoesNotContain("font-size", small.OuterHtml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_glyph_is_hidden_unless_the_caller_takes_the_name_on_itself()
    {
        using var context = new BunitContext();

        var hidden = context.Render<ChevronUpIcon>().Find("svg");

        // The pair, not just the first: aria-hidden keeps it out of the accessibility
        // tree and focusable="false" keeps it out of the tab order, which older
        // engines would otherwise hand an SVG of its own.
        Assert.Equal("true", hidden.GetAttribute("aria-hidden"));
        Assert.Equal("false", hidden.GetAttribute("focusable"));

        // Written before the splat, so a caller that has taken the word off the
        // screen can name the glyph instead of being stuck with a decorative one.
        var named = context
            .Render<ChevronUpIcon>(parameters => parameters
                .AddUnmatched("aria-hidden", "false")
                .AddUnmatched("role", "img")
                .AddUnmatched("aria-label", "Not pinned"))
            .Find("svg");

        Assert.Equal("false", named.GetAttribute("aria-hidden"));
        Assert.Equal("img", named.GetAttribute("role"));
        Assert.Equal("Not pinned", named.GetAttribute("aria-label"));
    }

    [Fact]
    public void The_class_and_the_test_id_are_hooks_a_host_can_reach()
    {
        using var context = new BunitContext();

        var svg = context
            .Render<ChevronUpIcon>(parameters => parameters
                .Add(icon => icon.CssClass, "header-group__pin-glyph")
                .Add(icon => icon.TestId, "inbox-pane-pin-glyph"))
            .Find("svg");

        // Its own name first and the host's after it, so a host styling the glyph
        // never has to take the library's class away to add one of its own. The
        // host's is what a rule keys on: the section rail lifts and turns this glyph
        // through `header-group__pin-glyph`, which says which control the rule is
        // about, where `chevron-up-icon` would take hold of any chevron put there.
        Assert.Contains("chevron-up-icon", svg.ClassList);
        Assert.Contains("header-group__pin-glyph", svg.ClassList);
        Assert.Equal("inbox-pane-pin-glyph", svg.GetAttribute("data-testid"));
    }
}
