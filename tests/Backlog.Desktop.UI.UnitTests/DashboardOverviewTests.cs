using Backlog.Modules.Dashboard.Abstractions;
using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Dashboard.UI;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Overview tab: five headline tiles, a main column and a side column, every block
/// fetching through its tab's own insight and carrying its own state — and Needs you,
/// which always answers and says so when nothing needs the person.
/// </summary>
public class DashboardOverviewTests
{
    private static readonly string[] Tiles =
    [
        "dashboard-overview-tile-pulls",
        "dashboard-overview-tile-points",
        "dashboard-overview-tile-agent-time",
        "dashboard-overview-tile-spend",
        "dashboard-overview-tile-drift"
    ];

    /// <summary>
    /// The board's layout: the five tiles in its order across the top, then the main
    /// column — throughput, the roadmap against its pace — and the side column beside
    /// it, which comes after it in the document so it is the one that wraps under.
    /// </summary>
    [Fact]
    public void The_overview_lays_out_five_tiles_then_a_main_and_a_side_column()
    {
        using var context = Context();

        var pane = context.Render<DashboardPane>();
        var overview = pane.Find("[data-testid='dashboard-overview']");

        var tiles = overview.QuerySelectorAll("[data-testid='dashboard-overview-tiles'] > .dashboard-overview-tile");
        Assert.Equal(Tiles, tiles.Select(tile => tile.GetAttribute("data-testid")));

        var columns = overview.QuerySelector("[data-testid='dashboard-overview-columns']")!;
        Assert.Equal(
            ["dashboard-overview-main", "dashboard-overview-side"],
            columns.Children.Select(column => column.GetAttribute("data-testid")));

        Assert.Equal(
            ["dashboard-overview-throughput", "dashboard-overview-roadmap"],
            columns.Children[0].Children.Select(part => part.GetAttribute("data-testid")));

        var side = columns.Children[1];
        Assert.Equal("ASIDE", side.TagName);
        Assert.Equal(
            ["dashboard-overview-needs-you", "dashboard-overview-spend-model", "dashboard-overview-hours"],
            side.Children.Select(part => part.GetAttribute("data-testid")));
    }

    /// <summary>
    /// With no provider reachable, every block still draws and carries its source's own
    /// reason — the tiles keep their labels — rather than the tab staying empty.
    /// </summary>
    [Fact]
    public void Every_overview_block_explains_itself_when_no_provider_can_answer()
    {
        using var context = Context();

        var pane = context.Render<DashboardPane>();

        foreach (var id in Tiles.Concat(["dashboard-overview-throughput", "dashboard-overview-roadmap", "dashboard-overview-spend-model", "dashboard-overview-hours"]))
        {
            Assert.Contains(DashboardTestHost.UnavailableReason, pane.Find($"[data-testid='{id}']").TextContent, StringComparison.Ordinal);
        }

        Assert.Contains("Pull requests merged", pane.Find("[data-testid='dashboard-overview-tile-pulls']").TextContent, StringComparison.Ordinal);
    }

    /// <summary>
    /// One source answering leaves its blocks' figures on screen beside the others'
    /// reasons: the tile shows the count, its delta on the good tone and its sparkline,
    /// and the throughput block its bars and the three review figures.
    /// </summary>
    [Fact]
    public void A_block_whose_source_answers_shows_its_figures_beside_the_others_reasons()
    {
        using var context = Context(services =>
            services.AddSingleton<IProductivityInsights>(new ReadyProductivityInsights()));

        var pane = context.Render<DashboardPane>();

        var tile = pane.Find("[data-testid='dashboard-overview-tile-pulls']");
        Assert.Contains("23", tile.TextContent, StringComparison.Ordinal);
        var chip = pane.Find("[data-testid='dashboard-overview-tile-pulls-chip']");
        Assert.Equal("up 4 vs previous 12 weeks", chip.TextContent.Trim());
        Assert.Contains("dashboard-overview-chip--good", chip.ClassName, StringComparison.Ordinal);
        Assert.NotNull(pane.Find("[data-testid='dashboard-overview-tile-pulls-trend']"));

        Assert.NotNull(pane.Find("[data-testid='dashboard-overview-throughput-bars']"));
        var figures = pane.Find("[data-testid='dashboard-overview-throughput-figures']").TextContent;
        Assert.Contains("First review in", figures, StringComparison.Ordinal);
        Assert.Contains("3h", figures, StringComparison.Ordinal);
        Assert.Contains("Came back after review", figures, StringComparison.Ordinal);
        Assert.Contains("Commits per pull request", figures, StringComparison.Ordinal);

        Assert.Contains(DashboardTestHost.UnavailableReason, pane.Find("[data-testid='dashboard-overview-tile-drift']").TextContent, StringComparison.Ordinal);
    }

