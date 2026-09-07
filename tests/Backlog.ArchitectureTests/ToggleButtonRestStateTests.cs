using System.Text.RegularExpressions;

namespace Backlog.ArchitectureTests;

/// <summary>
/// An unselected toggle has to be painted, not left to the platform.
///
/// <para><c>ToggleButton</c> renders <c>btn btn--toggle</c>, and the stylesheet
/// paints the selected half with <c>.btn--toggle[aria-pressed="true"]</c>. Nothing
/// painted the other half. <c>.btn</c> declares padding, a border and a
/// <c>transition</c> for <c>background</c> but no <c>background</c> of its own, and
/// every modifier that supplies one — <c>btn--primary</c>, <c>btn--secondary</c>,
/// <c>btn--ghost</c>, <c>btn--danger</c> — is absent from a plain toggle. So an
/// unpressed toggle fell through to the user agent: <c>buttonface</c> for the slab
/// and <c>buttontext</c> for the label, computing to rgb(240, 240, 240) behind
/// black text. Backlog has one theme and it is dark, so every unselected chip in a
/// one-of-many strip — the dashboard's window filter, the Sessions pane's Show and
/// Group by, the Inbox's group-by — rendered as a white slab.
/// </para>
///
/// <para>This is the defect <c>.btn--icon</c> already carries a comment about, and
/// it is closed the same way that one was: state the rest background and the label
/// colour rather than inheriting the platform's.</para>
///
/// <para>Asserted against the stylesheet for the reason
/// <c>IndeterminateCheckboxTests</c> and <c>FollowableBadgeTests</c> both give: the
/// markup was never wrong. The class and <c>aria-pressed</c> were correct
/// throughout, which is exactly why <c>ToggleButtonTests</c> and
/// <c>SessionsPaneTests</c> stayed green while the strip showed white slabs —
/// bUnit brings no layout engine, so a render test cannot see a colour that is
/// never declared.</para>
/// </summary>
public class ToggleButtonRestStateTests
{
    /// <summary>
    /// The unselected half declares a background.
    ///
    /// <para>Asserted as "declares one" rather than as a particular colour: which
    /// token the rest state wears is a design decision that may move, but leaving
    /// the property unsaid hands the slab back to the user agent, which is the
    /// defect itself.</para>
    /// </summary>
    [Fact]
    public void An_unpressed_toggle_paints_its_own_background()
    {
        var background = Declaration(RestStateRule(), "background")
                      ?? Declaration(RestStateRule(), "background-color");

        Assert.False(
            string.IsNullOrWhiteSpace(background),
            "The .btn--toggle rest-state rule in components.css declares no background. .btn declares none "
            + "either, so an unpressed toggle falls through to the user agent's buttonface — a light slab in a "
            + "product that has only a dark theme.");

        Assert.DoesNotContain("buttonface", background, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// And a label colour.
    ///
    /// <para>The other half of the same fallthrough, and the half that survives a
    /// transparent background: with no <c>color</c> the label is the user agent's
    /// <c>buttontext</c>, which is black. A rest state that fixes only the slab
    /// leaves black text on the dark panel behind it.</para>
    /// </summary>
    [Fact]
    public void An_unpressed_toggle_paints_its_own_label()
    {
        var color = Declaration(RestStateRule(), "color");

        Assert.False(
            string.IsNullOrWhiteSpace(color),
            "The .btn--toggle rest-state rule in components.css declares no color, so an unpressed toggle's "
            + "label is the user agent's buttontext — black, on a dark panel.");

        Assert.DoesNotContain("buttontext", color, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The rest-state rule: the one selecting <c>.btn--toggle</c> with no
    /// attribute and no state qualifying it. Found by search rather than by an
    /// exact string so the rule may gain declarations without breaking the test
    /// that reads it, and asserted, so a rule that has gone missing fails here
    /// rather than further down against an empty body.</summary>
    private static string RestStateRule()
    {
        var match = Regex.Match(
            Stylesheet(),
            @"^\.btn--toggle\s*\{(?<body>[^}]*)\}",
            RegexOptions.Multiline);

        Assert.True(
            match.Success,
            "components.css has no rest-state rule for .btn--toggle: every rule naming it also qualifies it "
            + "with an attribute or a state, so the unpressed toggle is painted by nothing.");

        return match.Value;
    }

    /// <summary>One declaration's value out of a rule body, trimmed, or null when
    /// the rule does not state it. Anchored on the property name so
    /// <c>border-color</c> is not read as <c>color</c>.</summary>
    private static string? Declaration(string rule, string property)
    {
        var match = Regex.Match(rule, $@"(?:^|[{{;])\s*{Regex.Escape(property)}\s*:\s*(?<value>[^;}}]+)");

        return match.Success ? match.Groups["value"].Value.Trim() : null;
    }

    private static string Stylesheet() =>
        File.ReadAllText(Path.Combine(
            Repository.Root.FullName, "src", "Core", "Backlog.UI.Components", "wwwroot", "components.css"));
}
