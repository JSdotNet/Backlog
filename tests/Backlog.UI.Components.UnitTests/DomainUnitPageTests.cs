using Backlog.SharedKernel.Devbook;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The Domain page of the Domain view (local ADR 0047): an unsplit domain file
/// picks its units in a chapter bar, a split file opens on its one unit, an
/// aggregate reads as About beside Always true, and a domain service lists what
/// you can count on — with nothing technical on either.
/// </summary>
public sealed class DomainUnitPageTests
{
    private const string Split = """
        # Order

        ```meta
        type: domain
        ```

        ## Order

        ```meta
        type: aggregate
        ```

        A customer's request for goods.
        """;

    private static DomainChapterFile Unsplit => DomainUnits.Parse(DomainUnitsTests.Domain, DomainUnitsTests.DomainPath);

    private static DomainChapterFile Requirements => DomainUnits.Parse(DomainUnitsTests.Requirements, DomainUnitsTests.RequirementsPath);

    private static IRenderedComponent<DomainUnitPage> Render(BunitContext context, DomainChapterFile file, Action<ComponentParameterCollectionBuilder<DomainUnitPage>>? more = null)
    {
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context.Render<DomainUnitPage>(parameters =>
        {
            parameters.Add(p => p.File, file);
            parameters.Add(p => p.RequirementFiles, [Requirements]);
            more?.Invoke(parameters);
        });
    }

    [Fact]
    public void An_unsplit_file_offers_its_units_in_a_chapter_bar_led_by_their_kind()
    {
        using var context = new BunitContext();
        var page = Render(context, Unsplit);

        Assert.Equal("Chapters in domain.md · each one is a page of its own once split out", page.Find("[data-testid='domain-units-caption']").TextContent);

        var tabs = page.FindAll("[data-testid='domain-units-bar'] [role='tab']");
        Assert.Equal(["Order", "Pricing", "Glossary"], tabs.Select(tab => tab.TextContent.Trim()));
        Assert.Equal(
            ["aggregate", "domain-service", null],
            tabs.Select(tab => tab.QuerySelector(".domain-units__kind")?.GetAttribute("data-kind")));
        Assert.Contains("domain-units__bar-tab--active", tabs[0].ClassName);
    }

    [Fact]
    public void Picking_a_unit_shows_it_and_reports_the_pick()
    {
        using var context = new BunitContext();
        string? picked = null;
        var page = Render(context, Unsplit, parameters => parameters.Add(p => p.ActiveUnitChanged, (string key) => picked = key));

        page.Find("[data-testid='domain-unit-tab-pricing']").Click();

        Assert.Equal("pricing", picked);
        Assert.Equal("pricing", page.Find("[data-testid='domain-unit']").GetAttribute("data-unit"));
        Assert.NotNull(page.Find("[data-testid='domain-service-about']"));
    }

    [Fact]
    public void A_split_file_opens_on_its_one_unit_without_a_bar()
    {
        using var context = new BunitContext();
        var page = Render(context, DomainUnits.Parse(Split, ".devbook/domain/ordering/domain.order.md"));

        Assert.Empty(page.FindAll("[data-testid='domain-units-bar']"));
        Assert.Equal("order", page.Find("[data-testid='domain-unit']").GetAttribute("data-unit"));
    }

    [Fact]
    public void An_aggregate_opens_on_about_with_always_true_beside_it()
    {
        using var context = new BunitContext();
        var page = Render(context, Unsplit);

        var tabs = page.FindAll("[data-testid='domain-unit-tabs'] [role='tab']");
        Assert.Equal(["About", "Always true"], tabs.Select(tab => tab.TextContent.Trim()));
        Assert.Equal("true", tabs[0].GetAttribute("aria-selected"));

        page.Find("[data-testid='domain-unit-always-true-tab']").Click();
        Assert.NotNull(page.Find("[data-testid='domain-always-true']"));
    }

