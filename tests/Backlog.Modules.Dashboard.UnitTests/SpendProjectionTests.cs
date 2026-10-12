using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Dashboard.Services;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Dashboard.UnitTests;

/// <summary>
/// The month-end projection: month-to-date spend plus the average daily spend over the
/// last seven days times the days left in the month, set against the provider's
/// monthly budget.
/// </summary>
public class SpendProjectionTests
{
    /// <summary>On the first of the month there is one day of history, so the
    /// average is that day's spend and the thirty days after it are projected at it.</summary>
    [Fact]
    public void On_the_first_day_of_a_month_the_projection_runs_on_that_day_alone()
    {
        var projection = SpendProjections.Project(
            SpendProvider.Claude,
            Report(("2026-08-01", 3m)),
            new DateOnly(2026, 8, 1),
            budget: null);

        Assert.Equal(3m, projection.MonthToDate.Amount);
        Assert.Equal(3m, projection.AverageDaily.Amount);
        Assert.Equal(30, projection.DaysLeft);
        Assert.Equal(93m, projection.Projected.Amount);
    }

    /// <summary>Fewer than seven days into the month, the average is over the days
    /// there are - a quiet day counted as a day, not skipped - and never reaches back
    /// into the previous month.</summary>
    [Fact]
    public void With_fewer_than_seven_days_of_history_the_average_is_over_the_days_there_are()
    {
        var projection = SpendProjections.Project(
            SpendProvider.Claude,
            Report(("2026-07-31", 500m), ("2026-08-01", 2m), ("2026-08-03", 4m), ("2026-08-04", 6m)),
            new DateOnly(2026, 8, 4),
            budget: null);

        Assert.Equal(12m, projection.MonthToDate.Amount);
        Assert.Equal(3m, projection.AverageDaily.Amount);
        Assert.Equal(27, projection.DaysLeft);
        Assert.Equal(12m + (3m * 27), projection.Projected.Amount);
    }

    /// <summary>Past the seventh, only the last seven days set the pace; the earlier
    /// spend still counts towards the month to date.</summary>
    [Fact]
    public void Later_in_the_month_only_the_last_seven_days_set_the_average()
    {
        var projection = SpendProjections.Project(
            SpendProvider.Claude,
            Report(("2026-08-02", 100m), ("2026-08-13", 7m), ("2026-08-19", 7m)),
            new DateOnly(2026, 8, 19),
            budget: null);

        Assert.Equal(114m, projection.MonthToDate.Amount);
        Assert.Equal(2m, projection.AverageDaily.Amount);
        Assert.Equal(12, projection.DaysLeft);
        Assert.Equal(138m, projection.Projected.Amount);
    }

    [Fact]
    public void On_the_last_day_of_the_month_the_projection_is_the_month_to_date()
    {
        var projection = SpendProjections.Project(
            SpendProvider.Claude,
            Report(("2026-08-31", 10m)),
            new DateOnly(2026, 8, 31),
            budget: 5m);

        Assert.Equal(0, projection.DaysLeft);
        Assert.Equal(10m, projection.Projected.Amount);
        Assert.True(projection.IsProjectedPastBudget);
    }

    /// <summary>No budget is a state of its own: nothing to set the spend against, so
    /// the provider is never projected past one, however much it spends.</summary>
    [Fact]
    public void Without_a_budget_a_provider_is_never_projected_past_one()
    {
        var projection = SpendProjections.Project(
            SpendProvider.Copilot,
            Report(("2026-08-19", 1_000m)),
            new DateOnly(2026, 8, 19),
            budget: null);

        Assert.Null(projection.Budget);
        Assert.False(projection.IsProjectedPastBudget);
    }

    [Theory]
    [InlineData(100, true)]
    [InlineData(138, false)]
    [InlineData(200, false)]
    public void A_projection_above_the_budget_is_projected_past_it(decimal budget, bool past)
    {
        var projection = SpendProjections.Project(
            SpendProvider.Claude,
            Report(("2026-08-02", 100m), ("2026-08-13", 7m), ("2026-08-19", 7m)),
            new DateOnly(2026, 8, 19),
            budget);

        Assert.Equal(budget, projection.Budget!.Amount);
        Assert.Equal(past, projection.IsProjectedPastBudget);
    }

