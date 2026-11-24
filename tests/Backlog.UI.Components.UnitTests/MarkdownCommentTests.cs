namespace Backlog.UI.Components.UnitTests;

public sealed class MarkdownCommentTests
{
    private static readonly IReadOnlyList<MdBlock> Blocks =
        MarkdownPreview.ParseDocument("# A heading\n\nA paragraph.\n\n- A bullet");

    private static IRenderedComponent<MarkdownView> Render(
        BunitContext context,
        Action<ComponentParameterCollectionBuilder<MarkdownView>> parameters)
    {
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context.Render(parameters);
    }

    [Fact]
    public void A_view_nobody_annotates_renders_what_it_always_did()
    {
        // The wrapper only appears when something hangs off a block: a div around
        // every block changes what a stylesheet sees.
        using var context = new BunitContext();

        var view = Render(context, p => p.Add(v => v.Blocks, Blocks));

        Assert.Empty(view.FindAll(".md-block-row"));
        Assert.Empty(view.FindAll(".md-block__comment"));
    }

    [Fact]
    public void Editing_reports_the_whole_comment_with_its_body_replaced()
    {
        using var context = new BunitContext();
        MarkdownComment? edited = null;

        var view = Render(context, p => p
            .Add(v => v.Blocks, Blocks)
            .Add(v => v.Comments, new MarkdownComment[] { new("c1", 1, "Before", "Reviewer", "today") })
            .Add(v => v.OnEditComment, c => edited = c));

        view.Find("[data-testid='markdown-comment-edit-c1']").Click();
        view.Find("textarea").Input("After");
        view.Find("[data-testid='markdown-comment-save-c1']").Click();

        Assert.Equal("After", edited?.Body);

        // The rest of the record comes back untouched, so a host storing more
        // than the view shows can match it up.
        Assert.Equal("c1", edited?.Id);
        Assert.Equal(1, edited?.BlockIndex);
        Assert.Equal("Reviewer", edited?.Author);
    }

    [Fact]
    public void Cancelling_an_edit_reports_nothing_and_puts_the_body_back()
    {
        // The draft lives in the view until it is saved, which is the only
        // reason Cancel can mean anything.
        using var context = new BunitContext();
        var edits = 0;

        var view = Render(context, p => p
            .Add(v => v.Blocks, Blocks)
            .Add(v => v.Comments, new MarkdownComment[] { new("c1", 1, "Before") })
            .Add(v => v.OnEditComment, _ => edits++));

        view.Find("[data-testid='markdown-comment-edit-c1']").Click();
        view.Find("textarea").Input("After");
        view.Find("[data-testid='markdown-comment-cancel-c1']").Click();

        Assert.Equal(0, edits);
        Assert.Equal("Before", view.Find(".md-comment__body").TextContent);
    }

    /// <summary>
    /// A key pressed in a comment's draft is the draft's. The Devbook takes the
    /// screen and closes on Escape from its surface's <c>&lt;main&gt;</c>; an Escape in
    /// the draft must not reach it and throw the draft away with the surface. A key
    /// pressed elsewhere in the view still reaches the host.
    /// </summary>
    [Fact]
    public void A_key_pressed_in_a_comment_draft_does_not_reach_the_host()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var hostHeard = 0;

        var host = context.Render(builder =>
        {
            builder.OpenElement(0, "main");
            builder.AddAttribute(1, "onkeydown", EventCallback.Factory.Create<KeyboardEventArgs>(this, () => hostHeard++));
            builder.OpenComponent<MarkdownView>(2);
            builder.AddComponentParameter(3, nameof(MarkdownView.Blocks), Blocks);
            builder.AddComponentParameter(4, nameof(MarkdownView.Comments), new MarkdownComment[] { new("c1", 1, "Before") });
            builder.AddComponentParameter(5, nameof(MarkdownView.OnEditComment), EventCallback.Factory.Create<MarkdownComment>(this, _ => { }));
            builder.CloseComponent();
            builder.CloseElement();
        });

        host.Find("[data-testid='markdown-comment-edit-c1']").Click();

        // The draft handles no key itself, so with the walk ended at its box no
        // handler hears the key at all — which bUnit reports as having none to call.
        Assert.Throws<MissingEventHandlerException>(() =>
            host.Find("textarea").KeyDown(new KeyboardEventArgs { Key = "Escape" }));
        Assert.Throws<MissingEventHandlerException>(() =>
            host.Find("[data-testid='markdown-comment-save-c1']").KeyDown(new KeyboardEventArgs { Key = "Escape" }));