    [Fact]
    public void About_is_a_summary_of_the_chapter_not_its_prose()
    {
        using var context = new BunitContext();
        var about = Render(context, Unsplit).Find("[data-testid='domain-aggregate-about']");

        Assert.Equal("Order", about.QuerySelector("[data-testid='domain-unit-title']")!.TextContent);
        Assert.Equal("aggregate", about.QuerySelector("[data-testid='domain-unit-kicker']")!.TextContent);
        Assert.Equal("A customer's request for goods, and the boundary for its lines.", about.QuerySelector("[data-testid='domain-unit-lead']")!.TextContent);

        // The rest of the chapter's prose stays in the chapter.
        Assert.DoesNotContain("placed once and paid once", about.TextContent);
        Assert.DoesNotContain("never deleted", about.TextContent);
    }

    [Fact]
    public void About_reads_cards_values_and_events_in_words_and_no_field()
    {
        using var context = new BunitContext();
        var about = Render(context, Unsplit).Find("[data-testid='domain-aggregate-about']");

        Assert.Contains("What an order can hold", about.TextContent);
        var cards = about.QuerySelectorAll("[data-testid='domain-unit-member']");
        Assert.Equal(["entity", "value-object"], cards.Select(card => card.GetAttribute("data-type")));
        Assert.Contains("One product and its quantity on the order.", cards[0].TextContent);
        Assert.Equal("OrderLine", cards[0].QuerySelector("[data-testid='domain-unit-member-model']")!.TextContent);
        Assert.Null(cards[0].QuerySelector("[data-testid='domain-unit-member-proposed']"));
        Assert.NotNull(cards[1].QuerySelector("[data-testid='domain-unit-member-proposed']"));

        var values = about.QuerySelectorAll("[data-testid='domain-unit-enum'] .domain-about__value");
        Assert.Equal(["Draft", "Placed", "Paid", "Cancelled"], values.Select(value => value.TextContent));

        var raised = about.QuerySelector("[data-testid='domain-unit-event']")!;
        Assert.Equal("OrderPlaced", raised.GetAttribute("data-event"));
        Assert.Contains("Published when an Order is placed.", raised.TextContent);
        Assert.DoesNotContain("OrderPlaced", raised.TextContent);
        Assert.Equal("Heard by Billing, Shipping", raised.QuerySelector("[data-testid='domain-unit-event-consumers']")!.TextContent);

        // The event's payload is Model's.
        Assert.DoesNotContain("order_id", about.TextContent);
    }

    [Fact]
    public void About_links_to_always_true_the_glossary_and_model()
    {
        using var context = new BunitContext();
        DevbookReference? followed = null;
        string? picked = null;
        var page = Render(context, Unsplit, parameters =>
        {
            parameters.Add(p => p.InvariantFiles, [DomainUnits.Parse(DomainRulesTests.Invariants, DomainRulesTests.InvariantsPath)]);
            parameters.Add(p => p.ModelPath, ".devbook/domain/ordering/model.md");
            parameters.Add(p => p.OnNavigate, (DevbookReference reference) => followed = reference);
            parameters.Add(p => p.ActiveUnitChanged, (string key) => picked = key);
        });

        Assert.NotEmpty(page.FindAll("[data-testid='domain-unit-tile']"));

        page.Find("[data-testid='domain-unit-link-model-events']").Click();
        Assert.Equal(".devbook/domain/ordering/model.md", followed!.Path);

        page.Find("[data-testid='domain-unit-link-always-true']").Click();
        Assert.Equal("true", page.Find("[data-testid='domain-unit-always-true-tab']").GetAttribute("aria-selected"));
        Assert.Equal("Order · always true", page.Find("[data-testid='domain-unit-title']").TextContent);

        page.Find("[data-testid='domain-unit-about-tab']").Click();
        page.Find("[data-testid='domain-unit-link-glossary']").Click();
        Assert.Equal("ubiquitous-language", picked);
        Assert.Equal("ubiquitous-language", page.Find("[data-testid='domain-unit']").GetAttribute("data-unit"));
    }

