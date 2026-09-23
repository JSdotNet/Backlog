
namespace Backlog.Desktop.UI.UnitTests;

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
    public void Keeps_plain_heading_levels_after_sub_items_are_grouped()
    {
        var blocks = MarkdownPreview.Parse("# One\n\n### Three\n\n#### Four");

        Assert.Equal([1], blocks.OfType<MdHeading>().Select(h => h.Level));
        var subItem = Assert.IsType<MdSubItem>(blocks[1]);
        Assert.Equal(3, subItem.Level);
        var nestedHeading = Assert.IsType<MdHeading>(Assert.Single(subItem.Children));
        Assert.Equal(4, nestedHeading.Level);
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
        Assert.Equal(string.Empty, code.Language);
    }


    [Fact]
    public void Fenced_code_keeps_its_language()
    {
        var blocks = MarkdownPreview.Parse("```mermaid\ngraph TD\n```");

        var code = Assert.IsType<MdCode>(Assert.Single(blocks));
        Assert.Equal("mermaid", code.Language);
        Assert.Equal("graph TD", code.Text);
    }
    [Fact]
    public void Preserves_fenced_code_language()
    {
        var blocks = MarkdownPreview.Parse("```mermaid\ngraph TD\n    A --> B\n```");

        var code = Assert.IsType<MdCode>(Assert.Single(blocks));
        Assert.Equal("mermaid", code.Language);
        Assert.Equal("graph TD\n    A --> B", code.Text);
    }


    /// <summary>
    /// A fence longer than the one inside it holds the one inside it. The rule is
    /// CommonMark's and it is the devbook convention's own — <c>annotations.mjs</c>
    /// closes a fence on "a run of the same marker, at least as long, with nothing
    /// after it" — and reading only "starts with three backticks" broke it: a
    /// four-backtick block quoting a code sample ended at the sample's opening
    /// line, leaving the rest of the block loose in the document. Where the block
    /// was a private <c>annotation</c>, that loose remainder was somebody's note
    /// surviving into a read that says it carries none.
    /// </summary>
    [Fact]
    public void A_longer_fence_holds_the_fences_inside_it()
    {
        var blocks = MarkdownPreview.ParseDocument(
            "````annotation\nauthor: someone\nbody: |\n  ```csharp\n  var x = 1;\n  ```\n````");

        var code = Assert.IsType<MdCode>(Assert.Single(blocks));
        Assert.Equal("annotation", code.Language);
        Assert.Contains("var x = 1;", code.Text, StringComparison.Ordinal);
    }

    /// <summary>The other marker, which is the other way an author writes a block
    /// whose body holds backtick fences. It was not recognised as a fence at all:
    /// its body was read as prose and every fence inside it as a block of its
    /// own.</summary>
    [Fact]
    public void A_tilde_fence_is_a_fence()
    {
        var blocks = MarkdownPreview.ParseDocument(
            "~~~annotation\nauthor: someone\nbody: |\n  ```csharp\n  var x = 1;\n  ```\n~~~");

        var code = Assert.IsType<MdCode>(Assert.Single(blocks));
        Assert.Equal("annotation", code.Language);
        Assert.Contains("var x = 1;", code.Text, StringComparison.Ordinal);
    }

    /// <summary>A run of the other marker does not close this one, and neither
    /// does a shorter run of this one.</summary>
    [Fact]
    public void A_fence_is_closed_only_by_its_own_marker()
    {
        var blocks = MarkdownPreview.ParseDocument("````text\n~~~\n```\nstill inside\n````\n\nAfter.");

        var code = Assert.IsType<MdCode>(blocks[0]);
        Assert.Contains("still inside", code.Text, StringComparison.Ordinal);
        Assert.IsType<MdParagraph>(blocks[1]);
    }

    /// <summary>A closing fence carries no info string — a line that opens a new
    /// block is not the end of this one.</summary>
    [Fact]
    public void A_fence_line_with_a_language_does_not_close_a_fence()
    {
        var blocks = MarkdownPreview.ParseDocument("```text\n```csharp\nvar x = 1;\n```");

        var code = Assert.IsType<MdCode>(Assert.Single(blocks));
        Assert.Equal("text", code.Language);
        Assert.Contains("var x = 1;", code.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Fenced_code_keeps_indented_diagram_body()
    {
        var blocks = MarkdownPreview.Parse("```mermaid\ngraph TD\n    A[One] --> B[Two]\n```");

        var code = Assert.IsType<MdCode>(Assert.Single(blocks));
        Assert.Equal("mermaid", code.Language);
        Assert.Contains("A[One]", code.Text);
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