        Assert.Equal(0, hostHeard);
        Assert.NotEmpty(host.FindAll("textarea"));

        host.Find("li").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        Assert.Equal(1, hostHeard);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Before")]
    public void An_empty_or_unchanged_body_is_not_an_edit(string typed)
    {
        using var context = new BunitContext();
        var edits = 0;

        var view = Render(context, p => p
            .Add(v => v.Blocks, Blocks)
            .Add(v => v.Comments, new MarkdownComment[] { new("c1", 1, "Before") })
            .Add(v => v.OnEditComment, _ => edits++));

        view.Find("[data-testid='markdown-comment-edit-c1']").Click();
        view.Find("textarea").Input(typed);
        view.Find("[data-testid='markdown-comment-save-c1']").Click();

        Assert.Equal(0, edits);
    }

    [Fact]
    public void A_comment_offers_only_what_someone_is_listening_for()
    {
        using var context = new BunitContext();

        var view = Render(context, p => p
            .Add(v => v.Blocks, Blocks)
            .Add(v => v.Comments, new MarkdownComment[] { new("c1", 1, "A remark") }));

        Assert.Empty(view.FindAll("[data-testid='markdown-comment-edit-c1']"));
        Assert.Empty(view.FindAll("[data-testid='markdown-comment-resolve-c1']"));

        // But it is still shown: a comment nobody can act on is still a comment.
        Assert.Equal("A remark", view.Find(".md-comment__body").TextContent);
    }

    [Fact]
    public void The_margin_layout_is_the_same_markup_in_a_second_column()
    {
        using var context = new BunitContext();

        var inline = Render(context, p => p
            .Add(v => v.Blocks, Blocks)
            .Add(v => v.Comments, new MarkdownComment[] { new("c1", 1, "A remark") }));

        var margin = Render(context, p => p
            .Add(v => v.Blocks, Blocks)
            .Add(v => v.Comments, new MarkdownComment[] { new("c1", 1, "A remark") })
            .Add(v => v.Layout, MarkdownCommentLayout.Margin));

        // One markup, two layouts — so the two shapes cannot drift apart.
        Assert.Equal(inline.FindAll(".md-block-row").Count, margin.FindAll(".md-block-row").Count);
        Assert.Equal(inline.FindAll(".md-comment").Count, margin.FindAll(".md-comment").Count);

        Assert.DoesNotContain("md-view--margin", inline.Find(".md-view").ClassList);
        Assert.Contains("md-view--margin", margin.Find(".md-view").ClassList);
    }

    [Fact]
    public void Comments_show_without_anyone_offering_to_add_one()
    {
        // Reading someone else's review is not the same as writing one.
        using var context = new BunitContext();

        var view = Render(context, p => p
            .Add(v => v.Blocks, Blocks)
            .Add(v => v.Comments, new MarkdownComment[] { new("c1", 0, "A remark") }));

        Assert.Single(view.FindAll(".md-comment"));
        Assert.Empty(view.FindAll(".md-block__comment"));
    }

    [Fact]
    public void A_block_offering_a_comment_reserves_the_corner_it_hangs_in()
    {
        // The control is out of flow, so without the reservation a paragraph long
        // enough to reach the edge is read through the button sitting on it.
        using var context = new BunitContext();

        var view = Render(context, p => p
            .Add(v => v.Blocks, Blocks)
            .Add(v => v.OnAddComment, _ => { }));

        var block = view.Find(".md-block-row[data-block='1'] .md-block");

        Assert.Contains("md-block--affordance", block.ClassList);
        Assert.DoesNotContain("md-block--affordance-pair", block.ClassList);
        Assert.Single(view.FindAll(".md-block-row[data-block='1'] .md-block__affordances .md-block__comment"));
    }

    [Fact]
    public void A_block_with_nothing_in_the_corner_reserves_nothing()
    {
        // Annotated, because there are comments to show; no affordance, because
        // nobody is listening for a new one. A gutter here would hold nothing.
        using var context = new BunitContext();

        var view = Render(context, p => p
            .Add(v => v.Blocks, Blocks)
            .Add(v => v.Comments, new MarkdownComment[] { new("c1", 0, "A remark") }));

        Assert.Empty(view.FindAll(".md-block--affordance"));
        Assert.Empty(view.FindAll(".md-block__affordances"));
    }

