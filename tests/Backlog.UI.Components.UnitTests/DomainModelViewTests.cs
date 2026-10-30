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
        Assert.Equal("Order", view.Find("[data-testid='domain-model-title']").TextContent.Trim());
        Assert.Single(view.FindAll("[data-testid='domain-fields']"));

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

        // No Fields chapter: the root's fields stand in the kinds their types say,
        // and what it owns beside them.
        var board = view.Find("[data-testid='domain-fields']");
        Assert.Equal("false", board.GetAttribute("data-written"));
        Assert.Equal(["Ids pointing elsewhere"], board.QuerySelectorAll("[data-testid='domain-fields-group']").Select(g => g.GetAttribute("data-title")));
        Assert.Equal("id", board.QuerySelector("[data-name='customer_id'] [data-testid='domain-field-type']")!.TextContent.Trim());
        var owned = view.Find("[data-testid='domain-fields-owned']");
        Assert.Contains("Owned by the Order", owned.TextContent);
        Assert.Equal("OrderLine", owned.QuerySelector("[data-testid='domain-fields-owned-value-objects'] [data-testid='domain-fields-owned-part']")!.GetAttribute("data-name"));
        Assert.Contains("Pricing", owned.QuerySelector("[data-testid='domain-fields-owned-services']")!.TextContent);

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

        Assert.Equal("What Model tells other contexts", view.Find("[data-testid='domain-model-title']").TextContent.Trim());
        Assert.Contains("One of two is emitted", view.Find("[data-testid='domain-events-intro']").TextContent);

        // The index lists every event; the first is picked and its contract shows beneath.
        var rows = view.FindAll("[data-testid='domain-event-row']");
        Assert.Equal(["OrderPlaced", "OrderShipped"], rows.Select(r => r.GetAttribute("data-name")));
        Assert.Equal(["true", "false"], rows.Select(r => r.GetAttribute("data-selected")));
        Assert.Contains("an order is placed", rows[0].TextContent);
        Assert.Contains("Billing", rows[0].TextContent);

        var placed = view.Find("[data-testid='domain-event']");
        Assert.Equal("documented", placed.GetAttribute("data-delivery"));
        Assert.Equal("Documented", placed.QuerySelector("[data-testid='domain-event-delivery']")!.TextContent.Trim());
        Assert.Contains("Order", placed.QuerySelector("[data-testid='domain-event-raised-by']")!.TextContent);
        Assert.Equal("order_id", placed.QuerySelector("[data-testid='domain-event-payload'] th code")!.TextContent);
        Assert.Contains("Billing", placed.QuerySelector("[data-testid='domain-event-consumers']")!.TextContent);
        Assert.Contains("does not say", placed.QuerySelector("[data-testid='domain-event-rules']")!.TextContent);

        // Picking the other row shows its contract instead.
        view.FindAll("[data-testid='domain-event-pick']")[1].Click();
        var shipped = view.Find("[data-testid='domain-event']");
        Assert.Equal("OrderShipped", shipped.GetAttribute("data-name"));
        Assert.Equal("emitted", shipped.GetAttribute("data-delivery"));
        Assert.Contains("Not named", shipped.QuerySelector("[data-testid='domain-event-raised-by']")!.TextContent);
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
        Assert.Equal("Task", view.Find("[data-testid='domain-model-title']").TextContent.Trim());
    }

    [Fact]
    public void Fields_reads_the_model_files_purpose_groups_with_meanings_and_rule_counts()
    {
        using var context = Context();
        const string markdown = """
            # Tasks

            ## Fields

            ### Work state

            Where is the work?

            - `status` — The recorded lifecycle state. Rules: [starts in draft](domain.invariants.md#invariant-a-new-task-starts-in-draft), [moves](domain.invariants.md#how-a-task-moves).
            - `readiness` — Derived on every read.

            ### Identity

            What is it?

            - `id` · `title` — Who it is and what it says.
            """;
        var rules = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["domain.invariants.md#invariant-a-new-task-starts-in-draft"] = ["A new task starts in draft"],
            ["domain.invariants.md#how-a-task-moves"] = ["A new task starts in draft", "Status changes follow the lifecycle"]
        };
        var profiles = new Dictionary<string, DomainModelProfile>
        {
            ["Task"] = new("Task", "A refined, actionable item.", ["TaskItem", "task_id"], "draft"),
            ["SubItem"] = new("Sub-Item", "An ordered step.", ["SubItem"], "draft")
        };

        var view = context.Render<DomainModelView>(p => p
            .Add(v => v.Fences, [new DomainModelFence("Model diagram", Tasks)])
            .Add(v => v.Markdown, markdown)
            .Add(v => v.Rules, rules)
            .Add(v => v.Profiles, profiles));
        view.Find("[data-testid='domain-model-tab-fields']").Click();

        Assert.Equal("Draft", view.Find("[data-testid='domain-model-status']").TextContent.Trim());
        Assert.Equal(["TaskItem", "task_id"], view.FindAll("[data-testid='domain-model-aliases'] code").Select(c => c.TextContent));
        Assert.Equal("A refined, actionable item.", view.Find("[data-testid='domain-model-lead']").TextContent.Trim());
        Assert.Single(view.FindAll("[data-testid='domain-fields-legend']"));

        var board = view.Find("[data-testid='domain-fields']");
        Assert.Equal("true", board.GetAttribute("data-written"));
        var groups = board.QuerySelectorAll("[data-testid='domain-fields-group']");
        Assert.Equal(["Work state", "Identity", "Other fields"], groups.Select(g => g.GetAttribute("data-title")));
        Assert.Equal("Where is the work?", groups[0].QuerySelector("[data-testid='domain-fields-question']")!.TextContent);

        var status = board.QuerySelector("[data-name='status']")!;
        Assert.Equal("Task Status", status.QuerySelector("[data-testid='domain-field-type']")!.TextContent);
        Assert.Contains("The recorded lifecycle state.", status.QuerySelector("[data-testid='domain-field-meaning']")!.TextContent);
        Assert.DoesNotContain("Rules", status.QuerySelector("[data-testid='domain-field-meaning']")!.TextContent);
        Assert.Equal("2 rules", status.QuerySelector("[data-testid='domain-field-rules']")!.TextContent);

        var readiness = board.QuerySelector("[data-name='readiness']")!;
        Assert.Contains("domain-field-row--derived", readiness.ClassName);
        Assert.Equal("Readiness", readiness.QuerySelector("[data-testid='domain-field-type']")!.TextContent);

        // Two fields of different types in one bullet name no one type.
        var identity = board.QuerySelector("[data-name='id title']")!;
        Assert.Equal("id · title", identity.QuerySelector("code")!.TextContent);
        Assert.Null(identity.QuerySelector("[data-testid='domain-field-type']"));
        Assert.Equal(["due_on", "repo_ids", "depends_on"], groups[2].QuerySelectorAll("[data-testid='domain-field']").Select(r => r.GetAttribute("data-name")));
        Assert.Equal("date", groups[2].QuerySelector("[data-name='due_on'] [data-testid='domain-field-type']")!.TextContent);

        var owned = view.Find("[data-testid='domain-fields-owned']");
        Assert.Contains("Sub-Item", owned.QuerySelector("[data-testid='domain-fields-owned-entities']")!.TextContent);
        Assert.Contains("An ordered step.", owned.QuerySelector("[data-testid='domain-fields-owned-entities']")!.TextContent);
        Assert.Equal(["TaskStatus", "Readiness"], owned.QuerySelectorAll("[data-testid='domain-fields-enum']").Select(e => e.GetAttribute("data-name")));
    }

    [Fact]
    public void Events_says_what_the_context_listens_to()
    {
        using var context = Context();
        var chapters = new DomainChapterReading([], [])
        {
            Listens = [new DomainEventSubscription("Inbox", "ItemTriaged", "Publishes ItemTriaged", "Relies on Tasks creating a draft task.")]
        };

        var view = context.Render<DomainModelView>(p => p
            .Add(v => v.Fences, [new DomainModelFence("Model diagram", Order)])
            .Add(v => v.Title, "Tasks")
            .Add(v => v.Chapters, chapters));
        view.Find("[data-testid='domain-model-tab-events']").Click();

        var listens = view.Find("[data-testid='domain-events-listens']");
        Assert.Contains("What Tasks listens to", listens.TextContent);
        var feed = listens.QuerySelector("[data-testid='domain-events-feed']")!;
        Assert.Equal("Inbox", feed.GetAttribute("data-context"));
        Assert.Contains("ItemTriaged", feed.TextContent);
        Assert.Empty(view.FindAll("[data-testid='domain-event-row']"));
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

    [Fact]
    public void A_class_no_aggregate_holds_is_a_card_outside_every_band()
    {
        using var context = Context();

        var diagram = context.Render<DomainModelDiagram>(p => p.Add(d => d.Source, """
            classDiagram
                class Device {
                    <<aggregate root>>
                    +OwnerId OwnerId
                }
                class OwnerScope {
                    <<value object>>
                    +OwnerId OwnerId
                    +DeviceId DeviceId
                }
                class TaskChange {
                    +Guid Id
                }
            """));

        Assert.Equal("domain-model", diagram.Find("[data-testid='domain-model-diagram']").GetAttribute("data-rendered"));
        var cards = diagram.FindAll("[data-testid='domain-model-outside-class']");
        Assert.Equal(["OwnerScope", "TaskChange"], cards.Select(c => c.GetAttribute("data-name")));
        Assert.Contains("value object", cards[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("from another context", cards[1].TextContent, StringComparison.Ordinal);
    }

    private static BunitContext Context()
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context;
    }
}
