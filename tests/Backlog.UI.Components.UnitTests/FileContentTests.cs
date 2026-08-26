namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The half of a file pane that moves, on its own.
///
/// <para>What it draws inside FileView is <c>FileViewBodyTests</c>' and
/// <c>FileViewFrontmatterTests</c>' — those go through the pane, which is where
/// the parse, the frontmatter reading and the mode arbitration live. What is here
/// is the part that is genuinely this component's: the scroll region, and taking
/// the pane's answers at face value.</para>
/// </summary>
public sealed class FileContentTests
{
    private const string Markdown = """
        # A title

        Some prose.
        """;

    private const string Code = "var x = 1;";

    private const string WithFrontmatter = """
        ---
        description: What this is for.
        applyTo: "**/*.cs"
        ---

        # A title
        """;

    private static IRenderedComponent<FileContent> Render(
        BunitContext context,
        Action<ComponentParameterCollectionBuilder<FileContent>> extra) =>
        context.Render<FileContent>(parameters =>
        {
            parameters.Add(content => content.Name, "shared.md").Add(content => content.TestId, "fc");
            extra(parameters);
        });

    [Fact]
    public void The_region_is_a_named_tab_stop_because_it_scrolls()
    {
        // A div that scrolls is an interactive element: without tabindex a keyboard
        // user cannot reach the overflow in Chromium at all.
        using var context = new BunitContext();
        var cut = Render(context, parameters => parameters
            .Add(content => content.RendersMarkdown, true)
            .Add(content => content.Blocks, MarkdownPreview.ParseDocument(Markdown))
            .Add(content => content.MarkdownSource, Markdown));

        var region = cut.Find(".file-view__body");
        Assert.Equal("region", region.GetAttribute("role"));
        Assert.Equal("0", region.GetAttribute("tabindex"));
        Assert.Equal("shared.md", region.GetAttribute("aria-label"));
        Assert.Equal("max-height: 24rem", region.GetAttribute("style"));
        Assert.Equal("fc-body", region.GetAttribute("data-testid"));
    }

