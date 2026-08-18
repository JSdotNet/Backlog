namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// A fenced <c>meta</c> block is a record in a knowledge document and an
/// ordinary code fence everywhere else. The read view only knows which when a
/// host tells it, so the default has to stay what it has always been.
/// </summary>
public sealed class MarkdownViewMetaFenceTests
{
    private const string Document = """
        # Shared Technologies

        ```meta
        status: adopted
        related: [".tech/technology-graph.md"]
        ```

        Prose after the block.
        """;

    [Fact]
    public void By_default_a_meta_fence_is_still_a_code_block()
    {
        using var context = new BunitContext();

        var view = Render(context, Document);

        Assert.NotNull(view.Find("pre.md-code"));
        Assert.Contains("status: adopted", view.Find("pre.md-code code").TextContent, StringComparison.Ordinal);
        Assert.Empty(view.FindAll("dl.knowledge-fields"));
    }

    [Fact]
    public void Asked_for_it_the_fence_becomes_the_metadata_it_describes()
    {
        using var context = new BunitContext();

        var view = Render(context, Document, parameters => parameters
            .Add(v => v.RenderKnowledgeMetadata, true));

        Assert.NotNull(view.Find("dl.knowledge-fields"));
        Assert.Equal("adopted", view.Find(".knowledge-status").TextContent);
        Assert.Equal(".tech/technology-graph.md", view.Find("code.knowledge-ref--inert").TextContent);
        Assert.Empty(view.FindAll("pre.md-code"));

        // The rest of the document is untouched.
        Assert.Contains("Prose after the block.", view.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void The_folder_reaches_the_status()
    {
        // Naming the folder is what turns the status from a word into a value out
        // of a list, so the record offers that list. The plumbing being checked
        // here is that MarkdownView passes the folder down at all.
        using var context = new BunitContext();

        var view = Render(context, Document, parameters => parameters
            .Add(v => v.RenderKnowledgeMetadata, true)
            .Add(v => v.KnowledgeFolder, KnowledgeFolder.Tech));

        var select = view.Find(".knowledge-record__headline .status-editor select");
        Assert.Equal("adopted", select.GetAttribute("value"));
        Assert.Equal(
            ["candidate", "trial", "adopted", "hold", "retired"],
            select.QuerySelectorAll("option").Select(option => option.GetAttribute("value")));
    }

    [Fact]
    public void Without_a_folder_the_fence_still_reads_back_as_a_plain_pill()
    {
        // The read view's default is folder-blind, and most markdown in this
        // product is an entry body rather than a knowledge chapter.
        using var context = new BunitContext();

        var view = Render(context, Document, parameters => parameters
            .Add(v => v.RenderKnowledgeMetadata, true));

        Assert.Empty(view.FindAll("select"));
        Assert.Equal("knowledge-status knowledge-status--adopted", view.Find(".knowledge-status").GetAttribute("class"));
    }

    [Fact]
    public void A_reference_reports_itself_to_the_host_that_asked_to_hear_about_it()
    {
        using var context = new BunitContext();
        var followed = new List<KnowledgeReference>();

        var view = Render(context, Document, parameters => parameters
            .Add(v => v.RenderKnowledgeMetadata, true)
            .Add(v => v.OnKnowledgeNavigate, EventCallback.Factory.Create<KnowledgeReference>(this, followed.Add)));

        view.Find("button.knowledge-ref--action").Click();

        Assert.Equal(".tech/technology-graph.md", Assert.Single(followed).Raw);
    }

    [Fact]
    public void A_href_resolver_reaches_the_reference_too()
    {
        using var context = new BunitContext();

        var view = Render(context, Document, parameters => parameters
            .Add(v => v.RenderKnowledgeMetadata, true)
            .Add(v => v.KnowledgeHrefFor, reference => $"/knowledge/{reference.Path}"));

        Assert.Equal("/knowledge/.tech/technology-graph.md", view.Find("a.knowledge-ref--link").GetAttribute("href"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_diagram_fence_is_a_diagram_either_way(bool renderKnowledgeMetadata)
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = Render(context, "```mermaid\ngraph TD;\n  A-->B;\n```\n", parameters => parameters
            .Add(v => v.RenderKnowledgeMetadata, renderKnowledgeMetadata));

        Assert.NotEmpty(view.FindAll("[data-testid='diagram-view']"));
        Assert.Empty(view.FindAll("dl.knowledge-fields"));
    }

    [Fact]
    public void A_code_fence_that_only_looks_like_metadata_stays_a_code_block()
    {
        using var context = new BunitContext();

        var view = Render(context, "```yaml\nstatus: adopted\n```\n", parameters => parameters
            .Add(v => v.RenderKnowledgeMetadata, true));

        Assert.NotNull(view.Find("pre.md-code"));
        Assert.Empty(view.FindAll("dl.knowledge-fields"));
    }

    [Fact]
    public void A_heading_and_the_fence_under_it_are_drawn_as_one_record()
    {
        // The convention puts the meta fence directly under the heading it
        // describes, so that is what the two are: one record, one line, the
        // status beside the heading rather than in a block below it.
        using var context = new BunitContext();

        var view = Render(context, Document, parameters => parameters
            .Add(v => v.RenderKnowledgeMetadata, true));

        var headline = view.Find(".knowledge-record__headline");
        Assert.Equal("Shared Technologies", headline.QuerySelector("p.md-heading")?.TextContent);
        Assert.Equal("adopted", headline.QuerySelector(".knowledge-status")?.TextContent);

        // Drawn once each: the heading is not also emitted as its own block, and
        // the fence is not also emitted as a second record.
        Assert.Single(view.FindAll("p.md-heading"));
        Assert.Single(view.FindAll(".knowledge-record"));
    }

    [Fact]
    public void The_heading_keeps_the_markup_navigation_matches_on()
    {
        using var context = new BunitContext();

        var view = Render(context, Document, parameters => parameters
            .Add(v => v.RenderKnowledgeMetadata, true));

        var heading = view.Find(".knowledge-record__headline p.md-heading");
        Assert.Equal("md-heading md-heading--1", heading.GetAttribute("class"));
        Assert.Equal("heading", heading.GetAttribute("role"));
        Assert.Equal("1", heading.GetAttribute("aria-level"));
    }

    [Fact]
    public void Annotated_the_heading_and_the_fence_stay_two_blocks()
    {
        // Comments are anchored to block indices and every block is wrapped in a
        // row carrying its own. Folding two blocks into one row would leave the
        // fence's comments pointing at a row that is no longer there, so the
        // annotated view keeps the stacked rendering.
        using var context = new BunitContext();

        var view = Render(context, Document, parameters => parameters
            .Add(v => v.RenderKnowledgeMetadata, true)
            .Add(v => v.OnAddComment, EventCallback.Factory.Create<int>(this, _ => { })));

        Assert.NotNull(view.Find("[data-block='0'] p.md-heading"));
        Assert.NotNull(view.Find("[data-block='1'] .knowledge-record"));
        Assert.Empty(view.FindAll(".knowledge-record__headline p.md-heading"));

        // Still exactly one of each — the heading is not dropped for want of a
        // record to sit in.
        Assert.Single(view.FindAll("p.md-heading"));
        Assert.Single(view.FindAll(".knowledge-record"));
    }

    [Fact]
    public void A_fence_with_no_heading_before_it_is_a_record_on_its_own()
    {
        using var context = new BunitContext();

        var view = Render(context, """
            ```meta
            status: adopted
            ```
            """, parameters => parameters
            .Add(v => v.RenderKnowledgeMetadata, true));

        var headline = view.Find(".knowledge-record__headline");
        Assert.Single(headline.Children);
        Assert.Equal("adopted", headline.Children[0].TextContent);
    }

    [Fact]
    public void A_fence_that_does_not_follow_its_heading_is_not_pulled_up_to_it()
    {
        // Only *immediately* under. A fence with prose between it and a heading
        // is describing something else, and hoisting it would put a status next
        // to a title it was never written against.
        using var context = new BunitContext();

        var view = Render(context, """
            # Shared Technologies

            Prose in between.

            ```meta
            status: adopted
            ```
            """, parameters => parameters
            .Add(v => v.RenderKnowledgeMetadata, true));

        Assert.Empty(view.FindAll(".knowledge-record__headline p.md-heading"));
        Assert.Single(view.FindAll("p.md-heading"));
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
