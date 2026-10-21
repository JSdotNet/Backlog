using Backlog.SharedKernel.Devbook;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The Domain page of the Domain view (local ADR 0037): an unsplit domain file
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
}
