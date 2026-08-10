using Backlog.UI.Services;

namespace Backlog.UI.Tests;

/// <summary>
/// Re-ranking a sub-item is a rewrite of the entry's own text — that is the only
/// place the order lives — so these tests are written as the markdown before and
/// after the move, notes and all.
/// </summary>
public class SubItemReorderTests
{
    private const string ThreeSubItems =
        "# Ship the importer\n" +
        "`task` `*medium` `!draft`\n" +
        "\n" +
        "Some prose about the whole thing.\n" +
        "\n" +
        "## Read the file\n" +
        "Notes for reading.\n" +
        "\n" +
        "## Map the columns\n" +
        "\n" +
        "## Write the rows\n" +
        "Notes for writing.\n";

    [Fact]
    public void Finds_one_span_per_sub_item()
    {
        var spans = EntryTextParser.LocateSubItems(ThreeSubItems);

        Assert.Equal(3, spans.Count);
    }

    [Fact]
    public void Ignores_headings_inside_fenced_code()
    {
        var raw = "# Title\n\n```\n## not a sub-item\n```\n\n## A real one\n";

        var spans = EntryTextParser.LocateSubItems(raw);

        Assert.Single(spans);
    }

    [Fact]
    public void Moves_a_sub_item_up_with_its_notes()
    {
        var moved = EntryTextParser.MoveSubItem(ThreeSubItems, 2, 0);

        Assert.Equal(
            "# Ship the importer\n" +
            "`task` `*medium` `!draft`\n" +
            "\n" +
            "Some prose about the whole thing.\n" +
            "\n" +
            "## Write the rows\n" +
            "Notes for writing.\n" +
            "\n" +
            "## Read the file\n" +
            "Notes for reading.\n" +
            "\n" +
            "## Map the columns\n",
            moved);
    }

    [Fact]
    public void Moves_a_sub_item_down()
    {
        var moved = EntryTextParser.MoveSubItem(ThreeSubItems, 0, 1);
        var titles = EntryTextParser.Parse(moved).SubItems.Select(s => s.Title).ToArray();

        Assert.Equal(["Map the columns", "Read the file", "Write the rows"], titles);
    }

    [Fact]
    public void Leaves_the_body_above_the_sub_items_alone()
    {
        var moved = EntryTextParser.MoveSubItem(ThreeSubItems, 0, 2);

        Assert.StartsWith(
            "# Ship the importer\n`task` `*medium` `!draft`\n\nSome prose about the whole thing.\n\n## ",
            moved,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Does_not_multiply_the_blank_lines_between_sub_items()
    {
        var text = ThreeSubItems;
        for (var i = 0; i < 6; i++)
        {
            text = EntryTextParser.MoveSubItem(text, 0, 2);
        }

        Assert.DoesNotContain("\n\n\n", text, StringComparison.Ordinal);
        Assert.Equal(3, EntryTextParser.LocateSubItems(text).Count);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 1)]
    [InlineData(0, 3)]
    public void Refuses_a_move_that_goes_nowhere(int from, int to)
    {
        var moved = EntryTextParser.MoveSubItem(ThreeSubItems, from, to);

        Assert.Equal(ThreeSubItems, moved);
    }

    [Fact]
    public void Survives_an_entry_with_no_sub_items()
    {
        const string raw = "# Title\n`task`\n\nJust prose.\n";

        Assert.Equal(raw, EntryTextParser.MoveSubItem(raw, 0, 1));
    }
}
