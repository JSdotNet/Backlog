using Microsoft.AspNetCore.Components;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The sun, the struck-through folder and the play mark — the three glyphs the
/// Tasks scope chips wear in place of their words.
///
/// <para>One file for the three because the contract is the one ChevronUpIconTests
/// spells out, and it is the contract rather than the drawing that callers depend on:
/// an outline on the iconography grid, ink inherited, size explicit, hidden from
/// assistive technology, and the class and test id reachable.</para>
/// </summary>
public sealed class ScopeGlyphIconTests
{
    [Fact]
    public void The_sun_keeps_the_icon_contract() =>
        AssertIconContract<SunIcon>("sun-icon");

    [Fact]
    public void The_struck_folder_keeps_the_icon_contract() =>
        AssertIconContract<FolderOffIcon>("folder-off-icon");

    [Fact]
    public void The_play_mark_keeps_the_icon_contract() =>
        AssertIconContract<PlayIcon>("play-icon");

    [Fact]
    public void The_sort_mark_keeps_the_icon_contract() =>
        AssertIconContract<ArrowUpDownIcon>("arrow-up-down-icon");

    private static void AssertIconContract<TIcon>(string ownClass) where TIcon : IComponent
    {
        using var context = new BunitContext();

        // A fragment rather than the typed builder, whose parameter selectors cannot
        // be written against a type parameter.
        var svg = context
            .Render(builder =>
            {
                builder.OpenComponent<TIcon>(0);
                builder.AddAttribute(1, "CssClass", "chip__icon");
                builder.AddAttribute(2, "TestId", "glyph");
                builder.CloseComponent();
            })
            .Find("svg");

        Assert.Equal("0 0 24 24", svg.GetAttribute("viewBox"));
        Assert.Equal("none", svg.GetAttribute("fill"));
        Assert.Equal("currentColor", svg.GetAttribute("stroke"));
        Assert.Equal("2", svg.GetAttribute("stroke-width"));
        Assert.Equal("16", svg.GetAttribute("width"));
        Assert.Equal("16", svg.GetAttribute("height"));
        Assert.Equal("true", svg.GetAttribute("aria-hidden"));
        Assert.Equal("false", svg.GetAttribute("focusable"));

        // Its own name first and the host's after it, as on the chevron.
        Assert.Contains(ownClass, svg.ClassList);
        Assert.Contains("chip__icon", svg.ClassList);
        Assert.Equal("glyph", svg.GetAttribute("data-testid"));

        var shapes = svg.QuerySelectorAll("path");
        Assert.NotEmpty(shapes);
        Assert.Equal(shapes.Length, svg.Children.Length);

        foreach (var shape in shapes)
        {
            Assert.Null(shape.GetAttribute("fill"));
            Assert.Null(shape.GetAttribute("stroke"));
        }
    }
}
