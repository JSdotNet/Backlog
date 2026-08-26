using System.Text.RegularExpressions;

namespace Backlog.ArchitectureTests;

/// <summary>
/// A badge that leads somewhere has to look unlike one that does not, at rest.
///
/// <para><c>Badge</c> renders an anchor given a URL, a button given a delegate,
/// and the span every badge has always been given neither — so the markup was
/// always right and the two still computed identically. Same ink, same edge, same
/// size; only <c>:hover</c> and <c>:focus-visible</c> told them apart. An alias a
/// host can place looked exactly like one it cannot, which is the difference the
/// tri-state exists to express, and neither a reader skimming nor anyone using a
/// keyboard had any way to know which of a row of badges would do something.</para>
///
/// <para>Asserted against the stylesheet rather than a render, for the reason
/// <c>MarkdownHeadingWeightTests</c> and <c>FileViewHeaderLayoutTests</c> both
/// give for the same shape of test: the defect is entirely in what the stylesheet
/// does with markup that is already correct, and bUnit brings no layout engine, so
/// a render test could only confirm the element — which it already does
/// elsewhere.</para>
/// </summary>
public class FollowableBadgeTests
{
    /// <summary>
    /// The rule, and the two halves of it that carry the cue.
    ///
    /// <para>Both are checked because either alone is the bug. Ink without an edge
    /// is a colour change a reader may read as emphasis; an edge without ink is
    /// invisible on the kinds that draw no border of their own, which is most of
    /// them.</para>
    ///
    /// <para>The fill is checked for its <em>absence</em>.
    /// <c>.design/color-scheme.md#badge-and-chip-tones</c> reserves a filled
    /// surface for something the product acts on, and following a link is the
    /// reader acting — so a background here would be this rule spending the one
    /// tone the palette had set aside.</para>
    /// </summary>
    [Fact]
    public void A_badge_that_leads_somewhere_is_drawn_unlike_one_that_does_not()
    {
        var plain = Rule(@"^\.badge\s*\{");
        var followable = RestStateRule();

        var plainInk = Declaration(plain, "color");
        var followableInk = Declaration(followable, "color");

        Assert.NotNull(plainInk);

        Assert.NotNull(followableInk);
        Assert.NotNull(Declaration(followable, "border-color"));

        Assert.True(
            followableInk != plainInk,
            "A followable badge takes the same ink as a badge nobody can follow, so the two compute "
            + $"identically at rest and only :hover separates them. Both are {plainInk}.");

        Assert.Null(Declaration(followable, "background"));
        Assert.Null(Declaration(followable, "background-color"));
    }

    /// <summary>The cue is spent out of the palette and not invented here. A raw
    /// literal would already fail <c>DesignTokenTests</c>; what this adds is that
    /// the tokens are the pair the library already uses for "this one, out of the
    /// row" — <c>.badge--gh-open</c> and <c>.badge--priority-high</c> are the same
    /// two — rather than a second idiom beside it.</summary>
    [Fact]
    public void The_cue_is_the_pair_this_library_already_spends_on_singling_one_out()
    {
        var followable = RestStateRule();

        Assert.Equal("var(--color-primary-light)", Declaration(followable, "color"));
        Assert.Equal("var(--color-primary-dark)", Declaration(followable, "border-color"));

        // The premise, and the reason those two tokens rather than any other pair:
        // if the existing idiom ever changes, this rule should change with it
        // instead of quietly becoming the odd one out.
        var open = Rule(@"^\.badge--gh-open\s*\{");

        Assert.Equal(Declaration(followable, "color"), Declaration(open, "color"));
        Assert.Equal(Declaration(followable, "border-color"), Declaration(open, "border-color"));
    }

    /// <summary>
    /// The families held out of it, and why holding them out is not a hole.
    ///
    /// <para>These selectors out-rank a single class, so without the exclusion the
    /// rule would flatten a scale that is already saying something. Two of the
    /// three are filled and set their own ink for legibility against a semantic
    /// surface; the third is already an anchor everywhere it appears, and its state
    /// colours are the cue.</para>
    ///
    /// <para>Pinned rather than left to the comment, because the failure is silent
    /// in both directions: drop an entry and a status badge's ink goes amber over
    /// the error red, add one and a followable badge in that family stops being
    /// distinguishable with nothing to say so.</para>
    /// </summary>
    [Fact]
    public void A_kind_that_already_paints_per_value_keeps_its_own_scale()
    {
        var selector = RestStateSelector();

        foreach (var kind in new[] { ".badge--status", ".badge--feature", ".badge--gh" })
        {
            Assert.Contains(kind, selector, StringComparison.Ordinal);
        }

        // And the kind the whole rule was written for is not among them: an alias
        // is the case that forced this, because it is the one badge whose
        // followability is a genuine per-value fact.
        Assert.DoesNotContain(".badge--alias", selector, StringComparison.Ordinal);
    }

    /// <summary>The rest-state rule: the one whose selector qualifies
    /// <c>.badge</c> with <c>a</c> and is not a state. Found by search rather than
    /// by an exact string, so reordering the exclusions or adding an element to it
    /// does not break the test that reads it — and asserted, so a rule that has
    /// gone missing fails here rather than further down against nothing.</summary>
    private static string RestStateRule()
    {
        var match = RestState();

        Assert.True(
            match.Success,
            "components.css has no rest-state rule for a followable badge: no rule qualifies .badge with "
            + "an element and declares a colour outside :hover, :focus-visible or :active. Without one an "
            + "anchor badge and a span badge compute identically and only the pointer tells them apart.");

        return match.Value;
    }

    private static string RestStateSelector() => RestState().Groups["selector"].Value;

    /// <summary>Element-qualified, unstated, and declaring a colour. The state
    /// rules are excluded by the selector pattern refusing a colon before the
    /// brace, which is also what keeps this from matching
    /// <c>a.badge:hover</c>.</summary>
    private static Match RestState() =>
        Regex.Match(
            Stylesheet(),
            @"^(?<selector>a\.badge(?::not\([^)]*\))?,\s*\r?\n\s*button\.badge(?::not\([^)]*\))?)\s*\{(?<body>[^}]*color[^}]*)\}",
            RegexOptions.Multiline);

    /// <summary>One declaration's value out of a rule body, trimmed, or null when
    /// the rule does not state it. Anchored on the property name so
    /// <c>border-color</c> is not read as <c>color</c>.</summary>
    private static string? Declaration(string rule, string property)
    {
        var match = Regex.Match(rule, $@"(?:^|[{{;])\s*{Regex.Escape(property)}\s*:\s*(?<value>[^;}}]+)");

        return match.Success ? match.Groups["value"].Value.Trim() : null;
    }

    /// <summary>The rule opening with <paramref name="pattern"/>, up to its
    /// closing brace. Asserted rather than returned empty: a selector that no
    /// longer exists has to fail as a rule this test cannot find.</summary>
    private static string Rule(string pattern)
    {
        var css = Stylesheet();
        var opening = Regex.Match(css, pattern, RegexOptions.Multiline);

        Assert.True(opening.Success, $"components.css has no rule matching {pattern}.");

        var end = css.IndexOf('}', opening.Index);

        Assert.True(end > 0, $"The rule matching {pattern} in components.css is never closed.");

        return css[opening.Index..(end + 1)];
    }

    private static string Stylesheet() =>
        File.ReadAllText(Path.Combine(
            Repository.Root.FullName, "src", "Core", "Backlog.UI.Components", "wwwroot", "components.css"));
}
