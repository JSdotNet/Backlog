namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The AI affordances mount into the block rows and the comment items that
/// already exist, and nothing parallel is created.
///
/// <para>The first two tests are the regression guard and they are the reason
/// this file matters more than its size suggests: everything added to
/// MarkdownView is opt-in, so a view that asks for none of it has to render
/// exactly the markup it rendered before any of this existed. The rest of the
/// file is the opt-in half.</para>
/// </summary>
public sealed class MarkdownViewAiTests
{
    private static readonly IReadOnlyList<MdBlock> Blocks =
        MarkdownPreview.ParseDocument("# A heading\n\nA paragraph.\n\nAnother paragraph.");

    private static IRenderedComponent<MarkdownView> Render(
        BunitContext context,
        Action<ComponentParameterCollectionBuilder<MarkdownView>> parameters)
    {
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context.Render(parameters);
    }

    [Fact]
    public void A_view_given_neither_callback_nor_proposal_renders_what_it_always_did()
    {
        // The markup below is what this view rendered before the AI affordances
        // existed, written out rather than compared against itself: a snapshot
        // that lives in the test is the only version of this assertion that can
        // still fail once both sides are generated from the same component.
        using var context = new BunitContext();

        var view = Render(context, p => p.Add(v => v.Blocks, Blocks));

        view.MarkupMatches(
            """
            <div class="md-view">
              <p class="md-heading md-heading--1" role="heading" aria-level="1">A heading</p>
              <p class="md-p">A paragraph.</p>
              <p class="md-p">Another paragraph.</p>
            </div>
            """);
    }

    [Fact]
    public void The_new_parameters_at_their_defaults_change_nothing()
    {
        // Byte-identical, not merely equivalent: a wrapper that appeared only
        // when something was passed explicitly would still be a wrapper a
        // stylesheet could see.
        using var context = new BunitContext();

        var untouched = Render(context, p => p.Add(v => v.Blocks, Blocks));

        var defaulted = Render(context, p => p
            .Add(v => v.Blocks, Blocks)
            .Add(v => v.Proposals, Array.Empty<AiProposal>()));

        Assert.Equal(untouched.Markup, defaulted.Markup, StringComparer.Ordinal);

        // And none of the new hooks is anywhere in it.
        Assert.Empty(untouched.FindAll(".md-block-row"));
        Assert.Empty(untouched.FindAll(".md-block__rewrite"));
        Assert.Empty(untouched.FindAll(".ai-proposal"));
        Assert.Empty(untouched.FindAll("[data-testid='markdown-orphaned-comments']"));
    }

    [Fact]
    public void An_already_annotated_view_is_unchanged_by_the_addition_too()
    {
        // The guarantee is about every shape the view already rendered, not only
        // the plain one.
        using var context = new BunitContext();
        var comments = new MarkdownComment[] { new("c1", 1, "A remark") };

        var before = Render(context, p => p
            .Add(v => v.Blocks, Blocks)
            .Add(v => v.Comments, comments));

        var after = Render(context, p => p
            .Add(v => v.Blocks, Blocks)
            .Add(v => v.Comments, comments)
            .Add(v => v.Proposals, Array.Empty<AiProposal>()));

        Assert.Equal(before.Markup, after.Markup, StringComparer.Ordinal);
    }

    [Fact]
    public void A_rewrite_affordance_appears_beside_the_comment_one_and_reports_a_block()
    {
        using var context = new BunitContext();
        var rewritten = new List<int>();

        var view = Render(context, p => p
            .Add(v => v.Blocks, Blocks)
            .Add(v => v.OnRewriteBlock, rewritten.Add));

        var button = view.Find("[data-testid='markdown-rewrite-2']");

        Assert.Equal("Rewrite block 3 with AI", button.GetAttribute("aria-label"));

        button.Click();

        Assert.Equal(2, Assert.Single(rewritten));
    }

    [Fact]
    public void Asking_for_a_rewrite_is_enough_to_annotate_a_view_on_its_own()
    {
        // Annotated widened rather than gaining a second flag beside it, so a
        // view that only wants rewrites still gets the block rows the comments
        // would have brought.
        using var context = new BunitContext();

        var view = Render(context, p => p
            .Add(v => v.Blocks, Blocks)
            .Add(v => v.OnRewriteBlock, _ => { }));

        Assert.Equal(3, view.FindAll(".md-block-row").Count);
        Assert.Empty(view.FindAll(".md-block__comment:not(.md-block__rewrite)"));
    }

