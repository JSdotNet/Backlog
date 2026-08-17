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
}
