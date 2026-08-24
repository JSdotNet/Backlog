namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// A list item written over more than one line. Hard-wrapping a bullet used to
/// end the list and drop the rest of the sentence into a paragraph of its own —
/// one bullet on screen where the author wrote one, then unbulleted prose, then
/// the list starting over.
/// </summary>
public sealed class MarkdownContinuationTests
{
    [Fact]
    public void A_line_indented_under_an_item_is_the_same_item_continued()
    {
        var list = Assert.IsType<MdList>(Assert.Single(
            MarkdownPreview.ParseDocument("- First line\n    continued here\n- Second")));

        Assert.Equal(
            ["First line continued here", "Second"],
            list.Items.Select(item => MarkdownRender.PlainText(item.Content)));
    }

    [Fact]
    public void A_continuation_keeps_the_markup_it_was_written_with()
    {
        // The text is re-read as a whole, so emphasis opened on the wrapped line
        // is emphasis, not a pair of literal asterisks.
        var list = Assert.IsType<MdList>(Assert.Single(
            MarkdownPreview.ParseDocument("- An item\n    with **weight** on it")));

        var content = Assert.Single(list.Items).Content;
        Assert.Contains(content, part => part is MdStrong { Text: "weight" });
    }

    [Fact]
    public void A_continuation_belongs_to_the_nested_item_it_sits_under()
    {
        var list = Assert.IsType<MdList>(Assert.Single(
            MarkdownPreview.ParseDocument("- Top\n  - Nested\n      and its second line\n- Another top")));

        var nested = Assert.Single(Assert.Single(list.Items[0].Nested).Items);
        Assert.Equal("Nested and its second line", MarkdownRender.PlainText(nested.Content));
        Assert.Equal(2, list.Items.Count);
    }

    [Fact]
    public void A_wrapped_checklist_item_stays_one_checkbox()
    {
        const string source = "- [ ] A step\n    that took two lines\n- [x] Done";

        var list = Assert.IsType<MdList>(Assert.Single(MarkdownPreview.ParseDocument(source)));

        Assert.Equal([false, true], list.Items.Select(item => item.Done));
        Assert.Equal([0, 1], list.Items.Select(item => item.TaskIndex));
        Assert.Equal("A step that took two lines", MarkdownRender.PlainText(list.Items[0].Content));

        // The indices still name the lines the rewriter counts, so a click on
        // the second box flips the second box.
        Assert.Equal("- [ ] A step\n    that took two lines\n- [ ] Done", MarkdownPreview.ToggleTask(source, 1));
    }

    [Fact]
    public void Prose_at_the_item_s_own_indent_still_ends_the_list()
    {
        // Only text written *into* the item continues it. A line that starts
        // where the marker starts is a new paragraph, as it always was.
        var blocks = MarkdownPreview.ParseDocument("- An item\nprose after the list");

        Assert.Equal(2, blocks.Count);
        Assert.IsType<MdList>(blocks[0]);
        Assert.Equal("prose after the list", MarkdownRender.PlainText(Assert.IsType<MdParagraph>(blocks[1]).Content));
    }

    [Fact]
    public void The_view_renders_a_wrapped_item_as_one_bullet()
    {
        using var context = new BunitContext();

        var view = context.Render<MarkdownView>(p => p.Add(
            v => v.Blocks,
            MarkdownPreview.ParseDocument("- First line\n    continued here")));

        var item = Assert.Single(view.FindAll(".md-list li"));
        Assert.Equal("First line continued here", item.TextContent.Trim());
        Assert.Empty(view.FindAll("p"));
    }
}
