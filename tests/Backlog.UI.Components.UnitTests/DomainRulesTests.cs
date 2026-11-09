namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// How the Domain view reads a unit's Always true tab out of its invariants
/// subpage: each rule's claim and its mark — proved by a test, not yet, or
/// enforced nowhere yet — grouped by the concept its <c>Enforced at:</c> line
/// names, and the shared groupings' rules on the base page.
/// </summary>
public sealed class DomainRulesTests
{
    internal const string DomainPath = ".devbook/domain/ordering/domain.md";
    internal const string InvariantsPath = ".devbook/domain/ordering/domain.invariants.md";

    internal const string Domain = """
        # Ordering

        ```meta
        type: domain
        ```

        ## Order

        ```meta
        type: aggregate
        aliases: [Order]
        ```

        A customer's request for goods.

        ### Order Line

        ```meta
        type: entity
        aliases: [OrderLine]
        ```

        One product and its quantity.

        ## Pricing

        ```meta
        type: domain-service
        ```

        Works out what an order costs.

        ## Shared Value Objects

        ```meta
        type: shared-value-objects
        ```

        > Values more than one aggregate uses.

        ### Money

        ```meta
        type: value-object
        ```

        An amount in one currency.

        ## Shared Enums

        ```meta
        type: shared-enums
        ```

        ### Currency

        ```meta
        type: enum
        ```

        Which money: `eur`, `usd`.
        """;

    internal const string Invariants = """
        # Invariants

        ```meta
        type: invariants
        ```

        What each aggregate enforces.

        ## Order

        ```meta
        type: invariants
        related: [.devbook/domain/ordering/domain.md#order]
        ```

        > The rules an order holds itself to.

        ### Invariant: An order is placed once

        ```meta
        type: invariant
        tests: [unit:dotnet:Ordering.OrderTests.Placing_twice_is_refused]
        ```

        A placed order cannot be placed again. Placing twice would bill twice.

        Enforced at: `Place()`

        ### Invariant: A line has a quantity

        ```meta
        type: invariant
        ```

        Every order line holds one or more of its product.

        Enforced at: `OrderLine.SetQuantity()`

        ### Invariant: A cancelled order stays on record

        ```meta
        type: invariant
        ```

        Cancelling an order keeps it.

        Enforced at: open

        ### Invariant: Lines change through the order

        ```meta
        type: invariant
        tests: unit:dotnet:Ordering.OrderTests.Lines_are_read_only
        ```

        A line changes only through its order.

        Enforced at: all mutations (`OrderLine`'s setters are internal)

        ### Invariant: The total is worked out

        ```meta
        type: invariant
        ```

        The total is the sum of the lines.

        Enforced at: `PriceCalculator.Total()`

        ## Pricing

        ```meta
        type: invariants
        related: [.devbook/domain/ordering/domain.md#pricing]
        ```

        ### Invariant: A price is never negative

        ```meta
        type: invariant
        ```

        No order is priced below zero.

        Enforced at: `Pricing`

        ## Money

        ```meta
        type: invariants
        related: [.devbook/domain/ordering/domain.md#shared-value-objects, .devbook/domain/ordering/domain.md#money]
        ```

        ### Invariant: An amount has one currency

        ```meta
        type: invariant
        ```

        An amount names exactly one currency.

        Enforced at: `Money`

        ## Shared Enums

        ```meta
        type: invariants
        ```

        ### Invariant: A currency is a known code

        ```meta
        type: invariant
        ```

        A currency is one the enum lists.

        Enforced at: `Currency`
        """;

    private static DomainChapterFile Page => DomainUnits.Parse(Domain, DomainPath);

    private static DomainChapterFile Rules => DomainUnits.Parse(Invariants, InvariantsPath);

    private static IReadOnlyList<DomainRuleGroup> RulesOf(string key, DomainChapterFile? page = null, params DomainChapterFile[] invariants)
    {
        var file = page ?? Page;
        var unit = DomainUnits.Of(file, invariants.Length > 0 ? invariants : [Rules]).Single(u => u.Key == key);
        return DomainUnits.RulesOf(unit, file, invariants.Length > 0 ? invariants : [Rules], [file]);
    }

