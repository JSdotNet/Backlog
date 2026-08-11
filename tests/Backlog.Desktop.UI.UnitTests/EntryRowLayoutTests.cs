using Backlog.Modules.Backlog.DomainModels;
using Backlog.Desktop.UI.Services;

namespace Backlog.Desktop.UI.UnitTests;

public sealed class EntryRowLayoutTests
{
    [Fact]
    public void Title_and_metadata_only_entries_use_one_line_layout()
    {
        var row = new EntryRow
        {
            RawText = "# Ask about the trial length\n`task` `*medium` `!draft` `#sync`\n"
        };

        Assert.False(row.HasExpandableContent);
        Assert.True(row.UsesOneLineLayout);
        Assert.Contains("sync", row.PreviewTags);
    }

    [Fact]
    public void Entries_with_body_can_be_folded_to_one_line()
    {
        var row = new EntryRow
        {
            RawText = "# Ship the offline sync spike\n`task` `*high` `!draft`\n\nKeep working on a train.\n"
        };

        Assert.True(row.HasExpandableContent);
        Assert.False(row.UsesOneLineLayout);

        row.EntryCollapsed = true;

        Assert.True(row.UsesOneLineLayout);
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
}
