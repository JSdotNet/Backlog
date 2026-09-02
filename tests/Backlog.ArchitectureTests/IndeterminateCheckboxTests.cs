using System.Text.RegularExpressions;

namespace Backlog.ArchitectureTests;

/// <summary>
/// A box standing for "some of them" has to look unlike one standing for "none".
///
/// <para><c>Checkbox</c> carries its third state in two places, because
/// <c>indeterminate</c> is a DOM property with no attribute behind it and no
/// markup the component renders can set it: <c>aria-checked="mixed"</c>, which
/// assistive technology reads, and <c>checkbox--mixed</c> on the root, which the
/// stylesheet draws. The markup half was always right. The stylesheet half keyed
/// off that class to italicise <c>.checkbox__label</c> and nothing else — a cue
/// exactly one kind of caller could show, and not the kind that needed it. A bare
/// box renders no label text: <c>SelectionBar</c>'s select-all and a task row's
/// selection gutter both looked plainly unchecked on a partial selection, with
/// <c>aria-checked</c> carrying the whole state alone.</para>
///
/// <para>Asserted against the stylesheet for the reason
/// <c>FollowableBadgeTests</c> and <c>MarkdownHeadingWeightTests</c> both give for
/// this shape of test: the defect lives entirely in what the stylesheet does with
/// markup that is already correct, and bUnit brings no layout engine. A render
/// test can only confirm the class and the element — which
/// <c>CheckboxTests</c> does — and that is precisely what passed while the state
/// was invisible.</para>
///
/// <para><c>.design/interaction-guidelines.md#focus-and-selection</c> asks for the
/// indeterminate state by name, so it is a rule and not a preference.</para>
/// </summary>
public class IndeterminateCheckboxTests
{
    /// <summary>
    /// The mixed state reaches the box itself.
    ///
    /// <para>Two halves, and either alone is the bug. Without
    /// <c>appearance: none</c> the platform draws its own empty box over anything
    /// this rule says, so the mark never appears; without a mark the rule has
    /// taken the native control away and put nothing in its place, which is worse
    /// than the defect it was meant to fix.</para>
    /// </summary>
    [Fact]
    public void The_mixed_state_is_drawn_on_the_box_rather_than_only_on_a_label()
    {
        var mixed = Rule(@"^\.checkbox--mixed\s+\.checkbox__input\s*\{");

        Assert.Equal("none", Declaration(mixed, "appearance"));

        Assert.NotNull(Declaration(mixed, "background-image"));
        Assert.NotNull(Declaration(mixed, "background-size"));
    }

    /// <summary>
    /// The mark is spent out of the palette, and out of the same pair the settled
    /// state already reads as.
    ///
    /// <para>A raw literal would already fail <c>DesignTokenTests</c>; what this
    /// adds is that ticked and mixed stay one family — one with a tick, one with a
    /// bar — rather than the third state arriving as a second idiom beside the
    /// first. The difference between them should be a difference in shape, not in
    /// weight or hue.</para>
    /// </summary>
    [Fact]
    public void The_mixed_box_is_painted_out_of_the_palette()
    {
        var mixed = Rule(@"^\.checkbox--mixed\s+\.checkbox__input\s*\{");

        Assert.Equal("var(--color-primary)", Declaration(mixed, "background-color"));
        Assert.Contains("var(--color-text-inverse)", Declaration(mixed, "background-image")!, StringComparison.Ordinal);
    }

    /// <summary>
    /// Taking the platform's appearance away stays scoped to the one state that
    /// needs it.
    ///
    /// <para>Every settled checkbox in the product keeps the native control it has
    /// always had. Only the third state is drawn by hand, because it is the only
    /// one the platform cannot be told to render. An <c>appearance</c> declaration
    /// on the unqualified <c>.checkbox__input</c> would be a redesign of every
    /// checkbox in the product smuggled in behind a bug fix — silent, because it
    /// would look deliberate.</para>
    /// </summary>
    [Fact]
    public void A_settled_box_keeps_the_control_the_platform_gives_it()
    {
        var settled = Rule(@"^\.checkbox__input\s*\{");

        Assert.Null(Declaration(settled, "appearance"));
        Assert.Null(Declaration(settled, "background-image"));
    }

    /// <summary>The label cue is kept as well as the box cue, not instead of it.
    /// Where a caller does show words beside the box, "some of them" is a sentence
    /// as well as a shape, and the italics were never wrong — only alone.</summary>
    [Fact]
    public void A_caller_that_shows_words_beside_the_box_still_gets_the_second_cue()
    {
        var label = Rule(@"^\.checkbox--mixed\s+\.checkbox__label\s*\{");

        Assert.Equal("italic", Declaration(label, "font-style"));
    }

    /// <summary>One declaration's value out of a rule body, trimmed, or null when
    /// the rule does not state it. Anchored on the property name so
    /// <c>background-color</c> is not read as <c>background</c>.</summary>
    private static string? Declaration(string rule, string property)
    {
        var match = Regex.Match(rule, $@"(?:^|[{{;])\s*{Regex.Escape(property)}\s*:\s*(?<value>[^;}}]+)");

        return match.Success ? match.Groups["value"].Value.Trim() : null;
    }

    /// <summary>The rule opening with <paramref name="pattern"/>, up to its
    /// closing brace. Asserted rather than returned empty: a selector that no
    /// longer exists has to fail as a rule this test cannot find, rather than
    /// passing every later assertion against an empty string.</summary>
    private static string Rule(string pattern)
    {
        var css = Stylesheet();
        var opening = Regex.Match(css, pattern, RegexOptions.Multiline);

        Assert.True(
            opening.Success,
            $"components.css has no rule matching {pattern}. A checkbox standing for a partial "
            + "selection then draws as an empty box, and aria-checked=\"mixed\" carries the third "
            + "state on its own — visible to a screen reader and to nobody else.");

        var end = css.IndexOf('}', opening.Index);

        Assert.True(end > 0, $"The rule matching {pattern} in components.css is never closed.");

        return css[opening.Index..(end + 1)];
    }

    private static string Stylesheet() =>
        File.ReadAllText(Path.Combine(
            Repository.Root.FullName, "src", "Core", "Backlog.UI.Components", "wwwroot", "components.css"));
}