    [Fact]
    public void Deleting_reports_the_comments_id()
    {
        using var context = new BunitContext();
        string? deleted = null;

        var view = Render(context, p => p
            .Add(v => v.Blocks, Blocks)
            .Add(v => v.Comments, new MarkdownComment[] { new("c1", 1, "Stray note") })
            .Add(v => v.OnDeleteComment, id => deleted = id));

        view.Find("[data-testid='markdown-comment-delete-c1']").Click();

        Assert.Equal("c1", deleted);
    }

    [Fact]
    public void A_comment_offers_no_delete_when_nobody_is_listening()
    {
        using var context = new BunitContext();

        var view = Render(context, p => p
            .Add(v => v.Blocks, Blocks)
            .Add(v => v.Comments, new MarkdownComment[] { new("c1", 1, "A remark") }));

        Assert.Empty(view.FindAll("[data-testid='markdown-comment-delete-c1']"));
    }

    [Fact]
    public void Adding_a_remark_opens_it_straight_into_its_own_textarea()
    {
        // The affordance and the box to type into are the same act: an
        // empty-bodied comment is a draft, and a draft is shown editing without
        // a second press on Edit.
        //
        // The explicit re-render after the click stands in for what a real host
        // does on its own: OnAddComment's receiver is the host, so invoking it
        // re-renders the host and pushes the grown list back down as a
        // parameter — see Arc42DevbookPanel. bUnit has no such host above the
        // component under test, so the test plays that part.
        using var context = new BunitContext();
        var comments = new List<MarkdownComment>();

        var view = Render(context, p => p
            .Add(v => v.Blocks, Blocks)
            .Add(v => v.Comments, comments)
            .Add(v => v.OnAddComment, index => comments.Add(new MarkdownComment("draft", index, string.Empty, "You", "just now")))
            .Add(v => v.OnEditComment, edited =>
            {
                var i = comments.FindIndex(c => c.Id == edited.Id);
                comments[i] = edited;
            }));

        view.Find("[data-testid='markdown-comment-1']").Click();
        view.Render(p => p.Add(v => v.Comments, comments));

        Assert.Single(view.FindAll("[data-testid='markdown-comment-editor-draft']"));
        Assert.Empty(view.FindAll("[data-testid='markdown-comment-edit-draft']"));

        view.Find("[data-testid='markdown-comment-editor-draft']").Input("Worth flagging.");
        view.Find("[data-testid='markdown-comment-save-draft']").Click();
        view.Render(p => p.Add(v => v.Comments, comments));

        Assert.Equal("Worth flagging.", view.Find(".md-comment__body").TextContent);
    }

    [Fact]
    public void Cancelling_a_fresh_draft_asks_the_host_to_delete_it()
    {
        using var context = new BunitContext();
        var comments = new List<MarkdownComment>();
        string? deleted = null;

        var view = Render(context, p => p
            .Add(v => v.Blocks, Blocks)
            .Add(v => v.Comments, comments)
            .Add(v => v.OnAddComment, index => comments.Add(new MarkdownComment("draft", index, string.Empty, "You", "just now")))
            .Add(v => v.OnDeleteComment, id => deleted = id));

        view.Find("[data-testid='markdown-comment-1']").Click();
        view.Render(p => p.Add(v => v.Comments, comments));
        view.Find("[data-testid='markdown-comment-cancel-draft']").Click();

        Assert.Equal("draft", deleted);
    }

    [Fact]
    public void Cancelling_an_existing_comments_edit_does_not_delete_it()
    {
        // Only a draft with nothing said yet is abandoned on Cancel — an existing
        // remark just gets its wording put back, exactly as before.
        using var context = new BunitContext();
        var deletes = 0;

        var view = Render(context, p => p
            .Add(v => v.Blocks, Blocks)
            .Add(v => v.Comments, new MarkdownComment[] { new("c1", 1, "Before") })
            .Add(v => v.OnEditComment, _ => { })
            .Add(v => v.OnDeleteComment, _ => deletes++));

        view.Find("[data-testid='markdown-comment-edit-c1']").Click();
        view.Find("textarea").Input("After");
        view.Find("[data-testid='markdown-comment-cancel-c1']").Click();

        Assert.Equal(0, deletes);
        Assert.Equal("Before", view.Find(".md-comment__body").TextContent);
    }

