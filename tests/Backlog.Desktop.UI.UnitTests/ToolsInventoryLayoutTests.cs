using System.Globalization;
using System.Text.RegularExpressions;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The tools inventory is the shared <c>DataTable</c> wearing this pane's names,
/// and what this fixture holds is the half of it the library does not: the widths
/// its four columns are drawn at, and the fact that below them it stops being four
/// columns.
/// <para>
/// The row was a hand-rolled four-track grid whose <c>minmax()</c> floors add up to
/// 38.5rem — plus three 8px gaps and 16px of row padding, so 656px before a single
/// character is drawn — inside <c>overflow: hidden</c>. At a 375px window the pane
/// has about 338px to give it, so the Available column and the whole Actions column
/// were clipped away: no scrollbar, no wrapping, no page scroll, and every row's
/// Update, Install, Enable, Disable and Remove out of reach. The grid is gone and
/// the library's table is in its place, but the arithmetic that governs it is the
/// same arithmetic, which is why it is still pinned here rather than left to the
/// eye.
/// </para>
/// <para>
/// A container query, for the reason <see cref="FilterBarLayoutTests"/> gives: the
/// pane is not the window. It can be narrow in a wide window, and a viewport
/// threshold would answer a question about the window while the clipping is
/// happening in the pane.
/// </para>
/// <para>
/// The library's own table scrolls sideways rather than restacking, which is right
/// for a grid of figures and wrong for this pane: the reader is looking down a list
/// for one tool and then acting on it, and an action they have to scroll sideways
/// to find is barely better than one that was clipped. So the restack is written
/// here, over the library's element classes, rather than taken from it.
/// </para>
/// </summary>
public sealed class ToolsInventoryLayoutTests
{
    /// <summary>
    /// The step is derived from the column floors and from nothing else:
    /// 10 + 5.5 + 5.5 + 17.5 = 38.5rem, so the restack has to be in force at 39rem,
    /// the first round rem step that still covers it.
    /// <para>The library's <c>--spacing-sm</c> of cell padding is not added on top.
    /// Both sheets set <c>box-sizing: border-box</c> on everything, so a floor
    /// declared as <c>min-width</c> on a cell already contains that padding —
    /// counting it again is what put the step 4rem past the width the columns need,
    /// and restacked a table that still fitted.</para>
    /// </summary>
    private const string RestackStep = "@container tools-inventory (max-width: 39rem) {";

    [Fact]
    public void The_pane_is_what_the_tools_inventory_measures_itself_against()
    {
        var table = Block(Css(), ".tools-inventory {");

        Assert.Contains("container-type: inline-size;", table, StringComparison.Ordinal);
        Assert.Contains("container-name: tools-inventory;", table, StringComparison.Ordinal);
    }

