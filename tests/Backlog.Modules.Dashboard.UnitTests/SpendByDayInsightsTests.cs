using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Dashboard.Services;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Dashboard.UnitTests;

/// <summary>
/// What the Cost tab's spend-over-time chart and its model table read: the month day by
/// day with the days after today projected, the budget line, and tokens by direction.
/// </summary>
public sealed class SpendByDayInsightsTests
{
    /// <summary>The nineteenth of a thirty-one-day month: twelve days left to project.</summary>
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Every_day_of_the_month_is_a_bar_and_the_days_after_today_are_projected()
    {
        var claude = new Stub(Entries(("2026-08-02", 10m), ("2026-08-18", 7m), ("2026-08-19", 7m)));
        var copilot = new Stub(Entries(("2026-08-19", 3.5m)));

        var byDay = await Costs(claude, copilot).GetByDayAsync(TestContext.Current.CancellationToken);

        Assert.True(byDay.HasValue);
        var days = byDay.Value!.Days;
        Assert.Equal(31, days.Count);
        Assert.Equal(new DateOnly(2026, 8, 1), days[0].Date);
        Assert.Equal(new DateOnly(2026, 8, 31), days[^1].Date);

        // Reported days are what the providers spent between them that day.
        Assert.Equal(10m, days[1].Amount);
        Assert.Equal(0m, days[2].Amount);
        Assert.Equal(10.5m, days[18].Amount);
        Assert.All(days.Take(19), day => Assert.False(day.IsProjected));

        // Projected days are the providers' seven-day averages added: Claude 14 over 7
        // days is 2 a day, Copilot 3.5 over 7 is 0.5.
        Assert.All(days.Skip(19), day =>
        {
            Assert.True(day.IsProjected);
            Assert.Equal(2.5m, day.Amount);
        });

        Assert.Equal("USD", byDay.Value.Currency);
    }

