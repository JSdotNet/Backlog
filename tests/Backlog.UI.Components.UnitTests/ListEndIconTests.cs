using AngleSharp.Dom;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The list-end glyph, held to the contract ChevronUpIconTests sets out for every
/// glyph the library draws itself: the iconography chapter's grid, ink inherited,
/// size explicit, hidden from assistive technology unless a caller names it —
/// and, since it is Lucide's own <c>list-end</c>, the five strokes that drawing has.
/// </summary>
public sealed class ListEndIconTests
{
    [Fact]
    public void The_glyph_is_lucides_list_end_as_an_outline_on_the_icon_grid()
    {
        using var context = new BunitContext();

        var svg = context.Render<ListEndIcon>().Find("svg");

        Assert.Equal("0 0 24 24", svg.GetAttribute("viewBox"));
        Assert.Equal("none", svg.GetAttribute("fill"));
        Assert.Equal("currentColor", svg.GetAttribute("stroke"));
        Assert.Equal("2", svg.GetAttribute("stroke-width"));
        Assert.Equal("round", svg.GetAttribute("stroke-linecap"));
        Assert.Equal("round", svg.GetAttribute("stroke-linejoin"));

        // Three lines, the return arrow's head, and its curl: paths only, none
        // painting itself.
        var shapes = svg.QuerySelectorAll("path");
        Assert.Equal(5, shapes.Length);
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

        var byDefault = context.Render<ListEndIcon>().Find("svg");
        Assert.Equal("16", byDefault.GetAttribute("width"));
        Assert.Equal("16", byDefault.GetAttribute("height"));

        var medium = context
            .Render<ListEndIcon>(parameters => parameters.Add(icon => icon.Size, "20"))
            .Find("svg");

        Assert.Equal("20", medium.GetAttribute("width"));
        Assert.Equal("20", medium.GetAttribute("height"));
        Assert.DoesNotContain("font-size", medium.OuterHtml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_glyph_is_hidden_unless_the_caller_takes_the_name_on_itself()
    {
        using var context = new BunitContext();

        // The control holding the glyph carries the name ("Queue a session ..."),
        // so the glyph itself stays out of the accessibility tree and tab order.
        var hidden = context.Render<ListEndIcon>().Find("svg");
        Assert.Equal("true", hidden.GetAttribute("aria-hidden"));
        Assert.Equal("false", hidden.GetAttribute("focusable"));

        var named = context
            .Render<ListEndIcon>(parameters => parameters
                .AddUnmatched("aria-hidden", "false")
                .AddUnmatched("role", "img")
                .AddUnmatched("aria-label", "Queued"))
            .Find("svg");

        Assert.Equal("false", named.GetAttribute("aria-hidden"));
        Assert.Equal("img", named.GetAttribute("role"));
        Assert.Equal("Queued", named.GetAttribute("aria-label"));
    }

    [Fact]
    public void The_class_and_the_test_id_are_hooks_a_host_can_reach()
    {
        using var context = new BunitContext();

        var svg = context
            .Render<ListEndIcon>(parameters => parameters
                .Add(icon => icon.CssClass, "entry-queue__glyph")
                .Add(icon => icon.TestId, "entry-queue-glyph"))
            .Find("svg");

        Assert.Contains("list-end-icon", svg.ClassList);
        Assert.Contains("entry-queue__glyph", svg.ClassList);
        Assert.Equal("entry-queue-glyph", svg.GetAttribute("data-testid"));
    }
}
