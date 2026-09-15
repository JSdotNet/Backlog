using System.Text.RegularExpressions;

namespace Backlog.ArchitectureTests;

/// <summary>
/// The chrome a native control draws inside itself follows the control's colour
/// scheme, not the page's palette — so the controls have to be told the scheme.
///
/// <para>A <c>&lt;input type="time"&gt;</c> paints a clock at its end, a date field
/// a calendar, and each opens a picker the platform draws. None of that is styled
/// by <c>.field__input</c>: the glyph and the popup are rendered by the browser for
/// whatever <c>color-scheme</c> the element has, and with none declared that is
/// <c>normal</c> — the light scheme. The working-hours fields in the settings
/// screen therefore showed a near-black clock on a near-black field, and every
/// date field in the tasks pane and the roadmap editors the same calendar. The
/// field read correctly; the control drawn inside it did not, which is the same
/// defect <see cref="SelectOptionStylingTests"/> found in the list a select opens.</para>
///
/// <para>The fix is the same shape too: state <c>color-scheme: dark</c> on the
/// elements, where no host class can escape it. <c>TextField</c> lets a host replace
/// <c>field__input</c> outright through <c>InputCssClass</c>, and the settings screen
/// does exactly that — a class-scoped rule would have missed the field the defect
/// was reported against.</para>
///
/// <para>On the elements and not on <c>:root</c>, which is the obvious place for a
/// product with one theme. <c>ArchifyArtifactFrameTests</c> is why not: the artifact
/// frame is a transparent iframe whose document is pinned to <c>color-scheme:
/// normal</c>, and a browser paints an opaque canvas behind any frame whose scheme
/// differs from its embedder's. A root-level <c>dark</c> would put a slab behind
/// every diagram to fix a clock.</para>
///
/// <para>Asserted against the stylesheet because the defect is entirely in what the
/// stylesheet does with markup that is already correct; bUnit renders the
/// <c>type="time"</c> attribute faithfully and brings no layout engine to draw the
/// glyph.</para>
/// </summary>
public class NativeControlSchemeTests
{
    private static readonly string[] Controls = ["input", "select", "textarea"];

    /// <summary>
    /// Every native control is told the theme's scheme, by element.
    /// </summary>
    [Fact]
    public void Every_native_control_is_drawn_in_the_dark_scheme()
    {
        var rule = Rules().FirstOrDefault(rule =>
            Controls.All(control => Selectors(rule.Selector).Contains(control)));

        Assert.True(
            rule is not null,
            "components.css has no rule whose selector names `input`, `select` and `textarea` as bare "
            + "elements. The picker glyph a date or time input draws, and the popup it opens, are then "
            + "painted by the platform for the light scheme — a near-black clock on a near-black field.");

        Assert.True(
            Declaration(rule!.Body, "color-scheme") == "dark",
            $"The `{rule.Selector}` rule declares color-scheme as "
            + $"{Declaration(rule.Body, "color-scheme") ?? "nothing"}. It has to be `dark`: that is the "
            + "one theme the product has, and the platform draws the control's own chrome in whichever "
            + "scheme the element declares.");
    }

    /// <summary>
    /// And nowhere above them.
    ///
    /// <para>A <c>color-scheme</c> on the document root would reach the controls
    /// too, and also reach every iframe — including the Archify artifact frame,
    /// which is transparent only while its scheme matches the page's. The frame
    /// pins itself to <c>normal</c> for that reason, so the page has to stay
    /// undeclared.</para>
    /// </summary>
    [Fact]
    public void The_document_root_declares_no_scheme_of_its_own()
    {
        var declaring = Rules()
            .Where(rule => Selectors(rule.Selector).Any(selector => selector is ":root" or "html" or "body"))
            .Where(rule => Declaration(rule.Body, "color-scheme") is not null)
            .Select(rule => rule.Selector)
            .ToList();

        Assert.True(
            declaring.Count == 0,
            "These rules declare color-scheme on the document root: " + string.Join("; ", declaring)
            + ". The Archify artifact frame is transparent only while its `normal` scheme matches its "
            + "embedder's, so the scheme is stated on the controls that need it and not on the page.");
    }

    private sealed record CssRule(string Selector, string Body);

    /// <summary>Every rule in the library stylesheet as a selector and a body, with
    /// comments stripped first so prose containing a brace cannot be read as one.
    /// Nested blocks — the media queries — are skipped rather than parsed, as
    /// <see cref="SelectOptionStylingTests"/> skips them.</summary>
    private static IEnumerable<CssRule> Rules()
    {
        var css = Regex.Replace(
            File.ReadAllText(DesignPalette.LibraryStylesheet), @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

        return Regex.Matches(css, @"(?<selector>[^{}]+)\{(?<body>[^{}]*)\}")
            .Select(match => new CssRule(
                Regex.Replace(match.Groups["selector"].Value, @"\s+", " ").Trim(),
                match.Groups["body"].Value))
            .Where(rule => rule.Selector.Length > 0 && !rule.Selector.StartsWith('@'));
    }

    /// <summary>The selectors a rule lists, one per comma, trimmed.</summary>
    private static string[] Selectors(string selector) =>
        selector.Split(',').Select(part => part.Trim()).ToArray();

    /// <summary>The value one rule body gives a property, trimmed, or null when the
    /// rule does not state it.</summary>
    private static string? Declaration(string body, string property)
    {
        var match = Regex.Match(body, $@"(?:^|[{{;])\s*{Regex.Escape(property)}\s*:\s*(?<value>[^;}}]+)");

        return match.Success ? match.Groups["value"].Value.Trim() : null;
    }
}