    [Fact]
    public void A_service_page_lists_what_you_can_count_on_as_links_to_the_requirements()
    {
        using var context = new BunitContext();
        DevbookReference? followed = null;
        var page = Render(context, Unsplit, parameters =>
        {
            parameters.Add(p => p.ActiveUnit, "pricing");
            parameters.Add(p => p.OnNavigate, (DevbookReference reference) => followed = reference);
        });

        var links = page.FindAll("[data-testid='domain-unit-guarantee-link']");
        Assert.Equal(
            ["A price is never negative", "Discounts apply once", "A total is shown before paying"],
            links.Select(link => link.TextContent.Trim()));

        links[0].Click();
        Assert.Equal(".devbook/domain/ordering/requirements.md", followed!.Path);
        Assert.Equal("requirement-a-price-is-never-negative", followed.Slug);

        Assert.Equal("PriceChanged", page.Find("[data-testid='domain-unit-event']").GetAttribute("data-event"));
    }

    [Fact]
    public void A_service_page_is_its_summary_and_never_how_it_is_invoked()
    {
        using var context = new BunitContext();
        var service = Render(context, Unsplit, parameters => parameters.Add(p => p.ActiveUnit, "pricing"))
            .Find("[data-testid='domain-service-about']");

        Assert.Equal("domain service", service.QuerySelector("[data-testid='domain-unit-kicker']")!.TextContent);
        Assert.Equal("Pricing", service.QuerySelector("[data-testid='domain-unit-title']")!.TextContent);
        Assert.Equal("Works out what an order costs.", service.QuerySelector("[data-testid='domain-unit-lead']")!.TextContent);

        Assert.DoesNotContain("Invocation", service.TextContent);
        Assert.DoesNotContain("synchronously", service.TextContent);
        Assert.DoesNotContain("order_id", service.TextContent);
    }

    [Fact]
    public void A_service_page_links_to_its_promises_its_aggregate_and_model()
    {
        using var context = new BunitContext();
        DevbookReference? followed = null;
        var page = Render(context, Unsplit, parameters =>
        {
            parameters.Add(p => p.ActiveUnit, "pricing");
            parameters.Add(p => p.ModelPath, ".devbook/domain/ordering/model.md");
            parameters.Add(p => p.OnNavigate, (DevbookReference reference) => followed = reference);
        });

        var row = page.FindAll("[data-testid='domain-unit-links'] button");
        Assert.Equal(["3 promises →", "Order →", "How it runs, on Model →"], row.Select(link => link.TextContent));

        page.Find("[data-testid='domain-unit-link-promises']").Click();
        Assert.Equal(".devbook/domain/ordering/requirements.md", followed!.Path);
        Assert.Equal("pricing", followed.Slug);

        page.Find("[data-testid='domain-unit-link-unit-order']").Click();
        Assert.Equal("order", page.Find("[data-testid='domain-unit']").GetAttribute("data-unit"));
    }

    [Fact]
    public void The_glossary_lists_its_terms()
    {
        using var context = new BunitContext();
        var page = Render(context, Unsplit, parameters => parameters.Add(p => p.ActiveUnit, "ubiquitous-language"));

        var term = page.Find("[data-testid='domain-glossary-term']");
        Assert.Equal("Customer", term.GetAttribute("data-term"));
        Assert.Contains("Whoever places the order.", term.TextContent);
    }

    [Fact]
    public void A_file_with_no_unit_says_so()
    {
        using var context = new BunitContext();
        var page = Render(context, DomainUnits.Parse("# Empty\n\n## Shared Enums\n\n```meta\ntype: shared-enums\n```\n", DomainUnitsTests.DomainPath));

        Assert.NotNull(page.Find("[data-testid='domain-units-empty']"));
    }

