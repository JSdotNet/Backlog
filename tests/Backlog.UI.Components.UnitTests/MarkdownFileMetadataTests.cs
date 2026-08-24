namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// One rule for "the document's own metadata block", asked two ways: off the
/// parsed blocks by the view that stops drawing it, and off the text by the pane
/// that starts. The two must answer the same, because between them they decide
/// whether the file's status is drawn once, twice, or not at all.
/// </summary>
public sealed class MarkdownFileMetadataTests
{
    private const string Document = """
        # Shared Technologies

        ```meta
        status: adopted
        ```

        What the technologies are.
        """;

    [Fact]
    public void A_title_with_a_fence_under_it_is_the_files_own_block()
    {
        var record = MarkdownFileMetadata.Read(Document);

        Assert.NotNull(record);
        Assert.Equal("Shared Technologies", record.Title);
        Assert.Equal("adopted", record.Metadata.Status);
    }

    [Fact]
    public void The_blocks_say_the_same_thing_the_text_does()
    {
        Assert.True(MarkdownFileMetadata.OpensWithFileBlock(MarkdownPreview.ParseDocument(Document)));
    }

    [Fact]
    public void Blank_lines_above_the_title_are_not_the_document_starting_with_something_else()
    {
        var record = MarkdownFileMetadata.Read("\n\n" + Document);

        Assert.Equal("Shared Technologies", record?.Title);
    }

    [Fact]
    public void A_chapters_fence_is_not_the_files_own()
    {
        // Level one, and only level one: a `##` describes the chapter under it,
        // and the pane that draws the file's record would otherwise put the first
        // chapter's status against the file's name.
        Assert.Null(MarkdownFileMetadata.Read("## Hosting\n\n```meta\nstatus: trial\n```\n"));
        Assert.False(MarkdownFileMetadata.OpensWithFileBlock(
            MarkdownPreview.ParseDocument("## Hosting\n\n```meta\nstatus: trial\n```\n")));
    }

    [Fact]
    public void A_fence_that_does_not_follow_the_title_describes_something_else()
    {
        Assert.Null(MarkdownFileMetadata.Read("# Shared Technologies\n\nProse.\n\n```meta\nstatus: adopted\n```\n"));
    }

    [Fact]
    public void A_fence_that_is_not_metadata_is_not_read_as_any()
    {
        Assert.Null(MarkdownFileMetadata.Read("# Shared Technologies\n\n```yaml\nstatus: adopted\n```\n"));
    }

    [Fact]
    public void Frontmatter_is_not_stepped_over_on_the_way_to_the_title()
    {
        // Deliberate, and the reason the two readings can be trusted against each
        // other: the block reading sees whatever the parser made of those lines,
        // and a text reading that quietly skipped them would find a block the
        // other one cannot.
        Assert.Null(MarkdownFileMetadata.Read("---\ndescription: A file.\n---\n\n" + Document));
    }

    [Fact]
    public void Nothing_in_gives_nothing_back()
    {
        Assert.Null(MarkdownFileMetadata.Read(null));
        Assert.Null(MarkdownFileMetadata.Read("   "));
        Assert.False(MarkdownFileMetadata.OpensWithFileBlock([]));
    }

    [Fact]
    public void A_title_alone_states_no_record()
    {
        Assert.Null(MarkdownFileMetadata.Read("# Shared Technologies\n"));
    }
}
