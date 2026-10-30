using Backlog.UI.Components.Diagrams;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The technical half of a context's domain chapters, as the Model page reads it:
/// each domain service's invocation prose, and each domain event as a contract.
/// </summary>
public sealed class DomainChapterContractsTests
{
    private const string Domain = """
        # Ordering

        ```meta
        type: domain
        status: draft
        ```

        ## Order

        ```meta
        type: aggregate
        status: draft
        ```

        An order a customer places.

        ## Pricing

        ```meta
        type: domain-service
        status: draft
        ```

        Prices an order from the catalogue. It is a service because it reads two
        aggregates. Invocation semantics: command-invoked, once per
        checkout.

        ## Delivery

        ```meta
        type: domain-service
        status: draft
        ```

        Hands a paid order to the warehouse, and emits the `OrderShipped` event.

        Invocation semantics:

        - On payment, by the checkout command.
        - Otherwise never.

        ## Audit

        ```meta
        type: domain-service
        status: proposed
        ```

        Keeps a record of every change.

        ## OrderPlaced

        ```meta
        type: domain-event
        status: draft
        related: [.devbook/domain/ordering/domain.md#order]
        ```

        Published when an `Order` is placed.

        ```annotation
        id: n1
        note: not prose
        ```

        ### Payload

        - `order_id` - the order.

        ### Consumers

        - Billing.

        ### Published language rules

        - Consumers do not read the order's lines.

        ## OrderShipped

        ```meta
        type: domain-event
        status: draft
        ```

        Published by `Delivery` when the warehouse takes the order.

        ### Payload

        - `order_id` - the order.
        """;

    [Fact]
    public void A_domain_service_reads_as_its_first_sentence_and_how_it_runs()
    {
        var reading = Read(Domain);

        Assert.Equal(["Pricing", "Delivery", "Audit"], reading.Services.Select(s => s.Title));

        var pricing = reading.Services[0];
        Assert.Equal("Prices an order from the catalogue.", pricing.Summary);
        Assert.Equal("Command-invoked, once per\ncheckout.", pricing.Invocation);

        // A label that ends its line introduces the list beneath it.
        Assert.Equal("- On payment, by the checkout command.\n- Otherwise never.", reading.Services[1].Invocation);

        Assert.Null(reading.Services[2].Invocation);
    }

    [Fact]
    public void A_domain_event_reads_as_a_contract()
    {
        var placed = Read(Domain).Events[0];

        Assert.Equal("OrderPlaced", placed.Title);
        Assert.Equal("Published when an `Order` is placed.", placed.Summary);
        Assert.Equal(["Order"], placed.RaisedBy);
        Assert.Equal("- `order_id` - the order.", placed.Payload);
        Assert.Equal("- Billing.", placed.Consumers);
        Assert.Equal("- Consumers do not read the order's lines.", placed.Rules);
        Assert.Equal(DomainEventDelivery.Documented, placed.Delivery);
        Assert.DoesNotContain("not prose", placed.Summary);
    }

    [Fact]
    public void An_event_its_raiser_says_it_emits_is_emitted()
    {
        var shipped = Read(Domain).Events[1];

        Assert.Equal(["Delivery"], shipped.RaisedBy);
        Assert.Equal(DomainEventDelivery.Emitted, shipped.Delivery);
        Assert.Null(shipped.Consumers);
        Assert.Null(shipped.Rules);
    }

    [Fact]
    public void A_context_that_publishes_no_events_yet_has_only_documented_ones()
    {
        var reading = Read(Domain.Replace("Keeps a record of every change.", "Keeps a record. This context publishes no domain events yet."));

        Assert.All(reading.Events, e => Assert.Equal(DomainEventDelivery.Documented, e.Delivery));
    }

    [Fact]
    public void A_raiser_that_says_the_event_is_not_emitted_yet_keeps_it_documented()
    {
        var reading = Read(Domain.Replace("and emits the `OrderShipped` event.", "and would emit `OrderShipped`; it is not emitted yet."));

        Assert.Equal(DomainEventDelivery.Documented, reading.Events[1].Delivery);
    }

    [Fact]
    public void Chapters_across_a_split_file_are_read_together()
    {
        var reading = DomainChapterContracts.Read(
        [
            new DomainChapterSource(".domain/ordering/domain.md", "# Ordering\n\n## Order\n\n```meta\ntype: aggregate\n```\n\nAn order.\n"),
            new DomainChapterSource(".domain/ordering/domain.billing.md", "# Billing\n\n## Billed\n\n```meta\ntype: domain-event\nrelated: [domain.md#order]\n```\n\nPublished when billed.\n")
        ]);

        Assert.Equal(["Order"], Assert.Single(reading.Events).RaisedBy);
    }

    [Fact]
    public void A_related_link_into_another_context_raises_nothing_here()
    {
        var reading = Read(Domain
            .Replace("related: [.devbook/domain/ordering/domain.md#order]", "related: [.devbook/domain/billing/domain.md#order]")
            .Replace("Published when an `Order` is placed.", "Published when placed."));

        Assert.Empty(reading.Events[0].RaisedBy);
    }

    [Fact]
    public void A_quoted_related_link_still_names_its_raiser()
    {
        var reading = Read(Domain.Replace("related: [.devbook/domain/ordering/domain.md#order]", "related: [\".devbook/domain/ordering/domain.md#order\"]"));

        Assert.Equal(["Order"], reading.Events[0].RaisedBy);
    }

    private static DomainChapterReading Read(string markdown) =>
        DomainChapterContracts.Read([new DomainChapterSource(".domain/ordering/domain.md", markdown)]);
}