    /// <summary>
    /// Drift has its own tone: an open drift issue whose sync failed reads on the drift
    /// chip and names the failure, with no delta and no sparkline — the issue source has
    /// no earlier figure to compare with.
    /// </summary>
    [Fact]
    public void Open_drift_reads_on_the_drift_tone_with_no_delta_or_trend()
    {
        using var context = Context(services =>
            services.AddSingleton<IDriftInsights>(new FixedDriftInsights(new DriftInsight(
                [],
                [
                    new DriftIssue("backlog", 7, "[Devbook drift] domain/sync", "https://example.test/7", SyncFailed: true),
                    new DriftIssue("backlog", 8, "[Devbook drift] domain/tasks", "https://example.test/8", SyncFailed: false)
                ],
                IssuesNote: null))));

        var pane = context.Render<DashboardPane>();

        var tile = pane.Find("[data-testid='dashboard-overview-tile-drift']");
        Assert.Contains("2", tile.TextContent, StringComparison.Ordinal);
        Assert.Contains("issues open", tile.TextContent, StringComparison.Ordinal);
        var chip = pane.Find("[data-testid='dashboard-overview-tile-drift-chip']");
        Assert.Equal("Sync failed: domain/sync", chip.TextContent.Trim());
        Assert.Contains("dashboard-overview-chip--drift", chip.ClassName, StringComparison.Ordinal);
        Assert.Empty(pane.FindAll("[data-testid='dashboard-overview-tile-drift-trend']"));
    }

    /// <summary>
    /// The spend tile gives each provider its own line and never a total, and the chip
    /// warns when a provider's month-end projection passes its budget.
    /// </summary>
    [Fact]
    public void The_spend_tile_shows_a_line_per_provider_and_warns_when_one_will_pass_its_budget()
    {
        using var context = Context(services =>
            services.AddSingleton<ICostInsights>(new BudgetedCostInsights()));

        var pane = context.Render<DashboardPane>();

        Assert.Contains("Claude", pane.Find("[data-testid='dashboard-overview-tile-spend-claude']").TextContent, StringComparison.Ordinal);
        Assert.Contains("Copilot", pane.Find("[data-testid='dashboard-overview-tile-spend-copilot']").TextContent, StringComparison.Ordinal);

        Assert.Contains("of", pane.Find("[data-testid='dashboard-overview-tile-spend-claude']").TextContent, StringComparison.Ordinal);
        Assert.Contains("vs same days last month", pane.Find("[data-testid='dashboard-overview-tile-spend-claude']").TextContent, StringComparison.Ordinal);

        var chip = pane.Find("[data-testid='dashboard-overview-tile-spend-chip']");
        Assert.Equal("Claude will pass budget", chip.TextContent.Trim());
        Assert.Contains("dashboard-overview-chip--warning", chip.ClassName, StringComparison.Ordinal);

        // No line: the only spend by day adds the providers together.
        Assert.Empty(pane.FindAll("[data-testid='dashboard-overview-tile-spend-trend']"));
    }

