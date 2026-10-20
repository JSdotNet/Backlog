namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// How the Domain view reads a <c>.domain</c> file into units (local ADR 0037
/// §4–6): which chapters the chapter bar offers, what an aggregate's About tab
/// lists, which events a unit raises and which requirements a domain service is
/// held to — all from the chapters' own headings, first sentences and
/// <c>related</c> fields.
/// </summary>
public sealed class DomainUnitsTests
{
    internal const string DomainPath = ".devbook/domain/ordering/domain.md";
    internal const string RequirementsPath = ".devbook/domain/ordering/requirements.md";

    internal const string Domain = """
        # Ordering

        ```meta
        type: domain
        ```

        > One chapter per aggregate.

        ## Order

        ```meta
        type: aggregate
        related: [.devbook/domain/ordering/domain.invariants.md#order]
        ```

        A customer's request for goods, and the boundary for its lines. It is
        placed once and paid once.

        ```annotation
        id: n1
        note: Is "placed" the right word?
        ```

        ### Lifecycle notes

        An order is never deleted, only cancelled.

        ### Order Line

        ```meta
        type: entity
        aliases: [order line, OrderLine]
        ```

        One product and its quantity on the order. It has identity within the
        order.

        ### Money

        ```meta
        type: value-object
        ```

        An amount in a [currency](#currency), compared by value.

        ### Order Status

        ```meta
        type: enum
        ```

        Where the order is: `draft`, `placed`, `paid`, `cancelled`. A `cancelled` order stays.

        ## Pricing

        ```meta
        type: domain-service
        related: [.devbook/domain/ordering/requirements.md#pricing]
        ```

        Works out what an order costs. It is a service because it reads the price
        list as well as the order.

        ## OrderPlaced

        ```meta
        type: domain-event
        related: [.devbook/domain/ordering/domain.md#order]
        ```

        Published when an `Order` is placed.

        ### Payload

        - `order_id` - the order.

        ### Consumers

        - Billing, which raises the invoice.
        - [Shipping](../shipping/domain.md#shipment) — picks the goods.

        ## PriceChanged

        ```meta
        type: domain-event
        related: [domain.md#pricing]
        ```

        Published when a price list entry moves.

        ## Ubiquitous Language

        ```meta
        type: ubiquitous-language
        ```

        ### Customer

        ```meta
        type: term
        ```

        Whoever places the order.
        """;

    internal const string Requirements = """
        # Ordering requirements

        ## Pricing

        ```meta
        type: requirements
        ```

        ### Requirement: A price is never negative

        ```meta
        type: requirement
        ```

        The system SHALL price no order below zero.

        ### Requirement: Discounts apply once

        ```meta
        type: requirement
        ```

        The system SHALL apply a discount once.

        ## Checkout

        ```meta
        type: requirements
        ```

        ### Requirement: A total is shown before paying

        ```meta
        type: requirement
        related: [.devbook/domain/ordering/domain.md#pricing]
        ```

        The system SHALL show the total.

        ### Requirement: An order is placed once

        ```meta
        type: requirement
        ```

        The system SHALL place an order once.
        """;

    private static DomainChapterFile File => DomainUnits.Parse(Domain, DomainPath);

    [Fact]
    public void The_chapter_bar_offers_aggregates_services_and_the_glossary_in_file_order()
    {
        var units = DomainUnits.Of(File);

        Assert.Equal(["Order", "Pricing", "Glossary"], units.Select(unit => unit.Label));
        Assert.Equal([DomainUnitKind.Aggregate, DomainUnitKind.DomainService, DomainUnitKind.Glossary], units.Select(unit => unit.Kind));
        Assert.Equal(["aggregate", "domain-service", "ubiquitous-language"], units.Select(unit => unit.Type));
        Assert.Equal(".devbook/domain/ordering/domain.md#order", units[0].Reference);
    }

    [Fact]
    public void Prose_keeps_untyped_subchapters_and_drops_meta_annotations_and_typed_children()
    {
        var order = DomainUnits.Of(File)[0].Chapter;

        Assert.Contains("A customer's request for goods", order.Prose);
        Assert.Contains("### Lifecycle notes", order.Prose);
        Assert.DoesNotContain("```meta", order.Prose);
        Assert.DoesNotContain("annotation", order.Prose);
        Assert.DoesNotContain("Order Line", order.Prose);
        Assert.DoesNotContain("`draft`", order.Prose);
    }

    [Fact]
    public void A_summary_is_the_first_sentence_as_plain_text()
    {
        var order = DomainUnits.Of(File)[0].Chapter;

        Assert.Equal("A customer's request for goods, and the boundary for its lines.", order.Summary);
    }

    [Fact]
    public void Members_are_the_owned_entities_and_value_objects_with_a_model_name()
    {
        var members = DomainUnits.Members(DomainUnits.Of(File)[0].Chapter);

        Assert.Equal(
            [
                new DomainMember("Order Line", "entity", "One product and its quantity on the order.", "OrderLine"),
                new DomainMember("Money", "value-object", "An amount in a currency, compared by value.", "Money")
            ],
            members);
    }

    [Fact]
    public void An_enum_lists_the_values_its_first_sentence_names()
    {
        var status = Assert.Single(DomainUnits.Enums(DomainUnits.Of(File)[0].Chapter));

        Assert.Equal("Order Status", status.Title);
        Assert.Equal(["draft", "placed", "paid", "cancelled"], status.Values);
    }

