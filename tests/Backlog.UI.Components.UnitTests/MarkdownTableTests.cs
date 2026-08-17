namespace Backlog.UI.Components.UnitTests;

public sealed class MarkdownTableTests
{
    private const string Simple = """
        | Name | Status |
        | ---- | ------ |
        | One  | Open   |
        | Two  | Done   |
        """;

    [Fact]
    public void A_header_a_delimiter_and_rows_become_a_table()
    {
        var table = Assert.IsType<MdTable>(Assert.Single(MarkdownPreview.ParseDocument(Simple)));

        Assert.Equal(["Name", "Status"], table.Header.Cells.Select(c => MarkdownRender.PlainText(c.Content)));
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal(["Two", "Done"], table.Rows[1].Cells.Select(c => MarkdownRender.PlainText(c.Content)));
    }

    [Fact]
    public void The_delimiter_row_is_what_makes_it_a_table()
    {
        // Prose with a pipe in it is prose. Without this the parser would turn a
        // sentence about "a | b" into a one-column table.
        var blocks = MarkdownPreview.ParseDocument("A sentence with a | pipe in it.\nAnd another line.");

        Assert.IsType<MdParagraph>(Assert.Single(blocks));
    }

    [Fact]
    public void A_divider_is_still_a_divider_and_not_a_one_column_table()
    {
        var blocks = MarkdownPreview.ParseDocument("Text\n\n---\n\nMore text");

        Assert.Contains(blocks, block => block is MdDivider);
        Assert.DoesNotContain(blocks, block => block is MdTable);
    }

    [Theory]
    [InlineData(":---", MdAlign.Left)]
    [InlineData(":---:", MdAlign.Center)]
    [InlineData("---:", MdAlign.Right)]
    [InlineData("---", MdAlign.Default)]
    public void The_colons_on_the_delimiter_set_the_column(string delimiter, MdAlign expected)
    {
        var table = Assert.IsType<MdTable>(Assert.Single(
            MarkdownPreview.ParseDocument($"| H |\n| {delimiter} |\n| v |")));

        Assert.Equal(expected, Assert.Single(table.Alignment));
    }

    [Fact]
    public void Outer_pipes_are_optional()
    {
        var table = Assert.IsType<MdTable>(Assert.Single(
            MarkdownPreview.ParseDocument("Name | Status\n---- | ------\nOne | Open")));

        Assert.Equal(["Name", "Status"], table.Header.Cells.Select(c => MarkdownRender.PlainText(c.Content)));
        Assert.Equal(["One", "Open"], Assert.Single(table.Rows).Cells.Select(c => MarkdownRender.PlainText(c.Content)));
    }

    [Fact]
    public void A_cell_carries_inlines_like_anything_else()
    {
        var table = Assert.IsType<MdTable>(Assert.Single(
            MarkdownPreview.ParseDocument("| H |\n| --- |\n| **bold** and `code` |")));

        var cell = Assert.Single(Assert.Single(table.Rows).Cells);

        Assert.Contains(cell.Content, part => part is MdStrong { Text: "bold" });
        Assert.Contains(cell.Content, part => part is MdCodeSpan { Text: "code" });
    }

    [Fact]
    public void A_short_row_is_kept_rather_than_dropped()
    {
        // Losing a cell loses what the author wrote. A ragged table is a visible
        // problem; a silently truncated one is not.
        var table = Assert.IsType<MdTable>(Assert.Single(
            MarkdownPreview.ParseDocument("| A | B | C |\n| - | - | - |\n| only one |")));

        Assert.Equal(3, table.Header.Cells.Count);
        Assert.Single(Assert.Single(table.Rows).Cells);
    }

    [Fact]
    public void A_blank_line_ends_the_table()
    {
        var blocks = MarkdownPreview.ParseDocument(Simple + "\n\nA paragraph after it.");

        Assert.IsType<MdTable>(blocks[0]);
        Assert.Equal("A paragraph after it.", MarkdownRender.PlainText(Assert.IsType<MdParagraph>(blocks[1]).Content));
    }

    [Fact]
    public void The_view_renders_a_real_table_inside_a_region_that_scrolls()
    {
        using var context = new BunitContext();

        var view = context.Render<MarkdownView>(p => p.Add(v => v.Blocks, MarkdownPreview.ParseDocument(Simple)));

        Assert.Equal(2, view.FindAll(".md-table th").Count);
        Assert.Equal(4, view.FindAll(".md-table td").Count);
        Assert.Equal("col", view.FindAll(".md-table th")[0].GetAttribute("scope"));

        // It scrolls sideways, so a keyboard has to be able to reach it, and a
        // focusable region has to be named.
        var scroll = view.Find(".md-table-scroll");
        Assert.Equal("0", scroll.GetAttribute("tabindex"));
        Assert.Equal("region", scroll.GetAttribute("role"));
        Assert.False(string.IsNullOrWhiteSpace(scroll.GetAttribute("aria-label")));
    }

    [Fact]
    public void Alignment_reaches_the_cells_that_have_one()
    {
        using var context = new BunitContext();

        var view = context.Render<MarkdownView>(p => p.Add(
            v => v.Blocks,
            MarkdownPreview.ParseDocument("| L | C | R |\n| :- | :-: | -: |\n| a | b | c |")));

        var cells = view.FindAll(".md-table td");

        Assert.Contains("md-table__cell--left", cells[0].ClassList);
        Assert.Contains("md-table__cell--center", cells[1].ClassList);
        Assert.Contains("md-table__cell--right", cells[2].ClassList);
    }
}