    [Fact]
    public async Task The_budget_line_is_the_sum_of_the_budgets_there_are()
    {
        var claude = new Stub(Entries(("2026-08-02", 10m)));
        var copilot = new Stub(Entries(("2026-08-03", 1m)));
        var azure = new Stub(Entries(("2026-08-04", 1m)));
        var budgets = new FixedBudgets { [SpendProvider.Claude] = 100m, [SpendProvider.AzureFoundry] = 25m };

        var byDay = await Costs(claude, copilot, azure, budgets).GetByDayAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new DashboardMoney(125m, "USD"), byDay.Value!.Budget);
    }

    [Fact]
    public async Task With_no_budget_set_there_is_no_line()
    {
        var byDay = await Costs(new Stub(Entries(("2026-08-02", 10m))), Silent())
            .GetByDayAsync(TestContext.Current.CancellationToken);

        Assert.True(byDay.HasValue);
        Assert.Null(byDay.Value!.Budget);
    }

    /// <summary>The bars add the providers up, so two currencies refuse the way every
    /// other total here does — with the reason on the part rather than a figure in no
    /// currency at all.</summary>
    [Fact]
    public async Task Two_currencies_are_not_added_into_one_bar()
    {
        var claude = new Stub(Entries(("2026-08-02", 10m)));
        var azure = new Stub(Entries(("2026-08-02", 4m, "EUR")));

        var byDay = await Costs(claude, Silent(), azure).GetByDayAsync(TestContext.Current.CancellationToken);

        Assert.False(byDay.HasValue);
        Assert.False(string.IsNullOrWhiteSpace(byDay.Availability.Reason));
    }

    /// <summary>Across two currencies the bars do not add even when the providers never
    /// spend on the same day: the projected days would add their averages.</summary>
    [Fact]
    public async Task Two_currencies_on_different_days_still_refuse()
    {
        var claude = new Stub(Entries(("2026-08-02", 10m)));
        var azure = new Stub(Entries(("2026-08-03", 4m, "EUR")));

        var byDay = await Costs(claude, Silent(), azure).GetByDayAsync(TestContext.Current.CancellationToken);

        Assert.False(byDay.HasValue);
    }

    /// <summary>A provider that answered with nothing has no currency of its own, so its
    /// budget is counted in the month's rather than making a euro month refuse over a
    /// dollar it never reported.</summary>
    [Fact]
    public async Task A_quiet_provider_with_a_budget_counts_in_the_months_currency()
    {
        var claude = new Stub(SpendReport.Empty);
        var azure = new Stub(Entries(("2026-08-03", 4m, "EUR")));
        var budgets = new FixedBudgets { [SpendProvider.Claude] = 100m, [SpendProvider.AzureFoundry] = 50m };

        var byDay = await Costs(claude, Silent(), azure, budgets).GetByDayAsync(TestContext.Current.CancellationToken);

        Assert.True(byDay.HasValue);
        Assert.Equal("EUR", byDay.Value!.Currency);
        Assert.Equal(new DashboardMoney(150m, "EUR"), byDay.Value.Budget);
    }

    /// <summary>On the last day of the month there is nothing left to project.</summary>
    [Fact]
    public async Task On_the_last_day_of_the_month_no_day_is_projected()
    {
        var claude = new Stub(Entries(("2026-08-31", 3m)));
        var costs = new CostInsights(claude, Silent(), Silent(), new FakeTimeProvider(new DateTimeOffset(2026, 8, 31, 22, 0, 0, TimeSpan.Zero)));

        var byDay = await costs.GetByDayAsync(TestContext.Current.CancellationToken);

        Assert.Equal(31, byDay.Value!.Days.Count);
        Assert.DoesNotContain(byDay.Value.Days, day => day.IsProjected);
        Assert.Equal(3m, byDay.Value.Days[^1].Amount);
    }

    [Fact]
    public async Task The_chart_shares_the_month_read_with_the_cards()
    {
        var claude = new Stub(Entries(("2026-08-02", 10m)));
        var costs = Costs(claude, Silent());

        _ = await costs.GetProjectionAsync(TestContext.Current.CancellationToken);
        _ = await costs.GetByDayAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, claude.Calls);
    }

    [Fact]
    public async Task An_estimated_provider_makes_the_chart_an_estimate()
    {
        var claude = new Stub(new SpendReport(Entries(("2026-08-02", 10m)).Entries, IsEstimate: true));

        var byDay = await Costs(claude, Silent()).GetByDayAsync(TestContext.Current.CancellationToken);

        Assert.True(byDay.Value!.IsEstimate);
    }

    /// <summary>Input and output are summed per model, and stay null — an em dash, not a
    /// zero — for a provider that does not split its tokens by direction.</summary>
    [Fact]
    public async Task Spend_by_model_carries_input_and_output_tokens_where_the_provider_reports_them()
    {
        var claude = new Stub(new SpendReport(
        [
            new SpendEntry(new DateOnly(2026, 8, 2), "opus", 150, new DashboardMoney(5m, "USD")) { InputTokens = 100, OutputTokens = 50 },
            new SpendEntry(new DateOnly(2026, 8, 3), "opus", 30, new DashboardMoney(1m, "USD")) { InputTokens = 20, OutputTokens = 10 }
        ]));
        var copilot = new Stub(new SpendReport(
            [new SpendEntry(new DateOnly(2026, 8, 3), "gpt-5", null, new DashboardMoney(2m, "USD"))]));

        var byModel = await Costs(claude, copilot).GetByModelAsync(TestContext.Current.CancellationToken);

        var opus = Assert.Single(byModel.Value!.Rows, row => row.Name == "opus");
        Assert.Equal(120, opus.InputTokens);
        Assert.Equal(60, opus.OutputTokens);

        var gpt = Assert.Single(byModel.Value.Rows, row => row.Name == "gpt-5");
        Assert.Null(gpt.InputTokens);
        Assert.Null(gpt.OutputTokens);
    }

    private static CostInsights Costs(Stub claude, Stub copilot, Stub? azure = null, ISpendBudgetSettings? budgets = null) =>
        new(claude, copilot, azure ?? Silent(), new FakeTimeProvider(Now), budgets);

    private static Stub Silent() => new(SpendReport.Empty) { Availability = InsightAvailability.Unavailable("Not configured in this test.") };

    private static SpendReport Entries(params (string Date, decimal Amount)[] days) =>
        new([.. days.Select(day => new SpendEntry(DateOnly.Parse(day.Date, System.Globalization.CultureInfo.InvariantCulture), "opus", null, new DashboardMoney(day.Amount, "USD")))]);

    private static SpendReport Entries(params (string Date, decimal Amount, string Currency)[] days) =>
        new([.. days.Select(day => new SpendEntry(DateOnly.Parse(day.Date, System.Globalization.CultureInfo.InvariantCulture), "meter", null, new DashboardMoney(day.Amount, day.Currency)))]);

    /// <summary>One stub for all three ports: these tests are about the derivation, and
    /// which port a figure came in on is the constructor argument's to say.</summary>
    private sealed class Stub(SpendReport report) : IClaudeSpendSource, ICopilotSpendSource, IAzureFoundrySpendSource
    {
        public InsightAvailability Availability { get; init; } = InsightAvailability.Available;

        public int Calls { get; private set; }

        public Task<InsightAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Availability);

        public Task<SpendReport> GetSpendAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(report);
        }
    }

    private sealed class FixedBudgets : Dictionary<SpendProvider, decimal>, ISpendBudgetSettings
    {
        public event Action? Changed
        {
            add { }
            remove { }
        }

        public string SettingsPath => "in memory";

        public decimal? BudgetFor(SpendProvider provider) => TryGetValue(provider, out var amount) ? amount : null;

        public string? Set(SpendProvider provider, decimal? amount) => null;
    }
}
