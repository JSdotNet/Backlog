using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.DomainModels;

namespace Backlog.Modules.Tasks.UnitTests;

/// <summary>
/// What is attached to a task: one place, as a path, written on the metadata line
/// as <c>files:</c>.
/// <para>
/// The round trip is what most of these are about, for the reason the whole
/// grammar rests on: the markdown is canonical, so a field the parser reads and
/// the rewrite does not write is a field the next save deletes. The rest are about
/// the two words the row needs — what the place is called and whether to call it a
/// folder — which are read off the path rather than off the disk, so that a value
/// object stays comparable and a renamed file cannot change what a saved task
/// says.
/// </para>
/// </summary>
public class AttachmentTests
{
    [Fact]
    public void A_files_token_is_read_off_the_metadata_line()
    {
        var parsed = EntryTextParser.Parse("# Review the panel\n`task` `files:D:/reviews/panel-review`\n");

        Assert.Equal("D:/reviews/panel-review", parsed.Attachment?.Path);
    }

    [Fact]
    public void A_task_with_nothing_attached_carries_no_attachment()
    {
        var parsed = EntryTextParser.Parse("# Review the panel\n`task`\n");

        Assert.Null(parsed.Attachment);
    }

    /// <summary>Absent means absent, so <c>files:</c> with nothing after it is a
    /// reader asking for something rather than a reader asking for nothing — it is
    /// refused out loud, the same way <c>due:friday</c> is, instead of quietly
    /// reading as "detached".</summary>
    [Fact]
    public void An_empty_files_token_is_refused_rather_than_read_as_nothing()
    {
        var parsed = EntryTextParser.Parse("# Review the panel\n`task` `files:`\n");

        Assert.Null(parsed.Attachment);

        var refused = Assert.Single(parsed.Unreadable ?? []);
        Assert.Equal("files", refused.Name);
    }

    /// <summary>A path is whatever the file system will take, so the parser has no
    /// opinion about it. Spaces included: the tokens are backtick-delimited, which
    /// is what makes a path with a space in it expressible at all.</summary>
    [Fact]
    public void A_path_with_spaces_in_it_survives_the_line()
    {
        var parsed = EntryTextParser.Parse("# Review the panel\n`task` `files:C:/My Documents/panel review`\n");

        Assert.Equal("C:/My Documents/panel review", parsed.Attachment?.Path);
    }

    [Fact]
    public void Writing_the_token_puts_it_on_a_line_that_has_none()
    {
        var written = EntryTextParser.WithAttachment(
            "# Review the panel\n`task`\n",
            new Attachment("D:/reviews/panel-review"));

        Assert.Contains("`files:D:/reviews/panel-review`", written, StringComparison.Ordinal);
        Assert.Equal("D:/reviews/panel-review", EntryTextParser.Parse(written).Attachment?.Path);
    }

    /// <summary>Attaching a second place replaces the first, because there is only
    /// ever one attachment — the model has no list for a second one to go into.</summary>
    [Fact]
    public void Attaching_again_replaces_rather_than_adds()
    {
        var written = EntryTextParser.WithAttachment(
            "# Review the panel\n`task` `files:D:/old`\n",
            new Attachment("D:/new"));

        Assert.DoesNotContain("D:/old", written, StringComparison.Ordinal);
        Assert.Equal("D:/new", EntryTextParser.Parse(written).Attachment?.Path);
    }

    [Fact]
    public void Detaching_takes_the_token_off_the_line()
    {
        var written = EntryTextParser.WithAttachment("# Review the panel\n`task` `files:D:/reviews`\n", null);

        Assert.DoesNotContain("files:", written, StringComparison.Ordinal);
        Assert.Null(EntryTextParser.Parse(written).Attachment);
    }

    /// <summary>
    /// The canonical rewrite carries it, which is the assertion that matters most
    /// here.
    /// <para>
    /// <see cref="EntryTextParser.ToRawText"/> composes the metadata line from the
    /// DTO and nothing else, so a field the DTO holds and the rewrite does not
    /// write is destroyed by the next flush save with no error anywhere to notice
    /// it by. This is the test that fails if the attachment is added to the
    /// aggregate and forgotten here.
    /// </para>
    /// </summary>
    [Fact]
    public void The_canonical_rewrite_keeps_the_attachment()
    {
        var entry = new TaskItem("Review the panel", string.Empty, EntryType.Task);
        entry.SetAttachment(new Attachment("D:/reviews/panel-review"));

        var dto = new TaskItemDto(
            entry.Id,
            entry.Title,
            entry.ContentMd,
            entry.Type,
            entry.Priority,
            entry.Status,
            entry.Area,
            [],
            entry.Order,
            0,
            0,
            [],
            Attachment: entry.Attachment);

        var raw = EntryTextParser.ToRawText(dto);

        Assert.Contains("`files:D:/reviews/panel-review`", raw, StringComparison.Ordinal);
        Assert.Equal("D:/reviews/panel-review", EntryTextParser.Parse(raw).Attachment?.Path);
    }

    [Fact]
    public void Setting_an_attachment_and_clearing_it_both_reach_the_aggregate()
    {
        var entry = new TaskItem("Review the panel", string.Empty, EntryType.Task);

        entry.SetAttachment(new Attachment("D:/reviews"));
        Assert.Equal("D:/reviews", entry.Attachment?.Path);

        entry.SetAttachment(null);
        Assert.Null(entry.Attachment);
    }

    // --- The two words the row needs ---------------------------------------

    [Theory]
    [InlineData("D:/reviews/panel-review", "panel-review")]
    [InlineData("D:\\reviews\\panel-review", "panel-review")]
    [InlineData("D:/reviews/panel-review/", "panel-review")]
    [InlineData("panel-review", "panel-review")]
    [InlineData("C:/My Documents/panel review", "panel review")]
    public void The_name_is_the_last_segment_whichever_separator_wrote_it(string path, string expected) =>
        Assert.Equal(expected, new Attachment(path).Name);

    /// <summary>Both separators, because a path written on Windows is read on the
    /// phone and committed to a repository that has seen both.</summary>
    [Theory]
    [InlineData("D:/reviews/panel.zip", true)]
    [InlineData("D:/reviews/panel.ZIP", true)]
    [InlineData("D:/reviews/panel.tar.gz", true)]
    [InlineData("D:/reviews/panel-review", false)]
    [InlineData("D:/reviews/zipped", false)]
    public void An_archive_is_told_from_a_folder_by_its_spelling(string path, bool isArchive) =>
        Assert.Equal(isArchive, new Attachment(path).IsArchive);

    /// <summary>A blank path is not an attachment to nowhere — it is no attachment.
    /// One place decides that so the parser, the aggregate and the pane cannot
    /// disagree about what an empty string meant.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_path_is_nothing_attached(string? path) => Assert.Null(Attachment.From(path));

    [Fact]
    public void A_path_is_trimmed_on_the_way_in()
    {
        Assert.Equal("D:/reviews", Attachment.From("  D:/reviews  ")?.Path);
    }
}
