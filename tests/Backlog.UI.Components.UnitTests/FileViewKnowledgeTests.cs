namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// Everything the file says about itself, drawn from the file: a chapter's status
/// where its heading is, a diagram where its fence is, a copy button beside the
/// heading that names the chapter, and the remarks somebody left in the margin.
///
/// <para>The point of asking for these here rather than around the outside is
/// that the pane parses the file once. The knowledge panes used to parse the same
/// text a second time to draw them, which is why a chapter's diagrams appeared
/// below the file that contained them.</para>
/// </summary>
public sealed class FileViewKnowledgeTests
{
    /// <summary>A knowledge chapter as the convention writes one: the heading,
    /// the <c>meta</c> fence directly under it, and a sub-chapter after.</summary>
    private const string Chaptered = """
        # Shared Technologies

        ```meta
        status: adopted
        related: [".tech/technology-graph.md"]
        ```

        What the technologies are.

        ## Hosting

        Where it runs.
        """;

    private static IRenderedComponent<FileView> Render(
        BunitContext context,
        Action<ComponentParameterCollectionBuilder<FileView>> extra,
        string body = Chaptered) =>
        context.Render<FileView>(parameters =>
        {
            parameters
                .Add(v => v.Name, "shared-technologies.md")
                .Add(v => v.Body, body)
                .Add(v => v.TestId, "file");
            extra(parameters);
        });

    [Fact]
    public void The_files_own_status_is_drawn_in_the_header_beside_its_name()
    {
        // The fence under the `#` title describes the whole file, so it belongs on
        // the part of the pane that says which file this is — and that part stays
        // put while the body scrolls, which is where the answer to "is this still
        // current?" has to be.
        using var context = new BunitContext();

        var view = Render(context, parameters => parameters
            .Add(v => v.RenderKnowledgeMetadata, true));

        var headline = view.Find(".file-view__header .knowledge-record__headline");

        Assert.Equal("shared-technologies.md", headline.QuerySelector("h3.file-view__name")!.TextContent);
        Assert.Equal("adopted", headline.QuerySelector(".badge--status")!.TextContent);

        // The fence is a record and not a listing, and it is drawn once — in the
        // header, so the body holds neither a second record nor the raw block.
        Assert.Empty(view.FindAll("pre.md-code"));
        Assert.Single(view.FindAll(".knowledge-record"));
        Assert.Empty(view.FindAll(".file-view__body .knowledge-record"));
    }

    [Fact]
    public void Without_being_asked_the_fence_is_the_code_block_it_has_always_been()
    {
        using var context = new BunitContext();

        var view = Render(context, _ => { });

        Assert.Contains("status: adopted", view.Find(".file-view__body pre.md-code code").TextContent, StringComparison.Ordinal);
        Assert.Empty(view.FindAll(".knowledge-record"));

        // Including the header: a pane that was never told this is a knowledge
        // document has no record to draw beside the name either.
        Assert.Empty(view.FindAll(".file-view__header .knowledge-record"));
    }

    [Fact]
    public void A_status_change_names_the_chapter_and_the_block_it_belongs_to()
    {
        // Both keys travel because neither is enough: the block index is what the
        // view anchors by, and the heading is what a host knows the chapter as.
        // The record moved into the header; the fence it was read from did not
        // move at all, so neither key changes.
        using var context = new BunitContext();
        var changes = new List<KnowledgeStatusChange>();

        var view = Render(context, parameters => parameters
            .Add(v => v.RenderKnowledgeMetadata, true)
            .Add(v => v.KnowledgeFolder, KnowledgeFolder.Tech)
            .Add(v => v.OnKnowledgeStatusChanged, EventCallback.Factory.Create<KnowledgeStatusChange>(this, changes.Add)));

        view.Find(".knowledge-record__headline .status-editor select").Change("retired");

        var change = Assert.Single(changes);

        Assert.Equal("retired", change.Status);
        Assert.Equal("Shared Technologies", change.Heading);

        // The title's index, not the fence's: that is what the record was read
        // from and what everything else in the view is anchored by.
        Assert.Equal(0, change.BlockIndex);
    }

