namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// A devbook <c>annotation</c> fence in a chapter view: read as one block however
/// much code it quotes, drawn as a review note under the block it is about
/// wherever the view knows it is reading a knowledge document, and left the
/// fenced block it always was everywhere else.
/// </summary>
public sealed class MarkdownAnnotationFenceTests
{
    /// <summary>A paragraph with the rule's three-line note under it, then a
    /// second paragraph — the note's anchor is the block above it.</summary>
    private static string Chapter(string fence) => $"""
        # Outline

        The outline is one indexed range read.

        {fence}

        The next passage.
        """.Replace("\r\n", "\n");

    [Fact]
    public void A_note_quoting_a_code_sample_is_one_block_and_not_a_note_and_some_prose()
    {
        var blocks = MarkdownPreview.ParseDocument(Chapter(DevbookAnnotationSamples.NoteWithCodeSample));

        var note = Assert.Single(blocks.OfType<MdCode>());
        Assert.True(DevbookAnnotationFence.IsAnnotationBlock(note.Language));
        Assert.Contains(DevbookAnnotationSamples.CodeSampleTail, note.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(
            blocks.OfType<MdParagraph>(),
            paragraph => MarkdownRender.PlainText(paragraph.Content).Contains(DevbookAnnotationSamples.CodeSampleTail, StringComparison.Ordinal));

        var parsed = DevbookAnnotationFence.Parse(note.Text);
        Assert.Contains("```csharp", parsed.Body, StringComparison.Ordinal);
        Assert.EndsWith(DevbookAnnotationSamples.CodeSampleTail, parsed.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void A_fence_opens_and_closes_by_CommonMarks_rule()
    {
        var fence = MarkdownFence.Open("  ````annotation ");

        Assert.Equal(new MarkdownFence('`', 4, "annotation"), fence);
        Assert.False(fence!.Value.IsClosedBy("  ```"));
        Assert.False(fence.Value.IsClosedBy("````csharp"));
        Assert.False(fence.Value.IsClosedBy("~~~~"));
        Assert.True(fence.Value.IsClosedBy("````"));
        Assert.True(fence.Value.IsClosedBy("`````  "));
        Assert.Null(MarkdownFence.Open("``not a fence"));
        Assert.Equal("", MarkdownFence.Open("~~~")!.Value.Language);
    }

    [Fact]
    public void A_task_line_quoted_inside_a_note_takes_no_task_index()
    {
        // The toggle walks the text the way the parse does; a flag flipped on
        // every ``` line read the sample inside the note as prose between two
        // blocks and handed its `- [ ]` an index the view never showed.
        var fence = DevbookAnnotationSamples.NoteWithCodeSample.Replace(
            DevbookAnnotationSamples.CodeSampleLine,
            "- [ ] a checklist line in a sample",
            StringComparison.Ordinal);
        var source = $"{fence}\n\n- [ ] the real task\n";

        var toggled = MarkdownPreview.ToggleTask(source, 0);

        Assert.Contains("- [x] the real task", toggled, StringComparison.Ordinal);
        Assert.Contains("- [ ] a checklist line in a sample", toggled, StringComparison.Ordinal);
    }

    [Fact]
    public void A_knowledge_chapter_draws_the_note_attached_under_its_block_and_no_listing()
    {
        using var context = new BunitContext();

        var view = context.Render<MarkdownView>(parameters => parameters
            .Add(v => v.Blocks, MarkdownPreview.ParseDocument(Chapter(DevbookAnnotationSamples.FullThread)))
            .Add(v => v.RenderDevbookMetadata, true));

        var note = view.Find("[data-testid='devbook-annotation-note']");
        Assert.Contains("devbook-note--attached", note.ClassList);
        Assert.Empty(view.FindAll("pre"));

        // Directly after the paragraph it annotates, before the next one.
        Assert.Equal("The outline is one indexed range read.", note.PreviousElementSibling!.TextContent);
        Assert.Equal("The next passage.", note.NextElementSibling!.TextContent);
    }

    [Fact]
    public void A_document_path_alone_is_enough_to_read_the_note()
    {
        using var context = new BunitContext();

        var view = context.Render<MarkdownView>(parameters => parameters
            .Add(v => v.Blocks, MarkdownPreview.ParseDocument(Chapter(DevbookAnnotationSamples.ThreeLineNote)))
            .Add(v => v.DevbookDocumentPath, ".arc42/04-solution-strategy.md"));

        Assert.Single(view.FindAll("[data-testid='devbook-annotation-note']"));
    }

    [Fact]
    public void In_an_entrys_body_the_fence_is_still_only_a_fenced_block()
    {
        using var context = new BunitContext();

        var view = context.Render<MarkdownView>(parameters => parameters
            .Add(v => v.Blocks, MarkdownPreview.ParseDocument(Chapter(DevbookAnnotationSamples.ThreeLineNote))));

        Assert.Empty(view.FindAll("[data-testid='devbook-annotation-note']"));
        Assert.Contains("author: jobsc", view.Find("pre.md-code").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_note_row_offers_no_remark_and_no_rewrite_but_keeps_its_index()
    {
        using var context = new BunitContext();
        var blocks = MarkdownPreview.ParseDocument(Chapter(DevbookAnnotationSamples.ThreeLineNote));
        var noteIndex = blocks.ToList().FindIndex(block => block is MdCode);

        var view = context.Render<MarkdownView>(parameters => parameters
            .Add(v => v.Blocks, blocks)
            .Add(v => v.RenderDevbookMetadata, true)
            .Add(v => v.OnAddComment, (int _) => { })
            .Add(v => v.OnRewriteBlock, (int _) => { }));

        Assert.Empty(view.FindAll($"[data-testid='markdown-comment-{noteIndex}']"));
        Assert.Empty(view.FindAll($"[data-testid='markdown-rewrite-{noteIndex}']"));

        // The blocks either side still carry both, under the indices they always
        // had: the note is counted, only not offered.
        Assert.Single(view.FindAll($"[data-testid='markdown-comment-{noteIndex - 1}']"));
        Assert.Single(view.FindAll($"[data-testid='markdown-rewrite-{noteIndex + 1}']"));
        Assert.Single(view.FindAll($"[data-block='{noteIndex}'] [data-testid='devbook-annotation-note']"));
    }

    [Fact]
    public void A_file_view_reading_a_chapter_draws_the_note_not_the_yaml()
    {
        using var context = new BunitContext();

        var view = context.Render<FileView>(parameters => parameters
            .Add(v => v.Name, "04-solution-strategy.md")
            .Add(v => v.Body, Chapter(DevbookAnnotationSamples.NoteWithCodeSample))
            .Add(v => v.RenderDevbookMetadata, true));

        var note = view.Find("[data-testid='devbook-annotation-note']");
        Assert.Contains(DevbookAnnotationSamples.CodeSampleTail, note.TextContent, StringComparison.Ordinal);

        // The only listing left is the sample, and it is inside the note.
        var listing = Assert.Single(view.FindAll("pre"));
        Assert.Equal(DevbookAnnotationSamples.CodeSampleLine, listing.TextContent);
        Assert.DoesNotContain("author: jobsc", view.Markup, StringComparison.Ordinal);
    }
}
