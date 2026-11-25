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
