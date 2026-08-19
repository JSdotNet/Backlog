namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The filter bar answers to its column rather than to the window.
/// <para>
/// The bar lives in the split's list half, which is 30rem wide by default however
/// wide the window is. Its responsive rules used to be viewport queries, so at a
/// 1400px viewport none of them fired and <c>.filter-bar { overflow: hidden }</c>
/// clipped the bar in silence instead: measured there, the six status chips took 369
/// of the column's 480px and squeezed the area group down to a single chip. A
/// viewport query cannot see that case, which is why these are container queries and
/// why this test exists — the failure mode is invisible rather than obviously
/// broken.
/// </para>
/// <para>
/// Status collapses and areas do not. An area exists because somebody typed it and
/// its count is where the work is; status is one of a fixed six and only the chosen
/// one has to stay legible.
/// </para>
/// </summary>
public sealed class FilterBarLayoutTests
{
    private const string CollapseStep = "@container backlog-list (max-width: 38rem) {";

    private const string TightenStep = "@container backlog-list (max-width: 26rem) {";

    [Fact]
    public void The_list_column_is_what_the_filter_bar_measures_itself_against()
    {
        var css = Css();
        var list = Block(css, ".backlog-list {");

        Assert.Contains("container-type: inline-size;", list, StringComparison.Ordinal);
        Assert.Contains("container-name: backlog-list;", list, StringComparison.Ordinal);

        // The half still scrolls on its own and still refuses to be pushed wider by
        // its contents; GlobalPaneMarkupTests pins the scrolling half of that.
        Assert.Contains("min-width: 0;", list, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_the_status_chips_collapse()
    {
        var css = Css();
        var collapse = Block(css, CollapseStep);

        Assert.Contains(
            ".filter-group--status .chip:not(.chip--active):not(:first-child)",
            collapse,
            StringComparison.Ordinal);

        // The rule this replaced was unscoped, so it hid the area chips as well —
        // the group whose room the collapse is supposed to be buying.
        Assert.DoesNotContain(".filter-group .chip", css, StringComparison.Ordinal);
        Assert.DoesNotContain(".filter-group--areas .chip:not", css, StringComparison.Ordinal);

        // One step for one decision: the tightening lives lower down, where the
        // column is nearly as narrow as the separator can drag it.
        Assert.Equal(1, CountOccurrences(collapse, "display: none;"));

        var tighten = Block(css, TightenStep);

        Assert.Contains(".chip__count", tighten, StringComparison.Ordinal);
        Assert.Contains("gap: var(--spacing-xs);", tighten, StringComparison.Ordinal);

        // The collapse has to come first in source order, because the tighten step
        // matches everywhere the collapse step does.
        Assert.True(
            css.IndexOf(CollapseStep, StringComparison.Ordinal) < css.IndexOf(TightenStep, StringComparison.Ordinal),
            "The narrower container step must come after the wider one it overrides.");
    }

    /// <summary>
    /// No viewport query is left holding an opinion about the bar. One that measured
    /// the window would be answering a question about the column, and the answer
    /// would be wrong in exactly the case that prompted this: a wide window with a
    /// narrow list beside an open entry.
    /// </summary>
    [Fact]
    public void No_viewport_query_governs_the_filter_bar()
    {
        var css = Css();

        var offenders = MediaBlocks(css)
            .Where(block => block.Contains(".filter-", StringComparison.Ordinal)
                || block.Contains(".chip", StringComparison.Ordinal))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "A viewport query is still styling the filter bar, which sizes itself off the split's list column:\n"
            + string.Join("\n\n", offenders));
    }

    /// <summary>The rule that genuinely is about the window stays a media query: below
    /// 60rem the whole split stops being one, because neither half of it is readable
    /// at half of that.</summary>
    [Fact]
    public void The_split_still_stacks_on_a_narrow_window()
    {
        var stack = Block(Css(), "@media (max-width: 60rem) {");

        Assert.Contains(".backlog-split {", stack, StringComparison.Ordinal);
        Assert.Contains("flex-direction: column;", stack, StringComparison.Ordinal);
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

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;

        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0;
             index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
