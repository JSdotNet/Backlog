namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The Features page of the Domain view (local ADR 0037), read from the
/// chapters as written: columns by <c>depends-on</c> depth, a card's sub-features,
/// flag or setting and requirements with how many are proved, a proposed card
/// dashed and a deprecated one at the end of its column, and the detail a pick
/// opens. A split <c>features.&lt;name&gt;.md</c> reads the same way.
/// </summary>
public sealed class DomainFeaturesTests
{
    internal const string FeaturesPath = ".devbook/domain/ordering/features.md";

    internal const string Features = """
        # Features

        ```meta
        type: features
        ```

        ## Fax orders

        ```meta
        type: feature
        status: deprecated
        ```

        Send an order by fax.

        ## Ordering

        ```meta
        type: feature
        status: draft
        related: [.devbook/domain/ordering/domain.md#order, .devbook/arc42/adr/0001-x.md]
        ```

        Place an order for goods. It is paid once.

        ## Checkout

        ```meta
        type: feature
        status: draft
        feature-flag: .devbook/domain/ordering/context.md#new-checkout
        depends-on: [.devbook/domain/ordering/features.md#ordering, .devbook/domain/billing/features.md#invoicing]
        related: [.devbook/domain/ordering/domain.md#order, .devbook/domain/ordering/model.md#order, .devbook/domain/ordering/domain.invariants.md#order]
        ```

        Pay for an order in one step.

        ### Card payment

        ```meta
        type: sub-feature
        ```

        Pay by card.

        ### Invoice

        ```meta
        type: sub-feature
        ```

        Pay later.

        ## Quotes

        ```meta
        type: feature
        status: proposed
        setting: .devbook/domain/ordering/context.md#quote-validity
        depends-on: [.devbook/domain/ordering/features.md#checkout]
        ```

        Ask for a price first.
        """;

    internal const string Context = """
        # Ordering

        ```meta
        type: context
        ```

        ## New checkout

        ```meta
        type: feature-flag
        ```

        Turns checkout on.
        """;

    internal const string Domain = """
        # Ordering

        ```meta
        type: domain
        ```

        ## Order

        ```meta
        type: aggregate
        ```

        A customer's request for goods.
        """;

    internal const string Invariants = """
        # Invariants

        ```meta
        type: invariants
        ```

        ## Order

        ```meta
        type: invariants
        related: [.devbook/domain/ordering/domain.md#order]
        ```

        ### Invariant: A paid order is not paid again

        ```meta
        type: invariant
        tests: [unit:dotnet:Ordering.Tests.A_paid_order_is_refused]
        ```

        A paid order refuses a second payment.

        Enforced at: `Order.Pay()`

        ### Invariant: An order holds a line

        ```meta
        type: invariant
        ```

        An order is never placed empty.

        Enforced at: `Order.Place()`
        """;

    internal const string Requirements = """
        # Requirements

        ```meta
        type: requirements
        ```

        ## Checkout

        ```meta
        type: requirements
        related: [.devbook/domain/ordering/features.md#checkout]
        ```

        > Paying in one step. The rules an order keeps are its
        > [invariants](domain.invariants.md#order).

        ### Requirement: A paid order is not paid again

        ```meta
        type: requirement
        tests: [unit:dotnet:Ordering.Tests.A_paid_order_is_refused]
        ```

        The system SHALL refuse a second payment for a paid order.

        #### Scenario: Paying twice

        - **Given** an order that is paid and a card that is valid
        - **When** the customer pays again
        - **Then** the payment is refused, and the order stays paid

        ### Requirement: Checkout shows the total

        ```meta
        type: requirement
        ```

        The system SHALL show the total before the customer pays.

        ## Ordering

        ```meta
        type: requirements
        ```

        ### Requirement: An order is placed once

        ```meta
        type: requirement
        related: [.devbook/domain/ordering/features.md#ordering]
        tests: [unit:dotnet:Ordering.Tests.Placing_twice_is_refused]
        ```

        The system SHALL place an order once.
        """;

    internal static DomainChapterFile FeaturesFile => DomainUnits.Parse(Features, FeaturesPath);

    internal static IReadOnlyList<DomainChapterFile> Files =>
    [
        DomainUnits.Parse(Context, ".devbook/domain/ordering/context.md"),
        DomainUnits.Parse(Domain, ".devbook/domain/ordering/domain.md"),
        DomainUnits.Parse(Invariants, ".devbook/domain/ordering/domain.invariants.md"),
        FeaturesFile,
        DomainUnits.Parse(Requirements, ".devbook/domain/ordering/requirements.md")
    ];

