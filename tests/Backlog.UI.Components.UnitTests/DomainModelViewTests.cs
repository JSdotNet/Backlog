using Backlog.UI.Components.Diagrams;
using Bunit;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The Domain view's Model page as markup: it opens on Diagram with a
/// Diagram | Fields | Events bar, draws an arranged fence as bands, and hands a
/// fence it cannot arrange to the mermaid renderer it has always had.
/// </summary>
public sealed class DomainModelViewTests
{
    private const string Order = """
        classDiagram
            class Order {
                <<aggregate root>>
                +Id customer_id
            }
            class OrderLine {
                <<value object>>
            }
            class Pricing {
                <<service>>
            }
            Order "1" *-- "1..*" OrderLine : lines
        """;

    [Fact]
    public void The_page_opens_on_Diagram_with_Fields_and_Events_beside_it()
    {
        using var context = Context();

        var view = context.Render<DomainModelView>(p => p.Add(v => v.Fences, [new DomainModelFence("Model diagram", Order)]));

        Assert.Equal(["Diagram", "Fields", "Events"], view.FindAll("[role='tab']").Select(tab => tab.TextContent.Trim()));
        Assert.Equal("true", view.Find("[data-testid='domain-model-tab-diagram']").GetAttribute("aria-selected"));
        Assert.Single(view.FindAll("[data-testid='domain-model-band']"));

        view.Find("[data-testid='domain-model-tab-fields']").Click();
        Assert.Single(view.FindAll("[data-testid='domain-model-fields-placeholder']"));

        view.Find("[data-testid='domain-model-tab-events']").Click();
        Assert.Single(view.FindAll("[data-testid='domain-model-events-placeholder']"));
    }

    [Fact]
    public void An_arranged_fence_draws_the_band_its_parts_references_and_services()
    {
        using var context = Context();

        var diagram = context.Render<DomainModelDiagram>(p => p.Add(d => d.Source, Order));

        Assert.Equal("domain-model", diagram.Find("[data-testid='domain-model-diagram']").GetAttribute("data-rendered"));
        var band = diagram.Find("[data-testid='domain-model-band']");
        Assert.Equal("Order", band.QuerySelector(".domain-model__root [data-testid='domain-model-element']")!.GetAttribute("data-name"));

        var part = Assert.Single(band.QuerySelectorAll("[data-testid='domain-model-part']"));
        Assert.Equal("OrderLine", part.GetAttribute("data-part"));
        Assert.Equal("1..*", part.QuerySelector("[data-testid='domain-model-multiplicity']")!.TextContent);
        Assert.Contains("value object", part.TextContent);

        // Outside the band, not inside it.
        Assert.Empty(band.QuerySelectorAll("[data-testid='domain-model-reference']"));
        Assert.Equal("customer_id", diagram.Find("[data-testid='domain-model-reference']").GetAttribute("data-target"));

        Assert.Contains("Pricing", diagram.Find("[data-testid='domain-model-services']").TextContent);
        Assert.Empty(context.JSInterop.Invocations["backlogDiagrams.render"]);
    }

    [Fact]
    public void A_fence_that_does_not_parse_is_drawn_with_mermaid()
    {
        using var context = Context();

        var diagram = context.Render<DomainModelDiagram>(p => p.Add(d => d.Source, "classDiagram\n    Task <--> Other"));

        Assert.Equal("mermaid", diagram.Find("[data-testid='domain-model-diagram']").GetAttribute("data-rendered"));
        Assert.Single(diagram.FindAll("[data-testid='diagram-view']"));
        Assert.Empty(diagram.FindAll("[data-testid='domain-model-band']"));
    }

    private static BunitContext Context()
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context;
    }
}