    /// <summary>Through the cost abstraction: each answering provider gets its own
    /// projection and its own budget, read in the currency that provider reports.</summary>
    [Fact]
    public async Task The_cost_insights_project_every_answering_provider_against_its_own_budget()
    {
        var budgets = new FixedBudgets { [SpendProvider.Claude] = 100m, [SpendProvider.AzureFoundry] = 500m };
        var costs = new CostInsights(
            new Source(Report(("2026-08-13", 7m), ("2026-08-19", 7m))),
            new Source(available: false),
            new Source(Report("EUR", ("2026-08-19", 7m))),
            new FakeTimeProvider(new DateTimeOffset(2026, 8, 19, 9, 0, 0, TimeSpan.Zero)),
            budgets);

        var result = await costs.GetProjectionAsync(TestContext.Current.CancellationToken);

        Assert.True(result.HasValue);
        Assert.Equal([SpendProvider.Claude, SpendProvider.AzureFoundry], result.Value!.Providers.Select(p => p.Provider));

        var claude = result.Value.Providers[0];
        Assert.Equal(14m + (2m * 12), claude.Projected.Amount);
        Assert.False(claude.IsProjectedPastBudget);

        var azure = result.Value.Providers[1];
        Assert.Equal(new DashboardMoney(500m, "EUR"), azure.Budget);
    }

    /// <summary>A budget changed after the month was read counts on the next ask,
    /// without a refresh: the budget is read at derivation, not cached with the spend.</summary>
    [Fact]
    public async Task A_changed_budget_counts_without_reading_the_spend_again()
    {
        var budgets = new FixedBudgets();
        var claude = new Source(Report(("2026-08-19", 10m)));
        var costs = new CostInsights(
            claude,
            new Source(available: false),
            new Source(available: false),
            new FakeTimeProvider(new DateTimeOffset(2026, 8, 19, 9, 0, 0, TimeSpan.Zero)),
            budgets);

        var before = await costs.GetProjectionAsync(TestContext.Current.CancellationToken);
        budgets[SpendProvider.Claude] = 1m;
        var after = await costs.GetProjectionAsync(TestContext.Current.CancellationToken);

        Assert.False(before.Value!.Providers[0].IsProjectedPastBudget);
        Assert.True(after.Value!.Providers[0].IsProjectedPastBudget);
        Assert.Equal(1, claude.Calls);
    }

    [Fact]
    public async Task A_host_with_no_budgets_projects_with_none()
    {
        var costs = new CostInsights(
            new Source(Report(("2026-08-19", 10m))),
            new Source(available: false),
            new Source(available: false),
            new FakeTimeProvider(new DateTimeOffset(2026, 8, 19, 9, 0, 0, TimeSpan.Zero)));

        var result = await costs.GetProjectionAsync(TestContext.Current.CancellationToken);

        Assert.Null(Assert.Single(result.Value!.Providers).Budget);
    }

    private static SpendReport Report(params (string Date, decimal Amount)[] days) => Report("USD", days);

    private static SpendReport Report(string currency, params (string Date, decimal Amount)[] days) =>
        new([.. days.Select(day => new SpendEntry(DateOnly.Parse(day.Date, System.Globalization.CultureInfo.InvariantCulture), "model", null, new DashboardMoney(day.Amount, currency)))]);

    private sealed class FixedBudgets : Dictionary<SpendProvider, decimal>, ISpendBudgetSettings
    {
        public event Action? Changed;

        public string SettingsPath => "in memory";

        public decimal? BudgetFor(SpendProvider provider) => TryGetValue(provider, out var amount) ? amount : null;

        public string? Set(SpendProvider provider, decimal? amount)
        {
            if (amount is { } value) this[provider] = value;
            else Remove(provider);

            Changed?.Invoke();
            return null;
        }
    }

    /// <summary>One stub for all three ports: these tests are about the projection,
    /// not about the ports being separate.</summary>
    private sealed class Source(SpendReport? report = null, bool available = true)
        : IClaudeSpendSource, ICopilotSpendSource, IAzureFoundrySpendSource
    {
        public int Calls { get; private set; }

        public Task<InsightAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(available ? InsightAvailability.Available : InsightAvailability.Unavailable("Not configured in this test."));

        public Task<SpendReport> GetSpendAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(report ?? SpendReport.Empty);
        }
    }
}