    [Fact]
    public void A_supplied_body_gives_up_the_tab_stop_and_keeps_the_name()
    {
        // The rule is about elements that are the only way to reach something. A
        // host supplies a body because it has controls to put in it, and those are
        // tab stops already.
        using var context = new BunitContext();
        var cut = Render(context, parameters => parameters
            .Add(content => content.SuppliedBody, (RenderFragment)(builder => builder.AddMarkupContent(0, "<p>host</p>"))));

        var region = cut.Find(".file-view__body");
        Assert.False(region.HasAttribute("tabindex"));
        Assert.Equal("region", region.GetAttribute("role"));
        Assert.Equal("shared.md", region.GetAttribute("aria-label"));
        Assert.Contains("host", region.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_editing_body_asks_the_stylesheet_for_the_pane_s_height()
    {
        using var context = new BunitContext();
        var cut = Render(context, parameters => parameters
            .Add(content => content.ShowsEditBody, true)
            .Add(content => content.SuppliedBody, (RenderFragment)(builder => builder.AddMarkupContent(0, "<textarea></textarea>"))));

        Assert.Contains("file-view__body--edit", cut.Find(".file-view__body").ClassName);
        Assert.StartsWith("file-view__body", cut.Find(".file-view__body").ClassName, StringComparison.Ordinal);
    }

    [Fact]
    public void Fill_gives_the_height_to_the_layout()
    {
        using var context = new BunitContext();
        var cut = Render(context, parameters => parameters
            .Add(content => content.Fill, true)
            .Add(content => content.Body, Code)
            .Add(content => content.Language, "csharp"));

        Assert.False(cut.Find(".file-view__body").HasAttribute("style"));
    }

    [Fact]
    public void No_max_height_never_scrolls()
    {
        using var context = new BunitContext();
        var cut = Render(context, parameters => parameters
            .Add(content => content.MaxHeight, null)
            .Add(content => content.Body, Code)
            .Add(content => content.Language, "csharp"));

        Assert.False(cut.Find(".file-view__body").HasAttribute("style"));
    }

    [Fact]
    public void Prose_or_source_is_the_pane_s_answer_and_not_a_guess()
    {
        // The name-driven decision is FileView's; here it is a flag, and the flag
        // is what chooses. A body handed as code with RendersMarkdown off is drawn
        // as code even though the text would parse as markdown perfectly well.
        using var context = new BunitContext();

        var prose = Render(context, parameters => parameters
            .Add(content => content.RendersMarkdown, true)
            .Add(content => content.Blocks, MarkdownPreview.ParseDocument(Markdown))
            .Add(content => content.MarkdownSource, Markdown));
        Assert.NotNull(prose.Find(".md-view"));
        Assert.Empty(prose.FindAll(".file-view__code"));

        var source = Render(context, parameters => parameters
            .Add(content => content.Body, Markdown)
            .Add(content => content.Language, "csharp"));
        Assert.NotNull(source.Find(".file-view__code"));
        Assert.Empty(source.FindAll(".md-view"));
    }

    [Fact]
    public void Comparing_beats_a_supplied_body_and_renames_the_region()
    {
        // Two regions called "shared.md" in one app leave a screen-reader user no
        // way to tell the pane from the comparison.
        using var context = new BunitContext();
        var cut = Render(context, parameters => parameters
            .Add(content => content.ShowsComparison, true)
            .Add(content => content.SelectedBaseline, new FileCompareBaseline("opened", "As opened", "# A title"))
            .Add(content => content.Comparison, MarkdownCompare.Compare("# A title", Markdown))
            .Add(content => content.SuppliedBody, (RenderFragment)(builder => builder.AddMarkupContent(0, "<p>host</p>"))));

        Assert.Equal("shared.md, As opened to Now", cut.Find(".file-view__body").GetAttribute("aria-label"));
        Assert.DoesNotContain("host", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_baseline_with_a_reason_gets_the_sentence_instead_of_a_diff()
    {
        using var context = new BunitContext();
        var cut = Render(context, parameters => parameters
            .Add(content => content.ShowsComparison, true)
            .Add(content => content.SelectedBaseline,
                new FileCompareBaseline("commit", "Last commit", null, "Never committed.")));

        var empty = cut.Find("[data-testid='fc-compare-unavailable']");
        Assert.Contains("Never committed.", empty.TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='fc-compare-view']"));
    }

    [Fact]
    public void The_frontmatter_strip_is_one_value_and_no_flag()
    {
        // "Never asked for" and "asked for, and the block states nothing" are the
        // same answer, and the pane has already made them the same value.
        using var context = new BunitContext();

        var none = Render(context, parameters => parameters
            .Add(content => content.RendersMarkdown, true)
            .Add(content => content.Blocks, MarkdownPreview.ParseDocument(Markdown))
            .Add(content => content.MarkdownSource, Markdown));
        Assert.Empty(none.FindAll("[data-testid='fc-frontmatter']"));

        var read = MarkdownFrontmatter.Read(WithFrontmatter);
        var strip = Render(context, parameters => parameters
            .Add(content => content.Frontmatter, read)
            .Add(content => content.RendersMarkdown, true)
            .Add(content => content.Blocks, MarkdownPreview.ParseDocument(read.Body))
            .Add(content => content.MarkdownSource, read.Body));

        Assert.Equal("What this is for.", strip.Find("[data-testid='fc-description']").TextContent);
        Assert.Contains("**/*.cs", strip.Find("[data-testid='fc-applies-to']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_strip_sits_outside_the_region_so_it_does_not_scroll_away()
    {
        using var context = new BunitContext();
        var read = MarkdownFrontmatter.Read(WithFrontmatter);
        var cut = Render(context, parameters => parameters
            .Add(content => content.Frontmatter, read)
            .Add(content => content.RendersMarkdown, true)
            .Add(content => content.Blocks, MarkdownPreview.ParseDocument(read.Body))
            .Add(content => content.MarkdownSource, read.Body));

        Assert.Empty(cut.Find(".file-view__body").QuerySelectorAll(".file-view__frontmatter"));
        Assert.NotNull(cut.Find(".file-view__frontmatter"));
    }

    [Fact]
    public void A_margin_is_only_a_layout_when_there_is_something_in_it()
    {
        // A host with an add handler makes every block a row; in margin mode each
        // of those reserves the column, so a file nobody has remarked on would give
        // up its width to hold nothing.
        using var context = new BunitContext();

        var empty = Render(context, parameters => parameters
            .Add(content => content.RendersMarkdown, true)
            .Add(content => content.Blocks, MarkdownPreview.ParseDocument(Markdown))
            .Add(content => content.MarkdownSource, Markdown)
            .Add(content => content.OnAddComment, EventCallback.Factory.Create<int>(this, _ => { })));
        Assert.Empty(empty.FindAll(".md-view--margin"));

        var remarked = Render(context, parameters => parameters
            .Add(content => content.RendersMarkdown, true)
            .Add(content => content.Blocks, MarkdownPreview.ParseDocument(Markdown))
            .Add(content => content.MarkdownSource, Markdown)
            .Add(content => content.Comments, new List<MarkdownComment> { new("c1", 0, "Someone", "A remark.") }));
        Assert.NotNull(remarked.Find(".md-view--margin"));
    }

    [Fact]
    public void No_test_id_leaves_every_derived_one_off()
    {
        using var context = new BunitContext();
        var read = MarkdownFrontmatter.Read(WithFrontmatter);
        var cut = context.Render<FileContent>(parameters => parameters
            .Add(content => content.Name, "shared.md")
            .Add(content => content.Frontmatter, read)
            .Add(content => content.Body, Code)
            .Add(content => content.Language, "csharp"));

        Assert.Empty(cut.FindAll("[data-testid]"));
    }
}
