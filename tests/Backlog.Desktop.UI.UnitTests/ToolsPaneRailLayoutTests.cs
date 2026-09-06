using System.Globalization;
using System.Text.RegularExpressions;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The transcript of what the pane ran is a column beside the inventory, not a
/// block under it.
/// <para>
/// It was the last child of the pane's content, which on this machine's fifty-odd
/// rows put it a scroll and a half below the button that produced it. The output
/// was already captured — the point of capturing it was that a refused install or
/// a search that could not reach nuget.org had nowhere left to be read — and
/// filing it under the whole inventory gave it somewhere nobody looks.
/// </para>
/// <para>
/// Pinned here rather than left to the eye because all three parts of it are
/// invisible when they are right and silent when they are wrong: a column that
/// reserves its full width while folded, a rail that scrolls away from the rows it
/// explains, and a gate low enough to buy the second column by restacking every
/// row in the first.
/// </para>
/// </summary>
public sealed class ToolsPaneRailLayoutTests
{
    private const string Gate = "@container tools-panel (min-width: ";

    /// <summary>The pane is the container, because a container query styles the
    /// descendants of its container and never the container itself — and it is the
    /// content grid that has to decide whether there is room for two columns.
    /// The window cannot answer that: this is a surface whose width is the shell's
    /// to give.</summary>
    [Fact]
    public void The_pane_is_what_its_own_content_measures_itself_against()
    {
        var pane = Block(Css(), ".tools-panel {");

        Assert.Contains("container-type: inline-size;", pane, StringComparison.Ordinal);
        Assert.Contains("container-name: tools-panel;", pane, StringComparison.Ordinal);
    }

    /// <summary>One column is the shape the pane keeps wherever the rail does not
    /// fit, and the shape it had everywhere before the rail existed. The fallback
    /// is the same markup, so nothing has to be rendered twice.</summary>
    [Fact]
    public void Without_room_for_two_columns_the_rail_is_the_block_it_was()
    {
        var css = Css();

        var content = Block(css, ".tools-panel__content {");
        Assert.Contains("display: grid;", content, StringComparison.Ordinal);
        Assert.DoesNotContain("grid-template-columns", content, StringComparison.Ordinal);

        // The rule above the transcript, which is what set it apart while it was a
        // footer. The two-column layout replaces it with one down the side.
        var side = Block(css, ".tools-panel__side {");
        Assert.Contains("border-top:", side, StringComparison.Ordinal);
    }

    [Fact]
    public void With_room_for_two_the_transcript_takes_the_second_column()
    {
        var gate = GateBlock(Css());

        Assert.Contains(".tools-panel__content {", gate, StringComparison.Ordinal);

        // fit-content() is what makes the rail cost only what it is showing:
        // FoldControl keeps its region in the DOM and merely `hidden` when closed,
        // and a display: none subtree contributes nothing to track sizing. So the
        // track measures the disclosure alone while it is folded.
        Assert.Contains("grid-template-columns: minmax(0, 1fr) fit-content(", gate, StringComparison.Ordinal);

        // And the floor that stops a two-line transcript drawing a column barely
        // wider than its own heading is on the grid item, because the track's
        // minimum is the item's contribution and nothing a descendant declares
        // reaches it. Only while the fold is open: closed, the trigger is the
        // whole rail and 20rem of it would be 20rem of nothing.
        Assert.Contains(".tools-panel__side:has(.fold--open)", gate, StringComparison.Ordinal);
        Assert.Contains("min-width:", BlockAt(gate, gate.IndexOf(":has(.fold--open)", StringComparison.Ordinal)), StringComparison.Ordinal);
    }

    /// <summary>
    /// Both numbers the fixture below compares are found by first occurrence, so a
    /// second block of either kind would be read by nobody. One of each, or the
    /// cross-check is quietly answering about the wrong rule.
    /// </summary>
    [Fact]
    public void There_is_one_gate_and_one_restack_step_to_compare()
    {
        var css = Css();

        Assert.Equal(1, Occurrences(css, Gate));
        Assert.Equal(1, Occurrences(css, "@container tools-inventory (max-width: "));
    }

    /// <summary>Fifty rows of inventory scroll the transcript off the screen
    /// exactly when a failed row is the reason somebody is scrolling. The pane is
    /// the scrollport, so the rail sticks to the top of it — and
    /// <c>align-items: start</c> is what gives it the distance to travel, since a
    /// rail stretched to the row has nowhere to move.</summary>
    [Fact]
    public void The_rail_stays_on_screen_while_the_inventory_scrolls()
    {
        var gate = GateBlock(Css());

        Assert.Contains("align-items: start;", gate, StringComparison.Ordinal);

        var side = BlockAt(gate, gate.IndexOf(".tools-panel__side {", StringComparison.Ordinal));
        Assert.Contains("position: sticky;", side, StringComparison.Ordinal);
        Assert.Contains("top: 0;", side, StringComparison.Ordinal);
    }

    /// <summary>
    /// The gate is set against the table's own restack step, not chosen: the rail
    /// takes at most the width of its track and the grid one <c>--spacing-md</c>
    /// gap, and what is left has to keep the table above the width its four
    /// columns need. A lower gate would buy the second column by stacking every
    /// row in the first, which is a worse pane than the footer this replaced.
    /// </summary>
    [Fact]
    public void The_gate_leaves_the_table_above_its_own_restack_step()
    {
        var css = Css();

        // Escaped, because the constant carries the query's own opening bracket.
        var gate = Number(css, Regex.Escape(Gate) + @"(\d+(?:\.\d+)?)rem");
        var rail = Number(GateBlock(css), @"fit-content\((\d+(?:\.\d+)?)rem\)");
        var restack = Number(css, @"@container tools-inventory \(max-width: (\d+(?:\.\d+)?)rem\)");

        // The gap between the two columns. The rail's own padding-inline-start
        // comes out of its track rather than out of the table's share, so it is
        // not in this sum.
        const double SpacingMd = 1;

        Assert.True(
            gate - rail - SpacingMd > restack,
            $"A {gate}rem pane less a {rail}rem rail and a {SpacingMd}rem gap leaves the table "
            + $"{gate - rail - SpacingMd}rem, which does not clear its {restack}rem restack step.");
    }

    private static int Occurrences(string css, string value)
    {
        var count = 0;

        for (var at = css.IndexOf(value, StringComparison.Ordinal); at >= 0;
             at = css.IndexOf(value, at + 1, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static double Number(string css, string pattern)
    {
        var match = Regex.Match(css, pattern);
        Assert.True(match.Success, $"{pattern} should match.");

        return double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    private static string Css() => File.ReadAllText(
        RepositoryRoot.File("src", "App", "Backlog.Desktop.UI", "wwwroot", "app.css")).Replace("\r\n", "\n");

    private static string GateBlock(string css) => Block(css, Gate);

    private static string Block(string css, string opening)
    {
        var start = css.IndexOf(opening, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{opening} should exist.");

        return BlockAt(css, start);
    }

    private static string BlockAt(string css, int start)
    {
        var depth = 0;

        for (var index = css.IndexOf('{', start); index < css.Length && index >= 0; index++)
        {
            if (css[index] == '{')
            {
                depth++;
            }
            else if (css[index] == '}' && --depth == 0)
            {
                return css[start..(index + 1)];
            }
        }

        return css[start..];
    }
}
