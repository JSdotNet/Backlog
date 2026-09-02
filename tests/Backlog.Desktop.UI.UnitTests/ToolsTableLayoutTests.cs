using System.Globalization;
using System.Text.RegularExpressions;

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
    /// <summary>
    /// The step is derived from the row's track list, so it moves when the list
    /// does. It is now 10 + 5.5 + 5.5 + 17.5 = 38.5rem of floors, three 8px gaps
    /// and 16px of row padding — 41rem, so the restack has to be in force at 41rem,
    /// the first round rem step that still covers the row's own minimum.
    /// <para>It was 32rem while the actions cell was a wrapping flex row and its
    /// track floor was 8rem. Three controls guaranteed to stay on one line need
    /// 17.5rem, and a track narrower than its own cell is the clipping this whole
    /// fixture exists for.</para>
    /// </summary>
    private const string RestackStep = "@container tools-table (max-width: 41rem) {";

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

    /// <summary>
    /// The three things a row lets you do land in the same three places in every
    /// row of the group.
    /// <para>The cell was a wrapping flex row, so each item started wherever the
    /// one before it ended: Update, Up to date and Disabled are three widths of
    /// the same slot, and they carried the toggle and Remove along with them — a
    /// column of controls that lined up nowhere. A status long enough to fill the
    /// cell dropped both onto a second line as well.</para>
    /// <para>Two reserved tracks at the end are what align them, and reserved is
    /// the point: every row is its own grid, so nothing synchronises a
    /// content-sized track across rows. Three rows whose toggles happen to measure
    /// the same are aligned by coincidence — and a marketplace row, which has no
    /// toggle at all, would collapse that track to nothing and move the status
    /// slot's edge 6rem with it.</para>
    /// </summary>
    [Fact]
    public void The_row_actions_land_in_the_same_places_in_every_row()
    {
        var css = Css();
        var actions = Block(css, ".tools-table__actions {");

        Assert.Contains("display: grid;", actions, StringComparison.Ordinal);

        // Reserved, not auto: an auto track is measured per row, and a row with no
        // toggle in it would collapse that track and take the alignment with it.
        Assert.Contains("grid-template-columns: minmax(0, 1fr) 6rem 4rem;", actions, StringComparison.Ordinal);

        // The wrap is what let them fall onto a second line under a long status.
        Assert.DoesNotContain("flex-wrap", actions, StringComparison.Ordinal);

        // A column each, stated rather than inherited from the order they arrive
        // in: a marketplace row has no toggle and a read-only host no Remove, and
        // under auto-placement the survivors would slide into the empty track.
        Assert.Contains("grid-column: 1;", Block(css, ".tools-table__action {"), StringComparison.Ordinal);

        Assert.Contains("grid-column: 2;", Block(css, ".tools-table__toggle {"), StringComparison.Ordinal);

        var remove = Block(css, ".tools-table__remove {");
        Assert.Contains("grid-column: 3;", remove, StringComparison.Ordinal);
        Assert.Contains("justify-self: end;", remove, StringComparison.Ordinal);
    }

    /// <summary>
    /// The status slot is whatever is left of the actions track after the two
    /// reserved ones, and it has to hold the widest thing that goes in it — an
    /// Install button or the "Done here" checkbox, both around 5.5rem here.
    /// <para>Pinned because the failure is silent. The first track is
    /// <c>minmax(0, …)</c>, so a slot too narrow neither scrolls nor clips: the
    /// button simply overlaps the toggle beside it. And the actions track sits at
    /// this floor for every table width up to about 70rem, which is most of them,
    /// so it is the common case and not an edge.</para>
    /// </summary>
    [Fact]
    public void The_status_slot_has_room_left_after_the_reserved_tracks()
    {
        var css = Css();

        var actionsFloor = Floors(Block(css, ".tools-table__row {")).Last();
        var reserved = Regex.Matches(Block(css, ".tools-table__actions {"), RemLength)
            .Select(match => double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .Sum();

        var status = actionsFloor - reserved - (2 * Spacing("xs"));

        Assert.True(
            status >= 6,
            $"A {actionsFloor}rem actions track less {reserved}rem of reserved slots and two gaps "
            + $"leaves {status}rem for the status, which does not hold an Install button.");
    }

    /// <summary>
    /// The step is arithmetic, not taste, so it is computed here rather than
    /// restated: the row's four <c>minmax()</c> floors, three <c>--spacing-sm</c>
    /// gaps and two of row padding, rounded up to the next rem.
    /// <para>Pinned because the two numbers have to move together and live forty
    /// lines apart. Widening a track without moving the step leaves a band where
    /// the row is wider than its box — which is the clipping this fixture exists
    /// for, reintroduced by an edit that looked local.</para>
    /// </summary>
    [Fact]
    public void The_restack_step_is_the_row_track_arithmetic_and_nothing_else()
    {
        var css = Css();
        var floors = Floors(Block(css, ".tools-table__row {"));

        Assert.Equal(4, floors.Count);

        var needed = floors.Sum() + (3 * Spacing("sm")) + (2 * Spacing("sm"));
        var step = Math.Ceiling(needed).ToString(CultureInfo.InvariantCulture);

        Assert.Contains($"@container tools-table (max-width: {step}rem) {{", css, StringComparison.Ordinal);
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

    private const string RemLength = @"(\d+(?:\.\d+)?)rem";

    /// <summary>The floors of a rule's <c>minmax()</c> tracks, in order.</summary>
    private static List<double> Floors(string rule) => Regex.Matches(rule, @"minmax\(" + RemLength)
        .Select(match => double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
        .ToList();

    /// <summary>A spacing token, as the component library declares it. Read
    /// rather than assumed: the gaps and padding in these arithmetic tests are
    /// tokens, and a hard-coded 0.5rem would leave them green while the widths they
    /// compute went quietly wrong.</summary>
    private static double Spacing(string step)
    {
        var tokens = File.ReadAllText(
            RepositoryRoot.File("src", "Core", "Backlog.UI.Components", "wwwroot", "components.css"));
        var match = Regex.Match(tokens, $@"--spacing-{step}:\s*" + RemLength);

        Assert.True(match.Success, $"--spacing-{step} should be declared in components.css.");

        return double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
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
