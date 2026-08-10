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
}
