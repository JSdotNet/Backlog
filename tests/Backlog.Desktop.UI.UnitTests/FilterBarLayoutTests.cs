namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The filter bar answers to its column rather than to the window.
/// <para>
/// The bar lives in the split's list half, which is 30rem wide by default however
/// wide the window is. Its responsive rules used to be viewport queries, so at a
/// 1400px viewport none of them fired and <c>.filter-bar { overflow: hidden }</c>
/// clipped the bar in silence instead: measured there, the six status chips took 369
/// of the column's 480px and squeezed the group beside them down to a single chip. A
/// viewport query cannot see that case, which is why these are container queries and
/// why this test exists — the failure mode is invisible rather than obviously
/// broken.
/// </para>
/// <para>
/// That measurement is history and is left standing as history: it is the evidence
/// these steps exist at all, and rewriting it to today's numbers would erase the
/// case it was taken from. The strip is four chips and about 230px now — Done and
/// Archived are gone from it — which is why the step that collapses it moved down
/// to 30rem, where it is true, instead of hiding three chips at a width where four
/// fit.
/// </para>
/// <para>
/// Status collapses and tags do not, until tags leave altogether. A tag exists
/// because somebody typed it and its count is where the work is; status is one of a
/// fixed four and only the chosen one has to stay legible.
/// </para>
/// </summary>
public sealed class FilterBarLayoutTests
{
    /// <summary>Where the tag pile leaves the bar. Its own step, above the cluster's:
    /// the bar's four groups come to 48.1rem with a two-chip pile in front of them and
    /// 30.2rem without it, so the pile is the whole difference between a bar that fits
    /// and one that is clipped. The two shared 38rem until the list was given a floor
    /// that sits between them — see <c>BacklogListMinimumWidthTests</c>.</summary>
    private const string TagStep = "@container backlog-list (max-width: 48rem) {";

    /// <summary>Where the open-work summary drops to "N open". Measured in the
    /// harness: select, the three scopes, the four-chip status strip and the full
    /// summary come to about 917px before the tag pile gets any of the bar, so the
    /// full sentence only stays while the column clears that with room to spare.</summary>
    private const string SummaryStep = "@container backlog-list (max-width: 60rem) {";

    private const string CollapseStep = "@container backlog-list (max-width: 38rem) {";

    /// <summary>Where the open-work summary comes down to its number alone. With the
    /// tag pile gone, select (~55px), the scopes (~270px), the status strip (293px)
    /// and "N open" with the gaps come to about 47rem; below it the noun goes, and
    /// the number is what is left to run off the bar's end — after status, never
    /// in its place.</summary>
    private const string SummaryCompactStep = "@container backlog-list (max-width: 47rem) {";

    /// <summary>Where the status strip comes down to the chosen chip. Its own step,
    /// below the one that takes the tag pile and the row cluster: those have
    /// nothing to do with the strip's width and never did. The collapsed strip
    /// offers only the chosen chip, so it must not fire any higher than it has
    /// to — the summary gives way instead.</summary>
    private const string StatusStep = "@container backlog-list (max-width: 30rem) {";

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
        var status = Block(css, StatusStep);

        Assert.Contains(
            ".filter-group--status .chip:not(.chip--active):not(:first-child)",
            status,
            StringComparison.Ordinal);

        // The rule this replaced was unscoped, so it hid every other group's chips
        // as well — the room the collapse is supposed to be buying.
        Assert.DoesNotContain(".filter-group .chip", css, StringComparison.Ordinal);

        // One decision per group: status loses its unchosen chips, tags loses the
        // group, and nothing else on the bar is touched at any step.
        Assert.DoesNotContain(".filter-group--scope", Block(css, TagStep), StringComparison.Ordinal);
        Assert.DoesNotContain(".filter-group--scope", Block(css, CollapseStep), StringComparison.Ordinal);
        Assert.DoesNotContain(".filter-group--scope", status, StringComparison.Ordinal);

        var tighten = Block(css, TightenStep);

        Assert.Contains(".chip__count", tighten, StringComparison.Ordinal);
        Assert.Contains("gap: var(--spacing-xs);", tighten, StringComparison.Ordinal);

        // Widest first, all of them: each step matches everywhere the ones above
        // it do, so a narrower rule written earlier would be overridden by the
        // wider one it was supposed to replace.
        string[] steps = [SummaryStep, TagStep, SummaryCompactStep, CollapseStep, StatusStep, TightenStep];
        var at = steps.Select(step => css.IndexOf(step, StringComparison.Ordinal)).ToList();

