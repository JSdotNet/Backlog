using Backlog.Modules.Tasks.DomainModels;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// What a task row previews from its text: whether there is more than the title,
/// its tags, and its sub-items. The text rewrites the row asks for are parser cases,
/// in <c>EntryRowRewriteTests</c> beside <c>EntryTextParserTests</c>.
/// </summary>
public sealed class EntryRowPreviewTests
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
        Assert.Equal(EntryStatus.Ready, (row.PreviewSubItems[0].Metadata?.Value as EntryMarkdownMetadata)?.Status);
        Assert.Equal(["parent"], row.PreviewSubItems[0].MetadataTags);
        Assert.Equal(3, row.PreviewSubItems[1].Level);
        Assert.Equal(EntryStatus.Done, (row.PreviewSubItems[1].Metadata?.Value as EntryMarkdownMetadata)?.Status);
        Assert.Equal("repo", row.PreviewSubItems[1].Area);
        Assert.Equal(["child"], row.PreviewSubItems[1].MetadataTags);
    }
}