    [Fact]
    public void Nothing_here_writes_the_status_it_reports()
    {
        using var context = new BunitContext();

        var view = Render(context, parameters => parameters
            .Add(v => v.RenderKnowledgeMetadata, true)
            .Add(v => v.KnowledgeFolder, KnowledgeFolder.Tech)
            .Add(v => v.OnKnowledgeStatusChanged, EventCallback.Factory.Create<KnowledgeStatusChange>(this, _ => { })));

        view.Find(".knowledge-record__headline .status-editor select").Change("retired");

        // The file is exactly the text it was handed: which file, which fence and
        // what happens when the file moved on is the host's.
        Assert.Contains("status: adopted", Chaptered, StringComparison.Ordinal);
        Assert.Equal("shared-technologies.md", view.Find(".knowledge-record__headline h3.file-view__name").TextContent);
    }

    [Fact]
    public void Each_chapter_offers_to_copy_itself_and_copies_only_itself()
    {
        using var context = new BunitContext();
        context.JSInterop.Setup<bool>("backlogClipboard.copy", _ => true).SetResult(true);

        var view = Render(context, parameters => parameters
            .Add(v => v.RenderKnowledgeMetadata, true)
            .Add(v => v.AllowChapterCopy, true));

        // One per heading, and the record's headline is still a heading.
        Assert.Equal(2, view.FindAll(".file-view__body .md-chapter-copy").Count);

        view.Find("[data-testid='markdown-chapter-copy-3']").Click();

        Assert.Equal(
            "## Hosting\n\nWhere it runs.",
            Assert.Single(context.JSInterop.Invocations["backlogClipboard.copy"]).Arguments[0]);
    }

    [Fact]
    public void The_chapter_the_hoisted_record_came_from_is_copied_whole_fence_and_all()
    {
        // The title whose record is drawn in the header is still the top of a
        // chapter, and what a reader pastes back has to be the source they were
        // looking at — fence and all, because the fence is in the file.
        using var context = new BunitContext();
        context.JSInterop.Setup<bool>("backlogClipboard.copy", _ => true).SetResult(true);

        var view = Render(context, parameters => parameters
            .Add(v => v.RenderKnowledgeMetadata, true)
            .Add(v => v.AllowChapterCopy, true));

        view.Find(".file-view__body [data-testid='markdown-chapter-copy-0']").Click();

        var copied = (string)Assert.Single(context.JSInterop.Invocations["backlogClipboard.copy"]).Arguments[0]!;

        Assert.StartsWith("# Shared Technologies", copied, StringComparison.Ordinal);
        Assert.Contains("status: adopted", copied, StringComparison.Ordinal);
        Assert.Contains("## Hosting", copied, StringComparison.Ordinal);
    }

    [Fact]
    public void No_chapter_copy_is_offered_until_a_host_asks_for_one()
    {
        using var context = new BunitContext();

        var view = Render(context, parameters => parameters
            .Add(v => v.RenderKnowledgeMetadata, true));

        Assert.Empty(view.FindAll(".md-chapter-copy"));
        Assert.Empty(view.FindAll(".md-chapter-head"));
    }

    [Fact]
    public void A_diagram_fence_is_a_diagram_inside_the_pane_and_not_below_it()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = Render(
            context,
            parameters => parameters.Add(v => v.RenderKnowledgeMetadata, true),
            "# Flow\n\n```mermaid\ngraph TD;\n  A-->B;\n```\n");

