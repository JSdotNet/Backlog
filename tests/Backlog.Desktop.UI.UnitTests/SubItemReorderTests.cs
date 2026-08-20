
namespace Backlog.Desktop.UI.UnitTests;

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
    public void Moving_a_level_two_sub_item_moves_its_level_three_descendants_with_it()
    {
        const string raw =
            "# Title\n\n" +
            "## Parent A\n" +
            "Parent notes.\n\n" +
            "### Child A\n" +
            "Child notes.\n\n" +
            "## Parent B\n" +
            "Sibling notes.\n";

        var moved = EntryTextParser.MoveSubItem(raw, 0, 2);

        Assert.Equal(
            "# Title\n\n" +
            "## Parent B\n" +
            "Sibling notes.\n\n" +
            "## Parent A\n" +
            "Parent notes.\n\n" +
            "### Child A\n" +
            "Child notes.\n",
            moved);
    }

    [Fact]
    public void Moving_a_level_three_sub_item_cannot_escape_its_parent_scope()
    {
        const string raw =
            "# Title\n\n" +
            "## Parent A\n\n" +
            "### Child A\n\n" +
            "## Parent B\n";

        Assert.Equal(raw, EntryTextParser.MoveSubItem(raw, 1, 2));
    }

    [Fact]
    public void Leaves_the_body_above_the_sub_items_alone()
    {
        var moved = EntryTextParser.MoveSubItem(ThreeSubItems, 0, 2);
        Assert.StartsWith("# Ship the importer\n`task` `*medium` `!draft`\n\nSome prose about the whole thing.\n\n## ", moved, StringComparison.Ordinal);
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

/// <summary>
/// Deleting a sub-item is the same kind of rewrite as re-ranking one, and it is
/// written the same way here: the markdown before and the markdown after, notes
/// and all. What is worth asserting exactly is the whitespace — a removal that
/// left the blank line its chapter used to sit above, or took the one in front of
/// the chapter after it, would be churn on every step anybody deleted.
/// </summary>
public class SubItemRemovalTests
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
    public void Removes_the_first_sub_item_with_its_notes()
    {
        var removed = EntryTextParser.RemoveSubItem(ThreeSubItems, 0);

        Assert.Equal(
            "# Ship the importer\n" +
            "`task` `*medium` `!draft`\n" +
            "\n" +
            "Some prose about the whole thing.\n" +
            "\n" +
            "## Map the columns\n" +
            "\n" +
            "## Write the rows\n" +
            "Notes for writing.\n",
            removed);
    }

    [Fact]
    public void Removes_a_sub_item_from_the_middle_and_closes_the_gap()
    {
        // The chapter with no notes of its own, which is also the one whose blank
        // lines belong to its neighbours rather than to it.
        var removed = EntryTextParser.RemoveSubItem(ThreeSubItems, 1);

        Assert.Equal(
            "# Ship the importer\n" +
            "`task` `*medium` `!draft`\n" +
            "\n" +
            "Some prose about the whole thing.\n" +
            "\n" +
            "## Read the file\n" +
            "Notes for reading.\n" +
            "\n" +
            "## Write the rows\n" +
            "Notes for writing.\n",
            removed);
    }

    [Fact]
    public void Removes_the_last_sub_item_and_its_notes_go_with_it()
    {
        var removed = EntryTextParser.RemoveSubItem(ThreeSubItems, 2);

        Assert.Equal(
            "# Ship the importer\n" +
            "`task` `*medium` `!draft`\n" +
            "\n" +
            "Some prose about the whole thing.\n" +
            "\n" +
            "## Read the file\n" +
            "Notes for reading.\n" +
            "\n" +
            "## Map the columns\n",
            removed);
    }

    [Fact]
    public void Removing_the_only_sub_item_leaves_the_entry_itself()
    {
        const string raw =
            "# Title\n" +
            "`task`\n" +
            "\n" +
            "Just prose.\n" +
            "\n" +
            "## The only step\n" +
            "Its notes.\n";

        Assert.Equal(
            "# Title\n" +
            "`task`\n" +
            "\n" +
            "Just prose.\n",
            EntryTextParser.RemoveSubItem(raw, 0));
    }

    [Fact]
    public void Leaves_the_prose_above_the_sub_items_alone()
    {
        // The one thing a step delete must never reach. The prose is the entry
        // talking about itself, and it is written in the same document.
        var removed = EntryTextParser.RemoveSubItem(ThreeSubItems, 0);

        Assert.Contains("Some prose about the whole thing.", removed, StringComparison.Ordinal);
        Assert.StartsWith("# Ship the importer\n`task` `*medium` `!draft`\n\nSome prose", removed, StringComparison.Ordinal);
    }

    [Fact]
    public void Removing_a_level_two_sub_item_takes_its_level_three_children_with_it()
    {
        // The same group MoveSubItem moves. A parent that vanished on its own would
        // leave its children under whatever chapter now precedes them, which is a
        // re-parenting nobody asked for.
        const string raw =
            "# Title\n\n" +
            "## Parent A\n" +
            "Parent notes.\n\n" +
            "### Child A\n" +
            "Child notes.\n\n" +
            "## Parent B\n" +
            "Sibling notes.\n";

        Assert.Equal(
            "# Title\n\n" +
            "## Parent B\n" +
            "Sibling notes.\n",
            EntryTextParser.RemoveSubItem(raw, 0));
    }

    [Fact]
    public void Removing_a_level_three_sub_item_leaves_its_parent_standing()
    {
        const string raw =
            "# Title\n\n" +
            "## Parent A\n" +
            "Parent notes.\n\n" +
            "### Child A\n" +
            "Child notes.\n\n" +
            "## Parent B\n";

        Assert.Equal(
            "# Title\n\n" +
            "## Parent A\n" +
            "Parent notes.\n\n" +
            "## Parent B\n",
            EntryTextParser.RemoveSubItem(raw, 1));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void Refuses_an_index_that_names_no_sub_item(int subItemIndex)
    {
        Assert.Equal(ThreeSubItems, EntryTextParser.RemoveSubItem(ThreeSubItems, subItemIndex));
    }

    [Fact]
    public void Survives_an_entry_with_no_sub_items()
    {
        const string raw = "# Title\n`task`\n\nJust prose.\n";
        Assert.Equal(raw, EntryTextParser.RemoveSubItem(raw, 0));
    }

    [Fact]
    public void What_is_left_reads_back_as_the_steps_that_were_not_deleted()
    {
        // The round trip, because the text is only the medium: what the pane draws
        // is the parse, and a removal that produced text nobody could read back
        // would be a removal that lost a step it did not touch.
        const string raw =
            "# Title\n\n" +
            "## [x] Done already\n" +
            "Notes for the finished one.\n\n" +
            "## Halfway\n\n" +
            "## [ ] Still to do\n";

        var parsed = EntryTextParser.Parse(EntryTextParser.RemoveSubItem(raw, 1));

        Assert.Equal(["Done already", "Still to do"], parsed.SubItems.Select(item => item.Title));
        Assert.Equal([true, false], parsed.SubItems.Select(item => item.Done));
        Assert.Equal(
            "Notes for the finished one.",
            EntryTextParser.GetSubItemNote(EntryTextParser.RemoveSubItem(raw, 1), 0));
    }
}
