namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// In the margin layout every remark is drawn in the margin, an orphan included:
/// level with the block its stored index names, clamped into the chapter, under its
/// own label and marked as approximate. Inline, orphans keep the labelled group at
/// the end. Where an orphan is drawn is presentation only — nothing here writes an
/// anchor (local ADR 0011).
/// </summary>
public sealed class MarkdownCommentMarginOrphanTests
{
    // Three blocks: 0 the heading, 1 the paragraph, 2 the list.
    private static readonly IReadOnlyList<MdBlock> Blocks =
        MarkdownPreview.ParseDocument("# A heading\n\nA paragraph.\n\n- A bullet");

    private static IRenderedComponent<MarkdownView> Render(
        BunitContext context,
        Action<ComponentParameterCollectionBuilder<MarkdownView>> parameters)
    {
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context.Render(parameters);
    }

    private static Action<ComponentParameterCollectionBuilder<MarkdownView>> Margin(
        params MarkdownComment[] comments) =>
        p => p
            .Add(v => v.Blocks, Blocks)
            .Add(v => v.Comments, comments)
            .Add(v => v.Layout, MarkdownCommentLayout.Margin);

    /// <summary>The row a remark card was drawn in, read off its <c>data-block</c>;
    /// -1 when it is not inside a row's margin cell.</summary>
    private static int RowOf(IRenderedComponent<MarkdownView> view, string commentId)
    {
        var card = view.Find($"[data-testid='markdown-comment-body-{commentId}']");

        if (card.Closest(".md-block-row__notes") is null) return -1;

        return int.Parse(card.Closest(".md-block-row")!.GetAttribute("data-block")!, System.Globalization.CultureInfo.InvariantCulture);
    }

    [Theory]
    [InlineData(1, "digest-of-the-old-wording", 1)] // reworded in place: its own index
    [InlineData(99, null, 2)]                       // past the end: the last block
    [InlineData(-5, null, 0)]                       // before the start: the first block
    public void An_orphan_stands_in_the_margin_level_with_the_block_it_last_named(int stored, string? digest, int row)
    {
        using var context = new BunitContext();

        var view = Render(context, Margin(new MarkdownComment("o1", stored, "Is this still true?", "Reviewer", "today", BlockHash: digest)));

        Assert.Equal(row, RowOf(view, "o1"));
        Assert.Empty(view.FindAll("[data-testid='markdown-orphaned-comments']"));
        Assert.Empty(view.FindAll(".md-comments--orphaned"));
    }

    [Fact]
    public void A_margin_orphan_is_marked_approximate_and_an_anchored_remark_is_not()
    {
        // The classes the stylesheet's highlight reads: solid for an anchored
        // remark's block, dashed for an orphan's guessed one.
        using var context = new BunitContext();

        var view = Render(context, Margin(
            new MarkdownComment("a1", 1, "In place"),
            new MarkdownComment("o1", 99, "Adrift")));

        var orphan = view.Find("[data-testid='markdown-comment-body-o1']");
        var anchored = view.Find("[data-testid='markdown-comment-body-a1']");

        Assert.Contains("md-comment--approximate", orphan.ClassList);
        Assert.Contains("md-comments--approximate", orphan.ParentElement!.ClassList);
        Assert.DoesNotContain("md-comment--approximate", anchored.ClassList);
        Assert.DoesNotContain("md-comments--approximate", anchored.ParentElement!.ClassList);
        Assert.NotNull(anchored.Closest(".md-block-row[data-block='1']"));
    }