    [Theory]
    [InlineData(".devbook/domain/ordering/domain.md", ".devbook/domain/ordering/domain.invariants.md")]
    [InlineData(".devbook/domain/ordering/domain.order.md", ".devbook/domain/ordering/domain.order.invariants.md")]
    [InlineData(".domain/ordering/domain.order.line.md", ".domain/ordering/domain.order.line.invariants.md")]
    public void A_domain_file_s_rules_are_its_invariants_file_beside_it(string domain, string invariants) =>
        Assert.Equal(invariants, DomainUnits.InvariantsPathFor(domain));

    [Fact]
    public void Each_rule_is_its_claim_marked_by_its_proof()
    {
        var rules = RulesOf("order").SelectMany(group => group.Rules).ToList();

        // The claim is the first sentence alone: the reason after it is the chapter's.
        Assert.Equal(Claims.Order(StringComparer.Ordinal), rules.Select(rule => rule.Claim).Order(StringComparer.Ordinal));
        var byTitle = rules.ToDictionary(rule => rule.Title);
        Assert.Equal(DomainRuleProof.Proved, byTitle["An order is placed once"].Proof);
        Assert.Equal(DomainRuleProof.Proved, byTitle["Lines change through the order"].Proof);
        Assert.Equal(DomainRuleProof.Unproved, byTitle["A line has a quantity"].Proof);
        Assert.Equal(DomainRuleProof.Open, byTitle["A cancelled order stays on record"].Proof);
        Assert.Equal("`Place()`", byTitle["An order is placed once"].EnforcedAt);
    }

    private static readonly string[] Claims =
    [
        "A placed order cannot be placed again.",
        "Every order line holds one or more of its product.",
        "Cancelling an order keeps it.",
        "A line changes only through its order.",
        "The total is the sum of the lines."
    ];

    [Fact]
    public void Rules_group_by_the_concept_their_enforced_at_names_falling_back_to_the_aggregate()
    {
        var groups = RulesOf("order");

        Assert.Equal(["Order", "Order Line"], groups.Select(group => group.Title));
        Assert.Equal(["aggregate", "entity"], groups.Select(group => group.Type));
        Assert.Equal([DomainPath + "#order", DomainPath + "#order-line"], groups.Select(group => group.Reference));

        // A method of the aggregate, an open line and a type the domain does not
        // name all fall back to the aggregate.
        Assert.Equal(
            ["An order is placed once", "A cancelled order stays on record", "The total is worked out"],
            groups[0].Rules.Select(rule => rule.Title));
        Assert.Equal(
            ["A line has a quantity", "Lines change through the order"],
            groups[1].Rules.Select(rule => rule.Title));
    }

    [Fact]
    public void The_older_invariants_md_spelling_is_read_too()
    {
        var older = DomainUnits.Parse(Invariants, ".devbook/domain/ordering/invariants.md");

        Assert.Equal(["Order", "Order Line"], RulesOf("order", null, older).Select(group => group.Title));
    }

    [Fact]
    public void An_aggregate_with_no_invariants_chapter_has_no_rules()
    {
        var file = DomainUnits.Parse("""
            # Billing

            ## Invoice

            ```meta
            type: aggregate
            ```

            A bill.
            """, ".devbook/domain/billing/domain.md");

        Assert.Empty(DomainUnits.RulesOf(DomainUnits.Of(file)[0], file, [Rules], [file]));
    }

    [Fact]
    public void A_split_page_reads_every_rule_of_its_own_invariants_file()
    {
        var split = DomainUnits.Parse("""
            # Order

            ## Order

            ```meta
            type: aggregate
            ```

            A customer's request for goods.
            """, ".devbook/domain/ordering/domain.order.md");
        var rules = DomainUnits.Parse("""
            # Order invariants

            ## The rules

            ### Invariant: An order is placed once

            ```meta
            type: invariant
            ```

            A placed order cannot be placed again.

            Enforced at: `Place()`
            """, ".devbook/domain/ordering/domain.order.invariants.md");
        // The unsplit file's rules are another file's and stay off the split page.
        var groups = DomainUnits.RulesOf(DomainUnits.Of(split)[0], split, [Rules, rules], [split]);

        var group = Assert.Single(groups);
        Assert.Equal("Order", group.Title);
        Assert.Equal(["An order is placed once"], group.Rules.Select(rule => rule.Title));
    }

