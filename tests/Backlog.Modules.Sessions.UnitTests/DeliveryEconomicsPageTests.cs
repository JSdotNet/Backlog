using Backlog.Modules.Sessions.UI;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Sessions.UnitTests;

/// <summary>
/// The Delivery economics page on its own: the flow and the window it reads, the phase
/// table, the selected phase's configurations and the lesson card, and what it says
/// when there is nothing to compare or nothing priced. The grouping and the medians are
/// <c>DeliveryEconomicsTests</c>'; what is asserted here is the page's part — which
/// figure lands where, and that nothing unmeasured is drawn as a figure.
/// </summary>
public sealed class DeliveryEconomicsPageTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

    private const string Sonnet = """{"mode":"delegate","agent":"csharp-coding:coding","model":"claude-sonnet-5-1","effort":"high"}""";

    private const string Opus = """{"mode":"delegate","agent":"csharp-coding:coding","model":"claude-opus-5-5"}""";

    [Fact]
    public void Each_phase_is_a_row_with_its_configuration_now_its_priced_runs_its_median_cost_and_its_share()
    {
        var priced = Mixed();

        using var context = Context();
        var page = Render(context, priced);

        page.WaitForAssertion(() =>
        {
            var rows = page.FindAll("[data-testid='economics-phase']");
            Assert.Equal(["Implement", "Review"], rows.Select(row => row.GetAttribute("data-phase")));

            var implement = rows[0];

            // The latest runs ran Opus with no recorded effort: the model, and no chip.
            Assert.Contains("Opus", implement.QuerySelector("[data-testid='economics-phase-now']")!.TextContent, StringComparison.Ordinal);
            Assert.Null(implement.QuerySelector("[data-testid='economics-phase-effort']"));

            Assert.StartsWith("11", implement.QuerySelector("[data-testid='economics-phase-runs']")!.TextContent.Trim(), StringComparison.Ordinal);
            Assert.Contains("$1.00", implement.QuerySelector("[data-testid='economics-phase-cost']")!.TextContent, StringComparison.Ordinal);

            // The share is a bar the eye reads and a percent that states it.
            var share = implement.QuerySelector("[data-testid='economics-phase-share']")!;
            Assert.Equal("true", share.QuerySelector(".delivery-economics__bar")!.GetAttribute("aria-hidden"));
            Assert.StartsWith("50%", share.QuerySelector(".delivery-economics__share-figure")!.TextContent, StringComparison.Ordinal);

            Assert.Contains("not reported", implement.QuerySelector("[data-testid='economics-phase-quality']")!.TextContent, StringComparison.Ordinal);
            Assert.StartsWith("2", implement.QuerySelector("[data-testid='economics-phase-configurations']")!.TextContent.Trim(), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void The_page_says_how_many_runs_it_read_where_their_costs_came_from_and_what_quality_stands_on()
    {
        using var context = Context();
        var page = Render(context, Mixed());

        page.WaitForAssertion(() =>
        {
            Assert.Equal(
                "11 runs of flow-code in the last 30 days · costs are Claude Code's own reported figures.",
                page.Find("[data-testid='economics-subtitle']").TextContent.Trim());

            Assert.Contains(
                "Quality measure stands on 0 of 11 runs",
                page.Find("[data-testid='economics-kpi-quality']").TextContent,
                StringComparison.Ordinal);

            Assert.Contains("Sessions", page.Find("[data-testid='economics-breadcrumb']").TextContent, StringComparison.Ordinal);
            Assert.Equal("page", page.Find("[data-testid='economics-breadcrumb'] [aria-current]").GetAttribute("aria-current"));
        });
    }

    [Fact]
    public void A_selected_phase_shows_a_card_per_configuration_and_the_lesson_with_its_request_to_copy()
    {
        using var context = Context();
        var page = Render(context, Mixed());

        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("[data-testid='economics-phase']").Count));

        page.Find("[data-testid='economics-phase'][data-phase='Implement']").Click();

        page.WaitForAssertion(() =>
        {
            Assert.Equal(["true", "false"], page.FindAll("[data-testid='economics-phase']").Select(row => row.GetAttribute("aria-pressed")));

            var panel = page.Find("[data-testid='economics-panel']");
            Assert.Equal("Implement", panel.QuerySelector("[data-testid='economics-panel-title']")!.TextContent.Trim());

            var cards = panel.QuerySelectorAll("[data-testid='economics-config']");
            Assert.Equal(2, cards.Length);

            // The cheaper configuration is marked in words, and only because whole runs
            // cost no more under it.
            var sonnet = cards.Single(card => card.TextContent.Contains("Sonnet", StringComparison.Ordinal));
            Assert.Equal("high", sonnet.QuerySelector("[data-testid='economics-config-effort']")!.TextContent.Trim());
            Assert.Equal("best on cost", sonnet.QuerySelector("[data-testid='economics-config-best']")!.TextContent.Trim());
            Assert.Contains("$1.00", sonnet.QuerySelector("[data-testid='economics-config-phase-cost']")!.TextContent, StringComparison.Ordinal);
            Assert.Contains("$2.00", sonnet.QuerySelector("[data-testid='economics-config-run-cost']")!.TextContent, StringComparison.Ordinal);
            Assert.Equal("not reported", sonnet.QuerySelector("[data-testid='economics-config-quality']")!.TextContent.Trim());

            var opus = cards.Single(card => card.TextContent.Contains("Opus", StringComparison.Ordinal));
            Assert.NotNull(opus.QuerySelector("[data-testid='economics-config-current']"));
            Assert.Null(opus.QuerySelector("[data-testid='economics-config-best']"));

            var lesson = panel.QuerySelector("[data-testid='economics-lesson']")!;
            Assert.StartsWith("Sonnet at high effort runs Implement for $2.00 less than Opus", lesson.QuerySelector("[data-testid='economics-lesson-headline']")!.TextContent, StringComparison.Ordinal);
            Assert.Contains("6 runs", lesson.QuerySelector("[data-testid='economics-lesson-samples']")!.TextContent, StringComparison.Ordinal);
            Assert.Contains("5 runs", lesson.QuerySelector("[data-testid='economics-lesson-samples']")!.TextContent, StringComparison.Ordinal);
            Assert.Equal("Quality not reported: this compares cost only.", lesson.QuerySelector("[data-testid='economics-lesson-quality']")!.TextContent.Trim());

            // The library's copy button, worded, with its own status line.
            var copy = lesson.QuerySelector("[data-testid='economics-lesson-copy-button']")!;
            Assert.Equal("BUTTON", copy.TagName);
            Assert.Equal("Copy the overlay request", copy.TextContent.Trim());
            Assert.Equal("status", lesson.QuerySelector("[data-testid='economics-lesson-copy-result']")!.GetAttribute("role"));
        });
    }

    [Fact]
    public void Pressing_copy_puts_the_request_on_the_clipboard_and_says_so()
    {
        using var context = Context();
        var copy = context.JSInterop.Setup<bool>("backlogClipboard.copy", _ => true);
        copy.SetResult(true);
        var page = Render(context, Mixed());

        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-testid='economics-lesson-copy-button']")));
        page.Find("[data-testid='economics-lesson-copy-button']").Click();

        page.WaitForAssertion(() =>
        {
            Assert.Equal("Copied", page.Find("[data-testid='economics-lesson-copy-result']").TextContent.Trim());

            var request = Assert.Single(copy.Invocations).Arguments[0] as string;
            Assert.StartsWith("Use devbook-config:local to set the Implement phase of flow-code to Sonnet at high effort", request, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Where_the_configuration_now_is_the_cheaper_the_lesson_says_keep_it_and_offers_nothing_to_copy()
    {
        // Sonnet ran the latest runs this time: the lesson confirms it.
        IReadOnlyList<PricedDeliveryRun> priced = [.. Series("s", 6, 1, Sonnet, 1m, 1m), .. Series("o", 5, 10, Opus, 3m, 1m)];

        using var context = Context();
        var page = Render(context, priced);

        page.WaitForAssertion(() =>
        {
            var lesson = page.Find("[data-testid='economics-lesson']");

            Assert.StartsWith("Keep Sonnet at high effort for Implement", lesson.QuerySelector("[data-testid='economics-lesson-headline']")!.TextContent, StringComparison.Ordinal);
            Assert.Null(lesson.QuerySelector("[data-testid='economics-lesson-copy-button']"));
        });
    }

    [Fact]
    public void A_phase_only_one_configuration_ran_says_there_is_nothing_to_compare()
    {
        using var context = Context();
        var page = Render(context, [.. Series("o", 6, 1, Opus, 3m, 1m)]);

        page.WaitForAssertion(() =>
        {
            var panel = page.Find("[data-testid='economics-panel']");

            Assert.Single(panel.QuerySelectorAll("[data-testid='economics-config']"));
            Assert.Contains("Only one configuration has run this phase", panel.QuerySelector("[data-testid='economics-one-configuration']")!.TextContent, StringComparison.Ordinal);
            Assert.Null(panel.QuerySelector("[data-testid='economics-lesson']"));
        });
    }

    [Fact]
    public void Two_configurations_short_of_five_runs_each_draw_no_lesson_and_say_why()
    {
        using var context = Context();
        var page = Render(context, [.. Series("o", 4, 1, Opus, 3m, 1m), .. Series("s", 4, 10, Sonnet, 1m, 1m)]);

        page.WaitForAssertion(() =>
        {
            var panel = page.Find("[data-testid='economics-panel']");

            Assert.Equal(2, panel.QuerySelectorAll("[data-testid='economics-config']").Length);
            Assert.Null(panel.QuerySelector("[data-testid='economics-lesson']"));
            Assert.Null(panel.QuerySelector("[data-testid='economics-config-best']"));
            Assert.Contains("at least five priced runs each", panel.QuerySelector("[data-testid='economics-no-lesson']")!.TextContent, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void A_store_that_fails_leaves_the_runs_counted_and_unpriced_rather_than_failing_the_page()
    {
        var runs = Series("o", 3, 1, Opus, 3m, 1m).Select(priced => priced.Run).ToList();

        using var context = Context();
        var page = context.Render<DeliveryEconomicsPage>(parameters => parameters
            .Add(p => p.Runs, runs)
            .Add(p => p.Price, _ => Task.FromException<PricedDeliveryRun>(new IOException("The request store is locked."))));

        page.WaitForAssertion(() =>
        {
            Assert.Equal("3 runs of flow-code in the last 30 days · no run could be priced.", page.Find("[data-testid='economics-subtitle']").TextContent.Trim());

            var cost = page.Find("[data-testid='economics-phase'][data-phase='Implement'] [data-testid='economics-phase-cost']");
            Assert.Contains("—", cost.TextContent, StringComparison.Ordinal);
            Assert.DoesNotContain("$0", page.Markup, StringComparison.Ordinal);
            Assert.StartsWith("0", page.Find("[data-testid='economics-phase'][data-phase='Implement'] [data-testid='economics-phase-runs']").TextContent.Trim(), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void An_estimated_figure_is_marked_as_one()
    {
        IReadOnlyList<PricedDeliveryRun> priced = [.. Series("o", 3, 1, Opus, 3m, 1m, CostSource.Estimated)];

        using var context = Context();
        var page = Render(context, priced);

        page.WaitForAssertion(() =>
        {
            Assert.EndsWith("costs estimated from tokens at the Settings prices.", page.Find("[data-testid='economics-subtitle']").TextContent.Trim(), StringComparison.Ordinal);
            Assert.Contains("est.", page.Find("[data-testid='economics-phase'][data-phase='Implement'] [data-testid='economics-phase-cost']").TextContent, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void With_no_finished_run_in_the_window_the_page_says_so_and_ninety_days_reaches_further_back()
    {
        IReadOnlyList<PricedDeliveryRun> priced = [.. Series("o", 2, 40, Opus, 3m, 1m)];

        using var context = Context();
        var page = Render(context, priced);

        page.WaitForAssertion(() =>
        {
            Assert.Contains("No finished delivery runs in the last 30 days", page.Find("[data-testid='economics-empty']").TextContent, StringComparison.Ordinal);
            Assert.Empty(page.FindAll("[data-testid='economics-phase']"));
        });

        page.Find("[data-testid='economics-window-90']").Click();

        page.WaitForAssertion(() =>
        {
            Assert.Equal("true", page.Find("[data-testid='economics-window-90']").GetAttribute("aria-pressed"));
            Assert.Equal(2, page.FindAll("[data-testid='economics-phase']").Count);
            Assert.StartsWith("2 runs of flow-code in the last 90 days", page.Find("[data-testid='economics-subtitle']").TextContent.Trim(), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void The_flow_passed_in_is_the_one_shown_and_the_selector_moves_to_another()
    {
        IReadOnlyList<PricedDeliveryRun> priced =
        [
            .. Series("o", 3, 1, Opus, 3m, 1m),
            .. Series("p", 1, 2, Opus, 2m, 1m, skill: "flow-spec")
        ];

        using var context = Context();
        var page = Render(context, priced, flow: "flow-spec");

        page.WaitForAssertion(() =>
        {
            Assert.Equal("flow-spec", page.Find("[data-testid='economics-flow'] select").GetAttribute("value"));
            Assert.StartsWith("1 run of flow-spec", page.Find("[data-testid='economics-subtitle']").TextContent.Trim(), StringComparison.Ordinal);
        });

        page.Find("[data-testid='economics-flow'] select").Change("flow-code");

        page.WaitForAssertion(() =>
            Assert.StartsWith("3 runs of flow-code", page.Find("[data-testid='economics-subtitle']").TextContent.Trim(), StringComparison.Ordinal));
    }

    [Fact]
    public void Without_a_flow_passed_in_the_flow_with_most_runs_is_shown()
    {
        IReadOnlyList<PricedDeliveryRun> priced =
        [
            .. Series("p", 1, 2, Opus, 2m, 1m, skill: "flow-spec"),
            .. Series("o", 3, 1, Opus, 3m, 1m)
        ];

        using var context = Context();
        var page = Render(context, priced);

        page.WaitForAssertion(() =>
            Assert.StartsWith("3 runs of flow-code", page.Find("[data-testid='economics-subtitle']").TextContent.Trim(), StringComparison.Ordinal));
    }

    [Fact]
    public void Each_run_is_priced_once_whatever_the_page_redraws()
    {
        var priced = Mixed();
        var asked = new Dictionary<string, int>(StringComparer.Ordinal);

        using var context = Context();
        var page = context.Render<DeliveryEconomicsPage>(parameters => parameters
            .Add(p => p.Runs, [.. priced.Select(run => run.Run)])
            .Add(p => p.Price, run =>
            {
                asked[run.Id] = asked.GetValueOrDefault(run.Id) + 1;
                return Task.FromResult(priced.Single(candidate => ReferenceEquals(candidate.Run, run)));
            }));

        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("[data-testid='economics-phase']").Count));

        page.Find("[data-testid='economics-phase'][data-phase='Review']").Click();

        page.WaitForAssertion(() =>
        {
            Assert.Equal("Review", page.Find("[data-testid='economics-panel-title']").TextContent.Trim());
            Assert.Equal(priced.Count, asked.Count);
            Assert.All(asked.Values, count => Assert.Equal(1, count));
        });
    }

    /// <summary>
    /// A refresh of the same flow and window keeps what is on screen until the new
    /// reading is priced, then replaces it whole — the view is never blanked and refilled
    /// run by run. The live line says it is pricing again, without a running count.
    /// </summary>
    [Fact]
    public void A_refresh_keeps_the_figures_on_screen_until_the_new_reading_is_priced()
    {
        var priced = Mixed();

        using var context = Context();
        var page = Render(context, priced);

        page.WaitForAssertion(() =>
        {
            Assert.Contains("$1.00", Cell(page, "Implement", "economics-phase-cost").TextContent, StringComparison.Ordinal);
            Assert.Equal("sr-only", page.Find("[data-testid='economics-pricing']").ClassName);
        });

        // The pane's Refresh hands over a new list of the same runs; each now costs $2
        // for Implement, and the store answers only when the gate opens.
        var gate = new TaskCompletionSource();
        var repriced = priced
            .Select(run => run with { Costs = [new DeliveryRunStageCost(Usd(2m, CostSource.Reported), []), run.Costs[1]] })
            .ToList();

        page.Render(parameters => parameters
            .Add(p => p.Runs, [.. repriced.Select(run => run.Run)])
            .Add(p => p.Price, async run =>
            {
                await gate.Task;
                return repriced.Single(candidate => ReferenceEquals(candidate.Run, run));
            }));

        page.WaitForAssertion(() =>
        {
            Assert.Equal(2, page.FindAll("[data-testid='economics-phase']").Count);
            Assert.Contains("$1.00", Cell(page, "Implement", "economics-phase-cost").TextContent, StringComparison.Ordinal);
            Assert.NotNull(page.Find("[data-testid='economics-panel']"));

            var status = page.Find("[data-testid='economics-pricing']");
            Assert.Equal("status", status.GetAttribute("role"));
            Assert.Equal("Pricing the runs again. The figures below are the last reading's until it finishes.", status.TextContent.Trim());
        });

        gate.SetResult();

        page.WaitForAssertion(() =>
        {
            Assert.Contains("$2.00", Cell(page, "Implement", "economics-phase-cost").TextContent, StringComparison.Ordinal);
            Assert.Equal("Priced all 11 runs.", page.Find("[data-testid='economics-pricing']").TextContent.Trim());
        });
    }

    /// <summary>A first reading has nothing earlier to keep: the live line says so once,
    /// and its words carry no count that would change with every run.</summary>
    [Fact]
    public void The_live_line_changes_when_pricing_starts_and_ends_and_not_per_run()
    {
        var priced = Mixed();
        var gate = new TaskCompletionSource();

        using var context = Context();
        var page = context.Render<DeliveryEconomicsPage>(parameters => parameters
            .Add(p => p.Runs, [.. priced.Select(run => run.Run)])
            .Add(p => p.Price, async run =>
            {
                if (run.Id != "o0") await gate.Task;
                return priced.Single(candidate => ReferenceEquals(candidate.Run, run));
            }));

        page.WaitForAssertion(() =>
        {
            Assert.Single(page.FindAll("[data-testid='economics-phase'][data-phase='Implement']"));
            Assert.Equal("Pricing the runs. The figures below cover the runs priced so far.", page.Find("[data-testid='economics-pricing']").TextContent.Trim());
            Assert.Contains("pricing 1 of 11", page.Find("[data-testid='economics-subtitle']").TextContent, StringComparison.Ordinal);
        });

        gate.SetResult();

        page.WaitForAssertion(() =>
            Assert.Equal("Priced all 11 runs.", page.Find("[data-testid='economics-pricing']").TextContent.Trim()));
    }

    [Fact]
    public void Opening_the_page_puts_the_focus_on_it()
    {
        using var context = Context();
        var focus = context.JSInterop.SetupVoid("backlogFocus", _ => true);

        var page = Render(context, Mixed());

        page.WaitForAssertion(() =>
        {
            Assert.Equal("delivery-economics", Assert.Single(focus.Invocations).Arguments[0]);

            var section = page.Find("[data-testid='delivery-economics']");
            Assert.Equal("delivery-economics", section.Id);
            Assert.Equal("-1", section.GetAttribute("tabindex"));
            Assert.Equal("delivery-economics-title", section.GetAttribute("aria-labelledby"));
        });
    }

    [Fact]
    public void The_breadcrumb_and_refresh_ask_the_pane()
    {
        var back = 0;
        var refreshed = 0;

        using var context = Context();
        var page = context.Render<DeliveryEconomicsPage>(parameters => parameters
            .Add(p => p.Runs, [])
            .Add(p => p.OnBack, () => back++)
            .Add(p => p.OnRefresh, () => refreshed++));

        page.Find("[data-testid='economics-back']").Click();
        page.Find("[data-testid='economics-refresh']").Click();

        page.WaitForAssertion(() =>
        {
            Assert.Equal(1, back);
            Assert.Equal(1, refreshed);
        });
    }

    [Fact]
    public void A_run_with_a_stage_unpriced_still_counts_and_no_share_reads_over_the_whole()
    {
        // Implement priced where Review was not, and the other way round: over the runs
        // priced in every stage, Implement once read 275% of the median run.
        IReadOnlyList<PricedDeliveryRun> priced =
        [
            Partly("a", 1, 10m, null),
            Partly("b", 2, 1m, 1m),
            Partly("c", 3, null, 1m)
        ];

        using var context = Context();
        var page = Render(context, priced);

        page.WaitForAssertion(() =>
        {
            // Medians $5.50 and $1.00: Implement is 85% of the flow's phase costs, Review 15%.
            Assert.StartsWith("85%", Cell(page, "Implement", "economics-phase-share").QuerySelector(".delivery-economics__share-figure")!.TextContent, StringComparison.Ordinal);
            Assert.StartsWith("15%", Cell(page, "Review", "economics-phase-share").QuerySelector(".delivery-economics__share-figure")!.TextContent, StringComparison.Ordinal);

            var largest = page.Find("[data-testid='economics-kpi-largest']").TextContent;
            Assert.Contains("Implement", largest, StringComparison.Ordinal);
            Assert.Contains("85% of the flow's phase costs", largest, StringComparison.Ordinal);

            // Every run priced something, so the median run stands on all three, and the
            // tile says two of them left a stage unpriced.
            Assert.Contains("2 of 3 runs partly priced", page.Find("[data-testid='economics-kpi-median']").TextContent, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// A phase only older runs carried keeps its median but has no share — and the dash
    /// says why, rather than claiming nothing priced it.
    /// </summary>
    [Fact]
    public void A_phase_the_latest_run_dropped_shows_its_median_and_says_why_it_has_no_share()
    {
        IReadOnlyList<PricedDeliveryRun> priced =
        [
            new(Run("new", Now.AddDays(-1), "flow-code", Stage("Implement", Opus), Stage("Review", Opus)),
                [new DeliveryRunStageCost(Usd(3m, CostSource.Reported), []), new DeliveryRunStageCost(Usd(1m, CostSource.Reported), [])]),
            new(Run("old", Now.AddDays(-2), "flow-code", Stage("Plan", Opus), Stage("Implement", Opus), Stage("Review", Opus)),
                [new DeliveryRunStageCost(Usd(5m, CostSource.Reported), []), new DeliveryRunStageCost(Usd(3m, CostSource.Reported), []), new DeliveryRunStageCost(Usd(1m, CostSource.Reported), [])])
        ];

        using var context = Context();
        var page = Render(context, priced);

        page.WaitForAssertion(() =>
        {
            Assert.Contains("$5.00", Cell(page, "Plan", "economics-phase-cost").TextContent, StringComparison.Ordinal);

            var dash = Cell(page, "Plan", "economics-phase-no-share");
            Assert.Equal("Not in the latest run, so not part of the flow's phase costs now", dash.GetAttribute("title"));
            Assert.Contains("Not in the latest run", dash.TextContent, StringComparison.Ordinal);

            // The phases the latest run carries share its costs between them: $3 and $1.
            var implement = Cell(page, "Implement", "economics-phase-share").QuerySelector(".delivery-economics__share-figure")!;
            Assert.StartsWith("75%", implement.TextContent, StringComparison.Ordinal);
            Assert.Contains("of the flow's phase costs", implement.TextContent, StringComparison.Ordinal);
        });
    }

    /// <summary>A phase no run priced has no cost and no share, and says so in its own
    /// words.</summary>
    [Fact]
    public void A_phase_no_run_priced_says_so_where_its_share_would_be()
    {
        IReadOnlyList<PricedDeliveryRun> priced = [Partly("a", 1, 2m, null), Partly("b", 2, 3m, null)];

        using var context = Context();
        var page = Render(context, priced);

        page.WaitForAssertion(() =>
        {
            Assert.Contains("—", Cell(page, "Review", "economics-phase-cost").TextContent, StringComparison.Ordinal);

            var dash = Cell(page, "Review", "economics-phase-no-share");
            Assert.Equal("No run priced this phase", dash.GetAttribute("title"));
            Assert.DoesNotContain("latest run", dash.TextContent, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void A_configuration_that_ran_a_phase_but_priced_none_of_it_shows_its_runs_and_not_priced()
    {
        IReadOnlyList<PricedDeliveryRun> priced =
        [
            Partly("o1", 1, null, 1m, Opus),
            Partly("o2", 2, null, 1m, Opus),
            Partly("s1", 3, 2m, 1m)
        ];

        using var context = Context();
        var page = Render(context, priced);

        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("[data-testid='economics-phase']").Count));
        page.Find("[data-testid='economics-phase'][data-phase='Implement']").Click();

        page.WaitForAssertion(() =>
        {
            var opus = page.FindAll("[data-testid='economics-config']").Single(card => card.TextContent.Contains("Opus", StringComparison.Ordinal));

            Assert.Equal("2 runs · 0 priced", opus.QuerySelector("[data-testid='economics-config-runs']")!.TextContent.Trim());
            Assert.Equal("not priced", opus.QuerySelector("[data-testid='economics-config-phase-cost']")!.TextContent.Trim());
        });
    }

    /// <summary>One run of Implement then Review, either stage left unpriced by a null.</summary>
    private static PricedDeliveryRun Partly(string id, int daysAgo, decimal? implementUsd, decimal? reviewUsd, string implement = Sonnet) =>
        new(
            Run(id, Now.AddDays(-daysAgo), "flow-code", Stage("Implement", implement), Stage("Review", implement)),
            [
                new DeliveryRunStageCost(implementUsd is { } i ? Usd(i, CostSource.Reported) : null, []),
                new DeliveryRunStageCost(reviewUsd is { } r ? Usd(r, CostSource.Reported) : null, [])
            ]);

    /// <summary>Opus ran the five latest runs at $3 for Implement; Sonnet at high effort
    /// the six before them at $1. Review is $1 under both.</summary>
    private static IReadOnlyList<PricedDeliveryRun> Mixed() =>
        [.. Series("o", 5, 1, Opus, 3m, 1m), .. Series("s", 6, 10, Sonnet, 1m, 1m)];

    private static IEnumerable<PricedDeliveryRun> Series(
        string name,
        int count,
        int from,
        string implement,
        decimal implementUsd,
        decimal reviewUsd,
        CostSource source = CostSource.Reported,
        string skill = "flow-code") =>
        Enumerable.Range(0, count).Select(index => new PricedDeliveryRun(
            Run($"{name}{index}", Now.AddDays(-(from + index)), skill, Stage("Implement", implement), Stage("Review", implement)),
            [new DeliveryRunStageCost(Usd(implementUsd, source), []), new DeliveryRunStageCost(Usd(reviewUsd, source), [])]));

    private static AngleSharp.Dom.IElement Cell(IRenderedComponent<DeliveryEconomicsPage> page, string phase, string cell) =>
        page.Find($"[data-testid='economics-phase'][data-phase='{phase}'] [data-testid='{cell}']");

    private static CostFigure Usd(decimal dollars, CostSource source) => new((long)(dollars * 1_000_000m), source);

    private static DeliveryRun Run(string id, DateTimeOffset started, string skill, params DeliveryRunStage[] stages) =>
        new(id, "backlog", "wt-" + id, "wt", "machine", "Machine", skill, "A run", "done", null, [],
            started, started.AddMinutes(30), stages, null, null, [], []);

    private static DeliveryRunStage Stage(string name, string execution) =>
        new(name, "done", 60_000, 1) { Execution = execution };

    private static IRenderedComponent<DeliveryEconomicsPage> Render(
        BunitContext context,
        IReadOnlyList<PricedDeliveryRun> priced,
        string? flow = null) =>
        context.Render<DeliveryEconomicsPage>(parameters => parameters
            .Add(p => p.Runs, [.. priced.Select(run => run.Run)])
            .Add(p => p.Flow, flow)
            .Add(p => p.Price, run => Task.FromResult(priced.Single(candidate => ReferenceEquals(candidate.Run, run)))));

    private static BunitContext Context()
    {
        var context = new BunitContext();
        context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));

        // Opening the page focuses it; the tests that care set the call up themselves.
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        return context;
    }
}
