using System.Text.RegularExpressions;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// What the tile map's pins and pills owe the keyboard and the pointer, asserted
/// against <c>components.css</c>: bUnit has no layout engine, and the whole of
/// each promise is in the cascade.
/// </summary>
public sealed class TileMapStylesheetTests
{
    /// <summary>
    /// The focus ring is two-tone box-shadow, so it reads against any tile — and
    /// Windows High Contrast drops box-shadow. A transparent outline is invisible
    /// beside the ring and painted in forced colours, so focus survives there
    /// (<c>accessibility.md#focus-visibility</c>).
    /// </summary>
    [Theory]
    [InlineData(".tile-map__pin:focus-visible")]
    [InlineData(".tile-map__pill:focus-visible")]
    [InlineData(".tile-map__pin[aria-pressed=\"true\"]:focus-visible")]
    [InlineData(".tile-map__pill[aria-pressed=\"true\"]:focus-visible")]
    public void A_focused_mark_keeps_an_outline_for_forced_colours(string selector)
    {
        var bodies = BodiesOf(selector).ToList();

        Assert.NotEmpty(bodies);
        Assert.DoesNotContain(bodies, body => Regex.IsMatch(body, @"outline:\s*none"));
        Assert.Contains(bodies, body => Regex.IsMatch(body, @"outline:\s*var\(--border-width-2\)\s+solid\s+transparent"));
        Assert.Contains(bodies, body => body.Contains("var(--color-border-focus)", StringComparison.Ordinal));
    }

    /// <summary>
    /// A pin is drawn at 1.75rem, and a press target owes 44 × 44 px
    /// (<c>accessibility.md#target-sizes-and-text</c>). A transparent layer half a
    /// rem past the drawing on every side takes the press out to 2.75rem — 44px —
    /// without growing what is drawn.
    /// </summary>
    [Theory]
    [InlineData(".tile-map__pin::before")]
    [InlineData(".tile-map__pill::before")]
    public void A_mark_is_pressed_over_44_pixels_while_drawn_smaller(string selector)
    {
        var body = Assert.Single(BodiesOf(selector));

        Assert.Matches(@"content:\s*""""", body);
        Assert.Matches(@"position:\s*absolute", body);
        Assert.Matches(@"inset:\s*-0\.5rem", body);

        var drawn = Assert.Single(BodiesOf(".tile-map__pin"));
        Assert.Matches(@"min-width:\s*1\.75rem", drawn);
        Assert.Matches(@"height:\s*1\.75rem", drawn);
    }

    /// <summary>The legend and the attribution share the map's foot and wrap
    /// rather than overlap, so the attribution stays visible on a narrow map
    /// (ADR 0061 §6).</summary>
    [Fact]
    public void The_legend_and_the_attribution_wrap_rather_than_overlap()
    {
        var foot = Assert.Single(BodiesOf(".tile-map__foot"));

        Assert.Matches(@"display:\s*flex", foot);
        Assert.Matches(@"flex-wrap:\s*wrap", foot);

        Assert.DoesNotContain(BodiesOf(".tile-map__legend"), body => Regex.IsMatch(body, @"position:\s*absolute"));
        Assert.DoesNotContain(BodiesOf(".tile-map__attribution"), body => Regex.IsMatch(body, @"position:\s*absolute"));
    }

    /// <summary>The layers a pin can sit on, lowest first: an unnumbered (past) pin, the
    /// numbered ones — each at the rank TileMap writes on it, the lowest number highest — the
    /// selected one, and the one under the pointer or the keyboard. A past event at the same
    /// place never hides an upcoming one, and pin 2 never hides pin 1.</summary>
    [Fact]
    public void Numbered_marks_draw_by_rank_above_unnumbered_ones_and_the_selected_above_all()
    {
        var numbered = Assert.Single(BodiesOf(".tile-map__mark--numbered"));
        Assert.Matches(@"z-index:\s*var\(--tile-map-rank,\s*1\)", numbered);

        var selected = ZIndex(".tile-map__pin[aria-pressed=\"true\"]");
        var focused = ZIndex(".tile-map__pin:focus-visible");
        var hovered = ZIndex(".tile-map__pin:hover");

        Assert.True(selected > TileMapRanks.Highest);
        Assert.True(focused > selected);
        Assert.True(hovered > selected);
        Assert.DoesNotContain(BodiesOf(".tile-map__pin"), body => Regex.IsMatch(body, @"z-index"));
    }

    /// <summary>One rule per selector: the pill's base rule is the only one naming it alone.</summary>
    [Fact]
    public void The_pill_is_styled_in_one_rule_of_its_own()
    {
        Assert.Single(BodiesOf(".tile-map__pill"));
    }

    /// <summary>The controls and the foot share one grid over the map: the controls in a
    /// column of their own, the foot beside it. On a narrow map the legend wraps within its
    /// column and never covers Fit all.</summary>
    [Fact]
    public void The_controls_and_the_foot_keep_to_their_own_columns()
    {
        var chrome = Assert.Single(BodiesOf(".tile-map__chrome"));
        Assert.Matches(@"display:\s*grid", chrome);
        Assert.Matches(@"pointer-events:\s*none", chrome);

        var controls = Assert.Single(BodiesOf(".tile-map__controls"));
        Assert.Matches(@"grid-column:\s*2", controls);
        Assert.DoesNotMatch(@"position:\s*absolute", controls);

        var foot = Assert.Single(BodiesOf(".tile-map__foot"));
        Assert.Matches(@"grid-column:\s*1", foot);
        Assert.DoesNotMatch(@"position:\s*absolute", foot);
    }

    /// <summary>A tooltip near an edge turns inwards rather than running off the map or
    /// under the controls.</summary>
    [Theory]
    [InlineData(".tile-map__pin--tip-end .tile-map__tooltip", @"right:\s*0")]
    [InlineData(".tile-map__pin--tip-start .tile-map__tooltip", @"left:\s*0")]
    [InlineData(".tile-map__pin--tip-below .tile-map__tooltip", @"top:\s*calc\(100%")]
    public void A_tooltip_near_an_edge_turns_inwards(string selector, string declaration)
    {
        Assert.Contains(BodiesOf(selector), body => Regex.IsMatch(body, declaration));
    }

    private static int ZIndex(string selector)
    {
        var values = BodiesOf(selector)
            .Select(body => Regex.Match(body, @"z-index:\s*(\d+)"))
            .Where(match => match.Success)
            .Select(match => int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
            .ToList();
        Assert.True(values.Count > 0, $"No z-index for {selector}.");
        return values.Max();
    }

    /// <summary>Every rule body whose selector list names <paramref name="selector"/>
    /// exactly, as one of its comma-separated selectors.</summary>
    private static IEnumerable<string> BodiesOf(string selector)
    {
        var css = Regex.Replace(
            File.ReadAllText(RepositoryRoot.File("src", "Core", "Backlog.UI.Components", "wwwroot", "components.css")),
            @"/\*.*?\*/",
            string.Empty,
            RegexOptions.Singleline);

        foreach (Match rule in Regex.Matches(css, @"([^{}]+)\{([^{}]*)\}"))
        {
            var selectors = rule.Groups[1].Value.Split(',').Select(part => Regex.Replace(part.Trim(), @"\s+", " "));
            if (selectors.Contains(selector, StringComparer.Ordinal)) yield return rule.Groups[2].Value;
        }
    }
}
