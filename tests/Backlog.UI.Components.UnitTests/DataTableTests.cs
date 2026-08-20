using Backlog.UI.Components.Data;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The component owns the frame and the caller owns the cells, so what is asserted
/// here is the frame: the heading row, the section headings and their counts, the
/// keying, and the empty state that replaces a table rather than sitting inside one.
/// </summary>
public sealed class DataTableTests
{
    private sealed record Row(string Id, string Name);

    private static readonly DataTableColumn[] Columns = [new("Name"), new("Id", "numeric")];

    private static readonly Row[] Sample =
    [
        new("1", "first"),
        new("2", "second")
    ];

    [Fact]
    public void The_headings_come_from_the_columns_and_carry_their_classes()
    {
        using var context = new BunitContext();

        var table = Render(context, parameters => parameters.Add(c => c.Items, Sample));
        var headings = table.FindAll("thead th");

        Assert.Equal(["Name", "Id"], headings.Select(heading => heading.TextContent.Trim()));

        // A heading is the one cell the caller does not write, so without a class on
        // the column there would be no way to make a heading agree with the cells
        // under it.
        Assert.Contains("numeric", headings[1].ClassList);
        Assert.Equal("col", headings[0].GetAttribute("scope"));
    }

    [Fact]
    public void Flat_rows_are_one_nameless_section()
    {
        using var context = new BunitContext();

        var table = Render(context, parameters => parameters.Add(c => c.Items, Sample));

        // One tbody, and no group heading in it: an ungrouped table is the same
        // shape as a grouped one with a single unnamed section, not a different one.
        Assert.Single(table.FindAll("tbody"));
        Assert.Empty(table.FindAll(".data-table__group"));
        Assert.Equal(2, table.FindAll("tbody tr").Count);
    }

    [Fact]
    public void A_section_heading_labels_its_columns_and_counts_its_rows()
    {
        using var context = new BunitContext();

        var table = Render(context, parameters => parameters
            .Add(c => c.Sections, Sections)
            .Add(c => c.RowNoun, "session"));

        var headings = table.FindAll(".data-table__group-heading");

        Assert.Equal(2, headings.Count);

        // scope="colgroup" on a th, not a td dressed as a heading: the group is
        // announced as a reader crosses into it rather than inferred from a bold row.
        Assert.All(headings, heading => Assert.Equal("colgroup", heading.GetAttribute("scope")));
        Assert.All(headings, heading => Assert.Equal(Columns.Length.ToString(), heading.GetAttribute("colspan")));

        Assert.Equal("DEV-LAPTOP", headings[0].QuerySelector(".data-table__group-name")!.TextContent.Trim());

        // The count is why grouping is worth having, and it is pluralised from the
        // noun the caller named rather than from a word this component picked.
        Assert.Equal("1 session", headings[0].QuerySelector(".data-table__group-count")!.TextContent.Trim());
        Assert.Equal("2 sessions", headings[1].QuerySelector(".data-table__group-count")!.TextContent.Trim());
    }

    [Fact]
    public void A_section_is_a_tbody_of_its_own()
    {
        using var context = new BunitContext();

        var table = Render(context, parameters => parameters.Add(c => c.Sections, Sections));

        // Semantically what a group of rows is, and what the stylesheet hangs the
        // section separator on without needing a modifier on the first row.
        Assert.Equal(2, table.FindAll("tbody").Count);
    }

    [Fact]
    public void Sections_and_rows_render_in_the_order_they_were_given()
    {
        using var context = new BunitContext();

        // Deliberately not alphabetical. Ordering belongs to whatever control the
        // reader used to ask for it, and a component that re-sorted its input would
        // be a second definition of that order, free to disagree with the first.
        DataTableSection<Row>[] unsorted =
        [
            new("Zulu", [new("9", "last")]),
            new("Alpha", [new("1", "first")])
        ];

        var table = Render(context, parameters => parameters.Add(c => c.Sections, unsorted));

        Assert.Equal(
            ["Zulu", "Alpha"],
            table.FindAll(".data-table__group-name").Select(name => name.TextContent.Trim()));
    }

    [Fact]
    public void An_empty_table_is_replaced_by_what_is_missing_and_why()
    {
        using var context = new BunitContext();

        var table = Render(context, parameters => parameters
            .Add(c => c.Items, Array.Empty<Row>())
            .Add(c => c.EmptyMessage, "No sessions on this PC.")
            .Add(c => c.EmptyDescription, "Neither agent has left a record yet."));

        // Not a heading row over nothing, which reads as a broken feature.
        Assert.Empty(table.FindAll("table"));
        Assert.Contains("No sessions on this PC.", table.Markup);
        Assert.Contains("Neither agent has left a record yet.", table.Markup);
    }

    [Fact]
    public void An_empty_set_of_sections_is_empty_too()
    {
        using var context = new BunitContext();

        var table = Render(context, parameters => parameters.Add(c => c.Sections, Array.Empty<DataTableSection<Row>>()));

        Assert.Empty(table.FindAll("table"));
    }

    /// <summary>
    /// Sections win over items rather than being merged with them, so a caller
    /// switching a grouping control on does not have to remember to clear the other
    /// parameter — and cannot show the same row twice by forgetting.
    /// </summary>
    [Fact]
    public void Sections_take_precedence_over_flat_items()
    {
        using var context = new BunitContext();

        var table = Render(context, parameters => parameters
            .Add(c => c.Items, Sample)
            .Add(c => c.Sections, Sections));

        Assert.Equal(3, table.FindAll(".data-table__row").Count);
    }

    /// <summary>
    /// The base class replaces every part class with it, so a host with its own table
    /// naming takes the whole block rather than half of it.
    /// </summary>
    [Fact]
    public void A_host_can_rename_the_whole_block()
    {
        using var context = new BunitContext();

        var table = Render(context, parameters => parameters
            .Add(c => c.Items, Sample)
            .Add(c => c.BaseClass, "runs"));

        Assert.Single(table.FindAll(".runs"));
        Assert.Single(table.FindAll(".runs__scroll"));
        Assert.Single(table.FindAll(".runs__table"));
        Assert.Empty(table.FindAll(".data-table"));
    }

    private static readonly DataTableSection<Row>[] Sections =
    [
        new("DEV-LAPTOP", [new("3", "laptop one")]),
        new("DEV-TOWER", [.. Sample])
    ];

    private static IRenderedComponent<DataTable<Row>> Render(
        BunitContext context,
        Action<ComponentParameterCollectionBuilder<DataTable<Row>>> configure) =>
        context.Render<DataTable<Row>>(parameters =>
        {
            parameters
                .Add(c => c.Columns, Columns)
                .Add(c => c.TestId, "runs")
                .Add(c => c.Row, Cells);

            configure(parameters);
        });

    /// <summary>One row's cells, which is the half of the table the caller owns.</summary>
    private static readonly RenderFragment<Row> Cells = row => builder =>
    {
        builder.OpenElement(0, "td");
        builder.AddContent(1, row.Name);
        builder.CloseElement();
        builder.OpenElement(2, "td");
        builder.AddContent(3, row.Id);
        builder.CloseElement();
    };
}
