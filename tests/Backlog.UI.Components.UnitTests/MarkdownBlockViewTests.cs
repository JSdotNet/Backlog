namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The block switch as its own component.
///
/// <para>Two claims are worth pinning and they pull in opposite directions. The
/// first is that nothing moved: a block drawn on its own has to be the same
/// markup as the same block drawn inside <c>MarkdownView</c>, or the read view
/// quietly gained a second rendering the day this became a component. The second
/// is that it can be dressed: the class hooks are the whole reason the knowledge
/// panels could stop hand-rolling this switch, so each one has to replace exactly
/// the element it names and nothing else.</para>
/// </summary>
public sealed class MarkdownBlockViewTests
{
    /// <summary>
    /// Every block record, minus the diagram: <c>DiagramView</c> mints an id per
    /// instance, so two renderings of one diagram are never string-equal and the
    /// comparison below would be about the id rather than about the markup. The
    /// diagram arm is asserted on its own further down.
    /// </summary>
    private static IReadOnlyList<MdBlock> Samples =>
    [
        new MdHeading(2, MarkdownPreview.ParseInlines("A heading"), null),
        new MdParagraph(MarkdownPreview.ParseInlines("A paragraph with **bold** and a #tag.")),
        new MdList(false,
        [
            new MdListItem(null, MarkdownPreview.ParseInlines("A plain bullet"), null,
                [new MdList(true, [new MdListItem(null, MarkdownPreview.ParseInlines("Nested and numbered"), null)])]),
            new MdListItem(true, MarkdownPreview.ParseInlines("A finished task"), 0),
            new MdListItem(false, MarkdownPreview.ParseInlines("An unfinished one"), 1)
        ]),
        new MdList(true, [new MdListItem(null, MarkdownPreview.ParseInlines("First"), null)]),
        new MdQuote(MarkdownPreview.ParseInlines("A quote.")),
        new MdCode("var x = 1;", "csharp"),
        new MdTable(
            new MdTableRow([new MdTableCell(MarkdownPreview.ParseInlines("Left")), new MdTableCell(MarkdownPreview.ParseInlines("Right"))]),
            [new MdTableRow([new MdTableCell(MarkdownPreview.ParseInlines("one"))])],
            [MdAlign.Left, MdAlign.Right]),
        new MdFootnotes([new MdFootnote("note", 1, MarkdownPreview.ParseInlines("The evidence."))]),
        new MdDivider()
    ];

    [Fact]
    public void Every_block_renders_the_same_alone_as_it_does_inside_the_read_view()
    {
        using var context = new BunitContext();

        foreach (var block in Samples)
        {
            var standalone = context.Render<MarkdownBlockView>(parameters => parameters
                .Add(view => view.Block, block));

            var inside = context.Render<MarkdownView>(parameters => parameters
                .Add(view => view.Blocks, [block]));

            // The read view's only contribution to an unannotated document is the
            // wrapper it lays the blocks out in, so anything else that differs is
            // a rendering that drifted.
            Assert.Equal($"<div class=\"md-view\">{standalone.Markup}</div>", inside.Markup);
        }
    }

    [Fact]
    public void Each_class_hook_dresses_the_one_element_it_names()
    {
        using var context = new BunitContext();

        var paragraph = context.Render<MarkdownBlockView>(parameters => parameters
            .Add(view => view.Block, new MdParagraph(MarkdownPreview.ParseInlines("Words.")))
            .Add(view => view.ParagraphCssClass, "knowledge-p"));

        Assert.Equal("knowledge-p", paragraph.Find("p").GetAttribute("class"));

        var quote = context.Render<MarkdownBlockView>(parameters => parameters
            .Add(view => view.Block, new MdQuote(MarkdownPreview.ParseInlines("Words.")))
            .Add(view => view.QuoteCssClass, "knowledge-quote"));

        Assert.Equal("knowledge-quote", quote.Find("blockquote").GetAttribute("class"));

        var code = context.Render<MarkdownBlockView>(parameters => parameters
            .Add(view => view.Block, new MdCode("var x = 1;", "csharp"))
            .Add(view => view.CodeCssClass, "knowledge-code"));

        Assert.Equal("knowledge-code", code.Find("pre").GetAttribute("class"));

        // The `code` inside a `pre` has never carried a class, and dressing the
        // `pre` must not give it one.
        Assert.Null(code.Find("pre code").GetAttribute("class"));

        var divider = context.Render<MarkdownBlockView>(parameters => parameters
            .Add(view => view.Block, new MdDivider())
            .Add(view => view.DividerCssClass, "knowledge-divider"));

        Assert.Equal("knowledge-divider", divider.Find("hr").GetAttribute("class"));

        var list = context.Render<MarkdownBlockView>(parameters => parameters
            .Add(view => view.Block, new MdList(false, [new MdListItem(null, MarkdownPreview.ParseInlines("A bullet"), null)]))
            .Add(view => view.ListCssClass, "knowledge-list"));

        Assert.Equal("knowledge-list", list.Find("ul").GetAttribute("class"));
        Assert.Empty(list.FindAll(".md-list"));
    }

