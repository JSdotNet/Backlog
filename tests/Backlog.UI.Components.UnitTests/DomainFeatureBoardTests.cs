namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The Features board and the Requirements view as a reader sees them (local ADR
/// 0036): cards in columns, dashed and struck-through statuses, a detail on pick;
/// requirements with their SHALL sentence and proof, Given / When / Then rows with
/// bullets, and an Always true link the host is told about.
/// </summary>
public sealed class DomainFeatureBoardTests
{
    private static IRenderedComponent<DomainFeatureBoard> Board(BunitContext context, Action<ComponentParameterCollectionBuilder<DomainFeatureBoard>>? more = null)
    {
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context.Render<DomainFeatureBoard>(parameters =>
        {
            parameters.Add(p => p.Columns, DomainFeatures.Columns(DomainFeaturesTests.FeaturesFile, DomainFeaturesTests.Files));
            more?.Invoke(parameters);
        });
    }

    [Fact]
    public void Cards_stand_in_columns_with_their_counts_flag_and_proof()
    {
        using var context = new BunitContext();
        var board = Board(context);

        Assert.Equal(["0", "1", "2"], board.FindAll("[data-testid='domain-features-column']").Select(column => column.GetAttribute("data-depth")));
        var checkout = board.Find("[data-testid='domain-feature-card'][data-key='checkout']");
        Assert.Equal("2 sub-features", checkout.QuerySelector("[data-testid='domain-feature-sub-features']")!.TextContent.Trim());
        Assert.Equal("Flag · New checkout", checkout.QuerySelector("[data-testid='domain-feature-gate']")!.TextContent.Trim());
        Assert.Equal("2 requirements · 1 proved", checkout.QuerySelector("[data-testid='domain-feature-requirements']")!.TextContent.Trim());
    }

    [Fact]
    public void A_proposed_card_is_dashed_and_a_deprecated_one_struck_through_last_in_its_column()
    {
        using var context = new BunitContext();
        var board = Board(context);

        Assert.Contains("domain-feature-card--proposed", board.Find("[data-key='quotes']").ClassList);
        var first = board.FindAll("[data-testid='domain-features-column']")[0].QuerySelectorAll("[data-testid='domain-feature-card']");
        Assert.Equal("fax-orders", first[^1].GetAttribute("data-key"));
        Assert.Contains("domain-feature-card--deprecated", first[^1].ClassList);
        Assert.Equal("Deprecated", first[^1].QuerySelector("[data-testid='domain-feature-status']")!.TextContent.Trim());
    }

    [Fact]
    public void Picking_a_card_opens_its_detail_and_reports_the_pick()
    {
        using var context = new BunitContext();
        string? picked = null;
        var board = Board(context, parameters => parameters.Add(p => p.ActiveFeatureChanged, key => picked = key));

        Assert.Equal("ordering", board.Find("[data-testid='domain-feature-detail']").GetAttribute("data-key"));
        board.Find("[data-key='checkout']").Click();

        Assert.Equal("checkout", picked);
        var detail = board.Find("[data-testid='domain-feature-detail']");
        Assert.Equal("checkout", detail.GetAttribute("data-key"));
        Assert.Contains("Ordering", detail.QuerySelector("[data-testid='domain-feature-builds-on']")!.TextContent);
        Assert.Contains("Invoicing", detail.QuerySelector("[data-testid='domain-feature-builds-on']")!.TextContent);
        Assert.Contains("New checkout", detail.QuerySelector("[data-testid='domain-feature-detail-gate']")!.TextContent);
        Assert.Contains("Order", detail.QuerySelector("[data-testid='domain-feature-model']")!.TextContent);
        Assert.Contains("2 requirements · 1 proved", detail.QuerySelector("[data-testid='domain-feature-detail-requirements']")!.TextContent);
        Assert.Equal("true", board.Find("[data-key='checkout']").GetAttribute("aria-pressed"));
    }

    [Fact]
    public void Following_a_chapter_in_the_detail_hands_its_reference_to_the_host()
    {
        using var context = new BunitContext();
        DevbookReference? followed = null;
        var board = Board(context, parameters => parameters
            .Add(p => p.ActiveFeature, "checkout")
            .Add(p => p.OnNavigate, reference => followed = reference));

        board.Find("[data-testid='domain-feature-builds-on'] button").Click();

        Assert.Equal(".devbook/domain/ordering/features.md", followed!.Path);
        Assert.Equal("ordering", followed.Slug);
    }

    private static IRenderedComponent<DomainRequirementsView> Requirements(BunitContext context, Action<ComponentParameterCollectionBuilder<DomainRequirementsView>>? more = null)
    {
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var file = DomainUnits.Parse(DomainFeaturesTests.Requirements, ".devbook/domain/ordering/requirements.md");
        return context.Render<DomainRequirementsView>(parameters =>
        {
            parameters.Add(p => p.Groups, DomainRequirements.Groups(file, DomainFeaturesTests.Files));
            more?.Invoke(parameters);
        });
    }

    [Fact]
    public void Requirements_read_per_feature_with_the_shall_sentence_and_proof()
    {
        using var context = new BunitContext();
        var view = Requirements(context);

        Assert.Equal(["checkout", "ordering"], view.FindAll("[data-testid='domain-requirements-feature']").Select(item => item.GetAttribute("data-key")));
        var requirements = view.FindAll("[data-testid='domain-requirement']");
        Assert.Equal(["true", "false"], requirements.Select(item => item.GetAttribute("data-proved")));
        Assert.Contains("SHALL refuse a second payment", requirements[0].QuerySelector("[data-testid='domain-requirement-shall']")!.TextContent);
        Assert.Equal("✓ Test linked", requirements[0].QuerySelector("[data-testid='domain-requirement-proof']")!.TextContent.Trim());
        Assert.Equal("No test linked", requirements[1].QuerySelector("[data-testid='domain-requirement-proof']")!.TextContent.Trim());
    }

    [Fact]
    public void A_scenario_reads_as_rows_with_an_and_clause_as_bullets()
    {
        using var context = new BunitContext();
        var view = Requirements(context);

        var steps = view.FindAll("[data-testid='domain-scenario-step']");
        Assert.Equal(["Given", "When", "Then"], steps.Select(step => step.GetAttribute("data-keyword")));
        Assert.Single(steps[0].QuerySelectorAll("[data-testid='domain-scenario-clause']"));
        Assert.Equal(["the payment is refused", "the order stays paid"], steps[2].QuerySelectorAll("li[data-testid='domain-scenario-clause']").Select(clause => clause.TextContent.Trim()));
    }

    [Fact]
    public void The_always_true_link_replaces_the_invariants_sentence_and_tells_the_host()
    {
        using var context = new BunitContext();
        DomainAlwaysTrueLink? opened = null;
        var view = Requirements(context, parameters => parameters.Add(p => p.OnOpenAlwaysTrue, unit => opened = unit));

        Assert.DoesNotContain("invariants", view.Find("[data-testid='domain-requirements-intro']").TextContent, StringComparison.OrdinalIgnoreCase);
        view.Find("[data-testid='domain-requirements-always-true-link']").Click();

        Assert.Equal("order", opened!.UnitKey);
        Assert.Equal(".devbook/domain/ordering/domain.md", opened.DocumentPath);
    }

    [Fact]
    public void Picking_another_feature_shows_its_requirements()
    {
        using var context = new BunitContext();
        var view = Requirements(context);

        view.Find("[data-testid='domain-requirements-feature'][data-key='ordering']").Click();

        Assert.Equal("ordering", view.Find("[data-testid='domain-requirements-group']").GetAttribute("data-key"));
        Assert.Single(view.FindAll("[data-testid='domain-requirement']"));
    }

    [Fact]
    public void An_aggregate_can_be_opened_on_always_true()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var page = context.Render<DomainUnitPage>(parameters => parameters
            .Add(p => p.File, DomainUnits.Parse(DomainFeaturesTests.Domain, ".devbook/domain/ordering/domain.md"))
            .Add(p => p.ActiveUnitPage, DomainUnitPage.AlwaysTruePage));

        Assert.Equal("true", page.Find("[data-testid='domain-unit-always-true-tab']").GetAttribute("aria-selected"));

        page.Find("[data-testid='domain-unit-about-tab']").Click();
        Assert.Equal("true", page.Find("[data-testid='domain-unit-about-tab']").GetAttribute("aria-selected"));
    }

    [Fact]
    public void The_page_opens_on_its_summary_and_sets_the_shall_word_apart()
    {
        using var context = new BunitContext();
        var view = Requirements(context);

        Assert.Equal("3 · 2 tested", view.Find("[data-testid='domain-requirements-tally']").TextContent.Trim());
        Assert.Contains("for Order", view.Find("[data-testid='domain-requirements-always-true-link']").TextContent, StringComparison.Ordinal);
        Assert.Equal(["1 / 2", "1 / 1"], view.FindAll(".domain-requirements__feature-tally [aria-hidden]").Select(tally => tally.TextContent.Trim()));
        Assert.Equal("width: 50%", view.Find(".domain-requirements__feature-fill").GetAttribute("style"));

        var shall = view.Find("[data-testid='domain-requirement-shall']");
        Assert.Equal("SHALL", shall.QuerySelector(".domain-requirement__shall-word")!.TextContent);
        Assert.Equal("The system SHALL refuse a second payment for a paid order.", shall.TextContent.Trim());
        Assert.Contains("domain-requirement__proof--none", view.FindAll("[data-testid='domain-requirement-proof']")[1].ClassName, StringComparison.Ordinal);
    }

    [Fact]
    public void A_long_feature_opens_two_requirements_and_lists_the_rest_as_rows()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var markdown = "# Requirements\n\n## Long\n\n```meta\ntype: requirements\n```\n"
            + string.Concat(Enumerable.Range(1, 7).Select(n => $"\n### Requirement: Rule {n}\n\n```meta\ntype: requirement\n```\n\nThe system SHALL keep rule {n}.\n"))
            + "\n## Short\n\n```meta\ntype: requirements\n```\n\n### Requirement: One rule\n\n```meta\ntype: requirement\n```\n\nThe system SHALL keep one rule.\n";
        var file = DomainUnits.Parse(markdown, ".devbook/domain/ordering/requirements.md");
        var view = context.Render<DomainRequirementsView>(parameters => parameters.Add(p => p.Groups, DomainRequirements.Groups(file, [file])));

        Assert.Equal(2, view.FindAll(".domain-requirements__list [data-testid='domain-requirement']").Count);
        var toggles = view.FindAll("[data-testid='domain-requirement-toggle']");
        Assert.Equal(["Rule 3", "Rule 4", "Rule 5", "Rule 6"], toggles.Select(toggle => toggle.QuerySelector(".domain-requirement__toggle-title")!.TextContent));
        Assert.All(toggles, toggle => Assert.Equal("false", toggle.GetAttribute("aria-expanded")));
        Assert.Equal("and 1 more in Long", view.Find("[data-testid='domain-requirements-more']").TextContent.Trim());
        Assert.Equal(2, view.FindAll("[data-testid='domain-requirement-shall']").Count);

        view.FindAll("[data-testid='domain-requirement-toggle']")[0].Click();
        Assert.Equal("true", view.FindAll("[data-testid='domain-requirement-toggle']")[0].GetAttribute("aria-expanded"));
        Assert.Equal(3, view.FindAll("[data-testid='domain-requirement-shall']").Count);

        view.Find("[data-testid='domain-requirements-more']").Click();
        Assert.Equal(5, view.FindAll("[data-testid='domain-requirement-toggle']").Count);
        Assert.Empty(view.FindAll("[data-testid='domain-requirements-more']"));

        // Another feature opens with its own rows closed; coming back finds these closed too.
        view.Find("[data-testid='domain-requirements-feature'][data-key='short']").Click();
        view.Find("[data-testid='domain-requirements-feature'][data-key='long']").Click();
        Assert.Equal(4, view.FindAll("[data-testid='domain-requirement-toggle']").Count);
        Assert.All(view.FindAll("[data-testid='domain-requirement-toggle']"), toggle => Assert.Equal("false", toggle.GetAttribute("aria-expanded")));
    }
}
