namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The Features board and the Requirements view as a reader sees them (local ADR
/// 0037): cards in columns, dashed and struck-through statuses, a detail on pick;
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

        // The chip shows the chapter's title; a listener also hears what it is.
        var gate = checkout.QuerySelector("[data-testid='domain-feature-gate']")!;
        Assert.Equal("flag", gate.GetAttribute("data-kind"));
        Assert.Equal("New checkout", gate.QuerySelector(".domain-feature-card__gate-name")!.TextContent.Trim());
        Assert.Equal("Flag:", gate.QuerySelector(".sr-only")!.TextContent.Trim());

        var tally = checkout.QuerySelector("[data-testid='domain-feature-requirements']")!;
        Assert.Equal("2 req · 1 tested", tally.QuerySelector("[aria-hidden='true']")!.TextContent.Trim());
        Assert.Equal("2 requirements, 1 tested", tally.QuerySelector(".sr-only")!.TextContent.Trim());

        Assert.Equal("after Ordering, Invoicing", checkout.QuerySelector("[data-testid='domain-feature-after']")!.TextContent.Trim());
        Assert.Null(board.Find("[data-key='ordering']").QuerySelector("[data-testid='domain-feature-after']"));
    }

    [Fact]
    public void A_setting_chip_is_outlined_like_a_flag_and_names_its_kind_only_to_a_listener()
    {
        using var context = new BunitContext();
        var board = Board(context);

        var gate = board.Find("[data-key='quotes'] [data-testid='domain-feature-gate']");
        Assert.Equal("setting", gate.GetAttribute("data-kind"));
        var flag = board.Find("[data-key='checkout'] [data-testid='domain-feature-gate']");
        Assert.Equal(flag.ClassName, gate.ClassName);
        Assert.Equal("Setting: Quote validity", gate.GetAttribute("title"));
        Assert.Equal("Flag: New checkout", flag.GetAttribute("title"));
        Assert.Equal("Setting: Quote validity", gate.TextContent.Trim());
    }

    [Fact]
    public void A_card_shows_no_summary_and_no_tally_without_requirements()
    {
        using var context = new BunitContext();
        var board = Board(context);

        Assert.Empty(board.FindAll(".domain-feature-card__summary"));
        Assert.DoesNotContain("Place an order for goods.", board.Find("[data-key='ordering']").TextContent, StringComparison.Ordinal);
        Assert.Null(board.Find("[data-key='quotes']").QuerySelector("[data-testid='domain-feature-requirements']"));
    }

    [Fact]
    public void A_proposed_card_is_dashed_and_deprecated_features_gather_at_the_end_of_the_last_column()
    {
        using var context = new BunitContext();
        var board = Board(context);

        var quotes = board.Find("[data-testid='domain-feature-card'][data-key='quotes']");
        Assert.Contains("domain-feature-card--proposed", quotes.ClassList);
        Assert.Equal("Proposed", quotes.QuerySelector("[data-testid='domain-feature-status']")!.TextContent.Trim());

        // Fax orders no longer stands as a card in its own column.
        Assert.Empty(board.FindAll("[data-testid='domain-feature-card'][data-key='fax-orders']"));
        var columns = board.FindAll("[data-testid='domain-features-column']");
        Assert.Null(columns[0].QuerySelector("[data-testid='domain-features-deprecated']"));
        var box = columns[^1].QuerySelector("[data-testid='domain-features-deprecated']")!;
        Assert.Equal("Deprecated · 1", box.QuerySelector("[data-testid='domain-features-deprecated-title']")!.TextContent.Trim());
        Assert.Equal(["fax-orders"], box.QuerySelectorAll("[data-testid='domain-feature-deprecated']").Select(item => item.GetAttribute("data-key")));
        Assert.Contains("Kept so old links resolve", box.TextContent, StringComparison.Ordinal);

        // It still opens its detail.
        board.Find("[data-testid='domain-feature-deprecated'][data-key='fax-orders']").Click();
        Assert.Equal("fax-orders", board.Find("[data-testid='domain-feature-detail']").GetAttribute("data-key"));
        Assert.Equal("Deprecated", board.Find("[data-testid='domain-feature-detail-status']").TextContent.Trim());
    }

    [Fact]
    public void A_column_left_with_only_deprecated_features_is_not_drawn()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var file = DomainUnits.Parse("""
            # Features

            ## Live

            ```meta
            type: feature
            ```

            ## Old

            ```meta
            type: feature
            status: deprecated
            depends-on: [features.md#live]
            ```
            """, ".devbook/domain/x/features.md");
        var board = context.Render<DomainFeatureBoard>(parameters => parameters.Add(p => p.Columns, DomainFeatures.Columns(file, [file])));

        var column = Assert.Single(board.FindAll("[data-testid='domain-features-column']"));
        Assert.Equal("0", column.GetAttribute("data-depth"));
        Assert.NotNull(column.QuerySelector("[data-testid='domain-features-deprecated']"));
    }

    [Fact]
    public void A_picked_proposed_card_carries_the_selected_class()
    {
        using var context = new BunitContext();
        var board = Board(context);

        board.Find("[data-key='quotes']").Click();

        var quotes = board.Find("[data-key='quotes']");
        Assert.Contains("domain-feature-card--selected", quotes.ClassList);
        Assert.Contains("domain-feature-card--proposed", quotes.ClassList);
        Assert.Equal("Proposed", board.Find("[data-testid='domain-feature-detail-status']").TextContent.Trim());
        Assert.DoesNotContain("domain-feature-card--selected", board.Find("[data-key='ordering']").ClassList);
    }

    [Fact]
    public void The_page_title_names_the_context_and_the_lead_counts_its_features()
    {
        using var context = new BunitContext();
        var board = Board(context, parameters => parameters.Add(p => p.ContextName, "Ordering"));

        Assert.Equal("What Ordering lets a person do", board.Find("[data-testid='domain-features-title']").TextContent.Trim());
        Assert.StartsWith("4 features and their sub-features, laid out by what each builds on.", board.Find("[data-testid='domain-features-lead']").TextContent.Trim(), StringComparison.Ordinal);
        var legend = board.Find("[data-testid='domain-features-legend']").TextContent;
        Assert.Contains("flag or setting", legend, StringComparison.Ordinal);
        Assert.Contains("what it hangs on", legend, StringComparison.Ordinal);
        Assert.Contains("its promises on the Requirements page", legend, StringComparison.Ordinal);
        Assert.Contains("agreed, not yet the model", legend, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_a_context_name_the_page_title_is_Features()
    {
        using var context = new BunitContext();
        var board = Board(context);

        Assert.Equal("Features", board.Find("[data-testid='domain-features-title']").TextContent.Trim());
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
        Assert.Contains("Card payment", detail.QuerySelector("[data-testid='domain-feature-detail-sub-features']")!.TextContent);
        Assert.Contains("2 requirements", detail.QuerySelector("[data-testid='domain-feature-promises'] [data-testid='domain-feature-detail-requirements']")!.TextContent);
        Assert.Equal("true", board.Find("[data-key='checkout']").GetAttribute("aria-pressed"));
    }

    [Fact]
    public void The_gate_reads_its_link_then_its_kind_as_separate_words()
    {
        using var context = new BunitContext();
        var board = Board(context, parameters => parameters.Add(p => p.ActiveFeature, "checkout"));

        var gate = board.Find("[data-testid='domain-feature-detail-gate']");
        Assert.Equal("Gated by", gate.QuerySelector("dt")!.TextContent.Trim());
        Assert.Equal("New checkout · flag", Words(gate.QuerySelector("dd")!.TextContent));

        board.Find("[data-key='quotes']").Click();
        gate = board.Find("[data-testid='domain-feature-detail-gate']");
        Assert.Equal("Configured by", gate.QuerySelector("dt")!.TextContent.Trim());
        Assert.Equal("Quote validity · setting", Words(gate.QuerySelector("dd")!.TextContent));

        board.Find("[data-key='ordering']").Click();
        gate = board.Find("[data-testid='domain-feature-detail-gate']");
        Assert.Equal("Gated by", gate.QuerySelector("dt")!.TextContent.Trim());
        Assert.Equal("Always on", Words(gate.QuerySelector("dd")!.TextContent));
    }

    /// <summary>The text as a reader sees it: whitespace runs collapse to one space.</summary>
    private static string Words(string text) => System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

    [Fact]
    public void The_promises_pane_counts_the_requirements_and_bars_how_many_a_test_proves()
    {
        using var context = new BunitContext();
        var board = Board(context, parameters => parameters.Add(p => p.ActiveFeature, "checkout"));

        var promises = board.Find("[data-testid='domain-feature-promises']");
        Assert.Contains("What it promises", promises.TextContent, StringComparison.Ordinal);
        Assert.Equal("2 requirements", promises.QuerySelector("[data-testid='domain-feature-promise-count']")!.TextContent.Trim());
        var bar = promises.QuerySelector("[data-testid='domain-feature-promise-bar']")!;
        Assert.Equal("img", bar.GetAttribute("role"));
        Assert.Equal("1 of 2 requirements linked to a test", bar.GetAttribute("aria-label"));
        Assert.Equal("width: 50%", bar.QuerySelector(".domain-feature-detail__fill")!.GetAttribute("style"));
        Assert.Equal("1 of 2 linked to a test", promises.QuerySelector("[data-testid='domain-feature-promise-proof']")!.TextContent.Trim());
        Assert.Contains("Open them on Requirements", promises.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Requirements", board.Find(".domain-feature-detail__facts").TextContent, StringComparison.Ordinal);

        board.Find("[data-key='ordering']").Click();
        promises = board.Find("[data-testid='domain-feature-promises']");
        Assert.Equal("1 requirement", promises.QuerySelector("[data-testid='domain-feature-promise-count']")!.TextContent.Trim());
        Assert.Equal("All linked to a test", promises.QuerySelector("[data-testid='domain-feature-promise-proof']")!.TextContent.Trim());

        board.Find("[data-key='quotes']").Click();
        promises = board.Find("[data-testid='domain-feature-promises']");
        Assert.Equal("No requirements yet", promises.QuerySelector("[data-testid='domain-feature-promise-count']")!.TextContent.Trim());
        Assert.Null(promises.QuerySelector("[data-testid='domain-feature-promise-bar']"));
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
    public void The_rules_in_place_replace_the_invariants_sentence_and_link_each_to_its_row()
    {
        using var context = new BunitContext();
        DomainAlwaysTrueLink? opened = null;
        var view = Requirements(context, parameters => parameters.Add(p => p.OnOpenAlwaysTrue, unit => opened = unit));

        Assert.DoesNotContain("invariants", view.Find("[data-testid='domain-requirements-intro']").TextContent, StringComparison.OrdinalIgnoreCase);
        view.FindAll("[data-testid='domain-rule-list-toggle']")[0].Click();
        view.Find("[data-testid='domain-rule-list-open']").Click();

        Assert.Equal("order", opened!.UnitKey);
        Assert.Equal(".devbook/domain/ordering/domain.md", opened.DocumentPath);
        Assert.EndsWith("domain.invariants.md#invariant-a-paid-order-is-not-paid-again", opened.Rule, StringComparison.Ordinal);
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
        Assert.Contains("whoever changes the Order.", view.Find("[data-testid='domain-requirements-always-true']").TextContent, StringComparison.Ordinal);
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
