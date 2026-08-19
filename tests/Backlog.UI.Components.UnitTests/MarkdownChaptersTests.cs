namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The chapter splitter, which is what a "copy this chapter" button puts on the
/// clipboard. It slices the source and not the parse, so what comes back has to
/// be the text as it was written — which is why every assertion here is about
/// the text and not about a rendering of it.
/// </summary>
public sealed class MarkdownChaptersTests
{
    /// <summary>A level under a level under a level, with a sibling after it, so
    /// both halves of the nesting rule have something to be wrong about.</summary>
    private const string Nested = """
        # Guide

        Preamble under the title.

        ## Aggregate: Backlog Entry

        What the aggregate is.

        ### Entity: Task

        What the entity is.

        ## Next

        Something else entirely.
        """;

    [Fact]
    public void Every_heading_is_a_chapter_in_the_order_they_were_written()
    {
        var chapters = MarkdownChapters.Split(Nested);

        Assert.Equal(
            ["Guide", "Aggregate: Backlog Entry", "Entity: Task", "Next"],
            chapters.Select(chapter => chapter.Title));
        Assert.Equal([1, 2, 3, 2], chapters.Select(chapter => chapter.Level));
    }

    [Fact]
    public void A_chapter_contains_the_sub_chapters_written_under_it()
    {
        var aggregate = MarkdownChapters.Split(Nested)[1];

        // Copying the aggregate and getting its heading alone, with the entities
        // beneath it left behind, is a surprising reading of the word — and the
        // nesting is why the fold exists in the first place.
        Assert.Equal(
            "## Aggregate: Backlog Entry\n\nWhat the aggregate is.\n\n### Entity: Task\n\nWhat the entity is.",
            aggregate.Text);
    }

    [Fact]
    public void A_chapter_stops_at_the_next_heading_that_is_not_beneath_it()
    {
        var aggregate = MarkdownChapters.Split(Nested)[1];

        Assert.DoesNotContain("## Next", aggregate.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Something else entirely.", aggregate.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_file_title_carries_the_whole_file_with_it()
    {
        var guide = MarkdownChapters.Split(Nested)[0];

        // Nothing is beneath a `#` except everything, so its chapter is the file
        // — the same file, however the file itself happened to end its lines.
        Assert.Equal(Nested.Replace("\r\n", "\n"), guide.Text);
    }

    [Fact]
    public void Whatever_stands_before_the_first_heading_belongs_to_no_chapter()
    {
        var chapters = MarkdownChapters.Split("""
            A note nobody put a heading over.

            # Guide

            Prose.
            """);

        var guide = Assert.Single(chapters);

        Assert.Equal("# Guide\n\nProse.", guide.Text);
    }

    [Fact]
    public void A_heading_inside_a_fence_is_a_line_of_code_and_not_a_chapter()
    {
        // A shell snippet's `#` comment and a diff's `###` marker are not
        // chapters, and offering to copy one would copy from the wrong place
        // onwards.
        var chapters = MarkdownChapters.Split("""
            # Guide

            ```sh
            # Install it first
            ### Then run it
            ```

            ## Really a chapter
            """);

        Assert.Equal(["Guide", "Really a chapter"], chapters.Select(chapter => chapter.Title));
    }

    [Fact]
    public void A_fence_is_closed_by_its_own_character_so_a_quoted_fence_stays_content()
    {
        // Which is how a markdown file quotes a markdown fence, and this
        // library's own chapters do exactly that.
        var chapters = MarkdownChapters.Split("""
            # Guide

            ~~~
            ```
            ## Not a chapter
            ```
            ~~~

            ## A chapter
            """);

        Assert.Equal(["Guide", "A chapter"], chapters.Select(chapter => chapter.Title));
    }

    [Theory]
    [InlineData("#hashtag")]
    [InlineData("#!/bin/sh")]
    [InlineData("####### Seven hashes")]
    [InlineData("#")]
    [InlineData("###   ")]
    public void A_line_that_only_starts_with_a_hash_is_not_a_heading(string line)
    {
        // A tag, a shebang, a level nobody has, and a hash with no title after
        // it. A heading needs the space and needs something to say.
        var chapters = MarkdownChapters.Split($"{line}\n\n# A real heading\n\nProse.\n");

        var real = Assert.Single(chapters);

        Assert.Equal("A real heading", real.Title);
        Assert.Equal("# A real heading\n\nProse.", real.Text);
    }

    [Fact]
    public void The_blank_lines_that_only_separated_two_chapters_are_dropped()
    {
        // What is left is what someone pastes back into a file: the chapter, and
        // not the gap that happened to follow it.
        var chapters = MarkdownChapters.Split("## One\n\nText.\n\n\n\n## Two\n\nMore.\n\n\n");

        Assert.Equal("## One\n\nText.", chapters[0].Text);
        Assert.Equal("## Two\n\nMore.", chapters[1].Text);
    }

    [Fact]
    public void Blank_lines_inside_a_chapter_are_left_exactly_where_they_were()
    {
        var chapter = Assert.Single(MarkdownChapters.Split("## One\n\nText.\n\n\nMore text.\n"));

        Assert.Equal("## One\n\nText.\n\n\nMore text.", chapter.Text);
    }

    [Fact]
    public void A_file_written_on_Windows_comes_back_with_one_kind_of_line_ending()
    {
        var chapter = Assert.Single(MarkdownChapters.Split("# Guide\r\n\r\nProse.\r\n"));

        Assert.Equal("# Guide\n\nProse.", chapter.Text);
        Assert.DoesNotContain("\r", chapter.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_with_no_headings_has_no_chapters_rather_than_one_unnamed_one()
    {
        Assert.Empty(MarkdownChapters.Split("Just prose.\n\nMore prose.\n"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Nothing_to_split_is_no_chapters(string? source)
    {
        Assert.Empty(MarkdownChapters.Split(source));
    }
}
