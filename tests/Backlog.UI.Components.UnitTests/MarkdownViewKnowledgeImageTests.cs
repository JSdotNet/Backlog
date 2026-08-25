namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// An image written in a knowledge chapter, which is the one inline whose target
/// the reader never chose to follow — the <c>src</c> fetches on its own.
///
/// <para>What these pin is that the chapter's images and its links reach the same
/// verdict about the same target. They did not: a chapter's <c>assets/shot.png</c>
/// written as a link was already inert, and written as an image went into a
/// <c>src</c> the host resolved against its own origin, which is a 404 and a
/// broken-image icon. The picture lives beside the chapter on disk, outside any
/// <c>wwwroot</c>, so there was never an origin for that path to be right
/// about.</para>
///
/// <para>What the reader gets instead is the alt text, which is what the author
/// said the picture was for, and which is the substitution
/// <c>.md-image--inert</c> has always been for. The file itself is still correct
/// where it is read as plain markdown — a checkout, or GitHub — and this changes
/// nothing about that.</para>
/// </summary>
public sealed class MarkdownViewKnowledgeImageTests
{
    [Theory]
    // The one image the knowledge folders actually hold, written the way
    // `.domain/backlog/features.md` writes it.
    [InlineData("assets/backlog-entry-inline-markdown-editing.png")]
    // A sibling, and a picture kept above the chapter's own folder.
    [InlineData("shot.png")]
    [InlineData("../assets/shot.png")]
    // A path that climbs out of the repository. Nothing up there is ours to show.
    [InlineData("../../../etc/passwd")]
    public void An_image_beside_a_chapter_shows_its_alt_text_rather_than_a_broken_picture(string target)
    {
        using var context = new BunitContext();

        var view = Render(
            context,
            $"![Desktop backlog entry with inline Markdown editing]({target})",
            parameters => parameters
                .Add(v => v.KnowledgeDocumentPath, ".domain/backlog/features.md"));

        Assert.Empty(view.FindAll("img"));
        Assert.Equal(
            "Desktop backlog entry with inline Markdown editing",
            view.Find("span.md-image--inert").TextContent);
    }

    [Fact]
    public void One_target_reads_the_same_whether_it_was_written_as_a_link_or_as_an_image()
    {
        // The defect itself. `assets/shot.png` is the same path in both spellings
        // and the reader is owed the same answer about it; the link half already
        // gave that answer and the image half did not.
        using var context = new BunitContext();

        var view = Render(
            context,
            "![the shot](assets/shot.png) and [the shot](assets/shot.png).",
            parameters => parameters
                .Add(v => v.KnowledgeDocumentPath, ".domain/backlog/features.md"));

        Assert.Equal("the shot", view.Find("span.md-image--inert").TextContent);
        Assert.Equal("the shot", view.Find("span.md-link--inert").TextContent);
        Assert.Empty(view.FindAll("img"));
        Assert.Empty(view.FindAll("a.md-link"));
    }

    [Fact]
    public void An_image_pointing_at_a_chapter_is_still_an_image_and_not_a_reference()
    {
        // A chapter is not a picture, so `![…](domain.md)` is the author writing
        // one syntax and meaning the other. Turning it into the control a link
        // would have become would be this view deciding what they meant; the alt
        // text is what they actually wrote.
        using var context = new BunitContext();

        var view = Render(
            context,
            "![the model](../backlog/domain.md#backlog-entry)",
            parameters => parameters
                .Add(v => v.KnowledgeDocumentPath, ".domain/roadmap/features.md")
                .Add(v => v.OnKnowledgeNavigate, EventCallback.Factory.Create<KnowledgeReference>(this, _ => { })));

        Assert.Equal("the model", view.Find("span.md-image--inert").TextContent);
        Assert.Empty(view.FindAll(".knowledge-ref"));
        Assert.Empty(view.FindAll("img"));
    }

    [Fact]
    public void A_remote_image_in_a_chapter_is_left_exactly_as_it_was()
    {
        // An `https` picture was never this rule's to place. It has an origin of
        // its own and it renders as the image it has always rendered as.
        using var context = new BunitContext();

        var view = Render(
            context,
            "![a diagram](https://example.com/d.png)",
            parameters => parameters
                .Add(v => v.KnowledgeDocumentPath, ".domain/backlog/features.md"));

        var img = view.Find("img.md-image");

        Assert.Equal("https://example.com/d.png", img.GetAttribute("src"));
        Assert.Equal("a diagram", img.GetAttribute("alt"));
        Assert.Empty(view.FindAll("span.md-image--inert"));
    }

    [Theory]
    [InlineData("javascript:doEvil")]
    [InlineData("data:text/html,hi")]
    public void An_image_a_chapter_should_not_fetch_is_refused_here_too(string target)
    {
        // The renderer's own allow-list already refused these, and a chapter must
        // not be the document that gets them past it.
        using var context = new BunitContext();

        var view = Render(
            context,
            $"![what it was]({target})",
            parameters => parameters
                .Add(v => v.KnowledgeDocumentPath, ".domain/backlog/features.md"));

        Assert.Empty(view.FindAll("img"));
        Assert.Equal("what it was", view.Find("span.md-image--inert").TextContent);
    }

    [Fact]
    public void An_image_in_an_entry_body_is_left_exactly_as_it_was()
    {
        // Most markdown in this product is not a knowledge document, and none of
        // this reaches it. An entry body says nothing about where it came from, so
        // a relative `src` stays the relative `src` the author typed.
        using var context = new BunitContext();

        var view = Render(context, "![a diagram](diagram.png)");

        var img = view.Find("img.md-image");

        Assert.Equal("diagram.png", img.GetAttribute("src"));
        Assert.Empty(view.FindAll("span.md-image--inert"));
    }

    private static IRenderedComponent<MarkdownView> Render(
        BunitContext context,
        string source,
        Action<ComponentParameterCollectionBuilder<MarkdownView>>? extra = null) =>
        context.Render<MarkdownView>(parameters =>
        {
            parameters.Add(v => v.Blocks, MarkdownPreview.Parse(source));
            extra?.Invoke(parameters);
        });
}