    [Fact]
    public void A_margin_orphan_carries_its_own_label_and_hint_and_is_described_by_both()
    {
        using var context = new BunitContext();

        var view = Render(context, p => Margin(new MarkdownComment("o1", 99, "Adrift"))(p
            .Add(v => v.OnEditComment, _ => { })
            .Add(v => v.OnResolveComment, _ => { })
            .Add(v => v.OnDeleteComment, _ => { })
            .Add(v => v.OnAddComment, _ => { })));

        var card = view.Find("[data-testid='markdown-comment-body-o1']");
        var label = card.QuerySelector(".md-comment__orphaned-label")!;
        var hint = card.QuerySelector(".md-comment__orphaned-hint")!;

        Assert.Equal("Passage changed", label.TextContent.Trim());
        Assert.Equal(
            "This remark was left on text that has since been reworded or removed; it is shown near where that text was. Resolve or delete it, or leave a new remark where it belongs now.",
            hint.TextContent.Trim());
        Assert.Equal($"{label.Id} {hint.Id}", card.GetAttribute("aria-describedby"));

        // Its tools are the ones every remark carries.
        Assert.NotNull(card.QuerySelector("[data-testid='markdown-comment-resolve-o1']"));
        Assert.NotNull(card.QuerySelector("[data-testid='markdown-comment-delete-o1']"));
    }

    [Fact]
    public void A_resolved_margin_orphan_nobody_can_add_beside_names_only_delete()
    {
        using var context = new BunitContext();

        var view = Render(context, p => Margin(new MarkdownComment("o1", 99, "Adrift", Resolved: true))(p
            .Add(v => v.OnResolveComment, _ => { })
            .Add(v => v.OnDeleteComment, _ => { })));

        Assert.Equal(
            "This remark was left on text that has since been reworded or removed; it is shown near where that text was. Delete it.",
            view.Find(".md-comment__orphaned-hint").TextContent.Trim());
    }

    [Fact]
    public void A_margin_orphan_nobody_can_act_on_says_only_what_happened()
    {
        using var context = new BunitContext();

        var view = Render(context, Margin(new MarkdownComment("o1", 99, "Adrift")));

        Assert.Equal(
            "This remark was left on text that has since been reworded or removed; it is shown near where that text was.",
            view.Find(".md-comment__orphaned-hint").TextContent.Trim());
    }

    [Fact]
    public void Several_margin_orphans_never_share_an_id()
    {
        using var context = new BunitContext();

        var view = Render(context, Margin(
            new MarkdownComment("o1", 99, "First"),
            new MarkdownComment("o2", 42, "Second"),
            new MarkdownComment("o3", -1, "Third")));

        var ids = view.FindAll("[id]").Select(element => element.Id).ToList();

        Assert.Equal(6, view.FindAll(".md-comment--approximate .md-comment__orphaned-label, .md-comment--approximate .md-comment__orphaned-hint").Count);
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());

