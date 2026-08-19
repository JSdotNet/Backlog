namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The comparison without its furniture: a host that has already drawn the frame,
/// the header and the scroll region asks for the comparison itself.
///
/// <para>What survives Bare is the strip — the two version names, the count and
/// the fold toggle — because those describe this view and there is nowhere else
/// for them to live. What goes is everything a host would otherwise have twice:
/// the frame, the name, and above all the scroll region, which as a second
/// overflow container inside the first would be a second tab stop the reader has
/// to walk through to reach the file.</para>
/// </summary>
public sealed class MarkdownCompareBareTests
{
    private const string Before = """
        # Guide

        One.

        Two.

        ## Notes

        Read this first.
        """;

    private const string After = """
        # Guide

        One, edited.

        Two.

        ## Notes

        Read this first.
        """;

    private static IRenderedComponent<MarkdownCompareView> Render(BunitContext context, bool bare) =>
        context.Render<MarkdownCompareView>(parameters => parameters
            .Add(v => v.Section, MarkdownCompare.Compare(Before, After))
            .Add(v => v.FileName, "README.md")
            .Add(v => v.Path, "docs/README.md")
            .Add(v => v.BeforeLabel, "Last commit")
            .Add(v => v.AfterLabel, "Working tree")
            .Add(v => v.Bare, bare)
            .Add(v => v.TestId, "compare"));

    [Fact]
    public void Bare_gives_up_the_frame_the_header_and_the_scroll_region()
    {
        using var context = new BunitContext();

        var view = Render(context, bare: true);

        Assert.Empty(view.FindAll("article.md-compare"));
        Assert.Empty(view.FindAll(".md-compare__header"));
        Assert.Empty(view.FindAll(".md-compare__name"));
        Assert.Empty(view.FindAll(".md-compare__path"));
        Assert.Empty(view.FindAll(".md-compare__body"));

        // No second overflow container, and so no second tab stop: the host's own
        // body is the one the reader can see. The folds keep their own regions —
        // those are named by their triggers and were never tab stops.
        Assert.Empty(view.FindAll("[tabindex]"));
        Assert.Empty(view.FindAll("[role='region'][tabindex]"));
    }

    [Fact]
    public void The_bare_view_is_one_element_the_host_can_address()
    {
        using var context = new BunitContext();

        var view = Render(context, bare: true);

        var root = view.Find("[data-testid='compare']");

        Assert.Equal("DIV", root.TagName);
        Assert.Equal("md-compare__bare", root.GetAttribute("class"));
    }

    [Fact]
    public void The_strip_keeps_the_two_version_names_the_count_and_the_fold_toggle()
    {
        // Duplicating these in every host is exactly the drift the shared rule
        // exists to avoid, and they describe this view rather than the file.
        using var context = new BunitContext();

        var view = Render(context, bare: true);

        var bar = view.Find(".md-compare__bare > .md-compare__bar");

        Assert.Equal("Last commit → Working tree · 1 changed", bar.QuerySelector(".md-compare__meta")!.TextContent);
        Assert.Equal("Unchanged sections", bar.QuerySelector("[data-testid='compare-unchanged']")!.TextContent);
    }

    [Fact]
    public void The_fold_toggle_still_opens_every_fold_at_once()
    {
        using var context = new BunitContext();

        var view = Render(context, bare: true);

        Assert.All(
            view.FindAll(".md-compare-fold .fold__trigger"),
            trigger => Assert.Equal("false", trigger.GetAttribute("aria-expanded")));

        view.Find("[data-testid='compare-unchanged']").Click();

        Assert.NotEmpty(view.FindAll(".md-compare-fold .fold__trigger"));
        Assert.All(
            view.FindAll(".md-compare-fold .fold__trigger"),
            trigger => Assert.Equal("true", trigger.GetAttribute("aria-expanded")));
    }

    [Fact]
    public void The_comparison_itself_is_the_same_comparison_either_way()
    {
        // Bare is about what is drawn around it, so the tree inside has to be
        // untouched — otherwise a host would be reading a different diff
        // depending on who framed it.
        using var context = new BunitContext();

        var framed = Render(context, bare: false);
        var bare = Render(context, bare: true);

        // What the blocks are and what they say, rather than their markup byte for
        // byte: the fold ids are per-instance, so two renders of the same
        // comparison never produce the same string.
        Assert.Equal(Blocks(framed), Blocks(bare));
        Assert.Equal(FoldLabels(framed), FoldLabels(bare));

        static IReadOnlyList<string> Blocks(IRenderedComponent<MarkdownCompareView> view) =>
        [
            .. view.FindAll(".md-compare-block").Select(block => $"{block.ClassName}|{block.TextContent}")
        ];

        static IReadOnlyList<string> FoldLabels(IRenderedComponent<MarkdownCompareView> view) =>
        [
            .. view.FindAll(".md-compare-fold .fold__label").Select(label => label.TextContent)
        ];
    }

    [Fact]
    public void A_framed_view_is_exactly_what_it_was_before_Bare_existed()
    {
        // The default has to stay the default: every host already rendering this
        // asked for the frame by not mentioning it.
        using var context = new BunitContext();

        var view = Render(context, bare: false);

        Assert.Empty(view.FindAll(".md-compare__bare"));
        Assert.Empty(view.FindAll(".md-compare__bar"));

        Assert.Contains("md-compare", view.Find("article.md-compare").ClassList);
        Assert.Equal("README.md", view.Find(".md-compare__name").TextContent);
        Assert.Equal("docs/README.md", view.Find(".md-compare__path").TextContent);

        var body = view.Find(".md-compare__body");

        Assert.Equal("region", body.GetAttribute("role"));
        Assert.Equal("0", body.GetAttribute("tabindex"));
        Assert.Equal("README.md, Last commit to Working tree", body.GetAttribute("aria-label"));
    }

    [Fact]
    public void With_nothing_to_compare_the_bare_view_still_says_so()
    {
        using var context = new BunitContext();

        var view = context.Render<MarkdownCompareView>(parameters => parameters
            .Add(v => v.Section, null)
            .Add(v => v.FileName, "README.md")
            .Add(v => v.Bare, true)
            .Add(v => v.TestId, "compare"));

        Assert.Equal("No file selected", view.Find(".empty-state__title").TextContent);
        Assert.Empty(view.FindAll(".md-compare-block"));

        // Still inside the strip's frame rather than instead of it: the two
        // version names are what an empty comparison is empty *of*.
        Assert.NotNull(view.Find(".md-compare__bare .md-compare__bar"));
    }
}
