using System.Text.RegularExpressions;

namespace Backlog.ArchitectureTests;

/// <summary>
/// A one-of-many strip has to paint the option that is selected, and only that one.
///
/// <para><c>ToggleButton</c> carries its state in two places for the reason its own
/// comment gives: <c>aria-pressed</c>, which assistive technology reads, and
/// <c>btn--pressed</c> on the root, which the stylesheet draws. The markup half was
/// always right — press a second option in a <c>ButtonGroup</c> and the class and
/// the attribute both move. The stylesheet half never drew the class at all:
/// <c>btn--pressed</c> had no rule anywhere, the whole pressed appearance hung off
/// <c>[aria-pressed="true"]</c>, and <c>.btn--toggle</c> declared no background of
/// its own, so an unpressed toggle fell through to the user agent’s <c>buttonface</c>.
/// Animating the <c>background</c> shorthand between a system colour keyword and a
/// <c>color-mix()</c> result is an interpolation the browser never settles, so the
/// fill stayed on whichever option was selected first while every other cue moved.
/// The strip showed one answer and announced another.</para>
///
/// <para>Asserted against the stylesheet for the reason
/// <c>IndeterminateCheckboxTests</c>, <c>FollowableBadgeTests</c> and
/// <c>MarkdownHeadingWeightTests</c> all give for this shape of test: the defect
/// lives entirely in what the stylesheet does with markup that is already correct,
/// and bUnit brings no layout engine. <c>ToggleButtonTests</c> asserts both
/// carriers and <c>SessionsPaneTests</c> asserts <c>aria-pressed</c> alone, and both
/// stayed green throughout — which is exactly why neither could catch this. The
/// strips the defect was reported against have no class coverage at all.</para>
///
/// <para><c>.design/interaction-guidelines.md#focus-and-selection</c> requires the
/// selected state to be visually distinct, so a fill stuck on the wrong member is a
/// rule broken rather than a preference missed.</para>
/// </summary>
public class ToggleButtonPressedStylingTests
{
    /// <summary>
    /// The unpressed toggle paints itself rather than letting the platform do it.
    ///
    /// <para>Both halves, because either alone is a defect. Without a background the
    /// user agent draws <c>buttonface</c> — a light grey slab in a product that has
    /// no light theme, and one end of a transition the browser cannot interpolate.
    /// Without a colour the label inherits <c>buttontext</c>, which is near-black on
    /// every surface this library is served onto, so a toggle that declared only a
    /// transparent background would trade a wrong fill for an unreadable label.
    /// Every other variant that carries no fill of its own — <c>--secondary</c>,
    /// <c>--ghost</c>, <c>--icon</c> — states both already; <c>--toggle</c> was the
    /// one that stated neither.</para>
    /// </summary>
    [Fact]
    public void An_unpressed_toggle_declares_the_background_the_platform_would_otherwise_choose()
    {
        var toggle = Rule(@"^\.btn--toggle\s*\{");

        var background = Declaration(toggle, "background");

        Assert.NotNull(background);
        Assert.True(
            IsAuthored(background),
            $"The unpressed toggle declares its background as {background}, which is neither a design token "
            + "nor transparent. A system colour keyword is not a value the pressed fill can be interpolated "
            + "to or from, and it is not a colour this product chose.");

        Assert.NotNull(Declaration(toggle, "color"));
    }

    /// <summary>
    /// The pressed fill hangs off the class the component emits for it.
    ///
    /// <para><c>PressedCssClass</c> is documented as the hook a host replaces, so a
    /// host that supplies a pressed class of its own has said the appearance the
    /// library draws is not wanted. Keyed off <c>aria-pressed</c> the library
    /// painted over that host anyway, and its own <c>btn--pressed</c> was a class
    /// nothing drew.</para>
    /// </summary>
    [Fact]
    public void The_pressed_fill_is_drawn_on_the_class_the_component_emits_for_it()
    {
        var pressed = Rule(@"^\.btn--toggle\.btn--pressed\s*\{");

        var background = Declaration(pressed, "background");

        Assert.NotNull(background);
        Assert.Contains("var(--color-primary)", background, StringComparison.Ordinal);
        Assert.NotNull(Declaration(pressed, "border-color"));

        // Unanchored: the selector this guards against is just as wrong sharing a
        // line with the class it was moved onto as it is opening one of its own.
        Assert.DoesNotMatch(new Regex(@"\.btn--toggle\[aria-pressed"), Stylesheet());
    }

    /// <summary>
    /// A button animates the colour of its background and nothing else about it.
    ///
    /// <para>The shorthand names eight longhands, most of which are images,
    /// positions and keywords. Asking for all of them is how a fill that should have
    /// crossed from one colour to another was handed a pair the browser had no way
    /// to walk between, and left mid-interpolation on both members of the
    /// strip.</para>
    /// </summary>
    [Fact]
    public void A_button_animates_only_the_colour_of_its_background()
    {
        var animated = AnimatedProperties(Rule(@"^\.btn\s*\{"));

        Assert.Contains("background-color", animated);
        Assert.DoesNotContain("background", animated);
    }

    /// <summary>The properties a transition names, the first word of each entry —
    /// the timings and easings after it are not what is being animated.</summary>
    private static IReadOnlyCollection<string> AnimatedProperties(string rule)
    {
        var transition = Declaration(rule, "transition");

        Assert.NotNull(transition);

        return Regex.Replace(transition, @"\([^)]*\)", string.Empty)
            .Split(',')
            .Select(entry => entry.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>A value the design system owns: a token reference, a mix of one, or
    /// the absence of a fill. Anything else is either a literal — which
    /// <c>DesignTokenTests</c> already forbids — or a system colour, which is the
    /// defect.</summary>
    private static bool IsAuthored(string value) =>
        value == "transparent" || value.Contains("var(--", StringComparison.Ordinal);

    /// <summary>The value one rule body gives a property, trimmed, or null when the
    /// rule does not state it. Anchored on the property name so
    /// <c>background-color</c> is not read as <c>background</c>.</summary>
    private static string? Declaration(string rule, string property)
    {
        var match = Regex.Match(rule, $@"(?:^|[{{;])\s*{Regex.Escape(property)}\s*:\s*(?<value>[^;}}]+)");

        return match.Success ? match.Groups["value"].Value.Trim() : null;
    }

    /// <summary>The rule opening with <paramref name="pattern"/>, up to its closing
    /// brace. Asserted rather than returned empty, for the reason
    /// <c>IndeterminateCheckboxTests</c> gives: a selector that does not exist has
    /// to fail as a rule this test cannot find, rather than passing every later
    /// assertion against an empty string.</summary>
    private static string Rule(string pattern)
    {
        var css = Stylesheet();
        var opening = Regex.Match(css, pattern, RegexOptions.Multiline);

        Assert.True(
            opening.Success,
            $"components.css has no rule matching {pattern}. The pressed option in a one-of-many strip then "
            + "draws from somewhere other than the class ToggleButton emits, and the fill can sit on a "
            + "member that is no longer selected.");

        var end = css.IndexOf('}', opening.Index);

        Assert.True(end > 0, $"The rule matching {pattern} in components.css is never closed.");

        return css[opening.Index..(end + 1)];
    }

    private static string Stylesheet() =>
        File.ReadAllText(Path.Combine(
            Repository.Root.FullName, "src", "Core", "Backlog.UI.Components", "wwwroot", "components.css"));
}
