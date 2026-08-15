namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// Only the block shapes <see cref="MarkdownView"/> switches on — if the parser
/// stops producing one of these, the view silently renders nothing for it.
/// </summary>
public sealed class MarkdownPreviewTests
{
    [Fact]
    public void A_heading_keeps_its_level()
    {
        var blocks = MarkdownPreview.Parse("#### Deep heading");

        var heading = Assert.IsType<MdHeading>(Assert.Single(blocks));
        Assert.Equal(4, heading.Level);
    }

    [Fact]
    public void Task_lines_are_numbered_in_the_order_the_caller_will_rewrite_them()
    {
        var blocks = MarkdownPreview.Parse("- [ ] One\n- A bullet\n- [x] Two\n");

        var list = Assert.IsType<MdList>(Assert.Single(blocks));
        Assert.False(list.Ordered);
        Assert.Equal([0, null, 1], list.Items.Select(item => item.TaskIndex));
        Assert.Equal([false, null, true], list.Items.Select(item => item.Done));
    }

    [Fact]
    public void An_ordered_list_does_not_swallow_the_bullets_around_it()
    {
        var blocks = MarkdownPreview.Parse("1. First\n2. Second\n- A bullet\n");

        Assert.Equal(2, blocks.Count);
        Assert.True(Assert.IsType<MdList>(blocks[0]).Ordered);
        Assert.False(Assert.IsType<MdList>(blocks[1]).Ordered);
    }

    [Fact]
    public void A_fenced_block_keeps_its_language_and_its_lines()
    {
        var blocks = MarkdownPreview.Parse("```mermaid\ngraph TD;\n  a-->b;\n```\n");

        var code = Assert.IsType<MdCode>(Assert.Single(blocks));
        Assert.Equal("mermaid", code.Language);
        Assert.Equal("graph TD;\n  a-->b;", code.Text);
    }

    [Fact]
    public void A_fence_hides_whatever_looks_like_markdown_inside_it()
    {
        var blocks = MarkdownPreview.Parse("```\n- [ ] not a task\n```\n");

        Assert.IsType<MdCode>(Assert.Single(blocks));
    }

    [Fact]
    public void An_entry_folds_a_sub_item_heading_away_but_a_document_keeps_it()
    {
        const string body = "# Title\n\n## A section\n\nIts prose.\n";

        // As an entry: the `##` and everything under it belongs to the host.
        var entry = MarkdownPreview.Parse(body);
        Assert.Equal(2, entry.Count);
        Assert.IsType<MdHeading>(entry[0]);
        Assert.IsType<MdSubItem>(entry[1]);

        // As a document: a `##` is a heading, and nothing goes missing.
        var document = MarkdownPreview.ParseDocument(body);
        Assert.Equal(3, document.Count);
        Assert.Equal(2, Assert.IsType<MdHeading>(document[1]).Level);
        Assert.IsType<MdParagraph>(document[2]);
        Assert.DoesNotContain(document, block => block is MdSubItem);
    }

    [Fact]
    public void A_document_heading_keeps_a_bracket_it_was_written_with()
    {
        // `[x]` on a `##` is sub-item state in an entry and plain text in a
        // file — a document read that stripped it would drop the content.
        var document = MarkdownPreview.ParseDocument("## [x] Literal brackets\n");

        var heading = Assert.IsType<MdHeading>(Assert.Single(document));
        Assert.Null(heading.Done);
        Assert.Equal("[x] Literal brackets", MarkdownRender.PlainText(heading.Content));
    }

    [Fact]
    public void A_link_url_may_carry_brackets_of_its_own()
    {
        // Stopping at the first `)` truncated the URL and left the leftover
        // bracket sitting in the prose.
        var parts = MarkdownPreview.ParseInlines("see [C](https://en.wikipedia.org/wiki/C_(programming_language)) here");

        var link = Assert.IsType<MdLink>(parts[1]);
        Assert.Equal("https://en.wikipedia.org/wiki/C_(programming_language)", link.Url);
        Assert.Equal(" here", Assert.IsType<MdText>(parts[2]).Text);
    }

    [Fact]
    public void Quotes_and_dividers_are_blocks_of_their_own()
    {
        var blocks = MarkdownPreview.Parse("> Worth remembering\n\n---\n");

        Assert.IsType<MdQuote>(blocks[0]);
        Assert.IsType<MdDivider>(blocks[1]);
    }

    [Fact]
    public void Inline_markers_become_inline_parts_and_the_rest_stays_text()
    {
        var parts = MarkdownPreview.ParseInlines("a **bold** and `code` and #tag and [link](https://example.com)");

        Assert.Contains(parts, part => part is MdStrong { Text: "bold" });
        Assert.Contains(parts, part => part is MdCodeSpan { Text: "code" });
        Assert.Contains(parts, part => part is MdTag { Tag: "tag" });
        Assert.Contains(parts, part => part is MdLink { Text: "link", Url: "https://example.com" });
    }

    [Fact]
    public void A_half_typed_line_degrades_into_prose_rather_than_disappearing()
    {
        var blocks = MarkdownPreview.Parse("**unclosed emphasis and [a dangling link");

        var paragraph = Assert.IsType<MdParagraph>(Assert.Single(blocks));
        Assert.Equal("**unclosed emphasis and [a dangling link", Assert.IsType<MdText>(Assert.Single(paragraph.Content)).Text);
    }
}
