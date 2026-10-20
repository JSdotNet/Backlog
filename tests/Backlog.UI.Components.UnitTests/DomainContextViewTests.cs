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

        Assert.Equal(["GitHub", "Sessions"], view.FindAll("[data-testid='domain-neighbours-listens-to'] [data-testid='domain-neighbour']").Select(card => card.GetAttribute("data-name")));
        Assert.Equal(["Inbox"], view.FindAll("[data-testid='domain-neighbours-listen-to'] [data-testid='domain-neighbour']").Select(card => card.GetAttribute("data-name")));
        Assert.Equal(["Devbook"], view.FindAll("[data-testid='domain-neighbours-hand-in-hand'] [data-testid='domain-neighbour']").Select(card => card.GetAttribute("data-name")));
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