    private const string WithLifecycle = """
        # Tasks

        ## Task

        ```meta
        type: aggregate
        ```

        **Task lifecycle**

        Five recorded states.

        ```mermaid
        stateDiagram-v2
            [*] --> Draft
            Draft --> Ready : refined
            Ready --> Done : finished (TaskCompleted, not yet emitted)
        ```

        A refined, actionable item.

        ### Readiness

        ```meta
        type: value-object
        ```

        **Readiness derivation**

        ```mermaid
        flowchart TD
            A{"ticked?"} -->|yes| D["done"]
            A -->|no| R["ready"]
        ```

        Derived on read.

        ## Occurrence Spawning

        ```meta
        type: domain-service
        ```

        **Recurring task occurrences**

        ```mermaid
        flowchart TD
            A["ticked"] --> B["next occurrence"]
        ```

        Spawns the next occurrence.
        """;

    /// <summary>Devbook contract 37 opens a chapter with the diagrams it draws, so
    /// About draws the aggregate's lifecycle and a member's derivation in the
    /// lifecycle's style, never as Mermaid, below the summary card.</summary>
    [Fact]
    public void About_draws_the_lifecycle_and_the_members_flows_the_chapter_opens_with()
    {
        using var context = new BunitContext();
        var page = Render(context, DomainUnits.Parse(WithLifecycle, DomainUnitsTests.DomainPath));

        var flows = page.Find("[data-testid='domain-unit-flows']");
        Assert.Equal(
            ["Task lifecycle", "Readiness derivation · Readiness"],
            flows.QuerySelectorAll("[data-testid='domain-flow-title']").Select(title => string.Join(' ', title.TextContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))));
        Assert.Equal("Five recorded states.", flows.QuerySelector("[data-testid='domain-flow-lead']")!.TextContent.Trim());
        Assert.NotNull(flows.QuerySelector("[data-testid='domain-flow-legend']"));
        Assert.Equal(1, flows.QuerySelectorAll("[data-testid='domain-flow-row']").Length);
        Assert.Equal(1, flows.QuerySelectorAll("[data-testid='domain-flowchart']").Length);
        Assert.DoesNotContain("mermaid", page.Markup, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A domain service's page draws the process its chapter opens with.</summary>
    [Fact]
    public void A_service_page_draws_the_process_its_chapter_opens_with()
    {
        using var context = new BunitContext();
        var page = Render(context, DomainUnits.Parse(WithLifecycle, DomainUnitsTests.DomainPath));

        page.Find("[data-testid='domain-unit-tab-occurrence-spawning']").Click();

        var flows = page.Find("[data-unit='occurrence-spawning'] [data-testid='domain-unit-flows']");
        Assert.Equal("Recurring task occurrences", flows.QuerySelector("[data-testid='domain-flow-title']")!.TextContent.Trim());
        Assert.Equal(1, flows.QuerySelectorAll("[data-testid='domain-flowchart']").Length);
    }

    private const string TasksPath = ".devbook/domain/tasks/domain.md";

    private const string Tasks = """
        # Tasks

        ## Task

        ```meta
        type: aggregate
        ```

        **Task lifecycle**

        ```mermaid
        stateDiagram-v2
            state "In progress" as InProgress
            [*] --> Draft
            Draft --> Ready : refined
            Ready --> InProgress : started (TaskProjected, not yet emitted)
            InProgress --> Done : finished
        ```

        A refined, actionable item.

        ### Task Status

        ```meta
        type: enum
        ```

        Lifecycle state: `draft`, `ready`, `in_progress`, `done`.

        ### Priority

        ```meta
        type: enum
        ```

        Ranking of the task: `low`, `high`. A high one is shown first.

        ### Weekday

        ```meta
        type: enum
        ```

        A day of the week, `monday` through `sunday`.

        ### Readiness

        ```meta
        type: enum
        ```

        Where a task stands: `ready`, `blocked`.

        ## Projection

        ```meta
        type: domain-service
        related: [.devbook/domain/tasks/requirements.md#projection, .devbook/domain/tasks/requirements.md#ordering]
        ```

        Creates and closes downstream artifacts. This context publishes no domain events yet.

        ## TaskProjected

        ```meta
        type: domain-event
        related: [domain.md#task, domain.md#projection]
        ```

        Published when work on a task starts.

        ## TaskCompleted

        ```meta
        type: domain-event
        related: [domain.md#task]
        ```

        Published when a task is done.

        ## Ubiquitous Language

        ```meta
        type: ubiquitous-language
        ```

        ### Area

        ```meta
        type: term
        ```

        A grouping of tasks.
        """;

    private const string TasksRequirements = """
        # Tasks requirements

        ## Projection

        ```meta
        type: requirements
        ```

        ### Requirement: Starting work opens one artifact per target repository

        ```meta
        type: requirement
        ```

        The system SHALL open one artifact per target repository.

        ### Requirement: Completing a task closes its artifacts

        ```meta
        type: requirement
        ```

        The system SHALL close each artifact.

        ## Ordering

        ```meta
        type: requirements
        ```

        ### Requirement: Unranked tasks fall back to recency

        ```meta
        type: requirement
        ```

        The system SHALL order unranked tasks by recency.
        """;

    private const string TasksInvariants = """
        # Invariants

        ## Task

        ```meta
        type: invariants
        related: [.devbook/domain/tasks/domain.md#task]
        ```

        ### Invariant: A new task starts in draft

        ```meta
        type: invariant
        ```

        Every task is created in draft.
        """;

    private static IRenderedComponent<DomainUnitPage> RenderTasks(BunitContext context, string markdown = Tasks, Action<ComponentParameterCollectionBuilder<DomainUnitPage>>? more = null)
    {
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context.Render<DomainUnitPage>(parameters =>
        {
            parameters.Add(p => p.File, DomainUnits.Parse(markdown, TasksPath));
            parameters.Add(p => p.RequirementFiles, [DomainUnits.Parse(TasksRequirements, ".devbook/domain/tasks/requirements.md")]);
            parameters.Add(p => p.InvariantFiles, [DomainUnits.Parse(TasksInvariants, ".devbook/domain/tasks/domain.invariants.md")]);
            more?.Invoke(parameters);
        });
    }

    // The FocusAsync calls the page made: the only element it focuses here is a
    // flow section.
    private static IReadOnlyList<JSRuntimeInvocation> Focused(BunitContext context) =>
        [.. context.JSInterop.Invocations.Where(invocation => invocation.Identifier.EndsWith("focus", StringComparison.Ordinal))];

    [Fact]
    public void An_enum_row_leads_with_its_label_and_links_the_lifecycle_that_draws_it()
    {
        using var context = new BunitContext();
        var page = RenderTasks(context);

        var rows = page.FindAll("[data-testid='domain-unit-enum']");
        Assert.Equal(["Task Status", "Priority", "Weekday", "Readiness"], rows.Select(row => row.GetAttribute("data-enum")));
        Assert.Equal(
            ["Lifecycle state", "Ranking of the task", null, "Where a task stands"],
            rows.Select(row => row.QuerySelector("[data-testid='domain-unit-enum-label']")?.TextContent));
        Assert.Equal("Task Status", rows[0].QuerySelector(".domain-about__enum-title")!.TextContent);

        // Only the enum whose values are the lifecycle's states links to it; a
        // note stays beside its values.
        Assert.Equal(
            [true, false, false, false],
            rows.Select(row => row.QuerySelector("[data-testid='domain-unit-enum-moves']") is not null));
        Assert.Equal("A high one is shown first.", rows[1].QuerySelector("[data-testid='domain-unit-enum-note']")!.TextContent);
        Assert.Equal("How it moves →", rows[0].QuerySelector("[data-testid='domain-unit-enum-moves']")!.TextContent.Trim());

        // Following it focuses the lifecycle's section on the same page, once
        // per ask: asked again, it focuses again.
        Assert.Empty(Focused(context));
        page.Find("[data-testid='domain-unit-enum-moves']").Click();
        Assert.Equal("-1", page.Find("[data-testid='domain-flow-section']").GetAttribute("tabindex"));
        page.WaitForAssertion(() => Assert.Single(Focused(context)));
        page.Render();
        Assert.Single(Focused(context));
        page.Find("[data-testid='domain-unit-enum-moves']").Click();
        page.WaitForAssertion(() => Assert.Equal(2, Focused(context).Count));
    }

    [Fact]
    public void About_links_to_how_the_aggregate_moves_and_how_it_is_built()
    {
        using var context = new BunitContext();
        DevbookReference? followed = null;
        var page = RenderTasks(context, more: parameters =>
        {
            parameters.Add(p => p.ModelPath, ".devbook/domain/tasks/model.md");
            parameters.Add(p => p.OnNavigate, (DevbookReference reference) => followed = reference);
        });

        Assert.Equal(
            ["What is always true · 1 →", "How it moves →", "How it is built →", "Glossary →"],
            page.FindAll("[data-testid='domain-unit-links'] button").Select(link => link.TextContent));

        // With no flow file, how it moves is the lifecycle on this page.
        page.Find("[data-testid='domain-unit-link-flow']").Click();
        Assert.Null(followed);
        page.WaitForAssertion(() => Assert.IsType<ElementReference>(Assert.Single(Focused(context)).Arguments[0]));

        // How it is built opens Model on Diagram; the contracts open it on Events.
        page.Find("[data-testid='domain-unit-link-model-built']").Click();
        Assert.Equal(".devbook/domain/tasks/model.md", followed!.Raw);
        page.Find("[data-testid='domain-unit-link-model-events']").Click();
        Assert.Equal(".devbook/domain/tasks/model.md#events", followed.Raw);

        // With no Model file, there is no link to it.
        using var bare = new BunitContext();
        Assert.Empty(RenderTasks(bare).FindAll("[data-testid='domain-unit-link-model-built']"));
    }

    [Fact]
    public void A_units_events_say_what_the_rest_of_the_domain_hears_and_when_none_is_sent_yet()
    {
        using var context = new BunitContext();
        var page = RenderTasks(context, more: parameters => parameters.Add(p => p.DomainName, "Backlog"));

        var events = page.Find("[data-testid='domain-unit-events']");
        Assert.Equal("What the rest of Backlog hears", events.QuerySelector("[data-testid='domain-unit-events-title']")!.TextContent);
        Assert.Equal("Not sent yet.", events.QuerySelector("[data-testid='domain-unit-events-unsent']")!.TextContent);
        Assert.Empty(events.QuerySelectorAll("[data-testid='domain-unit-event-unsent']"));

        // With no domain name the heading reads the domain; a mixed list marks
        // only the rows not sent, and no footer.
        using var mixed = new BunitContext();
        var sent = RenderTasks(mixed, Tasks
            .Replace(" This context publishes no domain events yet.", string.Empty)
            .Replace("(TaskProjected, not yet emitted)", "(TaskProjected)")
            .Replace("A refined, actionable item.", "A refined, actionable item. It emits `TaskCompleted` once done."));
        var list = sent.Find("[data-testid='domain-unit-events']");
        Assert.Equal("What the rest of the domain hears", list.QuerySelector("[data-testid='domain-unit-events-title']")!.TextContent);
        Assert.Empty(list.QuerySelectorAll("[data-testid='domain-unit-events-unsent']"));
        Assert.Equal(
            [("TaskProjected", true), ("TaskCompleted", false)],
            list.QuerySelectorAll("[data-testid='domain-unit-event']").Select(row => (row.GetAttribute("data-event"), row.QuerySelector("[data-testid='domain-unit-event-unsent']") is not null)));
    }

    /// <summary>A unit whose chapter draws nothing shows no flow section.</summary>
    [Fact]
    public void A_unit_that_draws_nothing_has_no_flow_section()
    {
        using var context = new BunitContext();

        Assert.Empty(Render(context, Unsplit).FindAll("[data-testid='domain-unit-flows']"));
    }
}
