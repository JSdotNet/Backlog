using Backlog.Infrastructure.Devbook.Building;

namespace Backlog.Infrastructure.Devbook.UnitTests;

/// <summary>
/// The ported <c>parseDocument</c> at the two places devbook 1.19 changed it:
/// a <c>#</c> line inside a fenced block is content rather than a chapter, and a
/// comma inside a quoted list entry belongs to the entry. Both are held to the
/// generator by <c>DevbookBuilderParityTests</c> as well; these name the rule.
/// </summary>
public sealed class DevbookMarkdownParseTests
{
    [Fact]
    public void A_heading_line_inside_a_fence_starts_no_chapter()
    {
        const string markdown = """
            # Page

            ```mermaid
            flowchart TB
            # Not a heading
            ```

            ~~~bash
            # Nor this
            ~~~

            ## After
            """;

        var chapters = DevbookMarkdown.Parse(markdown).Chapters;

        Assert.Equal(["page", "after"], chapters.Select(chapter => chapter.Slug));
        Assert.Equal(12, chapters[1].Line);
    }

    [Fact]
    public void A_fence_closes_only_on_its_own_character_at_its_own_length_or_longer()
    {
        const string markdown = """
            # Page

            ````markdown
            ```
            # Still inside
            ~~~~
            # And still
            `````
            # Closed

              ```
              # Indented fences count too
              ```
            """;

        var chapters = DevbookMarkdown.Parse(markdown).Chapters;

        Assert.Equal(["page", "closed"], chapters.Select(chapter => chapter.Slug));
    }

    [Fact]
    public void A_meta_block_under_a_heading_is_still_read()
    {
        const string markdown = """
            # Page

            ```meta
            status: draft
            ```

            ## Child

            ```meta
            status: active
            ```
            """;

        var document = DevbookMarkdown.Parse(markdown);

        Assert.Equal("draft", document.FileMeta?["status"]);
        Assert.Equal("active", document.Chapters[1].Meta?["status"]);
    }

    [Fact]
    public void A_comma_inside_a_quoted_list_entry_belongs_to_the_entry()
    {
        const string markdown = """
            # Page

            ```meta
            demo: [a.demo.html#pay, "./b.demo.html#walkthrough/x?flags=retry,wallet", 'c.demo.html#q?flags=d,e', f.demo.html#g?flags=h,i]
            ```
            """;

        var demo = DevbookMarkdown.AsList(DevbookMarkdown.Parse(markdown).FileMeta?["demo"]);

        // An unquoted entry is still split on every comma, as the generator splits it.
        Assert.Equal(
            ["a.demo.html#pay", "./b.demo.html#walkthrough/x?flags=retry,wallet", "c.demo.html#q?flags=d,e", "f.demo.html#g?flags=h", "i"],
            demo);
    }
}