        Assert.NotNull(view.Find(".file-view__body [data-testid='diagram-view']"));
        Assert.Empty(view.FindAll("pre.md-code"));
    }

    [Fact]
    public void Remarks_are_drawn_in_the_margin_without_a_host_having_to_say_so()
    {
        // A file view is a reading surface wide enough for a column beside the
        // prose, which is what keeps the file reading as a file.
        using var context = new BunitContext();

        var view = Render(context, parameters => parameters
            .Add(v => v.Comments, new MarkdownComment[] { new("c1", 2, "Too vague.") }));

        Assert.Contains("md-view--margin", view.Find(".file-view__body .md-view").ClassList);
        Assert.Equal("Too vague.", view.Find(".md-block-row__notes .md-comment__body").TextContent);
    }

    [Fact]
    public void A_host_that_wants_the_remarks_under_the_prose_can_say_so()
    {
        using var context = new BunitContext();

        var view = Render(context, parameters => parameters
            .Add(v => v.Comments, new MarkdownComment[] { new("c1", 2, "Too vague.") })
            .Add(v => v.CommentLayout, MarkdownCommentLayout.Inline));

        Assert.DoesNotContain("md-view--margin", view.Find(".file-view__body .md-view").ClassList);
        Assert.Single(view.FindAll(".md-comment"));
    }

    [Fact]
    public void A_reviewed_chapter_shows_its_status_and_its_remarks_at_once()
    {
        // The regression this exists to catch: a document with comments on it used
        // to leave the chapter's status sitting in a raw code fence, and the
        // reader reviewing a chapter is exactly the reader who needs to see
        // whether it is still current.
        using var context = new BunitContext();

        var view = Render(context, parameters => parameters
            .Add(v => v.RenderKnowledgeMetadata, true)
            .Add(v => v.Comments, new MarkdownComment[] { new("c1", 2, "Say which host.") }));

        Assert.Equal("adopted", view.Find(".file-view__header .badge--status").TextContent);
        Assert.Equal("Shared Technologies", view.Find(".file-view__body p.md-heading--1").TextContent);
        Assert.Empty(view.FindAll("pre.md-code"));

        // And the remark is still in the margin beside the block it was left on.
        Assert.Contains("md-view--margin", view.Find(".md-view").ClassList);
        Assert.Equal(
            "Say which host.",
            view.Find("[data-block='2'] .md-block-row__notes .md-comment__body").TextContent);
    }

    [Fact]
    public void A_remark_on_the_hoisted_fence_is_drawn_beside_the_title_it_belonged_to()
    {
        // The anchor is untouched — a comment on the fence still says the fence's
        // index — but the fence has no row of its own, so the title's row absorbs
        // it. That was true when the fence was folded into the heading and it is
        // true now that the record is drawn in the header: either way the row
        // level with it on the screen is the title's.
        using var context = new BunitContext();

        var view = Render(context, parameters => parameters
            .Add(v => v.RenderKnowledgeMetadata, true)
            .Add(v => v.Comments, new MarkdownComment[] { new("c1", 1, "Is this still adopted?") }));

        Assert.Equal(
            "Is this still adopted?",
            view.Find("[data-block='0'] .md-block-row__notes .md-comment__body").TextContent);
        Assert.Empty(view.FindAll("[data-block='1']"));
        Assert.Empty(view.FindAll("[data-testid='markdown-orphaned-comments']"));
    }

    [Fact]
    public void Offering_to_add_a_remark_does_not_cost_the_chapter_its_status()
    {
        using var context = new BunitContext();

        var view = Render(context, parameters => parameters
            .Add(v => v.RenderKnowledgeMetadata, true)
            .Add(v => v.OnAddComment, EventCallback.Factory.Create<int>(this, _ => { })));

        Assert.NotNull(view.Find(".file-view__header .knowledge-record__headline .badge--status"));
        Assert.Empty(view.FindAll("pre.md-code"));
    }

    [Fact]
    public void A_reader_can_settle_a_remark_the_host_is_listening_for()
    {
        using var context = new BunitContext();
        var resolved = new List<string>();

        var view = Render(context, parameters => parameters
            .Add(v => v.Comments, new MarkdownComment[] { new("c1", 2, "Too vague.") })
            .Add(v => v.OnResolveComment, EventCallback.Factory.Create<string>(this, resolved.Add)));

        view.Find("[data-testid='markdown-comment-resolve-c1']").Click();

        Assert.Equal("c1", Assert.Single(resolved));
    }

    [Fact]
    public void A_file_nobody_annotates_renders_exactly_the_body_it_always_did()
    {
        // All of this is opt-in, so the pane a host asks nothing extra of has to
        // put the same markup in the DOM it put there before any of it existed.
        using var context = new BunitContext();

        var view = Render(context, _ => { });

        Assert.Empty(view.FindAll(".md-block-row"));
        Assert.Empty(view.FindAll(".md-chapter-head"));
        Assert.Empty(view.FindAll(".knowledge-record"));
        Assert.DoesNotContain("md-view--margin", view.Find(".md-view").ClassList);
    }
}
