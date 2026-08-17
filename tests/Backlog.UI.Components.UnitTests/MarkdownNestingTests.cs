namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The parts of the read view that used to flatten: nested lists, quotes that
/// ran over more than one line, strikethrough and images.
/// </summary>
public sealed class MarkdownNestingTests
{
    [Fact]
    public void An_indented_item_belongs_to_the_one_above_it()
    {
        var list = Assert.IsType<MdList>(Assert.Single(
            MarkdownPreview.ParseDocument("- Top\n  - Nested\n  - Also nested\n- Second top")));

        Assert.Equal(2, list.Items.Count);

        var children = Assert.Single(list.Items[0].Nested);
        Assert.Equal(["Nested", "Also nested"], children.Items.Select(i => MarkdownRender.PlainText(i.Content)));
        Assert.Empty(list.Items[1].Nested);
    }

    [Fact]
    public void Nesting_goes_as_deep_as_it_is_written()
    {
        var list = Assert.IsType<MdList>(Assert.Single(
            MarkdownPreview.ParseDocument("- One\n  - Two\n    - Three")));

        var two = Assert.Single(list.Items[0].Nested);
        var three = Assert.Single(two.Items[0].Nested);

        Assert.Equal("Three", MarkdownRender.PlainText(Assert.Single(three.Items).Content));
    }

    [Fact]
    public void A_nested_list_may_be_a_different_kind_from_its_parent()
    {
        var list = Assert.IsType<MdList>(Assert.Single(
            MarkdownPreview.ParseDocument("- A bullet\n  1. Numbered under it\n  2. And another")));

        Assert.False(list.Ordered);
        Assert.True(Assert.Single(list.Items[0].Nested).Ordered);
    }

    [Fact]
    public void A_nested_run_that_changes_kind_stays_under_the_item_it_was_written_under()
    {
        // Both runs are indented under the bullet, so both belong to it. When an
        // item could only hold one, the second escaped to the top level and
        // rendered at the outer indent — nested in the source, flat on screen.
        var list = Assert.IsType<MdList>(Assert.Single(
            MarkdownPreview.ParseDocument("- A bullet\n  - Nested bullet\n  1. Nested number\n- Another top")));

        Assert.Equal(2, list.Items.Count);

        var nested = list.Items[0].Nested;
        Assert.Equal(2, nested.Count);
        Assert.False(nested[0].Ordered);
        Assert.True(nested[1].Ordered);
    }

    [Fact]
    public void A_tab_indents_as_far_as_four_spaces()
    {
        var list = Assert.IsType<MdList>(Assert.Single(
            MarkdownPreview.ParseDocument("- Top\n\t- Nested with a tab")));

        Assert.Single(list.Items[0].Nested);
    }

    [Fact]
    public void A_nested_task_keeps_the_index_its_position_in_the_file_gives_it()
    {
        // ToggleTask counts task lines in document order and knows nothing about
        // depth, so the parser's indices have to agree with that or a click
        // rewrites the wrong line.
        const string source = "- [ ] First\n  - [ ] Nested\n- [ ] Third";

        var list = Assert.IsType<MdList>(Assert.Single(MarkdownPreview.ParseDocument(source)));
        var nested = Assert.Single(list.Items[0].Nested);

        Assert.Equal(0, list.Items[0].TaskIndex);
        Assert.Equal(1, Assert.Single(nested.Items).TaskIndex);
        Assert.Equal(2, list.Items[1].TaskIndex);

        // And the rewriter agrees: index 1 is the nested one.
        Assert.Equal("- [ ] First\n  - [x] Nested\n- [ ] Third", MarkdownPreview.ToggleTask(source, 1));
    }

    [Fact]
    public void The_view_renders_a_nested_list_inside_its_parent_item()
    {
        using var context = new BunitContext();

        var view = context.Render<MarkdownView>(p => p.Add(
            v => v.Blocks,
            MarkdownPreview.ParseDocument("- Top\n  - Nested")));

        Assert.Single(view.FindAll(".md-list .md-list"));
        Assert.Equal("Nested", view.Find(".md-list .md-list li").TextContent.Trim());
    }

    [Fact]
    public void Consecutive_quote_lines_are_one_quote()
    {
        // One quote per line put a bar and a gap down the middle of a sentence
        // that had simply been wrapped.
        var quote = Assert.IsType<MdQuote>(Assert.Single(
            MarkdownPreview.ParseDocument("> A quotation that runs\n> over two lines.")));

        Assert.Equal("A quotation that runs over two lines.", MarkdownRender.PlainText(quote.Content));
    }

    [Fact]
    public void A_blank_line_still_separates_two_quotes()
    {
        var blocks = MarkdownPreview.ParseDocument("> First.\n\n> Second.");

        Assert.Equal(2, blocks.OfType<MdQuote>().Count());
    }

    [Fact]
    public void Strikethrough_is_an_inline_of_its_own()
    {
        var parts = MarkdownPreview.ParseInlines("this is ~~struck~~ out");

        Assert.Contains(parts, part => part is MdStrike { Text: "struck" });
    }

    [Fact]
    public void The_view_renders_strikethrough_as_s()
    {
        using var context = new BunitContext();

        var view = context.Render<MarkdownView>(p => p.Add(
            v => v.Blocks,
            MarkdownPreview.ParseDocument("~~dropped~~")));

        Assert.Equal("dropped", view.Find("s").TextContent);
    }

    [Fact]
    public void An_image_is_not_read_as_a_link_with_a_stray_bang()
    {
        var parts = MarkdownPreview.ParseInlines("![a diagram](diagram.png)");

        var image = Assert.IsType<MdImage>(Assert.Single(parts));
        Assert.Equal("a diagram", image.Alt);
        Assert.Equal("diagram.png", image.Url);
    }

    [Fact]
    public void An_image_renders_with_its_alt_text_and_nothing_eager()
    {
        using var context = new BunitContext();

        var view = context.Render<MarkdownView>(p => p.Add(
            v => v.Blocks,
            MarkdownPreview.ParseDocument("![a diagram](https://example.com/d.png)")));

        var img = view.Find("img.md-image");
        Assert.Equal("https://example.com/d.png", img.GetAttribute("src"));
        Assert.Equal("a diagram", img.GetAttribute("alt"));
        Assert.Equal("lazy", img.GetAttribute("loading"));
    }

    [Theory]
    [InlineData("javascript:doEvil")]
    [InlineData("data:text/html,hi")]
    public void An_image_we_would_not_fetch_shows_its_alt_text_instead(string url)
    {
        // Stronger than the link case: a src fetches on its own, with no click
        // to consent to it.
        using var context = new BunitContext();

        var view = context.Render<MarkdownView>(p => p.Add(
            v => v.Blocks,
            MarkdownPreview.ParseDocument($"![what it was]({url})")));

        Assert.Empty(view.FindAll("img"));
        Assert.Equal("what it was", view.Find(".md-image--inert").TextContent);
    }
}
