namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The table as its own component. <c>MarkdownTableTests</c> holds what makes a
/// table and what its cells say; this holds the two things that are the
/// component's own — that the wrapper and not the table is the region a keyboard
/// arrives at, and that alignment is read defensively enough that a ragged row
/// cannot throw.
/// </summary>
public sealed class MarkdownTableViewTests
{
    private static MdTable Table =>
        new(
            new MdTableRow(
            [
                new MdTableCell(MarkdownPreview.ParseInlines("Left")),
                new MdTableCell(MarkdownPreview.ParseInlines("Middle")),
                new MdTableCell(MarkdownPreview.ParseInlines("Right"))
            ]),
            [
                new MdTableRow(
                [
                    new MdTableCell(MarkdownPreview.ParseInlines("one")),
                    new MdTableCell(MarkdownPreview.ParseInlines("two")),
                    new MdTableCell(MarkdownPreview.ParseInlines("three")),
                    new MdTableCell(MarkdownPreview.ParseInlines("a fourth nobody aligned"))
                ])
            ],
            [MdAlign.Left, MdAlign.Center, MdAlign.Right]);

    [Fact]
    public void The_wrapper_is_the_region_and_the_table_stays_a_table()
    {
        using var context = new BunitContext();

        var view = context.Render<MarkdownTableView>(parameters => parameters
            .Add(table => table.Table, Table));

        var wrapper = view.Find("div.md-table-scroll");

        // A table has a width of its own and cannot be reflowed to fit, so the
        // wrapper scrolls. Labelling the wrapper rather than the table is what
        // lets a keyboard reach the overflow without the table stopping being one.
        Assert.Equal("region", wrapper.GetAttribute("role"));
        Assert.Equal("0", wrapper.GetAttribute("tabindex"));
        Assert.Equal("Table", wrapper.GetAttribute("aria-label"));
        Assert.NotNull(view.Find("div.md-table-scroll > table.md-table"));
        Assert.Null(view.Find("table").GetAttribute("role"));
    }

    [Fact]
    public void A_column_nobody_aligned_takes_an_empty_class_rather_than_none()
    {
        using var context = new BunitContext();

        var view = context.Render<MarkdownTableView>(parameters => parameters
            .Add(table => table.Table, Table));

        Assert.Equal(
            ["md-table__cell--left", "md-table__cell--center", "md-table__cell--right"],
            view.FindAll("th").Select(cell => cell.GetAttribute("class")));

        // The fourth cell is past the end of the delimiter row. It reads
        // defensively rather than throwing, and it emits the empty class the read
        // view has always emitted rather than dropping the attribute.
        Assert.Equal(
            ["md-table__cell--left", "md-table__cell--center", "md-table__cell--right", string.Empty],
            view.FindAll("td").Select(cell => cell.GetAttribute("class")));
    }

    [Fact]
    public void One_hook_renames_all_three_alignment_modifiers()
    {
        using var context = new BunitContext();

        var view = context.Render<MarkdownTableView>(parameters => parameters
            .Add(table => table.Table, Table)
            .Add(table => table.ScrollCssClass, "pane-table-scroll")
            .Add(table => table.TableCssClass, "pane-table")
            .Add(table => table.CellCssClass, "pane-table__cell")
            .Add(table => table.AriaLabel, "Token table"));

        Assert.Equal("pane-table-scroll", view.Find("div").GetAttribute("class"));
        Assert.Equal("Token table", view.Find("div").GetAttribute("aria-label"));
        Assert.Equal("pane-table", view.Find("table").GetAttribute("class"));

        // A host renaming one modifier renames all three, so the stem is the hook
        // rather than three names that could be given inconsistently.
        Assert.Equal(
            ["pane-table__cell--left", "pane-table__cell--center", "pane-table__cell--right"],
            view.FindAll("th").Select(cell => cell.GetAttribute("class")));
    }

    [Fact]
    public void A_header_cell_says_which_column_it_heads()
    {
        using var context = new BunitContext();

        var view = context.Render<MarkdownTableView>(parameters => parameters
            .Add(table => table.Table, Table));

        Assert.All(view.FindAll("th"), cell => Assert.Equal("col", cell.GetAttribute("scope")));
    }
}
