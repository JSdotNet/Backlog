namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The Requirements page of the Domain view (local ADR 0036), read from the
/// chapters as written: per feature, each requirement's SHALL sentence and proof,
/// each scenario as Given / When / Then rows with a ", and" clause split apart,
/// and an Always true link where the text explained invariants.
/// </summary>
public sealed class DomainRequirementsTests
{
    private const string RequirementsPath = ".devbook/domain/ordering/requirements.md";

    private static DomainChapterFile RequirementsFile => DomainUnits.Parse(DomainFeaturesTests.Requirements, RequirementsPath);

    private static IReadOnlyList<DomainRequirementGroup> Groups => DomainRequirements.Groups(RequirementsFile, DomainFeaturesTests.Files);

    [Fact]
    public void A_requirements_file_reads_one_group_per_feature_with_its_tally()
    {
        Assert.Equal(["checkout", "ordering"], Groups.Select(group => group.Key));
        Assert.Equal("Checkout", Groups[0].Feature!.Title);
        Assert.Equal(new DomainProofTally(2, 1), Groups[0].Tally);
    }

    [Fact]
    public void A_requirement_reads_its_shall_sentence_and_whether_a_test_proves_it()
    {
        var requirement = Groups[0].Requirements[0];

        Assert.Equal("A paid order is not paid again", requirement.Title);
        Assert.Equal("The system SHALL refuse a second payment for a paid order.", requirement.Shall);
        Assert.True(requirement.Proved);
        Assert.Equal(["unit:dotnet:Ordering.Tests.A_paid_order_is_refused"], requirement.Tests);
        Assert.False(Groups[0].Requirements[1].Proved);
    }

    [Fact]
    public void A_scenario_reads_as_given_when_then_rows_with_an_and_clause_split_into_bullets()
    {
        var scenario = Assert.Single(Groups[0].Requirements[0].Scenarios);

        Assert.Equal("Paying twice", scenario.Title);
        Assert.Equal(["Given", "When", "Then"], scenario.Steps.Select(step => step.Keyword));
        Assert.Equal(["an order that is paid and a card that is valid"], scenario.Steps[0].Clauses);
        Assert.Equal(["the payment is refused", "the order stays paid"], scenario.Steps[2].Clauses);
    }

    [Fact]
    public void An_and_row_joins_the_row_above_it()
    {
        var chapter = DomainUnits.Parse("""
            # R

            ## G

            ### Requirement: X

            The system SHALL x.

            #### Scenario: S

            - **Given** a thing
            - **And** another thing
            - **When** it runs
            - **Then** it is done
            """, RequirementsPath).Chapters[0].Children[0].Children[0];

        var scenario = DomainRequirements.Scenario(chapter);
        Assert.Equal(["Given", "When", "Then"], scenario.Steps.Select(step => step.Keyword));
        Assert.Equal(["a thing", "another thing"], scenario.Steps[0].Clauses);
    }

    [Fact]
    public void A_clause_is_not_split_inside_a_code_span_or_emphasis()
    {
        Assert.Equal(["`a, and b`"], DomainRequirements.SplitClauses("`a, and b`"));
        Assert.Equal(["*a, and b* shows"], DomainRequirements.SplitClauses("*a, and b* shows"));
        Assert.Equal(["x", "y", "z"], DomainRequirements.SplitClauses("x, and y, and z"));
    }

    [Fact]
    public void A_shall_sentence_splits_around_its_first_shall_outside_code()
    {
        Assert.Equal(("The system ", " refuse a payment."), DomainRequirements.SplitShall("The system SHALL refuse a\npayment."));
        Assert.Null(DomainRequirements.SplitShall("The system **SHALL** refuse."));
        Assert.Null(DomainRequirements.SplitShall("The system _SHALL_ refuse."));
        Assert.Equal(("[x](a-SHALL.md) then ", "."), DomainRequirements.SplitShall("[x](a-SHALL.md) then SHALL."));
        Assert.Null(DomainRequirements.SplitShall("    The system SHALL be code."));
        Assert.Equal(("`SHALL` here and *SHALL* there, but ", " this."), DomainRequirements.SplitShall("`SHALL` here and *SHALL* there, but SHALL this."));
        Assert.Equal(("[SHALL](a.md) ", " go."), DomainRequirements.SplitShall("[SHALL](a.md) SHALL go."));
        Assert.Null(DomainRequirements.SplitShall("The system SHALLOW stays whole."));
        Assert.Null(DomainRequirements.SplitShall("- The system SHALL be a list."));
        Assert.Null(DomainRequirements.SplitShall("> The system SHALL be quoted."));
    }

    [Fact]
    public void A_sentence_explaining_invariants_gives_way_to_the_units_always_true_link()
    {
        var checkout = Groups[0];

        Assert.Equal("> Paying in one step.", checkout.Intro);
        var link = Assert.Single(checkout.AlwaysTrue);
        Assert.Equal("Order", link.Title);
        Assert.Equal(".devbook/domain/ordering/domain.md", link.DocumentPath);
        Assert.Equal("order", link.UnitKey);
    }

    [Fact]
    public void A_group_with_no_invariants_explained_or_aggregate_named_links_no_unit()
    {
        Assert.Empty(Groups[1].AlwaysTrue);
    }

    [Fact]
    public void A_sentence_naming_rules_no_unit_here_holds_stays()
    {
        var file = DomainUnits.Parse("""
            # Requirements

            ## Billing

            ```meta
            type: requirements
            ```

            > Paying. The rules are the ledger's
            > [invariants](../billing/domain.invariants.md#ledger).

            - A list that names invariants keeps its shape.

            ### Requirement: X

            The system SHALL x.
            """, RequirementsPath);

        var group = Assert.Single(DomainRequirements.Groups(file, DomainFeaturesTests.Files));
        Assert.Empty(group.AlwaysTrue);
        Assert.Contains("invariants", group.Intro, StringComparison.Ordinal);
        Assert.Equal("- A list that names invariants keeps its shape.", DomainRequirements.WithoutInvariantExplanations("- A list that names invariants keeps its shape."));
    }

    [Fact]
    public void A_split_requirements_file_reads_its_one_feature()
    {
        var text = DomainFeaturesTests.Requirements.Replace("# Requirements", "# Checkout");
        var split = DomainUnits.Parse(text[..text.IndexOf("## Ordering", StringComparison.Ordinal)], ".devbook/domain/ordering/requirements.checkout.md");

        var group = Assert.Single(DomainRequirements.Groups(split, DomainFeaturesTests.Files));
        Assert.Equal("Checkout", group.Title);
        Assert.Equal(2, group.Requirements.Count);
    }

    [Fact]
    public void Requirements_written_straight_under_the_title_are_one_group()
    {
        var file = DomainUnits.Parse("""
            # Checkout

            ## Requirement: One

            The system SHALL one.

            ## Requirement: Two

            The system SHALL two.
            """, ".devbook/domain/ordering/requirements.checkout.md");

        var group = Assert.Single(DomainRequirements.Groups(file, []));
        Assert.Equal("Checkout", group.Title);
        Assert.Equal(["One", "Two"], group.Requirements.Select(requirement => requirement.Title));
    }
}
