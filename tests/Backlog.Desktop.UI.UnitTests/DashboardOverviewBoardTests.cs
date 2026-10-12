using Backlog.Modules.Dashboard.Abstractions;
using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Dashboard.UI;
using Backlog.Modules.Dashboard.UI.Parts;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Overview against board Main (S4): Needs you's subtitle and action phrases, the
/// throughput's churned-of-reviewed figure, the roadmap's chip words and a missing pace,
/// and refreshes that move every figure they could have changed (entry 19, AC11).
/// </summary>
public sealed class DashboardOverviewBoardTests
{
    [Fact]
    public void Needs_you_says_everything_else_is_fine_and_links_with_the_rules_action()
    {
        var item = new NeedsYouItem(NeedsYouRule.Rework, "6 pull requests came back after review", "6 of 20 reviewed came back.", NeedsYouTab.Productivity);
        using var context = Context(services =>
            services.AddSingleton<IAttentionInsights>(new FixedAttention(new NeedsYouInsight([item], []))));

        var pane = context.Render<DashboardPane>();

        Assert.Equal(
            "Everything below is fine. These are not.",
            pane.Find("[data-testid='dashboard-overview-needs-you-subtitle']").TextContent.Trim());
        var link = pane.Find("[data-testid='dashboard-overview-needs-you-open-productivity']");
        Assert.Equal("See rework →", link.TextContent.Trim());
        Assert.DoesNotContain("Open", link.TextContent, StringComparison.Ordinal);
    }

    /// <summary>With nothing to list, the subtitle that introduces the exceptions is not
    /// drawn above "Nothing needs you."</summary>
    [Fact]
    public void The_subtitle_is_only_drawn_over_items()
    {
        using var context = Context(services =>
            services.AddSingleton<IAttentionInsights>(new FixedAttention(NeedsYouInsight.Empty)));

        var pane = context.Render<DashboardPane>();

        pane.WaitForAssertion(() => Assert.NotNull(pane.Find("[data-testid='dashboard-overview-needs-you-empty']")));
        Assert.Empty(pane.FindAll("[data-testid='dashboard-overview-needs-you-subtitle']"));
    }

