using System.Text.RegularExpressions;

namespace Backlog.ArchitectureTests;

/// <summary>
/// A Draft entry's check circle has to be visible on the panel it sits in.
///
/// <para>The circle's edge wears its status badge's colour. For Draft that was the
/// badge's fill, <c>--color-background-raised</c> — which is also what
/// <c>.task-panel</c> is filled with, so the detail pane drew a Draft entry's circle
/// in the colour of the surface under it and the circle was simply gone. Asserted
/// against the stylesheet for the reason <c>IndeterminateCheckboxTests</c> gives: the
/// markup was right, and bUnit brings no layout engine to notice it was invisible.</para>
/// </summary>
public class DraftCheckCircleTests
{
    [Fact]
    public void A_draft_circle_is_not_edged_in_the_panels_own_fill()
    {
        var css = Stylesheet();
        var panelFill = Declaration(Rule(css, @"^\.task-panel\s*\{"), "background");
        var draftEdge = Declaration(Rule(css, @"^:where\(\.task-item:has\([^\n]*\.badge--status-draft\)\)"), "border-color");

        Assert.NotNull(panelFill);
        Assert.NotNull(draftEdge);
        Assert.NotEqual(panelFill, draftEdge);
    }

    private static string? Declaration(string rule, string property)
    {
        var match = Regex.Match(rule, $@"(?:^|[{{;])\s*{Regex.Escape(property)}\s*:\s*(?<value>[^;}}]+)");

        return match.Success ? match.Groups["value"].Value.Trim() : null;
    }

    private static string Rule(string css, string pattern)
    {
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
