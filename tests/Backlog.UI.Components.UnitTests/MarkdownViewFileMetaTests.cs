namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The fence under a document's own title describes the whole document, not the
/// prose that happens to follow it — so a host with somewhere better to put it
/// can take it out of the body and draw it there. The read view's part of that
/// bargain is to stop drawing it: not as a record, and not as the code block it
/// would otherwise fall back to.
///
/// <para>Only the document's own block moves. A <c>##</c> chapter's record
/// belongs to the heading it was written under, that heading is in the body, and
/// nothing here changes where it is drawn.</para>
/// </summary>
public sealed class MarkdownViewFileMetaTests
{
    private const string Document = """
        # Shared Technologies

        ```meta
        status: adopted
        ```

        What the technologies are.

        ## Hosting

        ```meta
        status: trial
        ```

        Where it runs.
        """;

    [Fact]
    public void By_default_the_documents_own_record_is_still_drawn_inline()
    {
        // The parameter's default must not change what any existing host renders:
        // a view nobody has told about a header still folds the file's block into
        // the title, which is what it has always done.
        using var context = new BunitContext();

        var view = Render(context, Document, parameters => parameters
            .Add(v => v.RenderKnowledgeMetadata, true));

        var headline = view.Find(".knowledge-record__headline");

        Assert.Equal("Shared Technologies", headline.QuerySelector("p.md-heading--1")!.TextContent);
        Assert.Equal("adopted", headline.QuerySelector(".badge--status")!.TextContent);
        Assert.Equal(2, view.FindAll(".knowledge-record").Count);
    }

    [Fact]
    public void A_host_drawing_the_file_block_itself_gets_it_out_of_the_body()
    {
        using var context = new BunitContext();

        var view = Render(context, Document, parameters => parameters
            .Add(v => v.RenderKnowledgeMetadata, true)
            .Add(v => v.RenderFileKnowledgeMetadata, false));

        // The title is a plain heading again — and the fence is gone rather than
        // left behind as a listing, which is the record with the reading taken
        // off.
        var heading = view.Find("p.md-heading--1");
        Assert.Equal("Shared Technologies", heading.TextContent);
        Assert.Empty(view.FindAll("pre.md-code"));

        // The chapter's own record is untouched: one record left, and it is the
        // chapter's.
        var record = Assert.Single(view.FindAll(".knowledge-record"));
        Assert.Equal("Hosting", record.QuerySelector("p.md-heading")!.TextContent);
        Assert.Equal("trial", record.QuerySelector(".badge--status")!.TextContent);
    }

    [Fact]
    public void The_hoist_needs_the_records_to_be_asked_for_at_all()
    {
        // Off means off. A host that never asked for records gets its `meta` fence
        // as the code block it has always been, whatever it says about the body.
        using var context = new BunitContext();

        var view = Render(context, Document, parameters => parameters
            .Add(v => v.RenderFileKnowledgeMetadata, false));

        Assert.Equal(2, view.FindAll("pre.md-code").Count);
        Assert.Empty(view.FindAll(".knowledge-record"));
    }

    [Fact]
    public void A_fence_that_is_not_the_documents_own_is_left_where_it_is()
    {
        // The rule is the document's opening: a title, then the block. A file that
        // starts with a chapter has no block of its own, so there is nothing to
        // hoist and the chapter keeps its record.
        using var context = new BunitContext();

        var view = Render(context, """
            ## Hosting

            ```meta
            status: trial
            ```

            Where it runs.
            """, parameters => parameters
            .Add(v => v.RenderKnowledgeMetadata, true)
            .Add(v => v.RenderFileKnowledgeMetadata, false));

        var record = Assert.Single(view.FindAll(".knowledge-record"));
        Assert.Equal("Hosting", record.QuerySelector("p.md-heading")!.TextContent);
    }

    [Fact]
    public void The_title_still_offers_to_copy_the_chapter_it_opens()
    {
        // The heading goes through the same fragment whether or not it is a
        // record's headline, so losing the record must not lose the copy button
        // with it.
        using var context = new BunitContext();
        context.JSInterop.Setup<bool>("backlogClipboard.copy", _ => true).SetResult(true);

        var view = Render(context, Document, parameters => parameters
            .Add(v => v.Source, Document)
            .Add(v => v.AllowChapterCopy, true)
            .Add(v => v.RenderKnowledgeMetadata, true)
            .Add(v => v.RenderFileKnowledgeMetadata, false));

        view.Find("[data-testid='markdown-chapter-copy-0']").Click();

        var copied = (string)Assert.Single(context.JSInterop.Invocations["backlogClipboard.copy"]).Arguments[0]!;

        Assert.StartsWith("# Shared Technologies", copied, StringComparison.Ordinal);
        Assert.Contains("status: adopted", copied, StringComparison.Ordinal);
    }

    [Fact]
    public void A_comment_on_the_hoisted_fence_is_drawn_beside_the_title()
    {
        // The anchor is untouched — a comment on the fence still says the fence's
        // index — and the fence has no row of its own either way, so the title's
        // row keeps absorbing it. Anything else would leave the remark adrift in
        // the orphan block at the end, which is where a comment goes when its
        // block is gone rather than merely drawn elsewhere.
        using var context = new BunitContext();

        var view = Render(context, Document, parameters => parameters
            .Add(v => v.RenderKnowledgeMetadata, true)
            .Add(v => v.RenderFileKnowledgeMetadata, false)
            .Add(v => v.Comments, new MarkdownComment[]
            {
                new("c0", 0, "About the title."),
                new("c1", 1, "About the fence.")
            }));

        var notes = view.FindAll("[data-block='0'] .md-block-row__notes .md-comment__body")
            .Select(body => body.TextContent);

        Assert.Equal(["About the title.", "About the fence."], notes);
        Assert.Empty(view.FindAll("[data-block='1']"));
        Assert.Empty(view.FindAll("[data-testid='markdown-orphaned-comments']"));
    }

    private static IRenderedComponent<MarkdownView> Render(
        BunitContext context,
        string source,
        Action<ComponentParameterCollectionBuilder<MarkdownView>> extra) =>
        context.Render<MarkdownView>(parameters =>
        {
            parameters.Add(v => v.Blocks, MarkdownPreview.ParseDocument(source));
            extra(parameters);
        });
}