    [Fact]
    public void A_shared_grouping_with_rules_is_a_unit_of_the_base_page()
    {
        var units = DomainUnits.Of(Page, [Rules]);

        Assert.Equal(["order", "pricing", "shared-value-objects", "shared-enums"], units.Select(unit => unit.Key));
        Assert.Equal(DomainUnitKind.Shared, units[2].Kind);
        Assert.Equal("Shared Value Objects", units[2].Label);

        // Without its rules a grouping stays off the bar, as it always has.
        Assert.Equal(["order", "pricing"], DomainUnits.Of(Page).Select(unit => unit.Key));
    }

    [Fact]
    public void A_shared_grouping_s_rules_group_under_the_value_or_enum_they_are_about()
    {
        var values = RulesOf("shared-value-objects");
        var money = Assert.Single(values);
        Assert.Equal("Money", money.Title);
        Assert.Equal("value-object", money.Type);
        Assert.Equal(["An amount has one currency"], money.Rules.Select(rule => rule.Title));

        var enums = Assert.Single(RulesOf("shared-enums"));
        Assert.Equal("Currency", enums.Title);
        Assert.Equal("enum", enums.Type);
    }

    /// <summary>An Order whose rules the chapter groups itself: two groups, each
    /// pointing at the concept it is about, and one rule left outside them.</summary>
    internal const string GroupedInvariants = """
        # Invariants

        ```meta
        type: invariants
        ```

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

        #### Invariant: An order is placed once

        ```meta
        type: invariant
        tests: [unit:dotnet:Ordering.OrderTests.Placing_twice_is_refused]
        ```

        A placed order cannot be placed again.

        Enforced at: `Place()`

        #### Invariant: A cancelled order stays on record

        ```meta
        type: invariant
        ```

        Cancelling an order keeps it.

        Enforced at: open

        ### Lines and money

        ```meta
        type: invariants
        related: [.devbook/domain/ordering/domain.md#order, .devbook/domain/ordering/domain.md#order-line, .devbook/domain/ordering/domain.md#money]
        ```

        #### Invariant: A line has a quantity

        ```meta
        type: invariant
        ```

        Every order line holds one or more of its product.

        Enforced at: `OrderLine.SetQuantity()`

        ### Invariant: The total is worked out

        ```meta
        type: invariant
        ```

        The total is the sum of the lines.

        Enforced at: `PriceCalculator.Total()`
        """;

    [Fact]
    public void Rules_under_a_group_heading_read_as_that_group_and_link_what_it_is_about()
    {
        var grouped = DomainUnits.Parse(GroupedInvariants, InvariantsPath);
        var groups = RulesOf("order", null, grouped);

        // The groups the chapter wrote come in its order; a rule outside every
        // group keeps the concept its Enforced at: line names, the aggregate here.
        Assert.Equal(["How an order moves", "Lines and money", "Order"], groups.Select(group => group.Title));
        Assert.Equal(["An order is placed once", "A cancelled order stays on record"], groups[0].Rules.Select(rule => rule.Title));
        Assert.Equal(["A line has a quantity"], groups[1].Rules.Select(rule => rule.Title));
        Assert.Equal(["The total is worked out"], groups[2].Rules.Select(rule => rule.Title));

        // A written group is no model element: it has no marker, and its heading is
        // its words. What it is about is linked beside it, the aggregate every
        // group names left out, by the chapter's own
        // heading where the domain names it and by its anchor where it does not.
        Assert.Null(groups[0].Type);
        Assert.Equal(
            [("Order lifecycle", ".devbook/domain/ordering/flow.md#order-lifecycle")],
            groups[0].Links.Select(link => (link.Text, link.Reference)));
        Assert.Equal(
            [("Order Line", ".devbook/domain/ordering/domain.md#order-line"), ("Money", ".devbook/domain/ordering/domain.md#money")],
            groups[1].Links.Select(link => (link.Text, link.Reference)));
        Assert.Equal("aggregate", groups[2].Type);
        Assert.Empty(groups[2].Links);
    }

    [Fact]
    public void A_rule_with_no_claim_reads_its_heading()
    {
        var rules = DomainUnits.Parse("""
            # Invariants

            ## Order

            ### Invariant: An order has a customer

            ```meta
            type: invariant
            ```

            Enforced at: constructor
            """, InvariantsPath);

        var rule = Assert.Single(RulesOf("order", null, rules).SelectMany(group => group.Rules));
        Assert.Equal("An order has a customer", rule.Claim);
        Assert.Equal("constructor", rule.EnforcedAt);
    }
}