    private static DomainFeature Feature(string key) =>
        DomainFeatures.Read(FeaturesFile, Files).Single(feature => feature.Key == key);

    [Fact]
    public void Features_stand_in_columns_by_what_they_build_on_in_the_context()
    {
        var columns = DomainFeatures.Columns(FeaturesFile, Files);

        Assert.Equal([0, 1, 2], columns.Select(column => column.Depth));
        Assert.Equal(["ordering", "fax-orders"], columns[0].Features.Select(feature => feature.Key));
        Assert.Equal(["checkout"], columns[1].Features.Select(feature => feature.Key));
        Assert.Equal(["quotes"], columns[2].Features.Select(feature => feature.Key));
    }

    [Fact]
    public void A_deprecated_feature_goes_to_the_end_of_its_column_and_a_proposed_one_says_so()
    {
        var column = DomainFeatures.Columns(FeaturesFile, Files)[0];

        Assert.True(column.Features[^1].IsDeprecated);
        Assert.True(Feature("quotes").IsProposed);
        Assert.False(Feature("ordering").IsProposed);
    }

    [Fact]
    public void A_card_counts_its_sub_features_and_names_its_flag_or_setting()
    {
        var checkout = Feature("checkout");
        Assert.Equal(["Card payment", "Invoice"], checkout.SubFeatures.Select(sub => sub.Title));
        Assert.Equal(DomainFeatureGateKind.Flag, checkout.Gate!.Kind);
        Assert.Equal("New checkout", checkout.Gate.Chapter.Title);

        var quotes = Feature("quotes");
        Assert.Equal(DomainFeatureGateKind.Setting, quotes.Gate!.Kind);
        Assert.Equal("Quote validity", quotes.Gate.Chapter.Title);
        Assert.Null(Feature("ordering").Gate);
    }

    [Fact]
    public void A_card_counts_its_requirements_and_how_many_are_proved_from_their_tests()
    {
        Assert.Equal(new DomainProofTally(2, 1), Feature("checkout").Requirements);
        Assert.Equal("ordering/requirements.md#checkout", DomainUnits.Normalize(Feature("checkout").RequirementsGroup!.Reference));
        Assert.Equal(new DomainProofTally(1, 1), Feature("ordering").Requirements);
        Assert.Equal(DomainProofTally.None, Feature("quotes").Requirements);
    }

    [Fact]
    public void The_detail_names_what_it_builds_on_and_the_model_chapters_its_related_names()
    {
        var checkout = Feature("checkout");

        Assert.Equal(["Ordering", "Invoicing"], checkout.BuildsOn.Select(chapter => chapter.Title));
        Assert.Equal([true, false], checkout.BuildsOn.Select(chapter => chapter.Known));
        Assert.Equal(".devbook/domain/ordering/features.md#ordering", checkout.BuildsOn[0].Reference);
        Assert.Equal(["Order"], checkout.ModelChapters.Select(chapter => chapter.Title));
        Assert.Equal(["Order"], Feature("ordering").ModelChapters.Select(chapter => chapter.Title));
    }

    [Fact]
    public void A_summary_is_the_features_first_sentence()
    {
        Assert.Equal("Place an order for goods.", Feature("ordering").Summary);
    }

    [Fact]
    public void A_split_file_reads_its_one_feature_with_the_depth_the_base_file_gives_it()
    {
        var split = DomainUnits.Parse("""
            # Gift cards

            ```meta
            type: features
            ```

            ## Gift cards

            ```meta
            type: feature
            depends-on: [.devbook/domain/ordering/features.md#checkout]
            ```

            Pay with a gift card.
            """, ".devbook/domain/ordering/features.gift-cards.md");

        var column = Assert.Single(DomainFeatures.Columns(split, [.. Files, split]));
        Assert.Equal(2, column.Depth);
        Assert.Equal("Gift cards", Assert.Single(column.Features).Title);
    }

    [Fact]
    public void A_cycle_of_depends_on_stops_where_it_closes()
    {
        var file = DomainUnits.Parse("""
            # Features

            ## A

            ```meta
            type: feature
            depends-on: [features.md#b]
            ```

            ## B

            ```meta
            type: feature
            depends-on: [features.md#a]
            ```
            """, ".devbook/domain/x/features.md");

        var features = DomainFeatures.Read(file, [file]);
        Assert.Equal(2, features.Count);
        Assert.All(features, feature => Assert.InRange(feature.Depth, 0, 1));
    }
}