    [Fact]
    public void Events_are_the_domain_events_whose_related_names_the_unit_with_their_consumers()
    {
        var file = File;
        var units = DomainUnits.Of(file);

        var raised = Assert.Single(DomainUnits.EventsRaisedBy(units[0].Reference, [file]));
        Assert.Equal("OrderPlaced", raised.Name);
        Assert.Equal("Published when an Order is placed.", raised.Summary);
        Assert.Equal(["Billing", "Shipping"], raised.Consumers);

        // A relative `related` resolves against the event's own file.
        var priced = Assert.Single(DomainUnits.EventsRaisedBy(units[1].Reference, [file]));
        Assert.Equal("PriceChanged", priced.Name);
        Assert.Empty(priced.Consumers);
    }

    [Fact]
    public void A_reference_matches_whichever_folder_spelling_either_side_uses()
    {
        var legacy = DomainUnits.Parse(Domain, ".domain/ordering/domain.md");

        var raised = DomainUnits.EventsRaisedBy(".devbook/domain/ordering/domain.md#order", [legacy]);

        Assert.Equal("OrderPlaced", Assert.Single(raised).Name);
    }

    [Fact]
    public void A_service_counts_on_the_requirements_naming_it_and_those_it_names()
    {
        var pricing = DomainUnits.Of(File)[1];
        var requirements = DomainUnits.Parse(Requirements, RequirementsPath);

        var guarantees = DomainUnits.Guarantees(pricing, [requirements]);

        Assert.Equal(
            [
                new DomainGuarantee("A price is never negative", ".devbook/domain/ordering/requirements.md#requirement-a-price-is-never-negative"),
                new DomainGuarantee("Discounts apply once", ".devbook/domain/ordering/requirements.md#requirement-discounts-apply-once"),
                new DomainGuarantee("A total is shown before paying", ".devbook/domain/ordering/requirements.md#requirement-a-total-is-shown-before-paying")
            ],
            guarantees);
    }

    [Fact]
    public void The_glossary_lists_its_terms()
    {
        var glossary = DomainUnits.Of(File)[2].Chapter;

        Assert.Equal([new DomainTerm("Customer", "Whoever places the order.")], DomainUnits.Terms(glossary));
    }

    [Fact]
    public void A_heading_inside_a_code_fence_is_not_a_chapter()
    {
        var file = DomainUnits.Parse("""
            # X

            ## Real

            ```meta
            type: aggregate
            ```

            ```markdown
            ## Not a chapter
            ```
            """, DomainPath);

        Assert.Equal(["Real"], file.Chapters.Select(chapter => chapter.Title));
    }

    /// <summary>The real shape in sessions/domain.md: a domain service written as a
    /// sub-chapter of an aggregate is a unit of its own, not one of the aggregate's
    /// cards, and an enum listing its values as bullets still has them.</summary>
    [Fact]
    public void A_service_nested_under_an_aggregate_is_its_own_unit_and_a_bulleted_enum_keeps_its_values()
    {
        var file = DomainUnits.Parse("""
            # Sessions

            ## Session Log

            ```meta
            type: aggregate
            ```

            The record of a session, e.g. one run of an agent. It is kept.

            ### Recording

            ```meta
            type: domain-service
            ```

            Writes a run down.

            ### Session State

            ```meta
            type: enum
            ```

            How far along a session is:

            - `running` - still going.
            - `ended` - done.
            """, ".devbook/domain/sessions/domain.md");

        var units = DomainUnits.Of(file);
        Assert.Equal(["Session Log", "Recording"], units.Select(unit => unit.Label));
        Assert.Equal("The record of a session, e.g. one run of an agent.", units[0].Chapter.Summary);
        Assert.Empty(DomainUnits.Members(units[0].Chapter));
        Assert.Equal(["running", "ended"], Assert.Single(DomainUnits.Enums(units[0].Chapter)).Values);
        Assert.DoesNotContain("Writes a run down", units[0].Chapter.Prose);
    }

    [Fact]
    public void A_range_of_values_reads_as_its_sentence_and_the_model_name_prefers_the_heading()
    {
        var file = DomainUnits.Parse("""
            # Tasks

            ## Task

            ```meta
            type: aggregate
            ```

            A piece of work.

            ### Recurrence

            ```meta
            type: value-object
            aliases: [Repeat, recurrence]
            ```

            How often a task repeats.

            ### Weekday

            ```meta
            type: enum
            ```

            A day of the week, `monday` through `sunday`. Used for weekly repeats.
            """, DomainPath);

        var task = DomainUnits.Of(file)[0].Chapter;
        Assert.Equal("Recurrence", Assert.Single(DomainUnits.Members(task)).ModelName);
        var weekday = Assert.Single(DomainUnits.Enums(task));
        Assert.Empty(weekday.Values);
        Assert.Equal("A day of the week, monday through sunday.", weekday.Summary);
    }

    [Fact]
    public void Two_units_of_one_heading_get_two_keys()
    {
        var file = DomainUnits.Parse("# X\n\n## Order\n\n```meta\ntype: aggregate\n```\n\nOne.\n\n## Order\n\n```meta\ntype: domain-service\n```\n\nTwo.\n", DomainPath);

        Assert.Equal(["order", "order-1"], DomainUnits.Of(file).Select(unit => unit.Key));
    }

    [Theory]
    [InlineData("Requirement: The tick spawns the next occurrence", "requirement-the-tick-spawns-the-next-occurrence")]
    [InlineData("Sub-Item", "sub-item")]
    [InlineData("Unlinking leaves the task's repositories", "unlinking-leaves-the-tasks-repositories")]
    public void Anchors_follow_githubs_rule(string heading, string anchor) =>
        Assert.Equal(anchor, DomainUnits.Slug(heading));
}
