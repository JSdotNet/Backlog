namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The Requirements page of the Domain view (local ADR 0044), read from the
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

    private const string PlanningDomain = """
        # Planning

        ```meta
        type: domain
        ```

        ## Task

        ```meta
        type: aggregate
        ```

        A piece of work.

        ### Agenda Time

        ```meta
        type: value-object
        ```

        When a task is worked on today.

        ## Note

        ```meta
        type: aggregate
        ```

        A thing written down.

        ### Margin

        ```meta
        type: value-object
        ```

        The space beside it.

        ## Ubiquitous Language

        ```meta
        type: ubiquitous-language
        ```

        ### My Day

        ```meta
        type: term
        ```

        The tasks picked for today.
        """;

    private const string PlanningInvariants = """
        # Invariants

        ```meta
        type: invariants
        ```

        ## Task

        ```meta
        type: invariants
        related: [.devbook/domain/planning/domain.md#task]
        ```

        ### My Day and its agenda time

        ```meta
        type: invariants
        related: [.devbook/domain/planning/domain.md#task, .devbook/domain/planning/domain.md#my-day, .devbook/domain/planning/flow.md#task-lifecycle]
        ```

        #### Invariant: A task outside My Day holds no agenda time

        ```meta
        type: invariant
        ```

        A task with no My Day date has no agenda time.

        #### Invariant: An agenda duration is positive

        ```meta
        type: invariant
        ```

        A duration is more than zero minutes.
        """;

    private static IReadOnlyList<DomainRequirementGroup> PlanningGroups(string related)
    {
        var requirements = DomainUnits.Parse($"""
            # Requirements

            ```meta
            type: requirements
            ```

            ## My Day

            ```meta
            type: requirements
            related: [{related}]
            ```

            ### Requirement: Today lists the picked tasks

            The system SHALL list today's tasks.
            """, ".devbook/domain/planning/requirements.md");

        return DomainRequirements.Groups(requirements,
        [
            DomainUnits.Parse(PlanningDomain, ".devbook/domain/planning/domain.md"),
            DomainUnits.Parse(PlanningInvariants, ".devbook/domain/planning/domain.invariants.md"),
            requirements
        ]);
    }

    [Fact]
    public void A_term_an_aggregates_rules_are_about_links_that_aggregate_with_its_rule_count()
    {
        var group = Assert.Single(PlanningGroups(".devbook/domain/planning/domain.md#my-day"));

        var link = Assert.Single(group.AlwaysTrue);
        Assert.Equal("Task", link.Title);
        Assert.Equal("task", link.UnitKey);
        Assert.Equal(2, link.Rules);
    }

    [Fact]
    public void A_chapter_inside_an_aggregate_links_that_aggregate()
    {
        var group = Assert.Single(PlanningGroups(".devbook/domain/planning/domain.md#agenda-time"));

        var link = Assert.Single(group.AlwaysTrue);
        Assert.Equal("task", link.UnitKey);
        Assert.Equal(2, link.Rules);
    }

    [Fact]
    public void What_is_always_true_lists_the_rules_in_place_and_counts_them()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var view = context.Render<DomainRequirementsView>(parameters => parameters
            .Add(p => p.Groups, PlanningGroups(".devbook/domain/planning/domain.md#my-day")));

        var section = view.Find("[data-testid='domain-requirements-always-true']");
        Assert.Equal("2 rules · on Domain", section.QuerySelector("[data-testid='domain-requirements-always-true-meta']")!.TextContent.Trim());
        Assert.Contains("whoever changes the Task.", section.TextContent, StringComparison.Ordinal);
        Assert.Equal(
            ["A task outside My Day holds no agenda time.", "An agenda duration is positive."],
            section.QuerySelectorAll("[data-testid='domain-rule-list-claim']").Select(claim => claim.TextContent.Trim()));
    }

    [Fact]
    public void A_rule_shown_in_place_opens_on_where_it_is_enforced_and_links_to_its_row_on_always_true()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        DomainAlwaysTrueLink? opened = null;
        var view = context.Render<DomainRequirementsView>(parameters => parameters
            .Add(p => p.Groups, PlanningGroups(".devbook/domain/planning/domain.md#my-day"))
            .Add(p => p.OnOpenAlwaysTrue, link => opened = link));

        Assert.Empty(view.FindAll("[data-testid='domain-rule-list-detail']"));
        view.FindAll("[data-testid='domain-rule-list-toggle']")[0].Click();
        Assert.Equal("true", view.FindAll("[data-testid='domain-rule-list-toggle']")[0].GetAttribute("aria-expanded"));
        Assert.Contains("A task with no My Day date has no agenda time.", view.Find("[data-testid='domain-rule-list-detail']").TextContent, StringComparison.Ordinal);

        view.Find("[data-testid='domain-rule-list-open']").Click();

        Assert.Equal("task", opened!.UnitKey);
        Assert.Equal(".devbook/domain/planning/domain.invariants.md#invariant-a-task-outside-my-day-holds-no-agenda-time", opened.Rule);
    }

    [Fact]
    public void A_file_outside_the_domain_its_rules_are_about_links_the_aggregate()
    {
        var group = Assert.Single(PlanningGroups(".devbook/domain/planning/flow.md"));

        Assert.Equal("task", Assert.Single(group.AlwaysTrue).UnitKey);
    }

    [Fact]
    public void A_chapter_inside_an_aggregate_with_no_rules_links_nothing()
    {
        var group = Assert.Single(PlanningGroups(".devbook/domain/planning/domain.md#margin"));

        Assert.Empty(group.AlwaysTrue);
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
