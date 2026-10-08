using Backlog.Modules.Dashboard.Abstractions;
using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Dashboard.UI.Parts;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The headline tiles' comparison with the window before: a line under the figure when
/// the insight carried an earlier one, and no line at all when it did not.
/// <para>
/// Through the parts over scripted insights rather than through the module, because what
/// is asserted here is the wiring — which figure each tile compares, in what words — and
/// the arithmetic behind the earlier figures is the module's own tests' business.
/// </para>
/// </summary>
public sealed class DashboardDeltaPartTests
{
    private static readonly DashboardScope FourWeeks = new(Period: DashboardPeriod.FourWeeks);

    [Fact]
    public void The_merged_pull_requests_tile_says_how_far_it_moved_against_the_previous_window()
    {
        using var context = Context(services => services.AddSingleton<IProductivityInsights>(
            new ScriptedProductivity(Headline(merged: 11) with { PreviousPullRequestsMerged = 8 })));

        var part = context.Render<HeadlinePart>(parameters => parameters.Add(p => p.Scope, FourWeeks));

        part.WaitForAssertion(() => Assert.Contains(
            "up 3 vs previous 4 weeks",
            part.Find("[data-testid='dashboard-headline-pulls']").TextContent,
            StringComparison.Ordinal));

        // Only the merged count is compared; the tiles beside it carry no line.
        Assert.DoesNotContain("vs previous", part.Find("[data-testid='dashboard-headline-issues']").TextContent, StringComparison.Ordinal);
    }