        foreach (var card in view.FindAll(".md-comment--approximate"))
        {
            foreach (var id in card.GetAttribute("aria-describedby")!.Split(' '))
            {
                Assert.NotNull(card.QuerySelector($"[id='{id}']"));
            }
        }
    }

    [Fact]
    public void Inline_an_orphan_keeps_the_labelled_group_at_the_end()
    {
        using var context = new BunitContext();

        var view = Render(context, p => p
            .Add(v => v.Blocks, Blocks)
            .Add(v => v.Comments, new[] { new MarkdownComment("o1", 99, "Adrift") }));

        var group = view.Find("[data-testid='markdown-orphaned-comments']");

        Assert.NotNull(group.QuerySelector("[data-testid='markdown-comment-body-o1']"));
        Assert.Empty(view.FindAll(".md-comment--approximate"));
        Assert.Empty(view.FindAll(".md-comment__orphaned-label"));
        Assert.Equal(-1, RowOf(view, "o1"));
    }

    /// <summary>The row an approximate proposal was drawn in, read off its
    /// <c>data-block</c>; -1 when it is not inside a row's margin cell.</summary>
    private static int ProposalRowOf(IRenderedComponent<MarkdownView> view, string proposalId)
    {
        var card = view.Find($"[data-testid='markdown-proposal-{proposalId}']");

        if (card.Closest(".md-block-row__notes .md-approximate-proposal") is null) return -1;

        return int.Parse(card.Closest(".md-block-row")!.GetAttribute("data-block")!, System.Globalization.CultureInfo.InvariantCulture);
    }

    public static TheoryData<AiProposal, int> OrphanProposals => new()
    {
        // Past the end: the last block, as a remark is.
        { new AiProposal("p1", AiProposalKind.Rewrite, "A better paragraph.", BlockIndex: 99), 2 },
        // Before the start: the first block.
        { new AiProposal("p1", AiProposalKind.Rewrite, "A better paragraph.", BlockIndex: -3), 0 },
        // No block at all: beside the last one.
        { new AiProposal("p1", AiProposalKind.Answer, "An answer."), 2 },
        // Its remark deleted, its block still named: that block.
        { new AiProposal("p1", AiProposalKind.CommentResolution, "Settle it.", BlockIndex: 1, CommentId: "gone"), 1 },
    };

    [Theory]
    [MemberData(nameof(OrphanProposals))]
    public void In_the_margin_an_orphan_proposal_stands_beside_the_block_it_last_named(AiProposal proposal, int row)
    {
        using var context = new BunitContext();

        var view = Render(context, p => Margin()(p.Add(v => v.Proposals, new[] { proposal })));

        Assert.Equal(row, ProposalRowOf(view, "p1"));
        Assert.Empty(view.FindAll("[data-testid='markdown-orphaned-comments']"));
    }

    [Fact]
    public void A_margin_orphan_proposal_carries_the_proposal_wording_and_is_labelled_by_it()
    {
        using var context = new BunitContext();

        var view = Render(context, p => Margin()(p
            .Add(v => v.Proposals, new AiProposal[] { new("p1", AiProposalKind.Rewrite, "A better paragraph.", BlockIndex: 99) })));

        var group = view.Find("[data-testid='markdown-approximate-proposal-p1']");
        var label = group.QuerySelector(".md-comment__orphaned-label")!;
        var hint = group.QuerySelector(".md-comment__orphaned-hint")!;

        Assert.Equal("group", group.GetAttribute("role"));
        Assert.Equal("Passage changed", label.TextContent.Trim());
        Assert.Equal(
            "This proposal was made on text that has since been reworded or removed; it is shown near where that text was.",
            hint.TextContent.Trim());
        Assert.Equal(label.Id, group.GetAttribute("aria-labelledby"));
        Assert.Equal(hint.Id, group.GetAttribute("aria-describedby"));

        // The dashed "about here" edge, from the list it stands in.
        Assert.NotNull(group.Closest(".md-proposals--approximate"));
    }

    [Fact]
    public void A_margin_orphan_proposal_can_still_be_accepted_and_rejected()
    {
        using var context = new BunitContext();
        var accepted = new List<string>();
        var rejected = new List<string>();

        var view = Render(context, p => Margin()(p
            .Add(v => v.Proposals, new AiProposal[] { new("p1", AiProposalKind.Rewrite, "A better paragraph.", BlockIndex: 99) })
            .Add(v => v.OnAcceptProposal, proposal => accepted.Add(proposal.Id))
            .Add(v => v.OnRejectProposal, proposal => rejected.Add(proposal.Id))));

        view.Find("[data-testid='markdown-proposal-accept-p1']").Click();
        view.Find("[data-testid='markdown-proposal-reject-p1']").Click();

        Assert.Equal("p1", Assert.Single(accepted));
        Assert.Equal("p1", Assert.Single(rejected));
    }

    [Fact]
    public void Several_margin_orphan_proposals_never_share_an_id()
    {
        using var context = new BunitContext();

        var view = Render(context, p => Margin(new MarkdownComment("o1", 99, "Adrift"))(p
            .Add(v => v.Proposals, new AiProposal[]
            {
                new("p1", AiProposalKind.Rewrite, "One.", BlockIndex: 99),
                new("p2", AiProposalKind.Answer, "Two."),
                new("p3", AiProposalKind.Rewrite, "Three.", BlockIndex: -1),
            })));

        var ids = view.FindAll("[id]").Select(element => element.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(3, view.FindAll(".md-approximate-proposal").Count);
    }

    [Fact]
    public void In_the_margin_nothing_is_left_for_the_group_at_the_end()
    {
        using var context = new BunitContext();

        var view = Render(context, p => Margin(new MarkdownComment("o1", 99, "Adrift"))(p
            .Add(v => v.Proposals, new AiProposal[] { new("p1", AiProposalKind.Answer, "An answer.") })));

        Assert.Empty(view.FindAll("[data-testid='markdown-orphaned-comments']"));
        Assert.Empty(view.FindAll(".md-comments--orphaned"));
        Assert.Equal(2, RowOf(view, "o1"));
        Assert.Equal(2, ProposalRowOf(view, "p1"));
    }

    [Fact]
    public void Inline_an_orphan_proposal_keeps_the_group_at_the_end()
    {
        using var context = new BunitContext();

        var view = Render(context, p => p
            .Add(v => v.Blocks, Blocks)
            .Add(v => v.Proposals, new AiProposal[] { new("p1", AiProposalKind.Rewrite, "A better paragraph.", BlockIndex: 99) }));

        var group = view.Find("[data-testid='markdown-orphaned-comments']");

        Assert.NotNull(group.QuerySelector("[data-testid='markdown-proposal-p1']"));
        Assert.Empty(view.FindAll(".md-approximate-proposal"));
        Assert.Equal(
            "These proposals were made on text that has since been reworded or removed.",
            group.QuerySelector(".md-comments__orphaned-hint")!.TextContent.Trim());
    }

    [Fact]
    public void A_blocks_counter_counts_the_orphans_placed_beside_it_in_the_margin()
    {
        using var context = new BunitContext();

        var view = Render(context, p => Margin(
            new MarkdownComment("a1", 1, "In place"),
            new MarkdownComment("o1", 1, "Reworded", BlockHash: "digest-of-the-old-wording"),
            new MarkdownComment("o2", 99, "Adrift"))(p
            .Add(v => v.OnAddComment, _ => { })));

        Assert.Equal("2", view.Find("[data-testid='markdown-comment-1'] .md-block__count").TextContent);
        Assert.Equal("1", view.Find("[data-testid='markdown-comment-2'] .md-block__count").TextContent);

        // A block whose only remark is a placed orphan wears the commented state,
        // so its counter shows without a hover, as any commented block's does.
        Assert.Contains("md-block--commented", view.Find(".md-block-row[data-block='2'] > .md-block").ClassList);
    }

    [Fact]
    public void Inline_a_blocks_counter_leaves_the_orphans_to_the_group()
    {
        using var context = new BunitContext();

        var view = Render(context, p => p
            .Add(v => v.Blocks, Blocks)
            .Add(v => v.Comments, new[] { new MarkdownComment("a1", 1, "In place"), new MarkdownComment("o1", 99, "Adrift") })
            .Add(v => v.OnAddComment, _ => { }));

        Assert.Equal("1", view.Find("[data-testid='markdown-comment-1'] .md-block__count").TextContent);
        Assert.Empty(view.FindAll("[data-testid='markdown-comment-2'] .md-block__count"));
    }

    [Fact]
    public void Every_card_in_a_margin_cell_stands_in_its_stack()
    {
        // The stack is what the margin script positions so a row does not grow
        // to its remarks' height; a card outside it would stretch the row again.
        using var context = new BunitContext();

        var view = Render(context, p => Margin(
            new MarkdownComment("a1", 1, "In place"),
            new MarkdownComment("o1", 1, "Reworded", BlockHash: "digest-of-the-old-wording"))(p
            .Add(v => v.Proposals, new AiProposal[]
            {
                new("p1", AiProposalKind.Rewrite, "Anchored.", BlockIndex: 1),
                new("p2", AiProposalKind.Rewrite, "Adrift.", BlockIndex: 99),
            })));

        // Block 0 has nothing beside it; 1 has its remarks and p1; 2 has p2.
        Assert.Empty(view.Find(".md-block-row[data-block='0'] .md-block-row__notes").Children);
        foreach (var row in new[] { 1, 2 })
        {
            var stack = Assert.Single(view.Find($".md-block-row[data-block='{row}'] .md-block-row__notes").Children);
            Assert.Contains("md-block-row__notes-stack", stack.ClassList);
        }

        foreach (var card in view.FindAll(".md-comment, .md-proposal"))
        {
            Assert.NotNull(card.Closest(".md-block-row__notes-stack"));
        }
    }

    [Fact]
    public void A_row_with_nothing_beside_it_draws_no_stack_to_take_the_pointer()
    {
        // A positioned stack takes clicks even empty, and an empty one sat over
        // the earlier stack running on past it: the Edit under it never fired.
        // Three remarks on the heading, none on the paragraph or the list, with
        // the corner affordance on, so every block still has a row and a cell.
        using var context = new BunitContext();

        var view = Render(context, p => Margin(
            new MarkdownComment("a1", 0, "One"),
            new MarkdownComment("a2", 0, "Two"),
            new MarkdownComment("a3", 0, "Three"))(p
            .Add(v => v.OnAddComment, _ => { })));

        Assert.Equal(3, view.FindAll(".md-block-row__notes").Count);
        var stack = Assert.Single(view.FindAll(".md-block-row__notes-stack"));
        Assert.Equal("0", stack.Closest(".md-block-row")!.GetAttribute("data-block"));
        Assert.Empty(view.Find(".md-block-row[data-block='1'] .md-block-row__notes").Children);
        Assert.Empty(view.Find(".md-block-row[data-block='2'] .md-block-row__notes").Children);
    }

    [Fact]
    public void A_row_draws_its_stack_once_a_remark_arrives_beside_it()
    {
        using var context = new BunitContext();
        Action<ComponentParameterCollectionBuilder<MarkdownView>> withNone = p => Margin()(p.Add(v => v.OnAddComment, _ => { }));

        var view = Render(context, withNone);
        Assert.Empty(view.FindAll(".md-block-row__notes-stack"));

        view.Render(p => Margin(new MarkdownComment("a1", 2, "Now"))(p.Add(v => v.OnAddComment, _ => { })));

        var stack = Assert.Single(view.FindAll(".md-block-row__notes-stack"));
        Assert.Equal("2", stack.Closest(".md-block-row")!.GetAttribute("data-block"));
    }

    [Fact]
    public void A_margin_view_with_no_blocks_keeps_its_orphans_in_the_group()
    {
        // No block to stand beside, so the group is the only place left.
        using var context = new BunitContext();

        var view = Render(context, p => p
            .Add(v => v.Blocks, Array.Empty<MdBlock>())
            .Add(v => v.Comments, new[] { new MarkdownComment("o1", 0, "Adrift") })
            .Add(v => v.Layout, MarkdownCommentLayout.Margin));

        var group = view.Find("[data-testid='markdown-orphaned-comments']");

        Assert.NotNull(group.QuerySelector("[data-testid='markdown-comment-body-o1']"));
    }

    [Fact]
    public void A_margin_orphan_on_a_folded_meta_fence_stands_beside_the_heading_it_was_folded_into()
    {
        // A chapter's heading absorbs the `meta` fence under it, so the fence has
        // no row of its own; a guess that names the fence lands on the heading's.
        using var context = new BunitContext();

        var blocks = MarkdownPreview.ParseDocument("# A heading\n\n```meta\nstatus: draft\n```\n\nA paragraph.");

        var view = Render(context, p => p
            .Add(v => v.Blocks, blocks)
            .Add(v => v.RenderDevbookMetadata, true)
            .Add(v => v.Comments, new[] { new MarkdownComment("o1", 1, "Adrift", BlockHash: "digest-of-the-old-fence") })
            .Add(v => v.Layout, MarkdownCommentLayout.Margin));

        Assert.Empty(view.FindAll(".md-block-row[data-block='1']"));
        Assert.Equal(0, RowOf(view, "o1"));
    }
}