    [Fact]
    public void A_null_item_class_drops_the_attribute_a_bullet_otherwise_always_carries()
    {
        using var context = new BunitContext();

        var block = new MdList(false, [new MdListItem(null, MarkdownPreview.ParseInlines("A bullet"), null)]);

        var read = context.Render<MarkdownBlockView>(parameters => parameters
            .Add(view => view.Block, block));

        // The quirk, on purpose: both modifier slots are always written, so a
        // plain bullet's class is the space between two empty ones. A knowledge
        // list has no class at all, and the difference is visible to a stylesheet.
        Assert.Equal(" ", read.Find("li").GetAttribute("class"));

        var bare = context.Render<MarkdownBlockView>(parameters => parameters
            .Add(view => view.Block, block)
            .Add(view => view.ListItemCssClass, null));

        Assert.Null(bare.Find("li").GetAttribute("class"));
    }

    [Fact]
    public void A_host_that_has_already_decided_a_fence_is_a_diagram_gets_a_diagram()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var fence = new MdCode("C4Context\ntitle System", "c4context");

        // The library's own vocabulary does not include `c4*`, so left to itself
        // this is a code listing — which is what the arc42 panel would have got
        // for a block its own parser had already called a diagram.
        var unasked = context.Render<MarkdownBlockView>(parameters => parameters
            .Add(view => view.Block, fence));

        Assert.NotNull(unasked.Find("pre.md-code"));
        Assert.Empty(unasked.FindAll(".diagram-view"));

        var asked = context.Render<MarkdownBlockView>(parameters => parameters
            .Add(view => view.Block, fence)
            .Add(view => view.IsDiagram, _ => true));

        Assert.NotNull(asked.Find(".diagram-view"));

        // Answering the question is the host's; drawing it is still DiagramView's,
        // so a language it cannot render is still labelled as the source it shows.
        Assert.Equal("Code diagram", asked.Find(".diagram-view__title").TextContent);
    }

    [Fact]
    public void A_meta_fence_is_a_record_only_where_a_host_asked_for_one()
    {
        using var context = new BunitContext();

        var fence = new MdCode("status: adopted", MetadataReader.FenceLanguage);

        var listing = context.Render<MarkdownBlockView>(parameters => parameters
            .Add(view => view.Block, fence));

        Assert.NotNull(listing.Find("pre.md-code"));

        var record = context.Render<MarkdownBlockView>(parameters => parameters
            .Add(view => view.Block, fence)
            .Add(view => view.RenderKnowledgeMetadata, true)
            .Add(view => view.KnowledgeFolder, KnowledgeFolder.Tech));

        Assert.Empty(record.FindAll("pre.md-code"));
        Assert.NotNull(record.Find(".knowledge-record"));
    }

    [Fact]
    public void A_block_nothing_knows_how_to_draw_draws_nothing()
    {
        using var context = new BunitContext();

        // MdSubItem is real and deliberately unhandled: the host lays each one out
        // as its own card. Falling through to a paragraph would put a sub-item's
        // whole body back inside the entry it was lifted out of.
        var view = context.Render<MarkdownBlockView>(parameters => parameters
            .Add(block => block.Block, new MdSubItem(MarkdownPreview.ParseInlines("A sub-item"), false, false, [])));

        Assert.Equal(string.Empty, view.Markup);
    }
}