    /// <summary>
    /// Whatever still outgrows its column — an unbreakable version string, an action
    /// whose label is wider than the track — is reachable rather than deleted. The
    /// library's scroll box is what keeps it so, and it is the one half of the
    /// library's choice this pane does take.
    /// </summary>
    [Fact]
    public void Nothing_that_outgrows_the_inventory_is_clipped_out_of_reach()
    {
        var scroll = Block(Components(), ".data-table__scroll {");

        Assert.Contains("overflow-x: auto;", scroll, StringComparison.Ordinal);

        // The rule that hid the overflow outright is what made the clipping silent.
        Assert.DoesNotContain("overflow: hidden;", scroll, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pane_too_narrow_for_four_columns_stacks_the_row_instead()
    {
        var restack = Block(Css(), RestackStep);

        // Every part of a table that makes a row a row of cells, turned back into
        // blocks: after the restack a row is one column of labelled values.
        Assert.Contains("display: block;", restack, StringComparison.Ordinal);
        Assert.Contains(".tools-inventory .data-table__row", restack, StringComparison.Ordinal);

        // The heading row is the one part of a stack that cannot mean anything: it
        // names four columns that are no longer beside each other.
        Assert.Contains(".tools-inventory thead", restack, StringComparison.Ordinal);
        Assert.Contains(".tools-inventory__cell-label", restack, StringComparison.Ordinal);
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

        Assert.Contains("display: none;", Block(css, ".tools-inventory__cell-label {"), StringComparison.Ordinal);

        var restack = Block(css, RestackStep);
        var headRow = restack.IndexOf(".tools-inventory thead", StringComparison.Ordinal);
        var labels = restack.IndexOf(".tools-inventory__cell-label", StringComparison.Ordinal);

        Assert.True(headRow >= 0 && labels >= 0, "The restack should govern both the heading row and the in-cell labels.");
        Assert.Contains("display: none;", BlockAt(restack, headRow), StringComparison.Ordinal);
        Assert.Contains("display: inline;", BlockAt(restack, labels), StringComparison.Ordinal);
    }

    /// <summary>
    /// The cells this pane writes hold four lines of text between them — a name, a
    /// host badge, a meta line and the catalog's own note — and the library's cells
    /// do not wrap, because a table of paths and branch names is unreadable when
    /// they do. So the pane says so for its own cells rather than leaving the note
    /// to run the row off the side of the table.
    /// </summary>
    [Fact]
    public void The_cells_this_pane_writes_are_allowed_to_wrap()
    {
        Assert.Contains("white-space: nowrap;", Block(Components(), ".data-table__table th,"), StringComparison.Ordinal);
        Assert.Contains("white-space: normal;", Block(Css(), ".tools-inventory .data-table__table td {"), StringComparison.Ordinal);
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
    /// the point: every cell is its own grid, so nothing synchronises a
    /// content-sized track across rows. Three rows whose toggles happen to measure
    /// the same are aligned by coincidence — and a marketplace row, which has no
    /// toggle at all, would collapse that track to nothing and move the status
    /// slot's edge 6rem with it.</para>
    /// </summary>
    [Fact]
    public void The_row_actions_land_in_the_same_places_in_every_row()
    {
        var css = Css();
        var actions = Block(css, ".tools-inventory__actions {");

        Assert.Contains("display: grid;", actions, StringComparison.Ordinal);

        // Reserved, not auto: an auto track is measured per row, and a row with no
        // toggle in it would collapse that track and take the alignment with it.
        Assert.Contains("grid-template-columns: minmax(0, 1fr) 6rem 4rem;", actions, StringComparison.Ordinal);

        // The wrap is what let them fall onto a second line under a long status.
        Assert.DoesNotContain("flex-wrap", actions, StringComparison.Ordinal);

        // A column each, stated rather than inherited from the order they arrive
        // in: a marketplace row has no toggle and a read-only host no Remove, and
        // under auto-placement the survivors would slide into the empty track.
        Assert.Contains("grid-column: 1;", Block(css, ".tools-inventory__action {"), StringComparison.Ordinal);

        Assert.Contains("grid-column: 2;", Block(css, ".tools-inventory__toggle {"), StringComparison.Ordinal);

        var remove = Block(css, ".tools-inventory__remove {");
        Assert.Contains("grid-column: 3;", remove, StringComparison.Ordinal);
        Assert.Contains("justify-self: end;", remove, StringComparison.Ordinal);
    }

    /// <summary>
    /// The status slot is whatever is left of the actions column after the two
    /// reserved ones, and it has to hold the widest thing that goes in it — an
    /// Install button or the "Done here" checkbox, both around 5.5rem here.
    /// <para>Pinned because the failure is silent. The first track is
    /// <c>minmax(0, …)</c>, so a slot too narrow neither scrolls nor clips: the
    /// button simply overlaps the toggle beside it. And the actions column sits at
    /// its floor for every table width up to about 70rem, which is most of them,
    /// so it is the common case and not an edge.</para>
    /// </summary>
    [Fact]
    public void The_status_slot_has_room_left_after_the_reserved_tracks()
    {
        var css = Css();

        var actionsFloor = Floor(css, ".tools-inventory__col-actions {");
        var reserved = Regex.Matches(Block(css, ".tools-inventory__actions {"), RemLength)
            .Select(match => double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .Sum();

        // The floor is a border-box width, so the cell's own inline padding comes
        // out of it before the tracks do.
        var status = actionsFloor - (2 * Spacing("sm")) - reserved - (2 * Spacing("xs"));

        Assert.True(
            status >= 6,
            $"A {actionsFloor}rem actions column less its own padding, {reserved}rem of reserved "
            + $"slots and two gaps leaves {status}rem for the status, which does not hold an "
            + "Install button.");
    }

    /// <summary>
    /// The step is arithmetic, not taste, so it is computed here rather than
    /// restated: the four column floors, rounded up to the next rem. Their own cell
    /// padding is inside them already — see <see cref="RestackStep"/>.
    /// <para>Pinned because the two numbers have to move together and live a
    /// hundred lines apart. Widening a column without moving the step leaves a band
    /// where the row is wider than its box — which is the clipping this fixture
    /// exists for, reintroduced by an edit that looked local.</para>
    /// </summary>
    [Fact]
    public void The_restack_step_is_the_column_arithmetic_and_nothing_else()
    {
        var css = Css();

        // The two version columns share one rule, because they are the same column
        // asked twice — what is installed, and what is available.
        var floors = Floor(css, ".tools-inventory__col-tool {")
            + (2 * Floor(css, ".tools-inventory__col-version {"))
            + Floor(css, ".tools-inventory__col-actions {");

        // The floors are the whole sum: `box-sizing: border-box` puts the library's
        // --spacing-sm either side of each cell inside the min-width, not beside it.
        var step = Math.Ceiling(floors).ToString(CultureInfo.InvariantCulture);

        Assert.Contains($"@container tools-inventory (max-width: {step}rem) {{", css, StringComparison.Ordinal);
    }

    /// <summary>The clipping was a fact about the pane's width, and a media query
    /// cannot see it. One that fired on the window would restack a wide pane in a
    /// narrow window and leave a narrow pane in a wide one exactly as it was.</summary>
    [Fact]
    public void No_viewport_query_governs_the_tools_inventory()
    {
        var offenders = MediaBlocks(Css())
            .Where(block => block.Contains(".tools-inventory", StringComparison.Ordinal))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "A viewport query is still styling the tools inventory, which sizes itself off the pane it is in:\n"
            + string.Join("\n\n", offenders));
    }

    /// <summary>
    /// A folded group is hidden by the attribute the disclosure sets, and the
    /// library's own <c>display: grid</c> on that same element outranks the
    /// browser's rule for <c>hidden</c> — an author declaration beats the user
    /// agent's whatever their specificity. So the block says it itself, beside the
    /// display that would otherwise leave a closed group on screen.
    /// </summary>
    [Fact]
    public void A_folded_group_is_actually_hidden()
    {
        Assert.Contains("display: none;", Block(Components(), ".data-table[hidden] {"), StringComparison.Ordinal);
    }

    /// <summary>
    /// The hand-rolled grid is retired rather than left behind: a stylesheet keeps
    /// working with dead rules in it, and the next reader cannot tell which of the
    /// two tables in the sheet the pane is actually drawing.
    /// </summary>
    [Fact]
    public void The_grid_the_library_replaced_is_gone_from_the_repository()
    {
        var offenders = Directory
            .EnumerateFiles(RepositoryRoot.Directory("src"), "*.*", SearchOption.AllDirectories)
            .Where(file => file.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)
                || file.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
            // Source only. Build output holds copies of the sheet this walks, so a
            // stale bin/ or obj/ would fail the fixture naming a path no edit fixes.
            .Where(file => !file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(segment => segment is "bin" or "obj"))
            .Where(file => File.ReadAllText(file).Contains("tools-table", StringComparison.Ordinal))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "These files still name the retired tools grid:\n" + string.Join('\n', offenders));
    }

    private const string RemLength = @"(\d+(?:\.\d+)?)rem";

    /// <summary>A column's floor, as its own rule declares it.</summary>
    private static double Floor(string css, string opening)
    {
        var match = Regex.Match(Block(css, opening), @"min-width:\s*" + RemLength);

        Assert.True(match.Success, $"{opening} should declare the width its column needs.");

        return double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    /// <summary>A spacing token, as the component library declares it. Read
    /// rather than assumed: the gaps and padding in these arithmetic tests are
    /// tokens, and a hard-coded 0.5rem would leave them green while the widths they
    /// compute went quietly wrong.</summary>
    private static double Spacing(string step)
    {
        var match = Regex.Match(Components(), $@"--spacing-{step}:\s*" + RemLength);

        Assert.True(match.Success, $"--spacing-{step} should be declared in components.css.");

        return double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    private static string Css() => File.ReadAllText(
        RepositoryRoot.File("src", "App", "Backlog.Desktop.UI", "wwwroot", "app.css")).Replace("\r\n", "\n");

    private static string Components() => File.ReadAllText(
        RepositoryRoot.File("src", "Core", "Backlog.UI.Components", "wwwroot", "components.css")).Replace("\r\n", "\n");

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
