using System.Text.RegularExpressions;

namespace Backlog.ArchitectureTests;

/// <summary>
/// A roadmap day head is a real <c>&lt;button&gt;</c> that must not look like one (local
/// ADR 0019, §4; requirement "A day head does not look like a button").
///
/// <para>The head became a button so a click, Enter and Space press it and a screen
/// reader announces it. Everything the browser gives a button then has to be taken back
/// by the stylesheet: a <c>buttonface</c> fill, an outset border on all four sides,
/// padding, a radius and its own font. Left in place they draw a white slab per day in a
/// product with no light theme, which is what the owner reported. bUnit brings no layout
/// engine, so the reset is asserted here, against the stylesheet, as
/// <see cref="PlainButtonStylingTests"/> asserts the plain button's paint.</para>
///
/// <para>And it is asserted in order. The reset sits at the same weight as the day-off
/// and current-day rules, so it has to come before them: after them, its transparent
/// fill would win over a blocked date's weekend shade and over today's.</para>
/// </summary>
public class RoadmapDayHeadStylingTests
{
    private const string Toggle = ".roadmap-timeline__quarter--toggle";

    [Theory]
    [InlineData("appearance", "none")]
    [InlineData("padding", "0")]
    [InlineData("margin", "0")]
    [InlineData("border-top", "0")]
    [InlineData("border-right", "0")]
    [InlineData("border-bottom", "0")]
    [InlineData("border-radius", "0")]
    [InlineData("background", "transparent")]
    [InlineData("box-shadow", "none")]
    [InlineData("color", "inherit")]
    [InlineData("font", "inherit")]
    public void The_day_head_takes_back_what_the_browser_gives_a_button(string property, string value)
    {
        Assert.Equal(value, Declaration(Rule(Toggle), property));
    }

    /// <summary>The head's own left rule is the one border it keeps: the reset never
    /// names <c>border</c> or <c>border-left</c>.</summary>
    [Fact]
    public void The_day_head_keeps_the_heads_left_rule()
    {
        var rule = Rule(Toggle);

        Assert.Null(Declaration(rule, "border"));
        Assert.Null(Declaration(rule, "border-left"));
    }

    /// <summary>The reset comes before the rules that shade a head, so a blocked date
    /// wears a weekend head's fill and today its primary shade.</summary>
    [Theory]
    [InlineData(".roadmap-timeline__quarter--weekend")]
    [InlineData(".roadmap-timeline__quarter--current")]
    public void The_reset_comes_before_the_rules_that_shade_a_head(string shading)
    {
        Assert.True(
            Start(Toggle) < Start(shading),
            $"`{Toggle}` comes after `{shading}`. At the same weight its transparent fill then wins, and a "
            + "blocked date or today loses the shade a plain head of the same kind wears.");
    }

    /// <summary>Only keyboard focus sets a head apart at rest: it has a visible ring.</summary>
    [Fact]
    public void The_day_head_shows_a_focus_ring()
    {
        Assert.NotNull(Declaration(Rule(Toggle + ":focus-visible"), "outline"));
    }

    private static string Css => File.ReadAllText(DesignPalette.LibraryStylesheet);

    private static int Start(string selector)
    {
        var match = Regex.Match(Css, $@"^{Regex.Escape(selector)}\s*\{{", RegexOptions.Multiline);
        Assert.True(match.Success, $"components.css has no `{selector}` rule.");
        return match.Index;
    }

    private static string Rule(string selector)
    {
        var css = Css;
        var start = Start(selector);
        var end = css.IndexOf('}', start);

        Assert.True(end > 0, $"The `{selector}` rule in components.css is never closed.");

        return css[start..(end + 1)];
    }

    private static string? Declaration(string rule, string property)
    {
        var match = Regex.Match(rule, $@"(?:^|[{{;])\s*{Regex.Escape(property)}\s*:\s*(?<value>[^;}}]+)");

        return match.Success ? match.Groups["value"].Value.Trim() : null;
    }
}
