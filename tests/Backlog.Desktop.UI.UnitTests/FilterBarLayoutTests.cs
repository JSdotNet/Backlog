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

        // One decision per group: status loses its unchosen chips, tags loses the
        // group, and nothing else on the bar is touched at this step.
        Assert.DoesNotContain(".filter-group--scope", collapse, StringComparison.Ordinal);
        Assert.DoesNotContain(".filter-group--areas", collapse, StringComparison.Ordinal);

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
    /// The tags group shares the middle of the bar with the areas, and yields first.
    /// <para>
    /// It is the one group whose length has no ceiling: an entry wears any number of
    /// tags and every distinct one gets a chip, so left alone it would take the room
    /// the areas need in a bar that does not wrap.
    /// </para>
    /// </summary>
    [Fact]
    public void The_tags_group_shares_the_middle_and_yields_before_the_areas()
    {
        var css = Css();

        var tags = Block(css, ".filter-group--tags {");
        var areas = Block(css, ".filter-group--areas {");

        Assert.Contains("flex: 1 1 auto;", areas, StringComparison.Ordinal);

        // Same grow, higher shrink: both take the leftover, tags gives it back first.
        Assert.Contains("flex: 1 2 auto;", tags, StringComparison.Ordinal);

        // And the areas are never collapsed to buy that room, at either step.
        Assert.DoesNotContain(".filter-group--areas .chip", css, StringComparison.Ordinal);
    }

    /// <summary>
    /// Below 38rem the tag group leaves the bar altogether, and it is the only group
    /// allowed to.
    /// <para>
    /// Every other group is the whole of its own question — there is nowhere else to
    /// pick an area, a status or My Day, so hiding one would take the answer with it.
    /// A tag is also on the rows, and <c>TagFilterTests</c> pins the half of this
    /// that makes it safe: pressing a row's tag filters by it and pressing the one
    /// already chosen clears it, so the way back does not go with the group.
    /// </para>
    /// <para>
    /// The narrower step must say nothing about the group any more. A rule that
    /// fired below a rule that had already removed its subject is the next reader's
    /// evidence that the subject is still there.
    /// </para>
    /// </summary>
    [Fact]
    public void The_tag_group_is_the_one_group_that_leaves_the_bar()
    {
        var css = Css();
        var collapse = Block(css, CollapseStep);

        Assert.Contains(".filter-group--tags {", collapse, StringComparison.Ordinal);

        Assert.DoesNotContain(".filter-group--tags", Block(css, TightenStep), StringComparison.Ordinal);
    }

    /// <summary>
    /// At the same step the row's trailing cluster comes down to the one control on
    /// it that says how far the entry has got.
    /// <para>
    /// The pencil, the repository picker and the copy button are all controls a
    /// reader reached the row <em>through</em> rather than for, and all three are
    /// still one click away inside the entry. The status is the fact a backlog is
    /// scanned for, so it stays — as the colour that was already carrying it, in a
    /// circle, still the same select.
    /// </para>
    /// </summary>
    [Fact]
    public void The_rows_lose_their_cluster_but_never_their_status()
    {
        var collapse = Block(Css(), CollapseStep);

        foreach (var hidden in new[]
                 {
                     ".task-item__edit",
                     ".task-item__copy",
                     ".entry-row__pickers .metadata-editor--repo"
                 })
        {
            Assert.Contains(hidden, collapse, StringComparison.Ordinal);
        }

        // Not the status: it is narrowed to a dot, never removed.
        var dot = collapse[collapse.IndexOf(".entry-row__pickers .badge--status {", StringComparison.Ordinal)..];

        Assert.Contains("border-radius: var(--border-radius-full);", dot, StringComparison.Ordinal);
        Assert.Contains("width: var(--spacing-md);", dot, StringComparison.Ordinal);

        // The word goes, not the control — and not the words in the list it opens,
        // which .status-editor__select option draws in full colour.
        Assert.Contains("color: transparent;", dot, StringComparison.Ordinal);

        // Tokens only: no literal length, colour or font in any of it.
        Assert.DoesNotContain("6.5rem", collapse, StringComparison.Ordinal);
        Assert.DoesNotContain("px;", collapse, StringComparison.Ordinal);
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
}