    // --- The orphan group -------------------------------------------------

    /// <summary>A remark whose index is past the end of <see cref="Blocks"/>: its
    /// block has gone, so it lands in the orphan group.</summary>
    private static MarkdownComment Orphan(string id = "c9", string body = "Is this still true?", string? author = "Reviewer") =>
        new(id, 99, body, author, "today");

    [Fact]
    public void An_orphan_group_says_the_passage_changed_and_names_itself_by_it()
    {
        using var context = new BunitContext();

        var view = Render(context, p => p
            .Add(v => v.Blocks, Blocks)
            .Add(v => v.Comments, new[] { Orphan() }));

        var group = view.Find("[data-testid='markdown-orphaned-comments']");
        var label = group.QuerySelector(".md-comments__orphaned-label");

        Assert.NotNull(label);
        Assert.Equal("Passage changed", label!.TextContent.Trim());
        Assert.Equal("group", group.GetAttribute("role"));
        Assert.Equal(label.Id, group.GetAttribute("aria-labelledby"));
        Assert.False(string.IsNullOrEmpty(label.Id));
    }

    [Fact]
    public void An_orphan_group_tells_the_reader_what_they_can_do_with_a_remark()
    {
        using var context = new BunitContext();

        var view = Render(context, p => p
            .Add(v => v.Blocks, Blocks)
            .Add(v => v.Comments, new[] { Orphan() })
            .Add(v => v.OnEditComment, _ => { })
            .Add(v => v.OnResolveComment, _ => { })
            .Add(v => v.OnDeleteComment, _ => { })
            .Add(v => v.OnAddComment, _ => { }));

        var group = view.Find("[data-testid='markdown-orphaned-comments']");

        Assert.Equal(
            "These remarks were left on text that has since been reworded or removed. Resolve or delete each one, or leave a new remark where it belongs now.",
            group.QuerySelector(".md-comments__orphaned-hint")!.TextContent.Trim());

        // The tools to act on it are still on the remark itself.
        Assert.NotNull(group.QuerySelector("[data-testid='markdown-comment-edit-c9']"));
        Assert.NotNull(group.QuerySelector("[data-testid='markdown-comment-resolve-c9']"));
        Assert.NotNull(group.QuerySelector("[data-testid='markdown-comment-delete-c9']"));
    }

    [Fact]
    public void An_orphan_group_nobody_can_act_on_says_only_what_happened()
    {
        // An archived repository's chapter: the host binds no remark callback.
        using var context = new BunitContext();

        var view = Render(context, p => p
            .Add(v => v.Blocks, Blocks)
            .Add(v => v.Comments, new[] { Orphan() }));

        var group = view.Find("[data-testid='markdown-orphaned-comments']");

        Assert.Equal("Passage changed", group.QuerySelector(".md-comments__orphaned-label")!.TextContent.Trim());
        Assert.Equal(
            "These remarks were left on text that has since been reworded or removed.",
            group.QuerySelector(".md-comments__orphaned-hint")!.TextContent.Trim());
        Assert.Null(group.QuerySelector("[data-testid='markdown-comment-resolve-c9']"));
        Assert.Null(group.QuerySelector("[data-testid='markdown-comment-delete-c9']"));
    }

    [Fact]
    public void An_orphan_group_of_resolved_remarks_does_not_offer_to_resolve_them()
    {
        // Each card offers Reopen, not Resolve, so the hint must not name Resolve.
        using var context = new BunitContext();

        var view = Render(context, p => p
            .Add(v => v.Blocks, Blocks)
            .Add(v => v.Comments, new[] { Orphan() with { Resolved = true } })
            .Add(v => v.OnResolveComment, _ => { })
            .Add(v => v.OnDeleteComment, _ => { })
            .Add(v => v.OnAddComment, _ => { }));

        var group = view.Find("[data-testid='markdown-orphaned-comments']");

        Assert.Equal(
            "These remarks were left on text that has since been reworded or removed. Delete each one, or leave a new remark where it belongs now.",
            group.QuerySelector(".md-comments__orphaned-hint")!.TextContent.Trim());
    }

