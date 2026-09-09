using System.Text.RegularExpressions;

namespace Backlog.ArchitectureTests;

/// <summary>
/// A button with no variant still has to be a button this product drew.
///
/// <para><c>AppButton</c> offers <c>ButtonVariant.None</c> so a host that already
/// dresses plain <c>.btn</c> is not restyled by a modifier it never asked for, and
/// <c>.btn</c> carried the metrics every variant is built on: padding, a weight, a
/// radius, a transition. What it never carried was paint. <c>background</c> and
/// <c>color</c> were both left to the variants, so a button that took none of them
/// fell through to the user agent's <c>buttonface</c> and <c>buttontext</c> — a
/// light grey slab under near-black ink, in a product that has no light theme. It
/// was reported from the settings screen, where seventeen buttons take that
/// variant, as buttons that were white and could not be read.</para>
///
/// <para>The same fall-through has now been fixed twice on single variants —
/// <c>.btn--icon</c>, whose comment names <c>buttonface</c> outright, and
/// <c>.btn--toggle</c>, which <see cref="ToggleButtonPressedStylingTests"/> guards.
/// Both were the stem's omission surfacing on whichever variant declared least. So
/// this asserts it of the stem, where the other two inherit it from.</para>
///
/// <para>Asserted against the stylesheet for the reason
/// <c>ToggleButtonPressedStylingTests</c>, <c>IndeterminateCheckboxTests</c> and
/// <c>MarkdownHeadingWeightTests</c> all give: the defect lives entirely in what the
/// stylesheet does with markup that was already correct, and bUnit brings no layout
/// engine. <c>AppButtonTests</c> asserts that <c>ButtonVariant.None</c> emits
/// exactly <c>btn</c> and stayed green throughout — which is why it could not catch
/// this. There is nothing wrong with the class.</para>
/// </summary>
public class PlainButtonStylingTests
{
    /// <summary>
    /// The stem paints its own fill rather than letting the platform pick one.
    ///
    /// <para><c>transparent</c> is a fill: it says the surface behind the button is
    /// the one the reader should see, which is what every variant carrying no colour
    /// of its own — <c>--secondary</c>, <c>--ghost</c>, <c>--icon</c>,
    /// <c>--toggle</c> — already states. A system colour keyword is not, and it is
    /// also one end of a transition the browser cannot interpolate.</para>
    /// </summary>
    [Fact]
    public void A_button_with_no_variant_declares_its_own_fill()
    {
        var background = Declaration(BaseRule(), "background");

        Assert.NotNull(background);
        Assert.True(
            IsAuthored(background),
            $"`.btn` declares its background as {background}, which is neither a design token nor "
            + "transparent. A button that takes no variant modifier then draws the user agent's "
            + "`buttonface` — a light grey slab in a product with no light theme.");
    }

    /// <summary>
    /// And its own ink, for the reason the toggle needed both.
    ///
    /// <para>A stem that declared only a transparent background would trade a wrong
    /// fill for an unreadable label: the text falls through to <c>buttontext</c>,
    /// which is near-black on every surface this library is served onto. Taking away
    /// the platform's slab without taking away the ink it was legible against is the
    /// same defect arrived at from the other side.</para>
    ///
    /// <para><c>inherit</c> is the authored answer here rather than a token, because
    /// the stem is what a host wraps around its own text: <c>.btn--icon</c> already
    /// says <c>inherit</c> for that reason. A variant that wants a colour of its own
    /// states one, and every one that does is declared later in the file.</para>
    /// </summary>
    [Fact]
    public void A_button_with_no_variant_declares_its_own_ink()
    {
        var color = Declaration(BaseRule(), "color");

        Assert.NotNull(color);
        Assert.True(
            IsAuthored(color) || color == "inherit",
            $"`.btn` declares its colour as {color}, which is neither a design token nor inherited from "
            + "the surface it sits on. A button that takes no variant modifier then draws the user "
            + "agent's `buttontext`, which is near-black on every surface this library is served onto.");
    }

    /// <summary>
    /// The stem stays unbordered, because taking a boundary is not the stem's to give.
    ///
    /// <para><c>.btn</c> opens a <c>solid transparent</c> border so a variant that
    /// wants an edge can colour one in without the button changing size. Colouring it
    /// in on the stem instead would put a grey edge around every variant that relies
    /// on that transparency — <c>--primary</c> and <c>--ghost</c> both declare no
    /// <c>border-color</c> at all — which is a restyling of the whole product rather
    /// than a fix to the one button that had no paint. A host rendering a bare
    /// <c>.btn</c> gives it its own boundary; <c>.setting__actions .btn</c> and
    /// <c>.repo-card__header .btn</c> are the two that do.</para>
    /// </summary>
    [Fact]
    public void A_button_with_no_variant_leaves_its_boundary_to_the_variant_or_the_host()
    {
        var borderColor = Declaration(BaseRule(), "border-color");

        Assert.True(
            borderColor is null,
            $"`.btn` declares border-color: {borderColor}. Every variant that wants no edge — `--primary` "
            + "and `--ghost` state no border-color of their own — would gain one it never asked for.");
    }

    /// <summary>The stem rule, up to its closing brace. Asserted rather than returned
    /// empty for the reason <c>ToggleButtonPressedStylingTests</c> gives: a selector
    /// that does not exist has to fail as a rule this test cannot find, rather than
    /// passing every later assertion against an empty string.</summary>
    private static string BaseRule()
    {
        var css = File.ReadAllText(DesignPalette.LibraryStylesheet);
        var opening = Regex.Match(css, @"^\.btn\s*\{", RegexOptions.Multiline);

        Assert.True(
            opening.Success,
            "components.css has no `.btn` rule. Every button in the product is built on that stem, so "
            + "each one then draws whatever its variant happens to state and the platform fills the rest.");

        var end = css.IndexOf('}', opening.Index);

        Assert.True(end > 0, "The `.btn` rule in components.css is never closed.");

        return css[opening.Index..(end + 1)];
    }

    /// <summary>A value the design system owns: a token reference, a mix of one, or
    /// the absence of a fill. Anything else is either a literal — which
    /// <c>DesignTokenTests</c> already forbids — or a system colour, which is the
    /// defect.</summary>
    private static bool IsAuthored(string value) =>
        value == "transparent" || value.Contains("var(--", StringComparison.Ordinal);

    /// <summary>The value one rule body gives a property, trimmed, or null when the
    /// rule does not state it. Anchored on the property name so
    /// <c>background-color</c> in the transition list is not read as
    /// <c>background</c>, and <c>border-color</c> is not read as <c>border</c>.</summary>
    private static string? Declaration(string rule, string property)
    {
        var match = Regex.Match(rule, $@"(?:^|[{{;])\s*{Regex.Escape(property)}\s*:\s*(?<value>[^;}}]+)");

        return match.Success ? match.Groups["value"].Value.Trim() : null;
    }
}
