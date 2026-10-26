namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// A unit's rules shown where the reader meets what they constrain (local ADR
/// 0040): the catalog that finds the rules about a chapter, the one list that
/// draws them — mark, claim, where it is enforced, a link to its row on Always
/// true — and each page that shows them in place.
/// </summary>
public sealed class DomainRulesInPlaceTests
{
    private const string DomainPath = DomainRulesTests.DomainPath;
    private const string InvariantsPath = DomainRulesTests.InvariantsPath;

    private static DomainChapterFile Page => DomainUnits.Parse(DomainRulesTests.Domain, DomainPath);

    private static DomainChapterFile Rules => DomainUnits.Parse(DomainRulesTests.Invariants, InvariantsPath);

    private static DomainRuleCatalog Catalog => DomainRuleCatalog.Of([Page], [Rules]);

    private static BunitContext Context()
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context;
    }

    [Fact]
    public void The_rules_about_a_concept_are_those_its_enforced_at_line_or_its_group_names()
    {
        Assert.Equal(
            ["A line has a quantity", "Lines change through the order"],
            Catalog.About($"{DomainPath}#order-line").Select(rule => rule.Title));
        Assert.Equal(["An amount has one currency"], Catalog.About($"{DomainPath}#money").Select(rule => rule.Title));
    }

    [Fact]
    public void The_rules_about_a_unit_are_every_rule_on_its_always_true()
    {
        Assert.Equal(5, Catalog.About($"{DomainPath}#order").Count);
        Assert.Empty(Catalog.About($"{DomainPath}#nothing-here"));
        Assert.Empty(DomainRuleCatalog.Empty.About($"{DomainPath}#order"));
    }

    [Fact]
    public void A_reference_spelled_the_hosts_way_names_the_same_chapter()
    {
        Assert.Equal(2, Catalog.About(".domain/ordering/domain.md#Order-Line").Count);
    }

    [Fact]
    public void Each_rule_knows_its_row_on_always_true()
    {
        var rule = Catalog.About($"{DomainPath}#order-line")[0];

        Assert.Equal($"{InvariantsPath}#invariant-a-line-has-a-quantity", rule.Reference);
        Assert.Equal("order", rule.Place!.UnitKey);
        Assert.Equal(DomainPath, rule.Place.DocumentPath);
        Assert.Equal(rule.Reference, rule.Place.Rule);
        Assert.Equal($"{DomainPath}#order-line", rule.EnforcedOn);
        Assert.Equal([rule], Catalog.About(rule.Reference));
    }

    private static IReadOnlyList<DomainRule> OrderRules => Catalog.About($"{DomainPath}#order");

    [Fact]
    public void A_list_draws_each_rule_as_always_true_does_and_shows_three_before_the_rest()
    {
        using var context = Context();
        var list = context.Render<DomainRuleList>(parameters => parameters.Add(p => p.Rules, OrderRules));

        var rows = list.FindAll("[data-testid='domain-rule-list-rule']");
        Assert.Equal(3, rows.Count);
        Assert.Equal(["✓", "?", "○"], rows.Select(row => row.QuerySelector(".domain-rules__mark")!.TextContent));
        Assert.Equal(["proved", "open", "unproved"], rows.Select(row => row.GetAttribute("data-proof")));
        Assert.Contains("Open — not built until someone decides.", rows[1].TextContent, StringComparison.Ordinal);
        Assert.Equal("Proved by a test:", rows[0].QuerySelector(".sr-only")!.TextContent.Trim());

        var more = list.Find("[data-testid='domain-rule-list-more']");
        Assert.Equal("2 more rules", more.TextContent.Trim());
        more.Click();

        Assert.Equal(5, list.FindAll("[data-testid='domain-rule-list-rule']").Count);
        Assert.Equal("Show fewer", list.Find("[data-testid='domain-rule-list-more']").TextContent.Trim());
    }

    [Fact]
    public void A_list_one_past_the_limit_shows_every_rule()
    {
        using var context = Context();
        var list = context.Render<DomainRuleList>(parameters => parameters.Add(p => p.Rules, OrderRules.Take(4).ToList()));

        Assert.Equal(4, list.FindAll("[data-testid='domain-rule-list-rule']").Count);
        Assert.Empty(list.FindAll("[data-testid='domain-rule-list-more']"));
    }

    [Fact]
    public void A_row_opens_on_where_the_rule_is_enforced_and_links_to_its_row()
    {
        using var context = Context();
        DomainAlwaysTrueLink? opened = null;
        var list = context.Render<DomainRuleList>(parameters => parameters
            .Add(p => p.Rules, Catalog.About($"{DomainPath}#order-line"))
            .Add(p => p.OnOpen, link => opened = link));

        var toggle = list.FindAll("[data-testid='domain-rule-list-toggle']")[0];
        Assert.Equal("false", toggle.GetAttribute("aria-expanded"));
        toggle.Click();

        var detail = list.Find("[data-testid='domain-rule-list-detail']");
        Assert.Equal(detail.Id, list.FindAll("[data-testid='domain-rule-list-toggle']")[0].GetAttribute("aria-controls"));
        Assert.Equal("Every order line holds one or more of its product.", detail.QuerySelector("[data-testid='domain-rule-list-sentence']")!.TextContent);
        Assert.Equal("OrderLine.SetQuantity()", detail.QuerySelector("[data-testid='domain-rule-list-enforced'] code")!.TextContent);

        list.Find("[data-testid='domain-rule-list-open']").Click();
        Assert.Equal($"{InvariantsPath}#invariant-a-line-has-a-quantity", opened!.Rule);
    }

    [Fact]
    public void A_list_with_no_host_to_follow_links_nowhere_and_no_rules_draws_nothing()
    {
        using var context = Context();
        var list = context.Render<DomainRuleList>(parameters => parameters.Add(p => p.Rules, OrderRules));
        list.FindAll("[data-testid='domain-rule-list-toggle']")[0].Click();
        Assert.Empty(list.FindAll("[data-testid='domain-rule-list-open']"));

        var empty = context.Render<DomainRuleList>(parameters => parameters.Add(p => p.Rules, []));
        Assert.Empty(empty.Markup.Trim());
    }

    [Fact]
    public void Always_true_marks_the_rule_a_link_opened_it_on()
    {
        using var context = Context();
        var unit = DomainUnits.Of(Page, [Rules]).Single(u => u.Key == "order");
        var page = context.Render<DomainAlwaysTrue>(parameters => parameters
            .Add(p => p.Groups, DomainUnits.RulesOf(unit, Page, [Rules], [Page]))
            .Add(p => p.FocusRule, $"{InvariantsPath}#invariant-a-line-has-a-quantity"));

        var focused = Assert.Single(page.FindAll("[data-testid='domain-rule'][data-focused='true']"));
        Assert.Equal($"{InvariantsPath}#invariant-a-line-has-a-quantity", focused.GetAttribute("data-rule"));
        Assert.Equal("-1", focused.GetAttribute("tabindex"));
        Assert.Contains("domain-rules__rule--focused", focused.ClassName, StringComparison.Ordinal);
    }

    [Fact]
    public void A_member_cards_rule_count_opens_its_rules_in_place_and_each_lands_on_always_true()
    {
        using var context = Context();
        var page = context.Render<DomainUnitPage>(parameters => parameters
            .Add(p => p.File, Page)
            .Add(p => p.InvariantFiles, [Rules]));

        var count = page.Find("[data-testid='domain-unit-member-rules']");
        Assert.Equal("2 rules", count.TextContent.Trim());
        Assert.Empty(page.FindAll("[data-testid='domain-unit-member-rule-list']"));
        count.Click();

        var list = page.Find("[data-testid='domain-unit-member-rule-list']");
        Assert.Equal(list.Id, page.Find("[data-testid='domain-unit-member-rules']").GetAttribute("aria-controls"));
        Assert.Equal(
            ["A line has a quantity.", "Lines change through the order."],
            list.QuerySelectorAll("[data-testid='domain-rule-list-claim']").Select(claim => claim.TextContent.Trim()));

        page.FindAll("[data-testid='domain-unit-member-rule-list'] [data-testid='domain-rule-list-toggle']")[0].Click();
        page.Find("[data-testid='domain-rule-list-open']").Click();

        Assert.Equal("true", page.Find("[data-testid='domain-unit-always-true-tab']").GetAttribute("aria-selected"));
        Assert.Equal($"{InvariantsPath}#invariant-a-line-has-a-quantity", page.Find("[data-testid='domain-rule'][data-focused='true']").GetAttribute("data-rule"));
    }

    [Fact]
    public void The_host_can_open_a_unit_on_a_rule()
    {
        using var context = Context();
        var page = context.Render<DomainUnitPage>(parameters => parameters
            .Add(p => p.File, Page)
            .Add(p => p.InvariantFiles, [Rules])
            .Add(p => p.ActiveUnit, "order")
            .Add(p => p.ActiveUnitPage, DomainUnitPage.AlwaysTruePage)
            .Add(p => p.ActiveRule, $"{InvariantsPath}#invariant-an-order-is-placed-once"));

        Assert.Equal($"{InvariantsPath}#invariant-an-order-is-placed-once", page.Find("[data-testid='domain-rule'][data-focused='true']").GetAttribute("data-rule"));
    }

    [Fact]
    public void A_domain_service_shows_what_is_always_true_of_the_units_it_touches()
    {
        using var context = Context();
        var file = DomainUnits.Parse(DomainRulesTests.Domain, DomainPath);
        var service = DomainUnits.Of(file).Single(unit => unit.Key == "pricing");
        var page = context.Render<DomainServiceAbout>(parameters => parameters
            .Add(p => p.Chapter, service.Chapter)
            .Add(p => p.Rules, Catalog.About([$"{DomainPath}#order-line"])));

        Assert.Contains("What is always true of what it touches", page.Find("[data-testid='domain-unit-rules']").TextContent, StringComparison.Ordinal);
        Assert.Equal(2, page.FindAll("[data-testid='domain-rule-list-rule']").Count);
    }

    [Fact]
    public void A_glossary_rows_rule_count_opens_its_rules_in_place()
    {
        using var context = Context();
        var glossary = DomainUnits.Parse("""
            # Ordering

            ## Ubiquitous Language

            ```meta
            type: ubiquitous-language
            ```

            ### Basket

            ```meta
            type: term
            ```

            What a customer gathers before ordering.
            """, ".devbook/domain/ordering/glossary.md");
        var page = context.Render<DomainGlossary>(parameters => parameters
            .Add(p => p.Chapter, glossary.Chapters[0].Children[0])
            .Add(p => p.Files, [Page, glossary])
            .Add(p => p.DebounceMilliseconds, 0)
            .Add(p => p.RulesAbout, Catalog.About));

        var line = page.Find("[data-testid='domain-glossary-term'][data-term='Order Line']");
        var count = line.QuerySelector("[data-testid='domain-glossary-rules']")!;
        Assert.Equal("2 rules", count.TextContent.Trim());
        Assert.Null(page.Find("[data-testid='domain-glossary-term'][data-term='Basket']").QuerySelector("[data-testid='domain-glossary-rules']"));

        count.Click();
        Assert.Equal(2, page.Find("[data-testid='domain-glossary-term'][data-term='Order Line']").QuerySelectorAll("[data-testid='domain-rule-list-rule']").Length);
    }

    private const string FlowPath = ".devbook/domain/ordering/flow.md";

    [Fact]
    public void A_lifecycle_the_invariants_name_shows_its_rules_under_it_and_a_card_behind_its_count()
    {
        using var context = Context();
        var rules = DomainUnits.Parse("""
            # Invariants

            ## Order

            ```meta
            type: invariants
            related: [.devbook/domain/ordering/domain.md#order]
            ```

            ### How an order moves

            ```meta
            type: invariants
            related: [.devbook/domain/ordering/domain.md#order, .devbook/domain/ordering/flow.md#order-lifecycle]
            ```

            #### Invariant: A new order starts open

            ```meta
            type: invariant
            ```

            An order starts open.

            ### Paying

            ```meta
            type: invariants
            related: [.devbook/domain/ordering/flow.md#paid]
            ```

            #### Invariant: A paid order is not paid again

            ```meta
            type: invariant
            ```

            A paid order refuses a second payment.
            """, InvariantsPath);
        var flow = DomainUnits.Parse("""
            # Flow

            ## Order lifecycle

            How an order moves.

            ## Beside the status

            ### Paid

            Whether the order is paid.
            """, FlowPath);
        var catalog = DomainRuleCatalog.Of([Page], [rules]);

        var page = context.Render<DomainFlowPage>(parameters => parameters
            .Add(p => p.File, flow)
            .Add(p => p.RulesAbout, catalog.About));

        var lifecycle = page.Find("[data-testid='domain-flow-rules']");
        Assert.Equal(["A new order starts open."], lifecycle.QuerySelectorAll("[data-testid='domain-rule-list-claim']").Select(claim => claim.TextContent.Trim()));

        var card = page.Find("[data-testid='domain-flow-card'][data-card='paid']");
        card.QuerySelector("[data-testid='domain-flow-card-rules']")!.Click();
        Assert.Single(page.Find("[data-testid='domain-flow-card'][data-card='paid']").QuerySelectorAll("[data-testid='domain-rule-list-rule']"));
    }

    [Fact]
    public void A_fields_rule_count_opens_the_fields_rules_in_place_marked()
    {
        using var context = Context();
        const string fence = """
            classDiagram
                class Order {
                    <<aggregate root>>
                    +Id id
                    +OrderStatus status
                }
                class OrderStatus {
                    <<enumeration>>
                    placed
                }
            """;
        const string markdown = """
            # Ordering

            ## Fields

            ### State

            - `status` — Where the order is. Rules: [placed once](domain.invariants.md#invariant-an-order-is-placed-once), [kept](domain.invariants.md#invariant-a-cancelled-order-stays-on-record).
            """;

        var view = context.Render<DomainModelView>(p => p
            .Add(v => v.Fences, [new DomainModelFence("Model diagram", fence)])
            .Add(v => v.Markdown, markdown)
            .Add(v => v.Rules, DomainFieldBoards.RuleIndex([Rules]))
            .Add(v => v.RuleCatalog, Catalog));
        view.Find("[data-testid='domain-model-tab-fields']").Click();

        var count = view.Find("[data-name='status'] [data-testid='domain-field-rules']");
        Assert.Equal("2 rules", count.TextContent.Trim());
        Assert.Equal("false", count.GetAttribute("aria-expanded"));
        count.Click();

        var list = view.Find("[data-name='status'] [data-testid='domain-field-rule-list']");
        Assert.Equal(["proved", "open"], list.QuerySelectorAll("[data-testid='domain-rule-list-rule']").Select(row => row.GetAttribute("data-proof")));
    }

    [Fact]
    public void A_fields_rule_no_invariants_file_holds_reads_as_its_heading_unmarked()
    {
        using var context = Context();
        const string fence = """
            classDiagram
                class Order {
                    <<aggregate root>>
                    +Id id
                    +OrderStatus status
                }
                class OrderStatus {
                    <<enumeration>>
                    placed
                }
            """;
        const string markdown = """
            # Ordering

            ## Fields

            ### State

            - `status` — Where the order is. Rules: [placed once](domain.invariants.md#invariant-an-order-is-placed-once).
            """;

        var view = context.Render<DomainModelView>(p => p
            .Add(v => v.Fences, [new DomainModelFence("Model diagram", fence)])
            .Add(v => v.Markdown, markdown)
            .Add(v => v.Rules, DomainFieldBoards.RuleIndex([Rules]))
            .Add(v => v.RuleCatalog, DomainRuleCatalog.Empty));
        view.Find("[data-testid='domain-model-tab-fields']").Click();
        view.Find("[data-name='status'] [data-testid='domain-field-rules']").Click();

        var row = view.Find("[data-name='status'] [data-testid='domain-rule-list-rule']");
        Assert.Null(row.GetAttribute("data-proof"));
        Assert.Equal(string.Empty, row.QuerySelector(".domain-rules__mark")!.TextContent);
        Assert.Equal("An order is placed once.", row.QuerySelector("[data-testid='domain-rule-list-claim']")!.TextContent.Trim());
    }

    [Fact]
    public void A_requirements_link_to_one_rule_shows_that_rule_alone()
    {
        var requirements = DomainUnits.Parse("""
            # Requirements

            ## Checkout

            ```meta
            type: requirements
            ```

            > Paying. It rests on [a line's quantity](domain.invariants.md#invariant-a-line-has-a-quantity).

            ### Requirement: X

            The system SHALL x.
            """, ".devbook/domain/ordering/requirements.md");

        var group = Assert.Single(DomainRequirements.Groups(requirements, [Page, Rules, requirements]));
        var unit = Assert.Single(group.AlwaysTrue);
        Assert.Equal(["A line has a quantity"], unit.Listed.Select(rule => rule.Title));
    }

    [Fact]
    public void A_parts_rule_count_on_the_diagram_opens_its_rules_in_place()
    {
        using var context = Context();
        const string fence = """
            classDiagram
                class Order {
                    <<aggregate root>>
                    +Id id
                }
                class OrderLine {
                    <<entity>>
                    +Integer quantity
                }
                Order "1" *-- "1..*" OrderLine : lines
            """;

        var view = context.Render<DomainModelView>(p => p
            .Add(v => v.Fences, [new DomainModelFence("Model diagram", fence)])
            .Add(v => v.ChapterReferences, new Dictionary<string, string> { ["OrderLine"] = $"{DomainPath}#order-line" })
            .Add(v => v.RuleCatalog, Catalog));

        var count = view.Find("[data-testid='domain-model-rules']");
        Assert.Equal("2 rules", count.TextContent.Trim());
        count.Click();

        Assert.Equal(2, view.Find("[data-testid='domain-model-rule-list']").QuerySelectorAll("[data-testid='domain-rule-list-rule']").Length);
    }

    [Fact]
    public void A_features_detail_shows_what_is_always_true_of_what_it_uses()
    {
        var feature = DomainFeatures.Read(DomainFeaturesTests.FeaturesFile, DomainFeaturesTests.Files).Single(f => f.Key == "checkout");

        Assert.Equal(["A paid order is not paid again", "An order holds a line"], feature.Rules.Select(rule => rule.Title));

        using var context = Context();
        var board = context.Render<DomainFeatureBoard>(parameters => parameters
            .Add(p => p.Columns, DomainFeatures.Columns(DomainFeaturesTests.FeaturesFile, DomainFeaturesTests.Files))
            .Add(p => p.ActiveFeature, "checkout"));
        Assert.Equal(2, board.Find("[data-testid='domain-feature-rules']").QuerySelectorAll("[data-testid='domain-rule-list-rule']").Length);
    }
}
