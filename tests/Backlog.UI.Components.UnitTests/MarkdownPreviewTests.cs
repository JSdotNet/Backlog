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