    [Theory]
    [InlineData(true, true, false, "Resolve or delete each one.")]
    [InlineData(true, false, false, "Resolve each one.")]
    [InlineData(false, true, false, "Delete each one.")]
    [InlineData(true, false, true, "Resolve each one, or leave a new remark where it belongs now.")]
    [InlineData(false, false, true, "Leave a new remark where it belongs now.")]
    public void An_orphan_group_names_only_the_actions_it_offers(bool resolve, bool delete, bool add, string action)
    {
        using var context = new BunitContext();

        var view = Render(context, p =>
        {
            p.Add(v => v.Blocks, Blocks).Add(v => v.Comments, new[] { Orphan() });
            if (resolve) p.Add(v => v.OnResolveComment, _ => { });
            if (delete) p.Add(v => v.OnDeleteComment, _ => { });
            if (add) p.Add(v => v.OnAddComment, _ => { });
        });

        Assert.Equal(
            $"These remarks were left on text that has since been reworded or removed. {action}",
            view.Find(".md-comments__orphaned-hint").TextContent.Trim());
    }

    [Fact]
    public void A_tag_in_a_remark_reads_as_typed()
    {
        using var context = new BunitContext();

        var view = Render(context, p => p
            .Add(v => v.Blocks, Blocks)
            .Add(v => v.Comments, new MarkdownComment[] { new("c1", 1, "Check the #sync step.") }));

        var body = view.Find(".md-comment__body");

        Assert.Equal("Check the #sync step.", body.TextContent);
        Assert.Null(body.QuerySelector(".tag-chip"));
    }

    [Fact]
    public void An_orphan_group_of_proposals_alone_speaks_of_proposals()
    {
        using var context = new BunitContext();

        var view = Render(context, p => p
            .Add(v => v.Blocks, Blocks)
            .Add(v => v.Proposals, new AiProposal[] { new("p1", AiProposalKind.Rewrite, "A better paragraph.", BlockIndex: 99) }));

        var group = view.Find("[data-testid='markdown-orphaned-comments']");

        Assert.Equal("Passage changed", group.QuerySelector(".md-comments__orphaned-label")!.TextContent.Trim());
        Assert.Equal(
            "These proposals were made on text that has since been reworded or removed.",
            group.QuerySelector(".md-comments__orphaned-hint")!.TextContent.Trim());
    }

    [Fact]
    public void A_view_with_every_remark_in_place_has_no_orphan_label()
    {
        using var context = new BunitContext();

        var view = Render(context, p => p
            .Add(v => v.Blocks, Blocks)
            .Add(v => v.Comments, new MarkdownComment[] { new("c1", 1, "In place") }));

        Assert.Empty(view.FindAll("[data-testid='markdown-orphaned-comments']"));
        Assert.Empty(view.FindAll(".md-comments__orphaned-label"));
    }

    // --- The body ---------------------------------------------------------

    [Fact]
    public void A_remark_body_renders_its_inline_markdown()
    {
        using var context = new BunitContext();

        var view = Render(context, p => p
            .Add(v => v.Blocks, Blocks)
            .Add(v => v.Comments, new MarkdownComment[] { new("c1", 1, "See [the docs](https://example.test/docs), `code` and *now* **bold** ~~gone~~.") }));

        var body = view.Find(".md-comment__body");

        Assert.Equal("https://example.test/docs", body.QuerySelector("a.md-link")!.GetAttribute("href"));
        Assert.Equal("code", body.QuerySelector("code")!.TextContent);
        Assert.Equal("now", body.QuerySelector("em")!.TextContent);
        Assert.Equal("bold", body.QuerySelector("strong")!.TextContent);
        Assert.Equal("gone", body.QuerySelector("s")!.TextContent);
    }

    [Fact]
    public void A_plain_body_reads_exactly_as_it_was_written()
    {
        using var context = new BunitContext();
        const string Plain = "First line.\nSecond line, still plain.";

        var view = Render(context, p => p
            .Add(v => v.Blocks, Blocks)
            .Add(v => v.Comments, new MarkdownComment[] { new("c1", 1, Plain) }));

        Assert.Equal(Plain, view.Find(".md-comment__body").TextContent);
    }