        Assert.All(at, index => Assert.True(index >= 0));
        Assert.True(
            at.SequenceEqual(at.Order()),
            "Each narrower container step must come after the wider ones it overrides.");
    }

    /// <summary>
    /// The tags group takes the middle of the bar, and is the only group that takes
    /// any of it.
    /// <para>
    /// It shared the middle with an area group until that group was removed, and it
    /// is the one whose length has no ceiling: an entry wears any number of tags and
    /// every distinct one gets a chip. The scopes are pinned and status sits against
    /// the right edge, so there is nothing left to share the slack with — which is
    /// also why the group's own chips are never collapsed to buy room. It leaves the
    /// bar whole instead, one step down.
    /// </para>
    /// <para>
    /// A zero basis rather than <c>auto</c>: the slack is what the other groups leave,
    /// not a width it competes for. At <c>auto</c> a long pile shrank the statuses
    /// too and clipped them through a chip's name; what the strip cannot fit now is
    /// the More toggle's to reach.
    /// </para>
    /// </summary>
    [Fact]
    public void The_tags_group_takes_the_middle_of_the_bar()
    {
        var css = Css();

        var tags = Block(css, ".filter-group--tags {");

        Assert.Contains("flex: 1 1 0;", tags, StringComparison.Ordinal);

        // Nothing else grows into it: the scopes hold their room, status only shrinks.
        Assert.Contains("flex: 0 0 auto;", Block(css, ".filter-group--scope {"), StringComparison.Ordinal);
        // Status neither grows nor shrinks: a squeezed strip is clipped by the
        // bar's overflow, and a clipped status chip is a filter the reader cannot
        // see is pressed. It collapses to the chosen chip at its own step instead.
        Assert.Contains("flex: 0 0 auto;", Block(css, ".filter-group--status {"), StringComparison.Ordinal);

        // And the tag chips are never collapsed one by one to buy width — the group
        // grows and shrinks whole. The one rule that does reach a single chip is not
        // a width tactic: below 48rem a group with something picked keeps the pressed
        // chips and drops the rest, which is what stops the group going and taking
        // the only way out of the selection with it.
        Assert.DoesNotContain(".filter-group--tags .chip", css, StringComparison.Ordinal);
        Assert.DoesNotContain(".filter-group--tags--picked .chip", Block(css, StatusStep), StringComparison.Ordinal);
        Assert.DoesNotContain(".filter-group--tags--picked .chip", Block(css, TightenStep), StringComparison.Ordinal);
    }

    /// <summary>
    /// Below 38rem the tag group leaves the bar while nothing is picked, and it is
    /// the only group allowed to leave at all.
    /// <para>
    /// Every other group is the whole of its own question — there is nowhere else to
    /// pick a status, My Day or No repo, so hiding one would take the answer with it.
    /// A tag is also on the rows, which is what once made the group safe to drop
    /// outright.
    /// </para>
    /// <para>
    /// That stopped being enough when the selection became a set. A row's tag now
    /// adds and removes only itself rather than putting every row back, and
    /// "Untagged" is on no row at all — so a selection could outlive every control
    /// that could undo it. Hence the pair of rules: the group goes while the
    /// selection is empty, and comes back narrowed to the pressed chips once it is
    /// not. <c>TagFilterTests</c> pins the modifier this hangs on.
    /// </para>
    /// <para>
    /// The narrower steps must still say nothing about the group. A rule that fired
    /// below a rule that had already removed its subject is the next reader's
    /// evidence that the subject is still there.
    /// </para>
    /// </summary>
    [Fact]
    public void The_tag_group_is_the_one_group_that_leaves_the_bar()
    {
        var css = Css();
        var tags = Block(css, TagStep);

        Assert.Contains(
            ".filter-group--tags:not(.filter-group--tags--picked) {",
            tags,
            StringComparison.Ordinal);

        Assert.Contains(
            ".filter-group--tags--picked .chip:not(.chip--active) {",
            tags,
            StringComparison.Ordinal);

        // And it leaves at its own width. Sharing the cluster's step put the pile back
        // on a bar that had no room for it, at every width between the two — which is
        // the band the list's floor now keeps it in.
        Assert.DoesNotContain(".filter-group--tags", Block(css, CollapseStep), StringComparison.Ordinal);
        Assert.DoesNotContain(".filter-group--tags", Block(css, StatusStep), StringComparison.Ordinal);
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
        // which `select option` draws in full colour.
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

    /// <summary>
    /// The open-work summary keeps its room and never leaves the bar.
    /// <para>
    /// It is the only place the total is said, so no step may hide the group; the
    /// tag pile leaves before it does, and at that same step it drops its detail
    /// half and keeps "N open" at its own step, and below 47rem keeps only the
    /// number. It never shrinks and it comes after the status strip, so when the
    /// bar runs out of room it is the summary that runs off the end — the strip's
    /// position does not depend on it at all, and the strip's own collapse stays
    /// at 30rem where it was. At the narrowest step, where status gives up its
    /// auto margin, the summary takes one so it still holds the right edge.
    /// </para>
    /// </summary>
    [Fact]
    public void The_open_work_summary_keeps_its_room_and_shortens_rather_than_leaves()
    {
        var css = Css();

        Assert.Contains("flex: 0 0 auto;", Block(css, ".filter-group--summary {"), StringComparison.Ordinal);

        Assert.Contains(".open-work-summary__detail {", Block(css, SummaryStep), StringComparison.Ordinal);
        Assert.Contains(".open-work-summary__noun {", Block(css, SummaryCompactStep), StringComparison.Ordinal);

        // The summary never touches the status strip, and no step above the
        // strip's own collapses it on the summary's behalf.
        foreach (var step in new[] { SummaryStep, TagStep, SummaryCompactStep, CollapseStep })
        {
            Assert.DoesNotContain(".filter-group--status .chip", Block(css, step), StringComparison.Ordinal);
        }

        // It never shrinks either: the tag pile is the one group that gives way.
        Assert.DoesNotContain("flex: 0 1", Block(css, ".filter-group--summary {"), StringComparison.Ordinal);

        foreach (var step in new[] { SummaryStep, TagStep, SummaryCompactStep, CollapseStep, StatusStep, TightenStep })
        {
            var block = Block(css, step);
            var at = block.IndexOf(".filter-group--summary {", StringComparison.Ordinal);

            if (at >= 0)
            {
                Assert.DoesNotContain("display: none", BlockAt(block, at), StringComparison.Ordinal);
            }
        }

        Assert.Contains("margin-left: auto;", Block(Block(css, TightenStep), ".filter-group--summary {"), StringComparison.Ordinal);
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