    /// <summary>
    /// Spend by model is one bar per currency: a month billed in two currencies draws two
    /// bars rather than one that adds them, and a model with no amount adds nothing.
    /// </summary>
    [Fact]
    public void Spend_by_model_draws_one_bar_per_currency()
    {
        using var context = Context(services =>
            services.AddSingleton<ICostInsights>(new BudgetedCostInsights()));

        var pane = context.Render<DashboardPane>();

        var part = pane.Find("[data-testid='dashboard-overview-spend-model']");
        Assert.NotNull(part.QuerySelector("[data-testid='dashboard-overview-spend-model-bar-eur']"));
        Assert.NotNull(part.QuerySelector("[data-testid='dashboard-overview-spend-model-bar-usd']"));
        Assert.Contains("opus", part.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("haiku", part.TextContent, StringComparison.Ordinal);
    }

    /// <summary>
    /// A figure that moved the bad way reads on the warning tone: fewer pull requests
    /// merged than in the window before.
    /// </summary>
    [Fact]
    public void A_figure_that_moved_the_bad_way_reads_on_the_warning_tone()
    {
        using var context = Context(services =>
            services.AddSingleton<IProductivityInsights>(new ReadyProductivityInsights(previousMerged: 30)));

        var pane = context.Render<DashboardPane>();

        var chip = pane.Find("[data-testid='dashboard-overview-tile-pulls-chip']");
        Assert.Equal("down 7 vs previous 12 weeks", chip.TextContent.Trim());
        Assert.Contains("dashboard-overview-chip--warning", chip.ClassName, StringComparison.Ordinal);
    }

    /// <summary>
    /// Story points done are set out of the roadmap's planned effort, and a roadmap item
    /// behind its pace takes the chip from the delta, on the warning tone. The roadmap
    /// against pace draws the same items, one row each with its outlook as a chip.
    /// </summary>
    [Fact]
    public void Story_points_and_the_roadmap_rows_say_which_items_are_behind()
    {
        using var context = Context(services =>
            services.AddSingleton<ITaskInsights>(new PlannedTaskInsights()));

        var pane = context.Render<DashboardPane>();

        var tile = pane.Find("[data-testid='dashboard-overview-tile-points']");
        Assert.Contains("8", tile.TextContent, StringComparison.Ordinal);
        Assert.Contains("of 21 planned", tile.TextContent, StringComparison.Ordinal);
        var chip = pane.Find("[data-testid='dashboard-overview-tile-points-chip']");
        Assert.Equal("1 item behind pace", chip.TextContent.Trim());
        Assert.Contains("dashboard-overview-chip--warning", chip.ClassName, StringComparison.Ordinal);

        var rows = pane.FindAll("[data-testid='dashboard-overview-roadmap-row']");
        Assert.Equal(2, rows.Count);
        Assert.Contains("Multi-account support", rows[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("5 / 13 pts", rows[0].TextContent, StringComparison.Ordinal);
        Assert.Equal(
            ["Behind", "Finished"],
            pane.FindAll("[data-testid='dashboard-overview-roadmap-chip']").Select(found => found.TextContent.Trim()));
        Assert.Contains("Pace", pane.Find("[data-testid='dashboard-overview-roadmap-summary']").TextContent, StringComparison.Ordinal);
    }

    /// <summary>
    /// Hours worked is one bar split at office hours, with the hours worked against the
    /// hours planned.
    /// </summary>
    [Fact]
    public void Hours_worked_splits_inside_and_outside_office_hours()
    {
        using var context = Context(services =>
            services.AddSingleton<IHoursWorkedInsights>(new FixedHoursWorkedInsights()));

        var pane = context.Render<DashboardPane>();

        Assert.Equal("31.0h of 36.0h planned", pane.Find("[data-testid='dashboard-overview-hours-summary']").TextContent.Trim());
        var bar = pane.Find("[data-testid='dashboard-overview-hours-bar']").TextContent;
        Assert.Contains("Inside office hours", bar, StringComparison.Ordinal);
        Assert.Contains("Outside office hours", bar, StringComparison.Ordinal);
        Assert.Contains("24.0h", bar, StringComparison.Ordinal);
        Assert.Contains("7.0h", bar, StringComparison.Ordinal);
    }

    /// <summary>
    /// The empty state: nothing qualifies and every source answered, so the list says
    /// "Nothing needs you." and nothing else — no list, no skipped sources.
    /// </summary>
    [Fact]
    public void Needs_you_says_nothing_needs_you_when_nothing_qualifies()
    {
        using var context = Context(services =>
            services.AddSingleton<IAttentionInsights>(new FixedAttentionInsights(NeedsYouInsight.Empty)));

        var pane = context.Render<DashboardPane>();

        var part = pane.Find("[data-testid='dashboard-overview-needs-you']");
        Assert.Equal(NeedsYouInsight.NothingNeedsYou, pane.Find("[data-testid='dashboard-overview-needs-you-empty']").TextContent.Trim());
        Assert.Empty(part.QuerySelectorAll("[data-testid='dashboard-overview-needs-you-item']"));
        Assert.Empty(part.QuerySelectorAll("[data-testid='dashboard-overview-needs-you-skipped']"));
    }

    /// <summary>
    /// An empty list over a source that could not be read is a floor, not an all-clear:
    /// the reasons follow the sentence.
    /// </summary>
    [Fact]
    public void Needs_you_names_the_skipped_sources_under_an_empty_list()
    {
        using var context = Context(services =>
            services.AddSingleton<IAttentionInsights>(new FixedAttentionInsights(
                new NeedsYouInsight([], ["The month-end projection could not be read: no key."]))));

        var pane = context.Render<DashboardPane>();

        Assert.Equal(NeedsYouInsight.NothingNeedsYou, pane.Find("[data-testid='dashboard-overview-needs-you-empty']").TextContent.Trim());
        Assert.Contains(
            "The month-end projection could not be read: no key.",
            pane.Find("[data-testid='dashboard-overview-needs-you-skipped']").TextContent,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Each item links to its tab: following the link moves the pane's selection there.
    /// </summary>
    [Fact]
    public void A_needs_you_item_opens_the_tab_it_links_to()
    {
        using var context = Context(services =>
            services.AddSingleton<IAttentionInsights>(new FixedAttentionInsights(new NeedsYouInsight(
            [
                new NeedsYouItem(NeedsYouRule.Drift, "Sync failed on domain/sync", "A sweep will not retry until the label is cleared.", NeedsYouTab.Devbook),
                new NeedsYouItem(NeedsYouRule.PastBudget, "Claude will pass its budget", "Projected past it on the 28th.", NeedsYouTab.Cost)
            ],
            []))));

        context.JSInterop.SetupVoid("Blazor._internal.domWrapper.focus", _ => true);

        var pane = context.Render<DashboardPane>();

        var items = pane.FindAll("[data-testid='dashboard-overview-needs-you-item']");
        Assert.Equal(2, items.Count);
        Assert.Contains("Sync failed on domain/sync", items[0].TextContent, StringComparison.Ordinal);

        // The link is named by its item, so two links to one tab are told apart.
        var link = pane.Find("[data-testid='dashboard-overview-needs-you-open-cost']");
        var titleId = link.GetAttribute("aria-describedby");
        Assert.Equal("Claude will pass its budget", pane.Find($"#{titleId}").TextContent.Trim());

        link.Click();

        pane.WaitForAssertion(() =>
            Assert.Equal("true", pane.Find("[data-testid='dashboard-tab-cost']").GetAttribute("aria-selected")));

        // The link that was pressed went with the Overview panel, so focus goes to the
        // tab it opened rather than falling to the document.
        Assert.Single(context.JSInterop.Invocations["Blazor._internal.domWrapper.focus"]);
    }

    private static BunitContext Context(Action<IServiceCollection>? configure = null)
    {
        var context = new BunitContext();

        _ = context.Services.AddUnavailableDashboard("backlog", "backlog-ide");
        configure?.Invoke(context.Services);

        return context;
    }

    private static IReadOnlyList<InsightPoint> Weeks(params decimal[] values) =>
        [.. values.Select((value, index) => new InsightPoint($"W{38 + index}", value))];

    private sealed class FixedAttentionInsights(NeedsYouInsight insight) : IAttentionInsights
    {
        public Task<NeedsYouInsight> GetNeedsYouAsync(
            DashboardScope scope,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(insight);
    }

    private sealed class FixedDriftInsights(DriftInsight insight) : IDriftInsights
    {
        public Task<InsightResult<DriftInsight>> GetDriftAsync(
            DashboardScope scope,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<DriftInsight>.Ready(insight));
    }

    private sealed class ReadyProductivityInsights(int previousMerged = 19) : IProductivityInsights
    {
        public Task<InsightResult<ProductivityHeadline>> GetHeadlineAsync(
            DashboardScope scope,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<ProductivityHeadline>.Ready(new ProductivityHeadline(
                23,
                13,
                0.17m,
                TimeSpan.FromHours(3.2),
                Weeks(4, 5, 6, 8),
                Weeks(2, 3, 4, 4),
                Weeks(0.1m, 0.2m, 0.1m, 0.2m))
            {
                PreviousPullRequestsMerged = previousMerged,
                MedianCommitsPerPullRequest = 5,
                PullRequestsWithCommitCount = 23
            }));

        public Task<InsightResult<ProductivityScoreInsight>> GetScoreAsync(
            DashboardScope scope,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<ProductivityScoreInsight>.Unavailable(DashboardTestHost.UnavailableReason));

        public Task<InsightResult<ProductivityTrend>> GetTrendAsync(
            DashboardScope scope,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<ProductivityTrend>.Unavailable(DashboardTestHost.UnavailableReason));

        public Task<InsightResult<ReworkInsight>> GetReworkAsync(
            DashboardScope scope,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<ReworkInsight>.Unavailable(DashboardTestHost.UnavailableReason));

        public void Invalidate(DashboardScope scope)
        {
        }
    }

    private sealed class BudgetedCostInsights : ICostInsights
    {
        private static readonly DateOnly MonthStart = new(2026, 10, 1);
        private static readonly DateOnly Through = new(2026, 10, 12);

        public Task<InsightResult<SpendThisMonthInsight>> GetThisMonthAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<SpendThisMonthInsight>.Ready(new SpendThisMonthInsight(
            [
                new MonthlySpend(SpendProvider.Claude, new DashboardMoney(212m, "EUR"), null, MonthStart, Through, IsEstimate: true)
                {
                    PreviousSpend = new DashboardMoney(180m, "EUR")
                },
                new MonthlySpend(SpendProvider.Copilot, new DashboardMoney(10m, "USD"), new DashboardMoney(5m, "USD"), MonthStart, Through, IsEstimate: false)
            ])));

        public Task<InsightResult<SpendTrendInsight>> GetTrendAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<SpendTrendInsight>.Unavailable(DashboardTestHost.UnavailableReason));

        public Task<InsightResult<SpendByModelInsight>> GetByModelAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<SpendByModelInsight>.Ready(new SpendByModelInsight(
            [
                new InsightRow("opus", 1_000, new DashboardMoney(150m, "EUR"), "Claude"),
                new InsightRow("sonnet", 500, new DashboardMoney(62m, "EUR"), "Claude"),
                new InsightRow("haiku", 10, new DashboardMoney(0m, "EUR"), "Claude"),
                new InsightRow("gpt-5", null, new DashboardMoney(10m, "USD"), "Copilot")
            ])));

        public Task<InsightResult<SpendProjectionInsight>> GetProjectionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<SpendProjectionInsight>.Ready(new SpendProjectionInsight(
            [
                new SpendProjection(
                    SpendProvider.Claude,
                    new DashboardMoney(212m, "EUR"),
                    new DashboardMoney(10m, "EUR"),
                    new DashboardMoney(402m, "EUR"),
                    new DashboardMoney(300m, "EUR"),
                    Through,
                    DaysLeft: 19,
                    IsEstimate: true),
                new SpendProjection(
                    SpendProvider.Copilot,
                    new DashboardMoney(10m, "USD"),
                    new DashboardMoney(1m, "USD"),
                    new DashboardMoney(29m, "USD"),
                    new DashboardMoney(50m, "USD"),
                    Through,
                    DaysLeft: 19,
                    IsEstimate: false)
            ])));

        public Task<InsightResult<SpendByDayInsight>> GetByDayAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<SpendByDayInsight>.Unavailable("Two currencies cannot be added."));

        public void Invalidate()
        {
        }
    }

    private sealed class PlannedTaskInsights : ITaskInsights
    {
        public Task<InsightResult<TaskThroughputInsight>> GetThroughputAsync(
            DashboardScope scope,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<TaskThroughputInsight>.Ready(new TaskThroughputInsight(
                Weeks(1, 2, 1),
                Weeks(2, 3, 3),
                Unestimated: 0)
            {
                PreviousEffort = 5
            }));

        public Task<InsightResult<PlanInsight>> GetPlanAsync(
            DashboardScope scope,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<PlanInsight>.Ready(new PlanInsight(
                true,
                new PlanPace(11m, PlanPaceBasis.LastFourWeeks),
                [
                    new PlanItemInsight(Item("Multi-account support", total: 13, done: 5, finished: false), PlanOutlook.Behind, new DateOnly(2026, 11, 2)),
                    new PlanItemInsight(Item("Sync UI: devices", total: 8, done: 8, finished: true), PlanOutlook.Finished, new DateOnly(2026, 10, 1))
                ])));

        private static PlanItemProgress Item(string title, int total, int done, bool finished) =>
            new(
                Guid.NewGuid(),
                title,
                new DateOnly(2026, 9, 21),
                new DateOnly(2026, 10, 23),
                ["backlog"],
                GatheredCount: 3,
                DoneCount: finished ? 3 : 1,
                TotalEffort: total,
                DoneEffort: done,
                Unestimated: 0,
                IsFinished: finished,
                LastCompletedOn: null,
                Parts: [],
                PlacedByEffort: false);
    }

    private sealed class FixedHoursWorkedInsights : IHoursWorkedInsights
    {
        public Task<InsightResult<HoursWorkedInsight>> GetHoursWorkedAsync(
            DashboardScope scope,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<HoursWorkedInsight>.Ready(new HoursWorkedInsight(
            [
                new HoursWorkedPeriod(new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 5), TimeSpan.FromHours(20), TimeSpan.FromHours(3), TimeSpan.FromHours(28)),
                new HoursWorkedPeriod(new DateOnly(2026, 10, 6), new DateOnly(2026, 10, 6), TimeSpan.FromHours(4), TimeSpan.FromHours(4), TimeSpan.FromHours(8))
            ],
            [])));
    }
}