    [Fact]
    public void Markup_in_a_remark_body_stays_text()
    {
        using var context = new BunitContext();

        var view = Render(context, p => p
            .Add(v => v.Blocks, Blocks)
            .Add(v => v.Comments, new MarkdownComment[] { new("c1", 1, "<script>alert(1)</script> <b>x</b>") }));

        var body = view.Find(".md-comment__body");

        Assert.Empty(body.QuerySelectorAll("script"));
        Assert.Empty(body.QuerySelectorAll("b"));
        Assert.Equal("<script>alert(1)</script> <b>x</b>", body.TextContent);
    }

    [Fact]
    public void An_image_in_a_remark_shows_as_its_alt_text()
    {
        using var context = new BunitContext();

        var view = Render(context, p => p
            .Add(v => v.Blocks, Blocks)
            .Add(v => v.Comments, new MarkdownComment[] { new("c1", 1, "Look: ![the diagram](https://example.test/d.png)") }));

        var body = view.Find(".md-comment__body");

        Assert.Empty(body.QuerySelectorAll("img"));
        Assert.Equal("Look: the diagram", body.TextContent);
    }

    [Fact]
    public void A_link_in_a_remark_on_a_chapter_goes_where_the_chapters_own_link_goes()
    {
        using var context = new BunitContext();
        var followed = new List<DevbookReference>();
        const string Link = "[the sessions model](.domain/sessions/domain.md)";

        var view = Render(context, p => p
            .Add(v => v.Blocks, MarkdownPreview.Parse($"See {Link}.\n\nA paragraph."))
            .Add(v => v.RenderDevbookMetadata, true)
            .Add(v => v.OnDevbookNavigate, EventCallback.Factory.Create<DevbookReference>(this, followed.Add))
            .Add(v => v.Comments, new MarkdownComment[] { new("c1", 1, $"Compare {Link}.") }));

        var inBody = view.Find(".md-comment__body button.devbook-ref--action");
        var inChapter = view.FindAll("button.devbook-ref--action").First(b => b.Closest(".md-comment__body") is null);

        Assert.Equal(inChapter.GetAttribute("title"), inBody.GetAttribute("title"));
        Assert.Equal(inChapter.TextContent, inBody.TextContent);

        inBody.Click();

        Assert.Equal(".domain/sessions/domain.md", Assert.Single(followed).Path);
    }

    [Fact]
    public void Editing_a_markdown_body_edits_and_saves_its_source()
    {
        using var context = new BunitContext();
        MarkdownComment? edited = null;
        const string Source = "See [the docs](https://example.test) and `code`.";

        var view = Render(context, p => p
            .Add(v => v.Blocks, Blocks)
            .Add(v => v.Comments, new MarkdownComment[] { new("c1", 1, Source) })
            .Add(v => v.OnEditComment, c => edited = c));

        view.Find("[data-testid='markdown-comment-edit-c1']").Click();

        // The box holds what was typed, not what it rendered as.
        Assert.Equal(Source, view.Find("textarea").GetAttribute("value"));

        const string Revised = "See [the docs](https://example.test), `code` and *more*.";
        view.Find("textarea").Input(Revised);
        view.Find("[data-testid='markdown-comment-save-c1']").Click();

        Assert.Equal(Revised, edited?.Body);
    }

    // --- The author -------------------------------------------------------

    [Fact]
    public void A_slug_author_reads_as_a_name_and_keeps_the_slug_on_its_title()
    {
        using var context = new BunitContext();

        var view = Render(context, p => p
            .Add(v => v.Blocks, Blocks)
            .Add(v => v.Comments, new MarkdownComment[] { new("c1", 1, "A remark", "claude/chapter-review") }));

        var author = view.Find(".md-comment__author");

        Assert.Equal("Claude · chapter review", author.TextContent);
        Assert.Equal("claude/chapter-review", author.GetAttribute("title"));
    }

    [Fact]
    public void A_plain_author_reads_as_written_with_no_title()
    {
        using var context = new BunitContext();

        var view = Render(context, p => p
            .Add(v => v.Blocks, Blocks)
            .Add(v => v.Comments, new MarkdownComment[] { new("c1", 1, "A remark", "DEV-LAPTOP") }));

        var author = view.Find(".md-comment__author");

        Assert.Equal("DEV-LAPTOP", author.TextContent);
        Assert.False(author.HasAttribute("title"));
    }
}
