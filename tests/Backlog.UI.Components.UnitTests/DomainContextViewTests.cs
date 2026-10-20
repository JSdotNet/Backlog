namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The Domain view's context page as markup: an Overview | Neighbours bar that
/// opens on Overview, the Neighbours board's three groups with an external system
/// as a dashed card, a legend in plain words, and a following neighbour's name
/// handed back as the reference to its own context.
/// </summary>
public sealed class DomainContextViewTests
{
    [Fact]
    public void The_page_opens_on_Overview_with_Neighbours_beside_it()
    {
        using var context = Context();

        var view = context.Render<DomainContextView>(p => p
            .Add(v => v.ContextName, "Tasks")
            .Add(v => v.Overview, DomainContextOverview.Read(DomainContextOverviewTests.Context, [DomainContextOverviewTests.Requirements]))
            .Add(v => v.Neighbours, DomainNeighbours.Read(DomainNeighboursTests.Dependencies, DomainNeighboursTests.Path)));

        Assert.Equal(["Overview", "Neighbours"], view.FindAll("[role='tab']").Select(tab => tab.TextContent.Trim()));
        Assert.Equal("true", view.Find("[data-testid='domain-context-page-tab-overview']").GetAttribute("aria-selected"));
        Assert.Equal(2, view.FindAll("[data-testid='domain-context-page-switch']").Count);
        Assert.Contains("1 of 2 name a test", view.Find("[data-testid='domain-context-page-proof']").TextContent);

        view.Find("[data-testid='domain-context-page-tab-neighbours']").Click();
        Assert.Single(view.FindAll("[data-testid='domain-neighbours-legend']"));
    }