    [Fact]
    public void A_rewrite_proposal_renders_in_the_same_cell_the_comments_use()
    {
        // Which is what makes the margin layout work for it for free, with no
        // second layout to keep in step.
        using var context = new BunitContext();

        var view = Render(context, p => p
            .Add(v => v.Blocks, Blocks)
            .Add(v => v.Proposals, new AiProposal[]
            {
                new("p1", AiProposalKind.Rewrite, "A better paragraph.", BlockIndex: 1, Original: "A paragraph.")
            }));

        var card = view.Find(".md-block-row[data-block='1'] .md-block-row__notes [data-testid='markdown-proposal-p1']");

        Assert.Contains("A better paragraph.", card.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Accepting_raises_the_callback_and_writes_nothing()
    {
        // The host applies it, re-parses, and hands back new blocks — the same
        // discipline OnEditComment keeps, and for the same reason: the truth is
        // upstream.
        using var context = new BunitContext();
        AiProposal? accepted = null;

        var view = Render(context, p => p
            .Add(v => v.Blocks, Blocks)
            .Add(v => v.Proposals, new AiProposal[]
            {
                new("p1", AiProposalKind.Rewrite, "A better paragraph.", BlockIndex: 1)
            })
            .Add(v => v.OnAcceptProposal, proposal => accepted = proposal));

        view.Find("[data-testid='markdown-proposal-accept-p1']").Click();

        Assert.Equal("p1", accepted?.Id);

        // The document is exactly as it was: nothing was applied in place.
        Assert.Contains("A paragraph.", view.Find(".md-block-row[data-block='1'] .md-p").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejecting_leaves_the_document_byte_identical()
    {
        using var context = new BunitContext();
        AiProposal? rejected = null;

        var view = Render(context, p => p
            .Add(v => v.Blocks, Blocks)
            .Add(v => v.Proposals, new AiProposal[]
            {
                new("p1", AiProposalKind.Rewrite, "A better paragraph.", BlockIndex: 1)
            })
            .Add(v => v.OnRejectProposal, proposal => rejected = proposal));

        var before = view.Find(".md-block-row[data-block='1'] .md-block").InnerHtml;

        view.Find("[data-testid='markdown-proposal-reject-p1']").Click();

        Assert.Equal("p1", rejected?.Id);
        Assert.Equal(before, view.Find(".md-block-row[data-block='1'] .md-block").InnerHtml, StringComparer.Ordinal);
    }

    [Fact]
    public void Resolving_a_comment_with_AI_is_a_third_button_and_not_a_changed_second()
    {
        // One press must not do two things on one confirmation: a reader who
        // wanted the suggested wording but not the resolution has to be able to
        // say so.
        using var context = new BunitContext();
        var asked = new List<string>();
        var resolved = new List<string>();

        var view = Render(context, p => p
            .Add(v => v.Blocks, Blocks)
            .Add(v => v.Comments, new MarkdownComment[] { new("c1", 1, "A remark") })
            .Add(v => v.OnResolveComment, resolved.Add)
            .Add(v => v.OnResolveCommentWithAi, asked.Add));

        view.Find("[data-testid='markdown-comment-ai-resolve-c1']").Click();

        Assert.Equal("c1", Assert.Single(asked));
        Assert.Empty(resolved);

        // And the existing Resolve keeps its exact meaning.
        view.Find("[data-testid='markdown-comment-resolve-c1']").Click();

        Assert.Equal("c1", Assert.Single(resolved));
    }

    [Fact]
    public void A_resolution_proposal_renders_inside_the_comment_it_answers()
    {
        // It is about the remark rather than the block: the paragraph may be
        // fine and the comment the thing that needs settling.
        using var context = new BunitContext();

        var view = Render(context, p => p
            .Add(v => v.Blocks, Blocks)
            .Add(v => v.Comments, new MarkdownComment[] { new("c1", 1, "Too vague.") })
            .Add(v => v.Proposals, new AiProposal[]
            {
                new("p1", AiProposalKind.CommentResolution, "Name the quarter.", CommentId: "c1")
            }));

        Assert.Single(view.FindAll(
            "[data-testid='markdown-comment-body-c1'] [data-testid='markdown-proposal-p1']"));
    }

    [Fact]
    public void Accepting_a_resolution_does_not_also_resolve_the_comment()
    {
        // One act, one callback. The host calls resolve second if it wants to.
        using var context = new BunitContext();
        var resolved = new List<string>();
        AiProposal? accepted = null;

        var view = Render(context, p => p
            .Add(v => v.Blocks, Blocks)
            .Add(v => v.Comments, new MarkdownComment[] { new("c1", 1, "Too vague.") })
            .Add(v => v.OnResolveComment, resolved.Add)
            .Add(v => v.Proposals, new AiProposal[]
            {
                new("p1", AiProposalKind.CommentResolution, "Name the quarter.", CommentId: "c1")
            })
            .Add(v => v.OnAcceptProposal, proposal => accepted = proposal));

        view.Find("[data-testid='markdown-proposal-accept-p1']").Click();

        Assert.Equal("p1", accepted?.Id);
        Assert.Empty(resolved);
    }

    [Fact]
    public void A_proposal_whose_anchor_went_away_joins_the_orphan_region()
    {
        // Blocks are deleted while suggestions are open, and dropping one would
        // lose what was suggested. Same answer the comments already take, for the
        // reason already recorded there.
        using var context = new BunitContext();

        var view = Render(context, p => p
            .Add(v => v.Blocks, Blocks)
            .Add(v => v.Comments, new MarkdownComment[] { new("c1", 9, "A remark about a block that went") })
            .Add(v => v.Proposals, new AiProposal[]
            {
                new("p1", AiProposalKind.Rewrite, "A better paragraph.", BlockIndex: 9),
                new("p2", AiProposalKind.CommentResolution, "An answer to a comment that went", CommentId: "gone")
            }));

        var orphans = view.Find("[data-testid='markdown-orphaned-comments']");

        Assert.Contains("markdown-proposal-p1", orphans.InnerHtml, StringComparison.Ordinal);
        Assert.Contains("markdown-proposal-p2", orphans.InnerHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void A_proposal_alone_is_enough_to_annotate_a_view()
    {
        using var context = new BunitContext();

        var view = Render(context, p => p
            .Add(v => v.Blocks, Blocks)
            .Add(v => v.Proposals, new AiProposal[]
            {
                new("p1", AiProposalKind.Rewrite, "A better paragraph.", BlockIndex: 0)
            }));

        Assert.Equal(3, view.FindAll(".md-block-row").Count);
    }
}
