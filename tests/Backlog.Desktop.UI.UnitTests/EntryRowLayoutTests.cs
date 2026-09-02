using Backlog.Modules.Tasks.DomainModels;

namespace Backlog.Desktop.UI.UnitTests;

public sealed class EntryRowLayoutTests
{
    /// <summary>
    /// Whether there is more to an entry than its title.
    /// <para>
    /// This used to be half of a one-line-versus-expanded layout decision, and that
    /// decision is gone: every row in the list is one line, and the expansion is the
    /// detail pane beside it. What the predicate is still for is the note mark on
    /// the row — the only thing on a folded row that says it is worth opening — so
    /// it is asserted here on its own rather than through a layout that no longer
    /// exists.
    /// </para>
    /// </summary>
    [Fact]
    public void A_title_and_metadata_only_entry_has_nothing_more_to_show()
    {
        var row = new EntryRow
        {
            RawText = "# Ask about the trial length\n`task` `*medium` `!draft` `#sync`\n"
        };

        Assert.False(row.HasExpandableContent);
        Assert.Contains("sync", row.PreviewTags);
    }

    [Fact]
    public void An_entry_with_a_body_has_more_than_its_title()
    {
        var row = new EntryRow
        {
            RawText = "# Ship the offline sync spike\n`task` `*high` `!draft`\n\nKeep working on a train.\n"
        };

        Assert.True(row.HasExpandableContent);
    }

    [Fact]
    public void Checklist_items_toggle_in_the_raw_markdown()
    {
        const string raw =
            "# Prepare review\n" +
            "`task`\n\n" +
            "- [x] Book the room\n" +
            "- [ ] Write the one-slide summary\n";

        var toggled = EntryTextParser.ToggleChecklistItem(raw, 1);

        Assert.Contains("- [x] Write the one-slide summary", toggled);
        Assert.Contains("- [x] Book the room", toggled);
    }

    [Fact]
    public void Checklist_toggle_ignores_code_fences()
    {
        const string raw =
            "# Prepare review\n\n" +
            "```\n" +
            "- [ ] not a task\n" +
            "```\n\n" +
            "- [ ] Real task\n";

        var toggled = EntryTextParser.ToggleChecklistItem(raw, 0);

        Assert.Contains("- [ ] not a task", toggled);
        Assert.Contains("- [x] Real task", toggled);
    }

    [Fact]
    public void Checklist_toggle_skips_a_checkbox_line_with_no_text()
    {
        // The read view renders `- [ ]` with nothing after it as a plain bullet,
        // so it hands out no index for it. Counting it here would make every
        // index after it name the line above the one that was clicked.
        const string raw =
            "- [ ] \n" +
            "- [ ] Real task\n";

        var toggled = EntryTextParser.ToggleChecklistItem(raw, 0);

        Assert.Contains("- [ ] \n", toggled);
        Assert.Contains("- [x] Real task", toggled);
    }

    [Fact]
    public void Sub_item_heading_checkboxes_toggle_in_the_raw_markdown()
    {
        const string raw =
            "# Prepare review\n\n" +
            "## [x] First sub-item\n\n" +
            "## [ ] Second sub-item\n";

        var toggled = EntryTextParser.ToggleSubItem(raw, 1);

        Assert.Contains("## [x] First sub-item", toggled);
        Assert.Contains("## [x] Second sub-item", toggled);
    }

    [Fact]
    public void Plain_sub_item_headings_are_not_given_a_checkbox_when_toggled()
    {
        const string raw =
            "# Prepare review\n\n" +
            "## Plain sub-item\n";

        var toggled = EntryTextParser.ToggleSubItem(raw, 0);

        Assert.Equal(raw, toggled);
    }

    [Fact]
    public void Nested_sub_item_heading_checkboxes_toggle_in_the_raw_markdown()
    {
        const string raw =
            "# Prepare review\n\n" +
            "## Parent sub-item\n\n" +
            "### [ ] Child sub-item\n";

        var toggled = EntryTextParser.ToggleSubItem(raw, 1);

        Assert.Contains("### [x] Child sub-item", toggled);
        Assert.Contains("## Parent sub-item", toggled);
    }

    [Fact]
    public void Rendered_sub_items_remember_whether_their_heading_had_a_checkbox()
    {
        var row = new EntryRow
        {
            RawText =
                "# Prepare review\n\n" +
                "## [ ] Checkbox sub-item\n\n" +
                "## Plain sub-item\n"
        };

        Assert.True(row.PreviewSubItems[0].HasCheckbox);
        Assert.False(row.PreviewSubItems[0].Done);
        Assert.False(row.PreviewSubItems[1].HasCheckbox);
    }

    [Fact]
    public void Status_updates_rewrite_the_metadata_line_only()
    {
        const string raw =
            "# Prepare review\n" +
            "`task` `*high` `!draft` `@repos` `#sync`\n\n" +
            "Keep #bodytag here.\n";

        var rewritten = EntryTextParser.WithStatus(raw, EntryStatus.Ready);

        Assert.Contains("`!ready`", rewritten);
        Assert.Contains("`@repos`", rewritten);
        Assert.Contains("`#sync`", rewritten);
        Assert.Contains("Keep #bodytag here.", rewritten);
    }

