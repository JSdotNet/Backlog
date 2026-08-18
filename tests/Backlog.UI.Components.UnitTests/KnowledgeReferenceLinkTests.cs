namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// A reference renders as what the host can do with it, and it renders as a
/// link rather than as a badge.
///
/// <para>All three shapes used to carry <c>knowledge-related</c>, which shares
/// its rule with the status pill: border, full radius, filled surface, bold.
/// That made every reference look like a chip you could not click, and put a row
/// of them next to the one pill on the headline that genuinely is a state. The
/// assertions below are what stops that coming back.</para>
/// </summary>
public sealed class KnowledgeReferenceLinkTests
{
    private static readonly KnowledgeReference Chapter =
        KnowledgeReference.Parse(".tech/shared.md#markdown")!;

    [Fact]
    public void A_followable_reference_is_an_anchor_and_carries_nothing_of_the_pill()
    {
        using var context = new BunitContext();

        var link = context.Render<KnowledgeReferenceLink>(parameters => parameters
            .Add(l => l.Reference, Chapter)
            .Add(l => l.Href, "/knowledge/.tech/shared.md"));

        var anchor = link.Find("a");
        Assert.Equal("knowledge-ref knowledge-ref--link", anchor.GetAttribute("class"));
        Assert.Equal("/knowledge/.tech/shared.md", anchor.GetAttribute("href"));
        Assert.Equal(".tech/shared.md#markdown", anchor.GetAttribute("title"));

        // The badge look, gone and staying gone.
        Assert.DoesNotContain("knowledge-related", link.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("knowledge-status", link.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void The_button_form_is_the_anchor_form_bar_the_modifier()
    {
        // Whether the host routes a reference in-process or lets the browser
        // navigate is not something a reader should be able to see.
        using var context = new BunitContext();

        var link = context.Render<KnowledgeReferenceLink>(parameters => parameters
            .Add(l => l.Reference, Chapter)
            .Add(l => l.Href, "/knowledge/.tech/shared.md"));

        var action = context.Render<KnowledgeReferenceLink>(parameters => parameters
            .Add(l => l.Reference, Chapter)
            .Add(l => l.OnNavigate, EventCallback.Factory.Create<KnowledgeReference>(this, _ => { })));

        var button = action.Find("button");
        Assert.Equal("button", button.GetAttribute("type"));
        Assert.Equal("knowledge-ref knowledge-ref--action", button.GetAttribute("class"));
        Assert.Equal(
            link.Find("a").ClassList.Where(name => name != "knowledge-ref--link"),
            button.ClassList.Where(name => name != "knowledge-ref--action"));
        Assert.Equal(link.Find("a").TextContent, button.TextContent);
        Assert.Equal(link.Find("a").GetAttribute("title"), button.GetAttribute("title"));
    }

    [Fact]
    public void A_reference_nobody_can_follow_is_inert_text_and_is_not_link_coloured()
    {
        using var context = new BunitContext();

        var inert = context.Render<KnowledgeReferenceLink>(parameters => parameters
            .Add(l => l.Reference, Chapter));

        var code = inert.Find("code");
        Assert.Equal("knowledge-ref knowledge-ref--inert", code.GetAttribute("class"));

        // Not painted as followable, and not reachable by tab: the modifiers the
        // stylesheet colours as links are the two it does not have.
        Assert.DoesNotContain("knowledge-ref--link", code.ClassList);
        Assert.DoesNotContain("knowledge-ref--action", code.ClassList);
        Assert.Empty(inert.FindAll("a"));
        Assert.Empty(inert.FindAll("button"));
    }

    [Fact]
    public void A_link_wins_over_a_handler_so_one_reference_is_never_two_ways_to_go()
    {
        using var context = new BunitContext();

        var link = context.Render<KnowledgeReferenceLink>(parameters => parameters
            .Add(l => l.Reference, Chapter)
            .Add(l => l.Href, "/knowledge/tech")
            .Add(l => l.OnNavigate, EventCallback.Factory.Create<KnowledgeReference>(this, _ => { })));

        Assert.Single(link.FindAll("a"));
        Assert.Empty(link.FindAll("button"));
    }

    [Fact]
    public void Activating_a_button_reports_the_whole_reference()
    {
        using var context = new BunitContext();
        var followed = new List<KnowledgeReference>();

        var link = context.Render<KnowledgeReferenceLink>(parameters => parameters
            .Add(l => l.Reference, Chapter)
            .Add(l => l.OnNavigate, EventCallback.Factory.Create<KnowledgeReference>(this, followed.Add)));

        link.Find("button").Click();

        Assert.Same(Chapter, Assert.Single(followed));
    }

    [Theory]
    [InlineData(true, ".tech/shared.md#markdown")]
    [InlineData(false, "markdown")]
    public void The_short_label_shortens_the_text_and_never_the_title(bool showFullPath, string expected)
    {
        using var context = new BunitContext();

        var link = context.Render<KnowledgeReferenceLink>(parameters => parameters
            .Add(l => l.Reference, Chapter)
            .Add(l => l.ShowFullPath, showFullPath)
            .Add(l => l.Href, "/knowledge/tech")
            .Add(l => l.TestId, "reference"));

        var anchor = link.Find("a");
        Assert.Equal(expected, anchor.TextContent);
        Assert.Equal(".tech/shared.md#markdown", anchor.GetAttribute("title"));
        Assert.Equal("reference", anchor.GetAttribute("data-testid"));
    }
}
