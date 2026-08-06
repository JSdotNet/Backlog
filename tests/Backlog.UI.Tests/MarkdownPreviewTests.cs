using Backlog.UI.Services;

namespace Backlog.UI.Tests;

/// <summary>
/// The read view is what an entry looks like when it is not focused, so what
/// matters here is that ordinary markdown survives the trip and that
/// half-written markdown degrades into readable prose instead of vanishing.
/// </summary>
public class MarkdownPreviewTests
{
    private static string PlainText(IEnumerable<MdInline> inlines) =>
        string.Concat(inlines.Select(i => i switch
        {
            MdText t => t.Text,
            MdStrong s => s.Text,
            MdEm e => e.Text,
            MdCodeSpan c => c.Text,
            MdTag g => "#" + g.Tag,
            MdLink l => l.Text,
            _ => string.Empty
        }));

    [Fact]
    public void Renders_paragraphs()
    {
        var blocks = MarkdownPreview.Parse("Hello there.\n\nSecond one.");

        Assert.Equal(2, blocks.Count);
        Assert.All(blocks, b => Assert.IsType<MdParagraph>(b));
    }

    [Fact]
    public void Joins_wrapped_lines_into_one_paragraph()
    {
        var blocks = MarkdownPreview.Parse("one line\nand its continuation");

        var paragraph = Assert.IsType<MdParagraph>(Assert.Single(blocks));
        Assert.Equal("one line and its continuation", PlainText(paragraph.Content));
    }

    [Fact]
    public void Keeps_the_heading_level()
    {
        var blocks = MarkdownPreview.Parse("# One\n\n### Three\n\n#### Four");

        Assert.Equal([1, 3, 4], blocks.OfType<MdHeading>().Select(h => h.Level));
    }

    [Fact]
    public void A_level_two_heading_becomes_a_sub_item_not_a_heading()
    {
        var blocks = MarkdownPreview.Parse("## a sub-item");

        var subItem = Assert.IsType<MdSubItem>(Assert.Single(blocks));
        Assert.Equal("a sub-item", PlainText(subItem.Title));
        Assert.False(subItem.Done);
        Assert.Empty(subItem.Children);
    }

    [Fact]
    public void A_sub_item_takes_the_blocks_beneath_it_as_its_own()
    {
        var blocks = MarkdownPreview.Parse("## a sub-item\nsome notes\n\n- [ ] a step\n\n## another");

        Assert.Equal(2, blocks.Count);
        var first = Assert.IsType<MdSubItem>(blocks[0]);
        Assert.Equal(2, first.Children.Count);
        Assert.IsType<MdParagraph>(first.Children[0]);
        Assert.IsType<MdList>(first.Children[1]);

        var second = Assert.IsType<MdSubItem>(blocks[1]);
        Assert.Empty(second.Children);
    }

    [Fact]
    public void A_sub_item_ends_at_the_next_level_one_heading()
    {
        var blocks = MarkdownPreview.Parse("## a sub-item\nnotes\n\n# back to the top\n\ntail");

        var subItem = Assert.IsType<MdSubItem>(blocks[0]);
        Assert.Single(subItem.Children);
        Assert.IsType<MdHeading>(blocks[1]);
        Assert.IsType<MdParagraph>(blocks[2]);
    }

    [Fact]
    public void Prose_before_the_first_sub_item_stays_where_it_was()
    {
        var blocks = MarkdownPreview.Parse("intro line\n\n## a sub-item");

        Assert.IsType<MdParagraph>(blocks[0]);
        Assert.IsType<MdSubItem>(blocks[1]);
    }

    [Fact]
    public void A_sub_item_carries_its_done_state()
    {
        var blocks = MarkdownPreview.Parse("## [x] finished");

        var subItem = Assert.IsType<MdSubItem>(Assert.Single(blocks));
        Assert.True(subItem.Done);
        Assert.Equal("finished", PlainText(subItem.Title));
    }

    [Fact]
    public void Checklists_keep_their_boxes()
    {
        var blocks = MarkdownPreview.Parse("- [ ] todo\n- [x] done");

        var list = Assert.IsType<MdList>(Assert.Single(blocks));
        Assert.Equal([false, true], list.Items.Select(i => i.Done));
    }

    [Fact]
    public void Ordered_and_unordered_lists_stay_separate()
    {
        var blocks = MarkdownPreview.Parse("- a\n- b\n\n1. one\n2. two");

        var lists = blocks.OfType<MdList>().ToList();
        Assert.Equal(2, lists.Count);
        Assert.False(lists[0].Ordered);
        Assert.True(lists[1].Ordered);
    }

    [Fact]
    public void Fenced_code_is_kept_verbatim()
    {
        var blocks = MarkdownPreview.Parse("```\n# not a heading\n- not a list\n```");

        var code = Assert.IsType<MdCode>(Assert.Single(blocks));
        Assert.Equal("# not a heading\n- not a list", code.Text);
    }

    [Fact]
    public void Recognises_inline_emphasis_code_tags_and_links()
    {
        var blocks = MarkdownPreview.Parse("**bold** *soft* `mono` #tag [text](https://example.com)");

        var paragraph = Assert.IsType<MdParagraph>(Assert.Single(blocks));
        Assert.Contains(paragraph.Content, i => i is MdStrong { Text: "bold" });
        Assert.Contains(paragraph.Content, i => i is MdEm { Text: "soft" });
        Assert.Contains(paragraph.Content, i => i is MdCodeSpan { Text: "mono" });
        Assert.Contains(paragraph.Content, i => i is MdTag { Tag: "tag" });
        Assert.Contains(paragraph.Content, i => i is MdLink { Text: "text", Url: "https://example.com" });
    }

    [Fact]
    public void A_hash_inside_a_word_is_not_a_tag()
    {
        var blocks = MarkdownPreview.Parse("issue#42");

        var paragraph = Assert.IsType<MdParagraph>(Assert.Single(blocks));
        Assert.DoesNotContain(paragraph.Content, i => i is MdTag);
    }

    [Fact]
    public void Half_written_emphasis_stays_readable()
    {
        var blocks = MarkdownPreview.Parse("**not closed yet");

        var paragraph = Assert.IsType<MdParagraph>(Assert.Single(blocks));
        Assert.Equal("**not closed yet", PlainText(paragraph.Content));
    }

    [Fact]
    public void An_unterminated_fence_still_renders_its_contents()
    {
        var blocks = MarkdownPreview.Parse("```\nstill typing");

        var code = Assert.IsType<MdCode>(Assert.Single(blocks));
        Assert.Equal("still typing", code.Text);
    }

    [Fact]
    public void Empty_text_renders_nothing()
    {
        Assert.Empty(MarkdownPreview.Parse(string.Empty));
        Assert.Empty(MarkdownPreview.Parse(null));
    }

    [Fact]
    public void Quotes_and_dividers_are_recognised()
    {
        var blocks = MarkdownPreview.Parse("> quoted\n\n---");

        Assert.IsType<MdQuote>(blocks[0]);
        Assert.IsType<MdDivider>(blocks[1]);
    }
}
