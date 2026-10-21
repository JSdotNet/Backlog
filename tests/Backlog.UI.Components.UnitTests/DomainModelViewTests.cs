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

    private const string Tasks = """
        classDiagram
            class Task {
                <<aggregate root>>
                +Id id
                +String title
                +Date due_on
                +TaskStatus status
                +List~String~ repo_ids
                +List~Id~ depends_on
            }
            class SubItem {
                <<entity>>
                +Id id
                +String title
                +String status
            }
            class UsageEvent {
                <<value object>>
                +String action
            }
            class ProjectionRef {
                <<value object>>
                +String repo_id
                +String external_id
            }
            class Recurrence {
                <<value object>>
                +Integer interval
            }
            class TaskStatus {
                <<enumeration>>
                draft
            }
            class Readiness {
                <<enumeration>>
                ready
            }
            Task "1" *-- "0..*" SubItem : contains
            Task "1" *-- "0..*" UsageEvent : records
            Task "1" *-- "0..*" ProjectionRef : projects to
            Task "1" *-- "0..1" Recurrence : repeats by
            Task --> TaskStatus : has status
            Task "0..*" ..> "0..*" Task : depends on
            Task ..> Readiness : derived as
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
        Assert.Equal("Order", view.Find("[data-testid='domain-fields-class']").GetAttribute("data-name"));

        view.Find("[data-testid='domain-model-tab-events']").Click();
        Assert.Single(view.FindAll("[data-testid='domain-events-empty']"));
    }

    [Fact]
    public void Fields_groups_each_class_and_carries_the_notes_and_how_the_services_run()
    {
        using var context = Context();
        var chapters = new DomainChapterReading(
            [new DomainServiceInvocation("Pricing", "#pricing", "Prices an order.", "Command-invoked, once per checkout.")],
            []);

        var view = context.Render<DomainModelView>(p => p
            .Add(v => v.Fences, [new DomainModelFence("Model diagram", Order)])
            .Add(v => v.RelationshipNotes, "- `Order` is the only consistency boundary.")
            .Add(v => v.Chapters, chapters));
        view.Find("[data-testid='domain-model-tab-fields']").Click();

        var order = view.Find("[data-testid='domain-fields-class'][data-name='Order']");
        Assert.Equal(["reference", "owned-part"], order.QuerySelectorAll("[data-testid='domain-fields-group']").Select(g => g.GetAttribute("data-kind")));
        Assert.Contains("Ids pointing elsewhere", order.TextContent);
        Assert.Contains("customer_id", order.TextContent);

        Assert.Contains("only consistency boundary", view.Find("[data-testid='domain-model-relationship-notes']").TextContent);

        var service = view.Find("[data-testid='domain-service-invocation']");
        Assert.Contains("Domain services · how they run", view.Find("[data-testid='domain-services']").TextContent);
        Assert.Equal("Pricing", service.GetAttribute("data-name"));
        Assert.Contains("once per checkout", service.QuerySelector("[data-testid='domain-service-runs']")!.TextContent);
    }

    [Fact]
    public void Events_reads_each_event_as_a_contract()
    {
        using var context = Context();
        var chapters = new DomainChapterReading(
            [],
            [
                new DomainEventContract("OrderPlaced", "#orderplaced", "draft", "Published when an order is placed.", ["Order"], "- `order_id` - the order.", "- Billing.", null, DomainEventDelivery.Documented),
                new DomainEventContract("OrderShipped", "#ordershipped", "draft", "Published by Delivery.", [], null, null, null, DomainEventDelivery.Emitted)
            ]);

        var view = context.Render<DomainModelView>(p => p
            .Add(v => v.Fences, [new DomainModelFence("Model diagram", Order)])
            .Add(v => v.Chapters, chapters));
        view.Find("[data-testid='domain-model-tab-events']").Click();

        var events = view.FindAll("[data-testid='domain-event']");
        Assert.Equal(["documented", "emitted"], events.Select(e => e.GetAttribute("data-delivery")));

        var placed = events[0];
        Assert.Equal("Documented", placed.QuerySelector("[data-testid='domain-event-delivery']")!.TextContent.Trim());
        Assert.Contains("Order", placed.QuerySelector("[data-testid='domain-event-raised-by']")!.TextContent);
        Assert.Contains("order_id", placed.QuerySelector("[data-testid='domain-event-payload']")!.TextContent);
        Assert.Contains("Billing", placed.QuerySelector("[data-testid='domain-event-consumers']")!.TextContent);
        Assert.Contains("does not say", placed.QuerySelector("[data-testid='domain-event-rules']")!.TextContent);

        Assert.Contains("Not named", events[1].QuerySelector("[data-testid='domain-event-raised-by']")!.TextContent);
    }

    [Fact]
    public void A_fence_the_reader_cannot_read_says_so_on_Fields()
    {
        using var context = Context();

        var view = context.Render<DomainModelView>(p => p.Add(v => v.Fences, [new DomainModelFence("Broken", "classDiagram\n    Task <--> Other")]));
        view.Find("[data-testid='domain-model-tab-fields']").Click();

        Assert.Contains("could not be read into fields", view.Find("[data-testid='domain-fields-unread']").TextContent);
        Assert.Empty(view.FindAll("[data-testid='domain-services']"));
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
        var reference = diagram.Find("[data-testid='domain-model-reference']");
        Assert.Equal("Customer", reference.GetAttribute("data-target"));
        Assert.Equal("customer_id", reference.QuerySelector("[data-testid='domain-model-reference-via'] code")!.TextContent);

        Assert.Contains("Pricing", diagram.Find("[data-testid='domain-model-services']").TextContent);
        Assert.Empty(context.JSInterop.Invocations["backlogDiagrams.render"]);
    }

    [Fact]
    public void Diagram_is_headed_by_the_root_and_draws_its_parts_as_a_tree_around_its_card()
    {
        using var context = Context();
        var chapters = new DomainChapterReading(
            [new DomainServiceInvocation("Projection", "#projection", "Creates one GitHub issue per target repository.", null)],
            []);

        var view = context.Render<DomainModelView>(p => p
            .Add(v => v.Fences, [new DomainModelFence("Model diagram", Tasks)])
            .Add(v => v.Chapters, chapters));

        Assert.Equal("Task model", view.Find("[data-testid='domain-model-title']").TextContent);
        Assert.StartsWith("What the Task owns sits inside the boundary", view.Find("[data-testid='domain-model-intro']").TextContent);
        Assert.Contains("names by id", view.Find("[data-testid='domain-model-legend']").TextContent);

        // Four parts: two in the row above the root, two below, each saying its kind
        // and multiplicity and what it is made of.
        var tiers = view.FindAll("[data-testid='domain-model-tier']");
        Assert.Equal(["above", "below"], tiers.Select(t => t.GetAttribute("data-side")));
        Assert.Equal(
            [["SubItem", "UsageEvent"], ["ProjectionRef", "Recurrence"]],
            tiers.Select(t => t.QuerySelectorAll("[data-testid='domain-model-part']").Select(p => p.GetAttribute("data-part")).ToArray()));
        var subItem = view.Find("[data-part='SubItem']");
        Assert.Contains("entity", subItem.TextContent);
        Assert.Equal("title · status", subItem.QuerySelector(".domain-model__element-fields")!.TextContent);

        // The root card names its key fields; ids and parts are drawn elsewhere.
        Assert.Equal(["status", "due_on", "title"], view.Find("[data-testid='domain-model-key-fields']").TextContent.Split('·', StringSplitOptions.TrimEntries));

        // depends_on once, Readiness and ProjectionRef's own ids not at all.
        Assert.Equal(
            ["Task", "Repo"],
            view.FindAll("[data-testid='domain-model-reference']").Select(r => r.GetAttribute("data-target")));

        var service = view.Find("[data-testid='domain-model-service']");
        Assert.Equal("Projection", service.GetAttribute("data-name"));
        Assert.Contains("Domain services that act on the Task", view.Find("[data-testid='domain-model-services']").TextContent);
    }

    [Fact]
    public void All_fields_on_the_root_card_opens_Fields()
    {
        using var context = Context();

        var view = context.Render<DomainModelView>(p => p.Add(v => v.Fences, [new DomainModelFence("Model diagram", Tasks)]));
        view.Find("[data-testid='domain-model-all-fields']").Click();

        Assert.Equal("true", view.Find("[data-testid='domain-model-tab-fields']").GetAttribute("aria-selected"));
        Assert.Empty(view.FindAll("[data-testid='domain-model-title']"));
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