    /// <summary>No earlier figure is no line, never "unchanged" or a line against zero:
    /// either would be a claim about a window nobody counted.</summary>
    [Fact]
    public void The_merged_pull_requests_tile_draws_no_comparison_when_there_is_no_previous_figure()
    {
        using var context = Context(services => services.AddSingleton<IProductivityInsights>(
            new ScriptedProductivity(Headline(merged: 11))));

        var part = context.Render<HeadlinePart>(parameters => parameters.Add(p => p.Scope, FourWeeks));

        part.WaitForAssertion(() => Assert.Contains("11", part.Find("[data-testid='dashboard-headline-pulls']").TextContent, StringComparison.Ordinal));
        Assert.DoesNotContain("vs previous", part.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void The_comparison_names_the_period_the_reader_picked()
    {
        using var context = Context(services => services.AddSingleton<IProductivityInsights>(
            new ScriptedProductivity(Headline(merged: 5) with { PreviousPullRequestsMerged = 9 })));

        var part = context.Render<HeadlinePart>(parameters => parameters.Add(p => p.Scope, DashboardScope.Default));

        part.WaitForAssertion(() => Assert.Contains(
            "down 4 vs previous 12 weeks",
            part.Find("[data-testid='dashboard-headline-pulls']").TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public void The_agent_active_time_tile_compares_as_a_share_and_only_when_there_is_an_earlier_figure()
    {
        using var compared = Context(services => services.AddSingleton<ISessionInsights>(
            new ScriptedSessions(Sessions(TimeSpan.FromHours(5)) with { PreviousActiveTime = TimeSpan.FromHours(4) })));
        using var alone = Context(services => services.AddSingleton<ISessionInsights>(
            new ScriptedSessions(Sessions(TimeSpan.FromHours(5)))));

        var withDelta = compared.Render<SessionsPart>(parameters => parameters.Add(p => p.Scope, FourWeeks));
        var withoutDelta = alone.Render<SessionsPart>(parameters => parameters.Add(p => p.Scope, FourWeeks));

        withDelta.WaitForAssertion(() => Assert.Contains(
            "up 25% vs previous 4 weeks",
            withDelta.Find("[data-testid='dashboard-sessions-active']").TextContent,
            StringComparison.Ordinal));

        withoutDelta.WaitForAssertion(() => Assert.Contains(
            "5h",
            withoutDelta.Find("[data-testid='dashboard-sessions-active']").TextContent,
            StringComparison.Ordinal));
        Assert.DoesNotContain("vs previous", withoutDelta.Find("[data-testid='dashboard-sessions-active']").TextContent, StringComparison.Ordinal);
    }

    /// <summary>Spend compares with the same days of last month, and more of it is the
    /// worse direction; a provider with no earlier figure — Copilot, always — has no
    /// line.</summary>
    [Fact]
    public void Each_spend_tile_compares_with_the_same_days_of_last_month_where_there_is_a_figure()
    {
        var month = new SpendThisMonthInsight(
        [
            new MonthlySpend(SpendProvider.Claude, new DashboardMoney(12m, "USD"), null, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 19), true)
            {
                PreviousSpend = new DashboardMoney(8m, "USD")
            },
            new MonthlySpend(SpendProvider.Copilot, new DashboardMoney(3m, "USD"), null, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 19), false)
        ]);

        using var context = Context(services => services.AddSingleton<ICostInsights>(new ScriptedCosts(month)));

        var part = context.Render<SpendThisMonthPart>(parameters => parameters.Add(p => p.Scope, FourWeeks));

        part.WaitForAssertion(() =>
        {
            var claude = part.Find("[data-testid='dashboard-spend-month-claude']");
            Assert.Contains("up 50% vs same days last month", claude.TextContent, StringComparison.Ordinal);
            Assert.Contains("metric-tile__delta--worse", claude.InnerHtml, StringComparison.Ordinal);
        });

        Assert.DoesNotContain("vs same days", part.Find("[data-testid='dashboard-spend-month-copilot']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_effort_part_puts_the_story_points_done_above_the_columns_with_the_comparison()
    {
        var throughput = new TaskThroughputInsight(
            [new InsightPoint("W37", 1), new InsightPoint("W38", 2)],
            [new InsightPoint("W37", 3), new InsightPoint("W38", 5)],
            Unestimated: 0)
        {
            PreviousEffort = 6m
        };

        using var context = Context(services => services.AddSingleton<ITaskInsights>(new ScriptedTasks(throughput)));

        var part = context.Render<TaskEffortPart>(parameters => parameters.Add(p => p.Scope, FourWeeks));

        part.WaitForAssertion(() =>
        {
            var tile = part.Find("[data-testid='dashboard-tasks-effort-done']");
            Assert.Contains("Story points done", tile.TextContent, StringComparison.Ordinal);
            Assert.Equal("8", tile.QuerySelector(".metric-tile__value")!.TextContent.Trim());
            Assert.Contains("up 2 vs previous 4 weeks", tile.TextContent, StringComparison.Ordinal);
        });

        // Above the columns, in the order a reader meets them.
        var stack = part.Find("[data-testid='dashboard-tasks-effort'] .dashboard-part__stack");
        Assert.Equal(
            ["dashboard-tasks-effort-done", "dashboard-tasks-effort-bars"],
            stack.Children.Select(child => child.GetAttribute("data-testid")));
    }

    [Fact]
    public void The_story_points_tile_draws_no_comparison_when_there_is_no_previous_figure()
    {
        var throughput = new TaskThroughputInsight([new InsightPoint("W38", 2)], [new InsightPoint("W38", 5)], Unestimated: 0);

        using var context = Context(services => services.AddSingleton<ITaskInsights>(new ScriptedTasks(throughput)));

        var part = context.Render<TaskEffortPart>(parameters => parameters.Add(p => p.Scope, FourWeeks));

        part.WaitForAssertion(() => Assert.NotNull(part.Find("[data-testid='dashboard-tasks-effort-done']")));
        Assert.DoesNotContain("vs previous", part.Find("[data-testid='dashboard-tasks-effort-done']").TextContent, StringComparison.Ordinal);
    }

    private static BunitContext Context(Action<IServiceCollection> configure)
    {
        var context = new BunitContext();

        _ = context.Services.AddUnavailableDashboard("backlog");
        configure(context.Services);

        return context;
    }

    private static ProductivityHeadline Headline(int merged) =>
        new(merged, 4, 0.25m, TimeSpan.FromHours(6), [new InsightPoint("W33", merged)], [new InsightPoint("W33", 4)], [new InsightPoint("W33", 0.25m)]);

    private static AssistantSessionsInsight Sessions(TimeSpan active) =>
        new(3, active, new DateTimeOffset(2026, 8, 19, 9, 30, 0, TimeSpan.Zero), 0, false, [], [])
        {
            IdleAfter = TimeSpan.FromMinutes(5)
        };

    private sealed class ScriptedProductivity(ProductivityHeadline headline) : IProductivityInsights
    {
        public Task<InsightResult<ProductivityHeadline>> GetHeadlineAsync(DashboardScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<ProductivityHeadline>.Ready(headline));

        public Task<InsightResult<ProductivityScoreInsight>> GetScoreAsync(DashboardScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<ProductivityScoreInsight>.Unavailable(DashboardTestHost.UnavailableReason));

        public Task<InsightResult<ProductivityTrend>> GetTrendAsync(DashboardScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<ProductivityTrend>.Unavailable(DashboardTestHost.UnavailableReason));

        public Task<InsightResult<ReworkInsight>> GetReworkAsync(DashboardScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<ReworkInsight>.Unavailable(DashboardTestHost.UnavailableReason));

        public void Invalidate(DashboardScope scope)
        {
        }
    }

    private sealed class ScriptedSessions(AssistantSessionsInsight insight) : ISessionInsights
    {
        public Task<InsightResult<AssistantSessionsInsight>> GetSessionsAsync(DashboardScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<AssistantSessionsInsight>.Ready(insight));

        public void Invalidate()
        {
        }
    }

    private sealed class ScriptedCosts(SpendThisMonthInsight month) : ICostInsights
    {
        public Task<InsightResult<SpendThisMonthInsight>> GetThisMonthAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<SpendThisMonthInsight>.Ready(month));

        public Task<InsightResult<SpendTrendInsight>> GetTrendAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<SpendTrendInsight>.Unavailable(DashboardTestHost.UnavailableReason));

        public Task<InsightResult<SpendByModelInsight>> GetByModelAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<SpendByModelInsight>.Unavailable(DashboardTestHost.UnavailableReason));

        public Task<InsightResult<SpendProjectionInsight>> GetProjectionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<SpendProjectionInsight>.Unavailable(DashboardTestHost.UnavailableReason));

        public void Invalidate()
        {
        }
    }

    private sealed class ScriptedTasks(TaskThroughputInsight throughput) : ITaskInsights
    {
        public Task<InsightResult<TaskThroughputInsight>> GetThroughputAsync(DashboardScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<TaskThroughputInsight>.Ready(throughput));

        public Task<InsightResult<PlanInsight>> GetPlanAsync(DashboardScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<PlanInsight>.Unavailable(DashboardTestHost.UnavailableReason));
    }
}
