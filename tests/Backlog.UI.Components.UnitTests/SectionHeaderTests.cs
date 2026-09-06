namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The pane header shape the application hand-rolled at nine sites: an eyebrow
/// naming the category, a heading the landmark points at, a line of description,
/// and the controls that act on the section.
///
/// <para>What is pinned here is the shape and the wiring — that the eyebrow is a
/// real element or no element at all, that the id lands on the heading rather
/// than on the wrapper an <c>aria-labelledby</c> would then miss, and that the
/// element itself is the host's choice. The class names those hosts hand over
/// are pinned next door in <see cref="ClassHookTests"/>.</para>
/// </summary>
public sealed class SectionHeaderTests
{
    [Fact]
    public void An_eyebrow_renders_as_the_first_line_of_the_text_block()
    {
        using var context = new BunitContext();

        var header = context.Render<SectionHeader>(parameters => parameters
            .Add(h => h.Eyebrow, "Global pane")
            .Add(h => h.Title, "Inbox"));

        var text = header.Find(".section-header__text");

        Assert.Equal("P", text.Children[0].TagName);
        Assert.Equal("section-header__eyebrow", text.Children[0].GetAttribute("class"));
        Assert.Equal("Global pane", text.Children[0].TextContent);

        // Above the title, not beside it: the category reads first.
        Assert.Equal("H2", text.Children[1].TagName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void No_eyebrow_emits_no_element_at_all(string? eyebrow)
    {
        using var context = new BunitContext();

        var header = context.Render<SectionHeader>(parameters => parameters
            .Add(h => h.Eyebrow, eyebrow)
            .Add(h => h.Title, "Inbox"));

        // An empty paragraph still takes its margin, so absence has to mean the
        // element is gone rather than blank.
        Assert.Empty(header.FindAll(".section-header__eyebrow"));
        Assert.Single(header.Find(".section-header__text").Children);
    }

    [Theory]
    [InlineData(2, "H2")]
    [InlineData(3, "H3")]
    [InlineData(4, "H4")]
    [InlineData(5, "H5")]
    public void The_heading_carries_the_id_its_landmark_points_at(int level, string tag)
    {
        using var context = new BunitContext();

        var header = context.Render<SectionHeader>(parameters => parameters
            .Add(h => h.Level, level)
            .Add(h => h.TitleId, "dashboard-title")
            .Add(h => h.Title, "Productivity and cost"));

        var heading = header.Find(tag.ToLowerInvariant());

        Assert.Equal("dashboard-title", heading.GetAttribute("id"));

        // On the heading, not on the wrapper: an aria-labelledby pointing at the
        // wrapper would read the actions out with the name.
        Assert.Null(header.Find("header").GetAttribute("id"));
    }

    [Fact]
    public void The_wrapper_is_the_element_the_host_already_had()
    {
        using var context = new BunitContext();

        var header = context.Render<SectionHeader>(parameters => parameters
            .Add(h => h.Element, "div")
            .Add(h => h.CssClass, "ai-panel__header")
            .Add(h => h.Title, "Ask about this content"));

        Assert.Empty(header.FindAll("header"));
        Assert.NotNull(header.Find("div.ai-panel__header"));
    }

    [Fact]
    public void Every_part_defaults_to_the_librarys_own_name()
    {
        using var context = new BunitContext();

        var header = context.Render<SectionHeader>(parameters => parameters
            .Add(h => h.Eyebrow, "Global pane")
            .Add(h => h.Title, "Backlog")
            .Add(h => h.Description, "Everything captured, newest first.")
            .Add(h => h.Actions, "<button type=\"button\">New</button>"));

        // A host that hands over nothing gets the library's own block, which is
        // what components.css styles and what the storybook shows.
        Assert.NotNull(header.Find("header.section-header"));
        Assert.NotNull(header.Find(".section-header__text"));
        Assert.NotNull(header.Find(".section-header__eyebrow"));
        Assert.NotNull(header.Find("h2.section-header__title"));
        Assert.NotNull(header.Find(".section-header__description"));
        Assert.NotNull(header.Find(".section-header__actions button"));
    }

    [Fact]
    public void A_description_can_carry_the_test_id_the_pane_is_found_by()
    {
        using var context = new BunitContext();

        var header = context.Render<SectionHeader>(parameters => parameters
            .Add(h => h.Title, "Claude and Copilot")
            .Add(h => h.Description, "3 of 842 sessions")
            .Add(h => h.DescriptionTestId, "sessions-subtitle"));

        Assert.Equal("3 of 842 sessions", header.Find("[data-testid='sessions-subtitle']").TextContent);
    }
}
