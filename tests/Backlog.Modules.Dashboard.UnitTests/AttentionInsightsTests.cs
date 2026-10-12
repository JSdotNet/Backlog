using Backlog.Modules.Dashboard.Abstractions;
using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Dashboard.Services;

namespace Backlog.Modules.Dashboard.UnitTests;

/// <summary>
/// The Needs-you items: one rule per source, in order — drift, roadmap items behind,
/// providers past budget, rework over a quarter — each read through its tab's insight,
/// and a source that cannot be read skipped rather than failing the list.
/// </summary>
public class AttentionInsightsTests
{
    private static readonly DateOnly Today = new(2026, 10, 8);

    [Fact]
    public async Task Nothing_needs_you_when_every_source_answers_clean()
    {
        var result = await Attention().GetNeedsYouAsync(DashboardScope.Default, TestContext.Current.CancellationToken);

        Assert.True(result.IsEmpty);
        Assert.Empty(result.Skipped);
        Assert.Equal("Nothing needs you.", NeedsYouInsight.NothingNeedsYou);
    }

    [Fact]
    public async Task A_unit_whose_drift_issue_carries_sync_failed_or_whose_verdict_is_conflict_needs_you()
    {
        var drift = new StubDrift(Drift(
            [
                Unit(".devbook/domain/inbox/domain.md#capture", "aligned") with { SyncFailed = true },
                Unit(".devbook/domain/tasks/domain.md#task", "Conflict"),
                Unit(".devbook/domain/roadmap/domain.md#plan", "code-ahead")
            ]));

        var result = await Attention(drift: drift).GetNeedsYouAsync(DashboardScope.Default, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, item => Assert.Equal(NeedsYouRule.Drift, item.Rule));
        Assert.All(result.Items, item => Assert.Equal("dashboard-devbook", item.TabId));
        Assert.Contains(".devbook/domain/inbox/domain.md#capture", result.Items[0].Title, StringComparison.Ordinal);
        Assert.Contains("sync-failed", result.Items[0].Detail, StringComparison.Ordinal);
        Assert.Contains(".devbook/domain/tasks/domain.md#task", result.Items[1].Title, StringComparison.Ordinal);
        Assert.Contains("conflict", result.Items[1].Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_sync_failed_issue_no_recorded_unit_matches_still_needs_you_once()
    {
        var drift = new StubDrift(Drift(
            [Unit(".devbook/domain/inbox/domain.md#capture", "aligned") with { SyncFailed = true }],
            new DriftIssue("backlog", 12, "[Devbook drift] .devbook/domain/inbox/domain.md#capture", "https://example.test/12", true),
            new DriftIssue("backlog", 14, "[Devbook drift] .devbook/domain/sessions/domain.md#run", "https://example.test/14", true),
            new DriftIssue("backlog", 15, "[Devbook drift] .devbook/domain/review/domain.md#x", "https://example.test/15", false)));

        var result = await Attention(drift: drift).GetNeedsYouAsync(DashboardScope.Default, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Items.Count);
        Assert.Contains(".devbook/domain/sessions/domain.md#run", result.Items[1].Title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_roadmap_item_whose_outlook_is_behind_needs_you_with_the_points_left()
    {
        var tasks = new StubTasks(InsightResult<PlanInsight>.Ready(new PlanInsight(
            true,
            new PlanPace(5m, PlanPaceBasis.LastFourWeeks),
            [
                Plan("Dashboard redesign", PlanOutlook.Behind, total: 21, done: 8, projectedEnd: new DateOnly(2026, 10, 30)),
                Plan("Task views", PlanOutlook.OnTrack, total: 13, done: 5, projectedEnd: null),
                Plan("Inbox", PlanOutlook.Overdue, total: 8, done: 3, projectedEnd: null)
            ])));

        var result = await Attention(tasks: tasks).GetNeedsYouAsync(DashboardScope.Default, TestContext.Current.CancellationToken);

        var item = Assert.Single(result.Items);
        Assert.Equal(NeedsYouRule.RoadmapBehind, item.Rule);
        Assert.Equal(NeedsYouTab.Tasks, item.Tab);
        Assert.Equal("Dashboard redesign", item.Title);
        Assert.Contains("13 points left", item.Detail, StringComparison.Ordinal);
        Assert.Contains("30 Oct", item.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_provider_projected_past_its_budget_needs_you_with_the_day_it_crosses()
    {
        // 80 spent through the 8th at 5 a day: 100 on the 12th is not past 100, 105 on the 13th is.
        var cost = new StubCost(InsightResult<SpendProjectionInsight>.Ready(new SpendProjectionInsight(
            [
                Projection(SpendProvider.Claude, monthToDate: 80m, daily: 5m, budget: 100m),
                Projection(SpendProvider.Copilot, monthToDate: 10m, daily: 1m, budget: 100m),
                Projection(SpendProvider.AzureFoundry, monthToDate: 500m, daily: 50m, budget: null)
            ])));

        var result = await Attention(cost: cost).GetNeedsYouAsync(DashboardScope.Default, TestContext.Current.CancellationToken);

        var item = Assert.Single(result.Items);
        Assert.Equal(NeedsYouRule.PastBudget, item.Rule);
        Assert.Equal(NeedsYouTab.Cost, item.Tab);
        Assert.Contains("Claude", item.Title, StringComparison.Ordinal);
        Assert.Contains("13 Oct", item.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_provider_already_over_its_budget_says_so_instead_of_a_day()
    {
        var cost = new StubCost(InsightResult<SpendProjectionInsight>.Ready(new SpendProjectionInsight(
            [Projection(SpendProvider.Claude, monthToDate: 120m, daily: 5m, budget: 100m)])));

        var result = await Attention(cost: cost).GetNeedsYouAsync(DashboardScope.Default, TestContext.Current.CancellationToken);

        Assert.StartsWith("Already past", Assert.Single(result.Items).Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rework_needs_you_when_more_than_a_quarter_of_merged_pull_requests_came_back_after_review()
    {
        var result = await Attention(productivity: new StubProductivity(merged: 20, cameBack: 6))
            .GetNeedsYouAsync(DashboardScope.Default, TestContext.Current.CancellationToken);

        var item = Assert.Single(result.Items);
        Assert.Equal(NeedsYouRule.Rework, item.Rule);
        Assert.Equal(NeedsYouTab.Productivity, item.Tab);
        Assert.Contains("6 of 20", item.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rework_of_exactly_a_quarter_does_not_need_you()
    {
        var result = await Attention(productivity: new StubProductivity(merged: 20, cameBack: 5))
            .GetNeedsYouAsync(DashboardScope.Default, TestContext.Current.CancellationToken);

        Assert.True(result.IsEmpty);
    }

    [Fact]
    public async Task Rework_over_no_merged_pull_requests_does_not_need_you()
    {
        var result = await Attention(productivity: new StubProductivity(merged: 0, cameBack: 0))
            .GetNeedsYouAsync(DashboardScope.Default, TestContext.Current.CancellationToken);

        Assert.True(result.IsEmpty);
    }

    [Fact]
    public async Task The_items_come_in_rule_order_drift_roadmap_budget_rework()
    {
        var result = await Attention(
                drift: new StubDrift(Drift([Unit(".devbook/domain/tasks/domain.md#task", "conflict")])),
                tasks: new StubTasks(InsightResult<PlanInsight>.Ready(new PlanInsight(
                    true,
                    new PlanPace(5m, PlanPaceBasis.Manual),
                    [Plan("Dashboard redesign", PlanOutlook.Behind, 21, 8, null)]))),
                cost: new StubCost(InsightResult<SpendProjectionInsight>.Ready(new SpendProjectionInsight(
                    [Projection(SpendProvider.Claude, 80m, 5m, 100m)]))),
                productivity: new StubProductivity(merged: 10, cameBack: 5))
            .GetNeedsYouAsync(DashboardScope.Default, TestContext.Current.CancellationToken);

        Assert.Equal(
            [NeedsYouRule.Drift, NeedsYouRule.RoadmapBehind, NeedsYouRule.PastBudget, NeedsYouRule.Rework],
            result.Items.Select(item => item.Rule));
    }

    [Fact]
    public async Task An_unavailable_source_is_skipped_and_the_others_still_answer()
    {
        var result = await Attention(
                drift: new StubDrift(InsightResult<DriftInsight>.Unavailable("No GitHub token is configured.")),
                tasks: new StubTasks(failure: new HttpRequestException("The roadmap could not be read.")),
                productivity: new StubProductivity(merged: 10, cameBack: 5))
            .GetNeedsYouAsync(DashboardScope.Default, TestContext.Current.CancellationToken);

        Assert.Equal(NeedsYouRule.Rework, Assert.Single(result.Items).Rule);
        Assert.Equal(2, result.Skipped.Count);
        Assert.Contains("No GitHub token is configured.", result.Skipped[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_switched_off_roadmap_raises_nothing_and_is_not_skipped()
    {
        var result = await Attention(tasks: new StubTasks(InsightResult<PlanInsight>.Ready(PlanInsight.Off)))
            .GetNeedsYouAsync(DashboardScope.Default, TestContext.Current.CancellationToken);

        Assert.True(result.IsEmpty);
        Assert.Empty(result.Skipped);
    }

    [Fact]
    public async Task The_scope_reaches_the_scoped_insights()
    {
        var drift = new StubDrift(Drift([]));
        var tasks = new StubTasks(InsightResult<PlanInsight>.Ready(PlanInsight.Off));
        var productivity = new StubProductivity(merged: 0, cameBack: 0);
        var scope = DashboardScope.Default with { Repositories = RepositoryFocus.Of("specs"), Period = DashboardPeriod.FourWeeks };

        await Attention(drift: drift, tasks: tasks, productivity: productivity)
            .GetNeedsYouAsync(scope, TestContext.Current.CancellationToken);

        Assert.Equal(scope, drift.Asked);
        Assert.Equal(scope, tasks.Asked);
        Assert.Equal(scope, productivity.Asked);
    }

    [Fact]
    public async Task Cancellation_is_not_swallowed_as_an_unavailable_source()
    {
        var tasks = new StubTasks(failure: new OperationCanceledException());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Attention(tasks: tasks).GetNeedsYouAsync(DashboardScope.Default, TestContext.Current.CancellationToken));
    }

    private static AttentionInsights Attention(
        StubDrift? drift = null,
        StubTasks? tasks = null,
        StubCost? cost = null,
        StubProductivity? productivity = null) =>
        new(
            drift ?? new StubDrift(Drift([])),
            tasks ?? new StubTasks(InsightResult<PlanInsight>.Ready(PlanInsight.Off)),
            cost ?? new StubCost(InsightResult<SpendProjectionInsight>.Ready(SpendProjectionInsight.Empty)),
            productivity ?? new StubProductivity(merged: 0, cameBack: 0));

    private static InsightResult<DriftInsight> Drift(IReadOnlyList<DriftUnit> units, params DriftIssue[] issues) =>
        InsightResult<DriftInsight>.Ready(new DriftInsight(
            units.Count == 0 ? [] : [new DriftDirection("push", units)],
            issues,
            null));

    private static DriftUnit Unit(string unit, string? verdict) =>
        new("backlog", unit, "aggregate", "push", null, verdict, null, null, new DateTimeOffset(2026, 10, 5, 4, 30, 0, TimeSpan.Zero));

    private static PlanItemInsight Plan(string title, PlanOutlook outlook, int total, int done, DateOnly? projectedEnd) =>
        new(
            new PlanItemProgress(
                Guid.NewGuid(),
                title,
                new DateOnly(2026, 9, 28),
                new DateOnly(2026, 10, 23),
                ["backlog"],
                GatheredCount: 5,
                DoneCount: 2,
                TotalEffort: total,
                DoneEffort: done,
                Unestimated: 0,
                IsFinished: false,
                LastCompletedOn: null,
                Parts: [],
                PlacedByEffort: false),
            outlook,
            projectedEnd);

    private static SpendProjection Projection(SpendProvider provider, decimal monthToDate, decimal daily, decimal? budget)
    {
        var daysLeft = 23;

        return new SpendProjection(
            provider,
            new DashboardMoney(monthToDate, "USD"),
            new DashboardMoney(daily, "USD"),
            new DashboardMoney(monthToDate + (daily * daysLeft), "USD"),
            budget is { } amount ? new DashboardMoney(amount, "USD") : null,
            Today,
            daysLeft,
            IsEstimate: false);
    }

    private sealed class StubDrift(InsightResult<DriftInsight> result) : IDriftInsights
    {
        public DashboardScope? Asked { get; private set; }

        public Task<InsightResult<DriftInsight>> GetDriftAsync(DashboardScope scope, CancellationToken cancellationToken = default)
        {
            Asked = scope;
            return Task.FromResult(result);
        }
    }

    private sealed class StubTasks(InsightResult<PlanInsight>? result = null, Exception? failure = null) : ITaskInsights
    {
        public DashboardScope? Asked { get; private set; }

        public Task<InsightResult<TaskThroughputInsight>> GetThroughputAsync(DashboardScope scope, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The Needs-you list does not read throughput.");

        public Task<InsightResult<PlanInsight>> GetPlanAsync(DashboardScope scope, CancellationToken cancellationToken = default)
        {
            Asked = scope;
            return failure is null ? Task.FromResult(result!) : Task.FromException<InsightResult<PlanInsight>>(failure);
        }
    }

    private sealed class StubCost(InsightResult<SpendProjectionInsight> result) : ICostInsights
    {
        public Task<InsightResult<SpendThisMonthInsight>> GetThisMonthAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<InsightResult<SpendTrendInsight>> GetTrendAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<InsightResult<SpendByModelInsight>> GetByModelAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<InsightResult<SpendProjectionInsight>> GetProjectionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(result);

        public void Invalidate()
        {
        }
    }

    private sealed class StubProductivity(int merged, int cameBack) : IProductivityInsights
    {
        public DashboardScope? Asked { get; private set; }

        public Task<InsightResult<ProductivityHeadline>> GetHeadlineAsync(DashboardScope scope, CancellationToken cancellationToken = default)
        {
            Asked = scope;
            return Task.FromResult(InsightResult<ProductivityHeadline>.Ready(
                new ProductivityHeadline(merged, 0, 0m, null, [], [], [])));
        }

        public Task<InsightResult<ProductivityScoreInsight>> GetScoreAsync(DashboardScope scope, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<InsightResult<ProductivityTrend>> GetTrendAsync(DashboardScope scope, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<InsightResult<ReworkInsight>> GetReworkAsync(DashboardScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<ReworkInsight>.Ready(
                new ReworkInsight(cameBack, merged, 0, 0, 0, true, [], [])));

        public void Invalidate(DashboardScope scope)
        {
        }
    }
}
