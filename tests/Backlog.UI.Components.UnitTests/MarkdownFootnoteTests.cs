namespace Backlog.UI.Components.UnitTests;

public sealed class MarkdownFootnoteTests
{
    [Fact]
    public void A_reference_and_its_definition_become_a_mark_and_a_note()
    {
        var blocks = MarkdownPreview.ParseDocument("A claim[^1].\n\n[^1]: The evidence.");

        var paragraph = Assert.IsType<MdParagraph>(blocks[0]);
        var reference = Assert.IsType<MdFootnoteRef>(paragraph.Content[1]);
        Assert.Equal(1, reference.Number);

        var note = Assert.Single(Assert.IsType<MdFootnotes>(blocks[1]).Notes);
        Assert.Equal("1", note.Label);
        Assert.Equal("The evidence.", MarkdownRender.PlainText(note.Content));
    }

    [Fact]
    public void The_definition_line_is_not_left_in_the_prose()
    {
        var blocks = MarkdownPreview.ParseDocument("A claim[^1].\n\n[^1]: The evidence.");

        // Two blocks: the paragraph and the notes. The definition is not a third.
        Assert.Equal(2, blocks.Count);
        Assert.DoesNotContain(
            blocks.OfType<MdParagraph>(),
            p => MarkdownRender.PlainText(p.Content).Contains("[^1]:", StringComparison.Ordinal));
    }

    [Fact]
    public void A_definition_written_before_its_reference_still_resolves()
    {
        // Definitions are collected before anything is parsed, so where they sit
        // in the file does not matter.
        var blocks = MarkdownPreview.ParseDocument("[^note]: Defined first.\n\nReferenced after[^note].");

        var paragraph = Assert.IsType<MdParagraph>(blocks[0]);
        Assert.Contains(paragraph.Content, part => part is MdFootnoteRef { Number: 1 });
    }

    [Fact]
    public void Numbers_follow_the_order_they_are_first_referenced_not_defined()
    {
        var blocks = MarkdownPreview.ParseDocument(
            "First[^b] then[^a].\n\n[^a]: Defined first.\n[^b]: Defined second.");

        var notes = Assert.IsType<MdFootnotes>(blocks[^1]).Notes;

        Assert.Equal(["b", "a"], notes.Select(n => n.Label));
        Assert.Equal([1, 2], notes.Select(n => n.Number));
    }

    [Fact]
    public void The_same_reference_twice_is_one_note_with_one_number()
    {
        var blocks = MarkdownPreview.ParseDocument("Once[^x] and again[^x].\n\n[^x]: Said once.");

        var paragraph = Assert.IsType<MdParagraph>(blocks[0]);

        Assert.Equal(2, paragraph.Content.OfType<MdFootnoteRef>().Count());
        Assert.All(paragraph.Content.OfType<MdFootnoteRef>(), r => Assert.Equal(1, r.Number));
        Assert.Single(Assert.IsType<MdFootnotes>(blocks[1]).Notes);
    }

    [Fact]
    public void A_reference_with_nothing_behind_it_stays_the_text_that_was_typed()
    {
        // A mark that points nowhere is worse than no mark: it promises a note
        // at the bottom that the reader will go looking for.
        var blocks = MarkdownPreview.ParseDocument("A claim[^missing].");

        var paragraph = Assert.IsType<MdParagraph>(Assert.Single(blocks));

        Assert.DoesNotContain(paragraph.Content, part => part is MdFootnoteRef);
        Assert.Equal("A claim[^missing].", MarkdownRender.PlainText(paragraph.Content));
    }

    [Fact]
    public void A_body_with_no_footnotes_gets_no_notes_block()
    {
        Assert.DoesNotContain(MarkdownPreview.ParseDocument("Just prose."), block => block is MdFootnotes);
    }

    [Fact]
    public void A_definition_inside_a_fence_is_a_code_sample()
    {
        // The same blindness the parser has to a `- [ ]` in a fence.
        var blocks = MarkdownPreview.ParseDocument("```\n[^1]: not a note\n```\n\nA claim[^1].");

        Assert.IsType<MdCode>(blocks[0]);
        Assert.DoesNotContain(blocks, block => block is MdFootnotes);
    }

    [Fact]
    public void The_mark_links_to_the_note_and_the_note_links_back()
    {
        using var context = new BunitContext();

        var view = context.Render<MarkdownView>(p => p.Add(
            v => v.Blocks,
            MarkdownPreview.ParseDocument("A claim[^1].\n\n[^1]: The evidence.")));

        var mark = view.Find(".md-fnref a");
        Assert.Equal("#fn-1", mark.GetAttribute("href"));
        Assert.Equal("1", mark.TextContent);
        Assert.Equal("fnref-1", view.Find(".md-fnref").GetAttribute("id"));

        var note = view.Find(".md-footnotes__item");
        Assert.Equal("fn-1", note.GetAttribute("id"));

        // Following the mark down is only half of it.
        Assert.Equal("#fnref-1", view.Find(".md-footnotes__back").GetAttribute("href"));
    }

    [Fact]
    public void A_mark_says_nothing_in_a_label_it_would_only_clutter()
    {
        // PlainText is what names an aria-label; a bare number in the middle of
        // one reads as part of the text it is labelling.
        var parts = MarkdownPreview.ParseDocument("Task[^1] name\n\n[^1]: note")
            .OfType<MdParagraph>()
            .Single()
            .Content;

        Assert.Equal("Task name", MarkdownRender.PlainText(parts));
    }
}
