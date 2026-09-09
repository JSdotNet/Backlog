using System.Text.RegularExpressions;

namespace Backlog.ArchitectureTests;

/// <summary>
/// The list a select opens is painted by the platform, so it has to be told the
/// theme's colours — and told them once, for every select.
///
/// <para>A native <c>&lt;select&gt;</c>'s popup is drawn by the browser and the
/// operating system rather than by the page, and it defaults to black on white. In
/// a product with no light theme that is the whole defect: the control reads
/// correctly and the list it opens does not. The library knew this — the comment
/// over <c>select.field__input</c> says the platform "defaults to black on white
/// and so has to be told the theme's colours" — but it said so three times, once
/// per host class: <c>.status-editor__select option</c>,
/// <c>.metadata-editor__select option</c> and <c>select.field__input option</c>.</para>
///
/// <para>A rule keyed to the class a select happens to wear only covers the selects
/// wearing it. <c>SelectField</c> lets a host replace that class outright through
/// <c>SelectCssClass</c>, and four selects did: the knowledge source picker, which
/// is what the defect was reported against, and three in the settings screen. Both
/// host classes dress the control in the host's own stylesheet and neither dresses
/// the list, because <c>setting__input</c> was written for a <c>TextField</c>'s
/// input — where an option rule means nothing — and pointed at a select later.
/// <c>BadgeSelect</c> derives its class from a parameter, so it was one argument
/// away from the same hole.</para>
///
/// <para>Asserted against the stylesheet for the reason
/// <c>PlainButtonStylingTests</c> and <c>ToggleButtonPressedStylingTests</c> both
/// give: the defect is entirely in what the stylesheet does with markup that is
/// already correct, and bUnit brings no layout engine. <c>ClassHookTests</c> and
/// <c>SelectFieldShapeTests</c> assert which class each select wears and stayed
/// green throughout — <c>SelectFieldShapeTests</c> even asserts that
/// <c>SelectCssClass</c> replaces <c>field__input</c>, which is the exact move that
/// drops the coverage. Neither could catch this, because the class is right.</para>
/// </summary>
public class SelectOptionStylingTests
{
    /// <summary>
    /// Every select's list is dressed, whatever the select is wearing.
    ///
    /// <para>An element selector cannot be escaped by a host renaming a class,
    /// which is what makes this one rule the fix rather than a fourth copy of the
    /// old one.</para>
    /// </summary>
    [Fact]
    public void The_list_a_select_opens_is_dressed_for_every_select()
    {
        var rule = Rules().FirstOrDefault(r => r.Selector == "select option");

        Assert.True(
            rule is not null,
            "components.css has no `select option` rule. The list a select opens is then painted by the "
            + "platform, which defaults to black on white — so any select wearing a class no other rule "
            + "names opens an unreadable list in a product that has no light theme.");

        var background = Declaration(rule!.Body, "background");
        var color = Declaration(rule.Body, "color");

        Assert.True(
            background is not null && IsAuthored(background),
            $"`select option` declares its background as {background ?? "nothing"}, which is not a value "
            + "the design system owns.");

        Assert.True(
            color is not null && IsAuthored(color),
            $"`select option` declares its colour as {color ?? "nothing"}, which is not a value the "
            + "design system owns. A background alone leaves the label at the platform's near-black.");
    }

    /// <summary>
    /// And dressed once, by the element rather than by the class.
    ///
    /// <para>This is the defect itself, stated as a rule. Three class-scoped copies
    /// covered the selects that wore those three classes and no others, and nothing
    /// anywhere said that a fourth class was uncovered — the gap was invisible
    /// until someone opened the list. Colour only: a class-scoped rule that gives
    /// one select's list a different font or padding is a choice about that select,
    /// but a class-scoped rule about its <em>colours</em> is a claim that the
    /// platform's default is acceptable everywhere the class is absent.</para>
    /// </summary>
    [Fact]
    public void No_rule_ties_a_list_s_colours_to_the_class_its_select_happens_to_wear()
    {
        var keyed = Rules()
            .Where(rule => Regex.IsMatch(rule.Selector, @"(^|\s|,)[^,]*\.[^,]*\boption\b"))
            .Where(rule => Declaration(rule.Body, "background") is not null
                           || Declaration(rule.Body, "color") is not null)
            .Select(rule => rule.Selector)
            .ToList();

        Assert.True(
            keyed.Count == 0,
            "These rules give an option list its colours only when its select wears a particular class: "
            + string.Join("; ", keyed)
            + ". A select wearing any other class — `SelectCssClass` replaces the default outright — then "
            + "opens a list the platform paints black on white. State the colours on `select option` "
            + "instead, where no host class can escape them.");
    }

    private sealed record CssRule(string Selector, string Body);

    /// <summary>Every rule in the library stylesheet as a selector and a body, with
    /// comments stripped first so prose containing a brace cannot be read as one.
    /// Nested blocks — the media queries — are skipped rather than parsed: no
    /// option rule lives in one, and half-parsing a nested block would invent
    /// selectors that do not exist.</summary>
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

    /// <summary>A value the design system owns: a token reference, a mix of one, or
    /// the absence of a fill. Anything else is either a literal — which
    /// <c>DesignTokenTests</c> already forbids — or a system colour, which is the
    /// defect.</summary>
    private static bool IsAuthored(string value) =>
        value == "transparent" || value.Contains("var(--", StringComparison.Ordinal);

    /// <summary>The value one rule body gives a property, trimmed, or null when the
    /// rule does not state it. Anchored on the property name so
    /// <c>background-color</c> is not read as <c>background</c>, and
    /// <c>border-color</c> is not read as <c>color</c>.</summary>
    private static string? Declaration(string body, string property)
    {
        var match = Regex.Match(body, $@"(?:^|[{{;])\s*{Regex.Escape(property)}\s*:\s*(?<value>[^;}}]+)");

        return match.Success ? match.Groups["value"].Value.Trim() : null;
    }
}
