namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// A knowledge chapter writes its references in prose the same way it writes them
/// in a <c>meta</c> fence — a code span holding a repository path — so the two go
/// through the same plumbing and a reader can follow either.
///
/// <para>What is worth pinning is where that stops. A code span is a path far
/// less often than it is a command, a type name or a single word, and the read
/// view has no business turning any of those into a destination. So the rule is
/// deliberately narrow, and most of these tests are about what stays
/// <c>&lt;code&gt;</c>.</para>
/// </summary>
public sealed class MarkdownViewKnowledgeLinkTests
{
    [Fact]
    public void A_chapter_named_in_prose_is_a_reference_a_reader_can_follow()
    {
        using var context = new BunitContext();
        var followed = new List<KnowledgeReference>();

        var view = Render(
            context,
            "See `.domain/sessions/domain.md#invariants` for the rest.",
            parameters => parameters
                .Add(v => v.RenderKnowledgeMetadata, true)
                .Add(v => v.OnKnowledgeNavigate, EventCallback.Factory.Create<KnowledgeReference>(this, followed.Add)));

        view.Find("button.knowledge-ref--action").Click();

        var reference = Assert.Single(followed);
        Assert.Equal(".domain/sessions/domain.md", reference.Path);
        Assert.Equal("invariants", reference.Slug);
    }

    [Fact]
    public void The_hosts_own_route_reaches_a_reference_in_prose_too()
    {
        using var context = new BunitContext();

        var view = Render(
            context,
            "See `.arc42/03-context-and-scope.md` for the system around this.",
            parameters => parameters
                .Add(v => v.RenderKnowledgeMetadata, true)
                .Add(v => v.KnowledgeHrefFor, reference => $"/knowledge/{reference.Path}"));

        var link = view.Find("a.knowledge-ref--link");

        Assert.Equal("/knowledge/.arc42/03-context-and-scope.md", link.GetAttribute("href"));
        Assert.Equal(".arc42/03-context-and-scope.md", link.TextContent);
    }

    [Theory]
    [InlineData("order")]
    [InlineData("dotnet build Backlog.sln")]
    [InlineData("MarkdownRender.Inlines")]
    [InlineData("src/Core/Backlog.UI.Components/wwwroot/components.css")]
    [InlineData(".domain/sessions")]
    [InlineData(".domain/naming.md and .domain/model.md")]
    public void Everything_that_is_not_plainly_a_chapter_stays_code(string span)
    {
        // A destination on a word, a command or a folder is a promise nothing can
        // keep. The rule asks for a known knowledge folder at the front, `.md` at
        // the end, and nothing but a path in between.
        using var context = new BunitContext();

        var view = Render(context, $"Written as `{span}` in the prose.", parameters => parameters
            .Add(v => v.RenderKnowledgeMetadata, true));

        Assert.Equal(span, view.Find("code.md-inline-code").TextContent);
        Assert.Empty(view.FindAll(".knowledge-ref"));
    }

    [Fact]
    public void Outside_a_knowledge_document_a_path_is_a_path_being_quoted()
    {
        // Most markdown in this product is an entry's body, where `.domain/x.md`
        // is a string somebody typed and there is nowhere to navigate to.
        using var context = new BunitContext();

        var view = Render(context, "See `.domain/sessions/domain.md` for the rest.");

        Assert.Equal(".domain/sessions/domain.md", view.Find("code.md-inline-code").TextContent);
        Assert.Empty(view.FindAll(".knowledge-ref"));
    }

    [Fact]
    public void A_markdown_link_into_the_knowledge_folders_navigates_inside_the_app()
    {
        // It used to be an anchor with `target="_blank"` on a repository-relative
        // href, which resolves against the app's own origin and lands nowhere.
        using var context = new BunitContext();
        var followed = new List<KnowledgeReference>();

        var view = Render(
            context,
            "See [the sessions model](.domain/sessions/domain.md).",
            parameters => parameters
                .Add(v => v.RenderKnowledgeMetadata, true)
                .Add(v => v.OnKnowledgeNavigate, EventCallback.Factory.Create<KnowledgeReference>(this, followed.Add)));

        var button = view.Find("button.knowledge-ref--action");

        // The author's words, not the path they point at — and the path on the
        // title, where it was already going for a reference.
        Assert.Equal("the sessions model", button.TextContent);
        Assert.Equal(".domain/sessions/domain.md", button.GetAttribute("title"));
        Assert.Empty(view.FindAll("a.md-link"));

        button.Click();

        Assert.Equal(".domain/sessions/domain.md", Assert.Single(followed).Path);
    }

    [Theory]
    [InlineData("https://learn.microsoft.com")]
    [InlineData("http://example.test/page")]
    [InlineData("mailto:someone@example.test")]
    public void A_link_that_leaves_the_app_is_left_exactly_as_it_was(string url)
    {
        using var context = new BunitContext();

        var view = Render(context, $"See [the docs]({url}).", parameters => parameters
            .Add(v => v.RenderKnowledgeMetadata, true));

        var link = view.Find("a.md-link");

        Assert.Equal(url, link.GetAttribute("href"));
        Assert.Equal("_blank", link.GetAttribute("target"));
        Assert.Empty(view.FindAll(".knowledge-ref"));
    }

    [Fact]
    public void A_reference_nobody_can_follow_is_still_not_dressed_as_a_link()
    {
        // No href and no handler: the same bargain KnowledgeReferenceLink already
        // strikes for a metadata field, which is a code span again — so the prose
        // reads exactly as it did.
        using var context = new BunitContext();

        var view = Render(context, "See `.tech/shared.md` for the rest.", parameters => parameters
            .Add(v => v.RenderKnowledgeMetadata, true));

        Assert.Equal(".tech/shared.md", view.Find("code.knowledge-ref--inert").TextContent);
        Assert.Empty(view.FindAll("button.knowledge-ref--action"));
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
