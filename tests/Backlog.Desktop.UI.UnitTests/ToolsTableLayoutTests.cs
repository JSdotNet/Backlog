namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The tools table answers to the pane it is in rather than to the window, and
/// below the width its four columns need it stops being four columns.
/// <para>
/// The row was a fixed four-track grid whose <c>minmax()</c> floors add up to
/// 29rem — 464px, plus three 8px gaps and 16px of row padding, so 504px before a
/// single character is drawn — inside <c>.tools-table { overflow: hidden }</c>. At a
/// 375px window the pane has about 338px to give it, so the Available column and
/// the whole Actions column were clipped away: no scrollbar, no wrapping, no page
/// scroll, and every row's Update, Install, Enable, Disable and Remove out of
/// reach. Nothing looked broken, which is why this is pinned here rather than left
/// to the eye.
/// </para>
/// <para>
/// A container query, for the reason <see cref="FilterBarLayoutTests"/> gives: the
/// pane is not the window. It can be narrow in a wide window, and a viewport
/// threshold would answer a question about the window while the clipping is
/// happening in the pane.
/// </para>
/// </summary>
public sealed class ToolsTableLayoutTests
{
    /// <summary>504px is 31.5rem, so the restack has to be in force at 32rem —
    /// the first round rem step that still covers the row's own minimum.</summary>
    private const string RestackStep = "@container tools-table (max-width: 32rem) {";

    [Fact]
    public void The_pane_is_what_the_tools_table_measures_itself_against()
    {
        var table = Block(Css(), ".tools-table {");

        Assert.Contains("container-type: inline-size;", table, StringComparison.Ordinal);
        Assert.Contains("container-name: tools-table;", table, StringComparison.Ordinal);
    }

    /// <summary>
    /// Whatever still outgrows its track — an unbreakable version string, an action
    /// whose label is wider than the column — is reachable rather than deleted. The
    /// block axis stays clipped, which is what keeps the rows inside the frame's
    /// rounded corners.
    /// </summary>
    [Fact]
    public void Nothing_that_outgrows_the_table_is_clipped_out_of_reach()
    {
        var table = Block(Css(), ".tools-table {");

        Assert.Contains("overflow-x: auto;", table, StringComparison.Ordinal);
        Assert.Contains("overflow-y: hidden;", table, StringComparison.Ordinal);

        // The rule that hid the overflow outright is what made the clipping silent.
        Assert.DoesNotContain("overflow: hidden;", table, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pane_too_narrow_for_four_columns_stacks_the_row_instead()
    {
        var restack = Block(Css(), RestackStep);

        Assert.Contains(".tools-table__row {", restack, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: 1fr;", restack, StringComparison.Ordinal);

        // The heading row is the one part of a stack that cannot mean anything: it
        // names four columns that are no longer beside each other.
        Assert.Contains(".tools-table__row--head", restack, StringComparison.Ordinal);
        Assert.Contains(".tools-table__cell-label", restack, StringComparison.Ordinal);
    }

    /// <summary>
    /// Exactly one of the two ways a cell can be labelled is live at any width. Wide,
    /// the heading row says which column is which and the in-cell labels are gone;
    /// narrow, the labels say it and the heading row is gone. Both at once reads the
    /// same word twice.
    /// </summary>
    [Fact]
    public void The_in_cell_labels_are_only_there_when_the_heading_row_is_not()
    {
        var css = Css();

        Assert.Contains("display: none;", Block(css, ".tools-table__cell-label {"), StringComparison.Ordinal);

        var restack = Block(css, RestackStep);
        var headRow = restack.IndexOf(".tools-table__row--head", StringComparison.Ordinal);
        var labels = restack.IndexOf(".tools-table__cell-label", StringComparison.Ordinal);

        Assert.True(headRow >= 0 && labels >= 0, "The restack should govern both the heading row and the in-cell labels.");
        Assert.Contains("display: none;", BlockAt(restack, headRow), StringComparison.Ordinal);
        Assert.DoesNotContain("display: none;", BlockAt(restack, labels), StringComparison.Ordinal);
    }

    /// <summary>The clipping was a fact about the pane's width, and a media query
    /// cannot see it. One that fired on the window would restack a wide pane in a
    /// narrow window and leave a narrow pane in a wide one exactly as it was.</summary>
    [Fact]
    public void No_viewport_query_governs_the_tools_table()
    {
        var offenders = MediaBlocks(Css())
            .Where(block => block.Contains(".tools-table", StringComparison.Ordinal))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "A viewport query is still styling the tools table, which sizes itself off the pane it is in:\n"
            + string.Join("\n\n", offenders));
    }

    private static string Css() => File.ReadAllText(
        RepositoryRoot.File("src", "App", "Backlog.Desktop.UI", "wwwroot", "app.css")).Replace("\r\n", "\n");

    /// <summary>Every <c>@media</c> block in the sheet, braces matched so a nested
    /// rule cannot end the block early.</summary>
    private static IEnumerable<string> MediaBlocks(string css)
    {
        for (var at = css.IndexOf("@media", StringComparison.Ordinal); at >= 0;
             at = css.IndexOf("@media", at + 1, StringComparison.Ordinal))
        {
            yield return BlockAt(css, at);
        }
    }

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