    /// <summary>Refreshing Needs you drops every cache it reads through, not none.</summary>
    [Fact]
    public void Refreshing_needs_you_drops_the_drift_productivity_and_cost_reads()
    {
        var drift = new CountingDrift();
        var productivity = new Productivity();
        var costs = new CountingCosts();
        using var context = Context(services =>
        {
            services.AddSingleton<IDriftInsights>(drift);
            services.AddSingleton<IProductivityInsights>(productivity);
            services.AddSingleton<ICostInsights>(costs);
        });

        var pane = context.Render<DashboardPane>();
        pane.Find("[data-testid='dashboard-overview-needs-you-refresh']").Click();

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(1, drift.Invalidations);
            Assert.Equal(1, productivity.Invalidations);
            Assert.Equal(1, costs.Invalidations);
        });
    }

    /// <summary>
    /// A tile that could not be read offers to try again, and becomes ready when its
    /// source answers — without the scope changing or the pane reopening.
    /// </summary>
    [Fact]
    public void An_unavailable_tile_becomes_ready_when_tried_again()
    {
        var productivity = new Productivity { FailFirst = true };
        using var context = Context(services => services.AddSingleton<IProductivityInsights>(productivity));

        var pane = context.Render<DashboardPane>();
        var tile = pane.Find("[data-testid='dashboard-overview-tile-pulls']");
        Assert.Contains("Not answered yet.", tile.TextContent, StringComparison.Ordinal);

        productivity.FailFirst = false;
        pane.Find("[data-testid='dashboard-overview-tile-pulls-retry']").Click();

        pane.WaitForAssertion(() =>
        {
            var ready = pane.Find("[data-testid='dashboard-overview-tile-pulls']");
            Assert.Contains("12", ready.TextContent, StringComparison.Ordinal);
            Assert.Empty(pane.FindAll("[data-testid='dashboard-overview-tile-pulls-retry']"));
        });
    }

    /// <summary>Refreshing Throughput drops the shared headline read, and the pull
    /// requests tile beside it reads again rather than keeping the old figure.</summary>
    [Fact]
    public void Refreshing_throughput_moves_the_pull_requests_tile_too()
    {
        var productivity = new Productivity();
        using var context = Context(services => services.AddSingleton<IProductivityInsights>(productivity));

        var pane = context.Render<DashboardPane>();
        pane.WaitForAssertion(() =>
            Assert.Contains("12", pane.Find("[data-testid='dashboard-overview-tile-pulls']").TextContent, StringComparison.Ordinal));

        productivity.Merged = 40;
        pane.Find("[data-testid='dashboard-overview-throughput-refresh']").Click();

        pane.WaitForAssertion(() =>
            Assert.Contains("40", pane.Find("[data-testid='dashboard-overview-tile-pulls']").TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public void Throughput_reads_churned_of_reviewed_and_picks_the_latest_week()
    {
        using var context = Context(services => services.AddSingleton<IProductivityInsights>(new Productivity()));

        var pane = context.Render<DashboardPane>();

        pane.WaitForAssertion(() =>
            Assert.Equal("4 of 23", pane.Find("[data-testid='dashboard-overview-throughput-came-back']").TextContent.Trim()));
        var picked = pane.FindAll("[data-testid='dashboard-overview-throughput-bars'] .metric-stacked-bars__column--selected");
        Assert.Single(picked);
        Assert.Contains("W42", picked[0].TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void With_no_pace_the_roadmap_says_so_instead_of_zero_points_a_week()
    {
        using var context = Context(services => services.AddSingleton<ITaskInsights>(new Plan(pace: 0m)));

        var pane = context.Render<DashboardPane>();

        pane.WaitForAssertion(() =>
        {
            var summary = pane.Find("[data-testid='dashboard-overview-roadmap-summary']").TextContent;
            Assert.Contains("No pace", summary, StringComparison.Ordinal);
            Assert.DoesNotContain("0 pts/week", summary, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void The_roadmap_chips_use_the_boards_words()
    {
        using var context = Context(services => services.AddSingleton<ITaskInsights>(new Plan(pace: 5m)));

        var pane = context.Render<DashboardPane>();

        pane.WaitForAssertion(() => Assert.Equal(
            ["Behind", "Behind", "On track", "Not started", "Done"],
            pane.FindAll("[data-testid='dashboard-overview-roadmap-chip']").Select(chip => chip.TextContent.Trim())));
    }

    /// <summary>
    /// One definition of behind pace everywhere: an item past its end with work open is
    /// behind, as its chip says. Verify read "1 item behind pace" on this tile beside two
    /// Behind chips and "2 behind" on the Tasks tab.
    /// </summary>
    [Fact]
    public void The_points_tile_counts_overdue_items_as_behind_as_their_chips_do()
    {
        using var context = Context(services => services.AddSingleton<ITaskInsights>(new Plan(pace: 5m)));

        var pane = context.Render<DashboardPane>();

        pane.WaitForAssertion(() => Assert.Equal(
            "2 items behind pace",
            pane.Find("[data-testid='dashboard-overview-tile-points-chip']").TextContent.Trim()));
    }

    [Theory]
    [InlineData(PlanOutlook.Finished, 5, 5, "Done")]
    [InlineData(PlanOutlook.Overdue, 0, 5, "Behind")]
    [InlineData(PlanOutlook.Behind, 2, 5, "Behind")]
    [InlineData(PlanOutlook.OnTrack, 2, 5, "On track")]
    [InlineData(PlanOutlook.OnTrack, 0, 5, "Not started")]
    [InlineData(PlanOutlook.Unsized, 0, 0, "Not started")]
    [InlineData(PlanOutlook.NoPace, 0, 8, "Not started")]
    [InlineData(PlanOutlook.PlacedByEffort, 0, 8, "Not started")]
    [InlineData(PlanOutlook.NoPace, 3, 8, "On track")]
    [InlineData(PlanOutlook.PlacedByEffort, 1, 8, "On track")]
    public void Each_outlook_reads_as_the_nearest_board_word(PlanOutlook outlook, int done, int total, string word)
    {
        Assert.Equal(word, PlanOutlookFormat.Chip(Row(outlook, done, total)));
    }

    private static PlanItemInsight Row(PlanOutlook outlook, int done, int total, string title = "Item") =>
        new(
            new PlanItemProgress(
                Guid.NewGuid(),
                title,
                new DateOnly(2026, 9, 1),
                new DateOnly(2026, 10, 30),
                [],
                GatheredCount: 3,
                DoneCount: done > 0 ? 1 : 0,
                TotalEffort: total,
                DoneEffort: done,
                Unestimated: 0,
                IsFinished: outlook == PlanOutlook.Finished,
                LastCompletedOn: null,
                Parts: [],
                PlacedByEffort: outlook == PlanOutlook.PlacedByEffort),
            outlook,
            null);

    private static BunitContext Context(Action<IServiceCollection>? configure = null)
    {
        var context = new BunitContext();
        _ = context.Services.AddUnavailableDashboard("backlog");
        configure?.Invoke(context.Services);
        return context;
    }

    private sealed class FixedAttention(NeedsYouInsight insight) : IAttentionInsights
    {
        public Task<NeedsYouInsight> GetNeedsYouAsync(DashboardScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(insight);
    }

    private sealed class Productivity : IProductivityInsights
    {
        public bool FailFirst { get; set; }

        public int Merged { get; set; } = 12;

        public int Invalidations { get; private set; }

        public Task<InsightResult<ProductivityHeadline>> GetHeadlineAsync(DashboardScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(FailFirst
                ? InsightResult<ProductivityHeadline>.Unavailable("Not answered yet.")
                : InsightResult<ProductivityHeadline>.Ready(new ProductivityHeadline(
                    Merged, 3, 0.17m, TimeSpan.FromHours(3),
                    [new InsightPoint("W41", 5m), new InsightPoint("W42", Merged - 5m)],
                    [new InsightPoint("W41", 1m), new InsightPoint("W42", 2m)],
                    [])));

        public Task<InsightResult<ProductivityScoreInsight>> GetScoreAsync(DashboardScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<ProductivityScoreInsight>.Unavailable("Not used."));

        public Task<InsightResult<ReworkInsight>> GetReworkAsync(DashboardScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(FailFirst
                ? InsightResult<ReworkInsight>.Unavailable("Not answered yet.")
                : InsightResult<ReworkInsight>.Ready(new ReworkInsight(4, 23, 0, 0, 0, true, [])));

        public void Invalidate(DashboardScope scope) => Invalidations++;
    }

    private sealed class CountingDrift : IDriftInsights
    {
        public int Invalidations { get; private set; }

        public Task<InsightResult<DriftInsight>> GetDriftAsync(DashboardScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<DriftInsight>.Ready(new DriftInsight([], [], null)));

        public void Invalidate() => Invalidations++;
    }

    private sealed class CountingCosts : ICostInsights
    {
        public int Invalidations { get; private set; }

        public Task<InsightResult<SpendThisMonthInsight>> GetThisMonthAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<SpendThisMonthInsight>.Unavailable("Not configured."));

        public Task<InsightResult<SpendByModelInsight>> GetByModelAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<SpendByModelInsight>.Unavailable("Not configured."));

        public Task<InsightResult<SpendProjectionInsight>> GetProjectionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<SpendProjectionInsight>.Unavailable("Not configured."));

        public Task<InsightResult<SpendByDayInsight>> GetByDayAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<SpendByDayInsight>.Unavailable("Not configured."));

        public Task<InsightResult<SpendByWeekInsight>> GetByWeekAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<SpendByWeekInsight>.Unavailable("Not configured."));

        public void Invalidate() => Invalidations++;
    }

    private sealed class Plan(decimal pace) : ITaskInsights
    {
        public Task<InsightResult<TaskThroughputInsight>> GetThroughputAsync(DashboardScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<TaskThroughputInsight>.Ready(new TaskThroughputInsight([new InsightPoint("W42", 1m)], [new InsightPoint("W42", 3m)], 0)));

        public Task<InsightResult<PlanInsight>> GetPlanAsync(DashboardScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<PlanInsight>.Ready(new PlanInsight(
                true,
                new PlanPace(pace, PlanPaceBasis.LastFourWeeks),
                [
                    Row(PlanOutlook.Behind, 2, 8, "Late"),
                    Row(PlanOutlook.Overdue, 0, 5, "Overdue"),
                    Row(PlanOutlook.OnTrack, 3, 8, "Going"),
                    Row(PlanOutlook.OnTrack, 0, 5, "Later"),
                    Row(PlanOutlook.Finished, 5, 5, "Shipped")
                ])));
    }
}
