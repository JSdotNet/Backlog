using Backlog.Modules.Dashboard.Abstractions;
using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Dashboard.UI.Parts;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The brief's Plan and Cost figures (S10, AC34): running totals against the plan and
/// against each budget, with the week's own amount in the headline beside them; and the
/// title ", at a cost" when a provider passes its budget.
/// </summary>
public sealed class DashboardBriefFiguresTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_plan_figure_is_the_running_total_against_the_plan()
    {
        using var context = Context();

        var part = context.Render<BriefPlanPart>(parameters => parameters
            .Add(p => p.Scope, DashboardScope.Default)
            .Add(p => p.Week, BriefWeek.Of(Now, 0)));

        part.WaitForAssertion(() =>
            Assert.StartsWith("41 of 55", part.Find("[data-testid='dashboard-brief-plan-figure']").TextContent.Trim(), StringComparison.Ordinal));
        Assert.Contains("41 story points done", part.Find("[data-testid='dashboard-brief-plan-headline']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_cost_figure_is_each_providers_month_against_its_budget()
    {
        using var context = Context();

        var part = context.Render<BriefCostPart>(parameters => parameters
            .Add(p => p.Scope, DashboardScope.Default)
            .Add(p => p.Week, BriefWeek.Of(Now, 0)));

        part.WaitForAssertion(() =>
            Assert.StartsWith("€178 of €250 · €34 of €50", part.Find("[data-testid='dashboard-brief-cost-figure']").TextContent.Trim(), StringComparison.Ordinal));
        Assert.Contains("€41 spent", part.Find("[data-testid='dashboard-brief-cost-headline']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_title_adds_at_a_cost_when_a_provider_passes_its_budget()
    {
        using var context = Context();

        var title = context.Render<BriefTitle>(parameters => parameters
            .Add(p => p.Scope, DashboardScope.Default)
            .Add(p => p.Week, BriefWeek.Of(Now, 0)));

        title.WaitForAssertion(() =>
            Assert.Equal("Your best shipping week this quarter, at a cost.", title.Markup.Trim()));
    }

    /// <summary>The running totals and ", at a cost" are about the plan and the month as
    /// they stand now, so a week paged back to shows its own figures instead (review,
    /// Batch D).</summary>
    [Fact]
    public void An_earlier_week_shows_its_own_figures_not_the_months()
    {
        using var context = Context();
        var earlier = BriefWeek.Of(Now, 1);

        var plan = context.Render<BriefPlanPart>(parameters => parameters.Add(p => p.Scope, DashboardScope.Default).Add(p => p.Week, earlier));
        var cost = context.Render<BriefCostPart>(parameters => parameters.Add(p => p.Scope, DashboardScope.Default).Add(p => p.Week, earlier));
        var title = context.Render<BriefTitle>(parameters => parameters.Add(p => p.Scope, DashboardScope.Default).Add(p => p.Week, earlier));

        plan.WaitForAssertion(() => Assert.StartsWith("40", plan.Find("[data-testid='dashboard-brief-plan-figure']").TextContent.Trim(), StringComparison.Ordinal));
        Assert.DoesNotContain(" of ", plan.Find("[data-testid='dashboard-brief-plan-figure']").TextContent, StringComparison.Ordinal);
        cost.WaitForAssertion(() => Assert.StartsWith("€40", cost.Find("[data-testid='dashboard-brief-cost-figure']").TextContent.Trim(), StringComparison.Ordinal));
        title.WaitForAssertion(() => Assert.DoesNotContain("at a cost", title.Markup, StringComparison.Ordinal));
    }

    private static BunitContext Context()
    {
        var context = new BunitContext();
        _ = context.Services.AddUnavailableDashboard("backlog");
        context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
        context.Services.AddSingleton<IProductivityInsights>(new Productivity());
        context.Services.AddSingleton<ITaskInsights>(new Tasks());
        context.Services.AddSingleton<ICostInsights>(new Costs());
        return context;
    }

    /// <summary>Thirteen weeks, W30 to the running W42, each worth its own number.</summary>
    private static IReadOnlyList<InsightPoint> Weeks() =>
        [.. Enumerable.Range(30, 13).Select(week => new InsightPoint($"W{week}", week))];

    private sealed class Productivity : IProductivityInsights
    {
        public Task<InsightResult<ProductivityHeadline>> GetHeadlineAsync(DashboardScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<ProductivityHeadline>.Ready(new ProductivityHeadline(450, 450, 0m, null, Weeks(), Weeks(), [])));

        public Task<InsightResult<ProductivityScoreInsight>> GetScoreAsync(DashboardScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<ProductivityScoreInsight>.Unavailable("Not used."));

        public Task<InsightResult<ReworkInsight>> GetReworkAsync(DashboardScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<ReworkInsight>.Unavailable("Not used."));

        public void Invalidate(DashboardScope scope)
        {
        }
    }

    private sealed class Tasks : ITaskInsights
    {
        public Task<InsightResult<TaskThroughputInsight>> GetThroughputAsync(DashboardScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<TaskThroughputInsight>.Ready(new TaskThroughputInsight(Weeks(), Weeks(), 0)));

        public Task<InsightResult<PlanInsight>> GetPlanAsync(DashboardScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<PlanInsight>.Ready(new PlanInsight(
                true,
                new PlanPace(5m, PlanPaceBasis.LastFourWeeks),
                [
                    new PlanItemInsight(
                        new PlanItemProgress(Guid.NewGuid(), "Sync", new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 30), [], 3, 1, 55, 41, 0, false, null, [], false),
                        PlanOutlook.OnTrack,
                        null)
                ])));
    }

    private sealed class Costs : ICostInsights
    {
        public Task<InsightResult<SpendByWeekInsight>> GetByWeekAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<SpendByWeekInsight>.Ready(new SpendByWeekInsight(Weeks(), "EUR", false)));

        public Task<InsightResult<SpendProjectionInsight>> GetProjectionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<SpendProjectionInsight>.Ready(new SpendProjectionInsight(
            [
                Projection(SpendProvider.Claude, 178m, 300m, 250m),
                Projection(SpendProvider.AzureFoundry, 34m, 46m, 50m)
            ])));

        public Task<InsightResult<SpendThisMonthInsight>> GetThisMonthAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<SpendThisMonthInsight>.Unavailable("Not used."));

        public Task<InsightResult<SpendByModelInsight>> GetByModelAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<SpendByModelInsight>.Unavailable("Not used."));

        public Task<InsightResult<SpendByDayInsight>> GetByDayAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<SpendByDayInsight>.Unavailable("Not used."));

        public void Invalidate()
        {
        }

        private static SpendProjection Projection(SpendProvider provider, decimal spent, decimal projected, decimal budget) =>
            new(
                provider,
                new DashboardMoney(spent, "EUR"),
                new DashboardMoney(1m, "EUR"),
                new DashboardMoney(projected, "EUR"),
                new DashboardMoney(budget, "EUR"),
                new DateOnly(2026, 10, 14),
                17,
                IsEstimate: false);
    }
}