    [Fact]
    public void The_board_groups_the_neighbours_and_dashes_an_external_system()
    {
        using var context = Context();

        var view = context.Render<DomainNeighboursView>(p => p
            .Add(v => v.ContextName, "Tasks")
            .Add(v => v.Neighbours, DomainNeighbours.Read(DomainNeighboursTests.Dependencies, DomainNeighboursTests.Path)));

        Assert.Equal(["GitHub", "Sessions", "Inbox"], view.FindAll("[data-testid='domain-neighbours-listens-to'] [data-testid='domain-neighbour']").Select(card => card.GetAttribute("data-name")));
        Assert.Equal(["Productivity"], view.FindAll("[data-testid='domain-neighbours-listen-to'] [data-testid='domain-neighbour']").Select(card => card.GetAttribute("data-name")));
        Assert.Equal(["Devbook", "Roadmap Planning"], view.FindAll("[data-testid='domain-neighbours-hand-in-hand'] [data-testid='domain-neighbour']").Select(card => card.GetAttribute("data-name")));
        Assert.Contains("domain-neighbours__card--external", view.Find("[data-testid='domain-neighbour'][data-name='GitHub']").ClassName);
        Assert.DoesNotContain("domain-neighbours__card--external", view.Find("[data-testid='domain-neighbour'][data-name='Sessions']").ClassName);
        Assert.DoesNotContain("Partnership", view.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Customer/Supplier", view.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Following_a_neighbour_hands_back_its_context()
    {
        using var context = Context();
        DevbookReference? followed = null;

        var view = context.Render<DomainNeighboursView>(p => p
            .Add(v => v.Neighbours, DomainNeighbours.Read(DomainNeighboursTests.Dependencies, DomainNeighboursTests.Path))
            .Add(v => v.OnNavigate, reference => followed = reference));

        view.Find("[data-testid='domain-neighbour'][data-name='Inbox'] [data-testid='domain-neighbour-link']").Click();

        Assert.NotNull(followed);
        Assert.Equal(".devbook/domain/inbox/context.md", followed!.Path);
    }

    [Fact]
    public void The_middle_card_counts_each_side_and_every_side_with_neighbours_is_joined_to_it()
    {
        using var context = Context();

        var view = context.Render<DomainNeighboursView>(p => p
            .Add(v => v.ContextName, "Tasks")
            .Add(v => v.Neighbours, DomainNeighbours.Read(DomainNeighboursTests.Dependencies, DomainNeighboursTests.Path)));

        Assert.Equal("3 upstream · 1 downstream · 2 partners", view.Find("[data-testid='domain-neighbours-counts']").TextContent);
        Assert.Equal(["listens-to", "listen-to"], view.FindAll("[data-testid='domain-neighbours-join']").Select(join => join.GetAttribute("data-side")));
        Assert.Single(view.FindAll("[data-testid='domain-neighbours-drop']"));
        Assert.All(view.FindAll("[data-testid='domain-neighbours-join'], [data-testid='domain-neighbours-drop']"),
            line => Assert.Equal("true", line.GetAttribute("aria-hidden")));
        Assert.Empty(view.FindAll("[data-testid='domain-neighbours-overview']"));
    }

    [Fact]
    public void A_side_with_nobody_on_it_draws_no_line_and_no_count()
    {
        using var context = Context();

        var view = context.Render<DomainNeighboursView>(p => p
            .Add(v => v.ContextName, "Sync")
            .Add(v => v.Neighbours, new DomainNeighbourhood([], [new DomainNeighbour("Tasks", "A task is stored whole.", "tasks", false)], [])));

        Assert.Equal("1 downstream", view.Find("[data-testid='domain-neighbours-counts']").TextContent);
        Assert.Equal(["listen-to"], view.FindAll("[data-testid='domain-neighbours-join']").Select(join => join.GetAttribute("data-side")));
        Assert.Empty(view.FindAll("[data-testid='domain-neighbours-drop']"));
        Assert.Single(view.FindAll("[data-testid='domain-neighbours-listens-to'] [data-testid='domain-neighbours-none']"));
    }

    [Fact]
    public void The_middle_cards_Overview_link_opens_the_Overview_tab()
    {
        using var context = Context();

        var view = context.Render<DomainContextView>(p => p
            .Add(v => v.ContextName, "Tasks")
            .Add(v => v.InitialTab, "neighbours")
            .Add(v => v.Neighbours, DomainNeighbours.Read(DomainNeighboursTests.Dependencies, DomainNeighboursTests.Path)));

        Assert.Equal("true", view.Find("[data-testid='domain-context-page-tab-neighbours']").GetAttribute("aria-selected"));

        view.Find("[data-testid='domain-neighbours-overview']").Click();

        Assert.Equal("true", view.Find("[data-testid='domain-context-page-tab-overview']").GetAttribute("aria-selected"));
    }

    [Fact]
    public void The_page_opens_with_its_title_and_tags_each_card()
    {
        using var context = Context();
        var neighbours = DomainNeighbours.Read(DomainNeighboursTests.Dependencies, DomainNeighboursTests.Path).Describe(
            new Dictionary<string, DomainNeighbourContext>(StringComparer.OrdinalIgnoreCase) { ["inbox"] = new("Inbox", true) });

        var view = context.Render<DomainNeighboursView>(p => p
            .Add(v => v.ContextName, "Tasks")
            .Add(v => v.ContextSummary, "Core · draft")
            .Add(v => v.Neighbours, neighbours));

        Assert.Equal("Tasks and its neighbours", view.Find("[data-testid='domain-neighbours-title']").TextContent);
        Assert.Equal(
            "Who Tasks listens to on the left, who listens to Tasks on the right, and who it works hand in hand with below.",
            view.Find("[data-testid='domain-neighbours-lede']").TextContent);
        Assert.Equal("Core · draft", view.Find("[data-testid='domain-neighbours-summary']").TextContent);
        Assert.Single(view.FindAll("[data-testid='domain-neighbour'][data-name='Inbox'] [data-testid='domain-neighbour-core']"));
        Assert.Single(view.FindAll("[data-testid='domain-neighbour'][data-name='GitHub'] [data-testid='domain-neighbour-external']"));
        Assert.Empty(view.FindAll("[data-testid='domain-neighbour'][data-name='Sessions'] [data-testid='domain-neighbour-core']"));
        Assert.Equal("Hand in hand", view.Find("[data-testid='domain-neighbours-hand-in-hand'] h3").TextContent);

        // A phrase that lost only its full stop offers no tooltip.
        var inboxLine = view.Find("[data-testid='domain-neighbour'][data-name='Inbox'] [data-testid='domain-neighbour-line']");
        Assert.Null(inboxLine.GetAttribute("title"));
    }

    [Fact]
    public void A_cut_phrase_offers_its_whole_sentence_on_hover()
    {
        using var context = Context();
        const string sentence = "Work views can expose quick links to relevant environments without Tasks owning endpoint or launch semantics.";
        var environment = new DomainNeighbour("Environment", DomainNeighbours.Phrase(sentence), "environment", false) { Sentence = sentence };

        var view = context.Render<DomainNeighboursView>(p => p
            .Add(v => v.ContextName, "Tasks")
            .Add(v => v.Neighbours, new DomainNeighbourhood([environment], [], [])));

        var line = view.Find("[data-testid='domain-neighbour-line']");
        Assert.Equal("Work views can expose quick links to relevant environments…", line.TextContent);
        Assert.Equal(sentence, line.GetAttribute("title"));
    }

    [Fact]
    public void No_neighbours_says_so()
    {
        using var context = Context();

        var view = context.Render<DomainNeighboursView>();

        Assert.Single(view.FindAll("[data-testid='domain-neighbours-empty']"));
    }

    private static BunitContext Context()
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context;
    }
}