    [Fact]
    public void Tag_updates_replace_metadata_tags_without_touching_area_or_body()
    {
        const string raw =
            "# Prepare review\n" +
            "`task` `*high` `!draft` `@repos` `#sync` `#old`\n\n" +
            "Keep #bodytag here.\n";

        var rewritten = EntryTextParser.WithTags(raw, "#desktop spacing desktop");

        Assert.Contains("`@repos`", rewritten);
        Assert.Contains("`#desktop`", rewritten);
        Assert.Contains("`#spacing`", rewritten);
        Assert.DoesNotContain("`#old`", rewritten);
        Assert.Contains("Keep #bodytag here.", rewritten);
    }

    [Fact]
    public void Metadata_updates_insert_a_canonical_line_when_one_is_missing()
    {
        const string raw = "# Prepare review\n\nKeep notes here.\n";

        var rewritten = EntryTextParser.WithTags(raw, "#desktop");

        Assert.StartsWith("# Prepare review\n`task` `*medium` `!draft` `#desktop`\n", rewritten);
        Assert.Contains("Keep notes here.", rewritten);
    }

    [Fact]
    public void Tag_input_values_display_plain_tag_names_for_datalist_selection()
    {
        Assert.Equal("desktop qa-new-tag", EntryTextParser.FormatTagsInput(["desktop", "qa-new-tag"]));
    }

    [Fact]
    public void Metadata_tag_preview_excludes_body_tags()
    {
        var row = new EntryRow
        {
            RawText =
                "# Prepare review\n" +
                "`task` `*high` `!draft` `@repos` `#sync`\n\n" +
                "Keep #bodytag here.\n"
        };

        Assert.Equal(new[] { "sync" }, row.PreviewMetadataTags);
        Assert.Contains("bodytag", row.PreviewTags);
    }

    [Fact]
    public void Type_priority_and_area_updates_rewrite_canonical_metadata()
    {
        const string raw =
            "# Prepare review\n" +
            "`task` `*medium` `!draft` `@repos` `#sync`\n\n" +
            "Keep #bodytag here.\n";

        var typed = EntryTextParser.WithType(raw, EntryType.Prompt);
        var prioritized = EntryTextParser.WithPriority(typed, Priority.High);
        var filed = EntryTextParser.WithArea(prioritized, "Backlog");

        Assert.Contains("`prompt`", filed);
        Assert.Contains("`*high`", filed);
        Assert.Contains("`!draft`", filed);
        Assert.Contains("`@backlog`", filed);
        Assert.Contains("`#sync`", filed);
        Assert.DoesNotContain("`task`", filed);
        Assert.DoesNotContain("`*medium`", filed);
        Assert.DoesNotContain("`@repos`", filed);
        Assert.Contains("Keep #bodytag here.", filed);
    }

    [Fact]
    public void Area_update_can_clear_repository_metadata()
    {
        const string raw =
            "# Prepare review\n" +
            "`task` `*medium` `!draft` `@repos` `#sync`\n";

        var rewritten = EntryTextParser.WithArea(raw, string.Empty);

        Assert.DoesNotContain("`@repos`", rewritten);
        Assert.Contains("`#sync`", rewritten);
    }

    [Fact]
    public void Rendered_sub_items_include_level_and_metadata()
    {
        var row = new EntryRow
        {
            RawText =
                "# Prepare review\n" +
                "`task` `*medium` `!draft` `@repo`\n\n" +
                "## Parent item\n" +
                "`prompt` `*high` `!ready` `#parent`\n\n" +
                "### Child item\n" +
                "`idea` `*low` `!done` `@other` `#child`\n"
        };

        Assert.Equal(2, row.PreviewSubItems.Count);
        Assert.Equal(2, row.PreviewSubItems[0].Level);
        Assert.Equal(EntryStatus.Ready, row.PreviewSubItems[0].EntryMetadata()?.Status);
        Assert.Equal(["parent"], row.PreviewSubItems[0].MetadataTags);
        Assert.Equal(3, row.PreviewSubItems[1].Level);
        Assert.Equal(EntryStatus.Done, row.PreviewSubItems[1].EntryMetadata()?.Status);
        Assert.Equal("repo", row.PreviewSubItems[1].Area);
        Assert.Equal(["child"], row.PreviewSubItems[1].MetadataTags);
    }

    /// <summary>
    /// One step's notes are that step's, and writing them leaves its siblings alone.
    /// <para>
    /// This is what is left of "collapse state is tracked per sub-item". Which steps
    /// a reader has unfolded is the shared task row's own business now — it dies with
    /// the view, and the row holds it. What still has to be per sub-item, and is a
    /// fact about the markdown rather than about a view, is the text: the pane hands
    /// each step's notes to its own editor by index, and an edit that reached the
    /// wrong chapter would silently overwrite a neighbour.
    /// </para>
    /// </summary>
    [Fact]
    public void Notes_are_addressed_per_sub_item()
    {
        const string raw =
            "# Prepare review\n\n" +
            "## First\n" +
            "Notes one.\n\n" +
            "## Second\n" +
            "Notes two.\n";

        Assert.Equal("Notes one.", EntryTextParser.GetSubItemNote(raw, 0));
        Assert.Equal("Notes two.", EntryTextParser.GetSubItemNote(raw, 1));

        var rewritten = EntryTextParser.WithSubItemNote(raw, 0, "Rewritten one.");

        Assert.Equal("Rewritten one.", EntryTextParser.GetSubItemNote(rewritten, 0));
        Assert.Equal("Notes two.", EntryTextParser.GetSubItemNote(rewritten, 1));
        Assert.Equal("First", EntryTextParser.GetSubItemTitle(rewritten, 0));
    }
}
