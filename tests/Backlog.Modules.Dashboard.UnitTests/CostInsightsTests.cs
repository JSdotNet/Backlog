using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Dashboard.Services;

namespace Backlog.Modules.Dashboard.UnitTests;

/// <summary>
/// The three cost parts. The behaviour worth most of these tests is what happens
/// when one of the two providers cannot answer, because on a real machine that is
/// the normal case rather than the edge one.
/// </summary>
public class CostInsightsTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 9, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The point of asking both providers separately. A figure labelled as a total
    /// while silently missing half its inputs is the worst thing this dashboard
    /// could show, so the part renders whichever answered and does not pretend the
    /// other contributed nothing.
    /// </summary>
    [Fact]
    public async Task One_provider_being_unavailable_still_shows_the_others_figures()
    {
        var costs = Costs(
            claude: new StubSpendSource { Report = Spend(12.34m) },
            copilot: new StubSpendSource { Availability = InsightAvailability.Unavailable("No admin rights.") });

        var month = await costs.GetThisMonthAsync();

        Assert.True(month.HasValue);
        var provider = Assert.Single(month.Value!.Providers);
        Assert.Equal(SpendProvider.Claude, provider.Provider);
        Assert.Equal(12.34m, provider.Spend.Amount);
    }

    [Fact]
    public async Task Only_when_neither_provider_can_answer_does_the_part_go_unavailable_and_it_carries_both_reasons()
    {
        var costs = Costs(
            claude: new StubSpendSource { Availability = InsightAvailability.Unavailable("No Anthropic key.") },
            copilot: new StubSpendSource { Availability = InsightAvailability.Unavailable("No admin rights.") });

        var month = await costs.GetThisMonthAsync();

        Assert.False(month.HasValue);
        Assert.Contains("No Anthropic key.", month.Availability.Reason, StringComparison.Ordinal);
        Assert.Contains("No admin rights.", month.Availability.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_provider_that_throws_is_treated_as_one_that_refused()
    {
        var costs = Costs(
            claude: new StubSpendSource { Report = Spend(5m) },
            copilot: new StubSpendSource { Throw = new InvalidOperationException("GitHub answered 403.") });

        var month = await costs.GetThisMonthAsync();

        Assert.True(month.HasValue);
        _ = Assert.Single(month.Value!.Providers);
    }

    /// <summary>
    /// The whole month a bill will cover, starting on the first — not a rolling
    /// thirty days, which would never agree with an invoice.
    /// </summary>
    [Fact]
    public async Task This_month_means_the_calendar_month_so_far()
    {
        var claude = new StubSpendSource { Report = Spend(1m) };

        var month = await Costs(claude, Silent()).GetThisMonthAsync();

        Assert.True(month.HasValue);
        var provider = Assert.Single(month.Value!.Providers);
        Assert.Equal(new DateOnly(2026, 8, 1), provider.MonthStart);
        Assert.Equal(new DateOnly(2026, 8, 19), provider.Through);

        Assert.Equal(new DateOnly(2026, 8, 1), claude.From);
        Assert.Equal(new DateOnly(2026, 8, 19), claude.To);
    }

    [Fact]
    public async Task An_estimated_figure_is_marked_as_one()
    {
        var costs = Costs(
            claude: new StubSpendSource { Report = Spend(9m) with { IsEstimate = true } },
            copilot: new StubSpendSource { Report = Spend(3m) });

        var month = await costs.GetThisMonthAsync();

        Assert.True(month.HasValue);
        Assert.True(Assert.Single(month.Value!.Providers, one => one.Provider == SpendProvider.Claude).IsEstimate);
        Assert.False(Assert.Single(month.Value.Providers, one => one.Provider == SpendProvider.Copilot).IsEstimate);
    }

    /// <summary>
    /// There is no exchange rate in this product, so a mixed-currency window is
    /// refused rather than summed. It surfaces as the part's unavailable reason,
    /// which is a great deal better than a total that is quietly wrong.
    /// </summary>
    [Fact]
    public async Task Two_currencies_are_not_added_together()
    {
        var costs = Costs(
            claude: new StubSpendSource
            {
                Report = new SpendReport(
                [
                    new SpendEntry(new DateOnly(2026, 8, 1), "opus", 10, new DashboardMoney(4m, "USD")),
                    new SpendEntry(new DateOnly(2026, 8, 2), "opus", 10, new DashboardMoney(4m, "EUR"))
                ])
            },
            copilot: Silent());

        var month = await costs.GetThisMonthAsync();

        Assert.False(month.HasValue);
        Assert.Contains("exchange rate", month.Availability.Reason, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Available-with-nothing is not the same as unavailable. Somebody who used no
    /// credits this month should see a zero against their name, because that is a
    /// fact about the month; an absent row would read as "we could not find out".
    /// </summary>
    [Fact]
    public async Task A_provider_that_answers_with_nothing_still_appears_with_a_zero()
    {
        var costs = Costs(new StubSpendSource(), Silent());

        var month = await costs.GetThisMonthAsync();

        Assert.True(month.HasValue);
        var provider = Assert.Single(month.Value!.Providers);
        Assert.Equal(0m, provider.Spend.Amount);
    }

    [Fact]
    public async Task The_trend_reaches_back_far_enough_to_have_six_months_to_compare_against()
    {
        var costs = Costs(new StubSpendSource { Report = Spend(1m) }, Silent());

        var trend = await costs.GetTrendAsync();

        Assert.True(trend.HasValue);
        var series = Assert.Single(trend.Value!.ByProvider);
        Assert.Equal(SpendBucket.Month, trend.Value.Bucket);
        Assert.Equal(7, series.Points.Count);
        Assert.Equal("Feb 26", series.Points[0].Label);
        Assert.Equal("Aug 26", series.Points[^1].Label);
    }

    [Fact]
    public async Task A_month_neither_provider_reported_is_a_zero_bucket_rather_than_a_missing_one()
    {
        var costs = Costs(
            claude: new StubSpendSource
            {
                Report = new SpendReport([new SpendEntry(new DateOnly(2026, 8, 3), "opus", 1, new DashboardMoney(7m, "USD"))])
            },
            copilot: Silent());

        var trend = await costs.GetTrendAsync();

        Assert.True(trend.HasValue);
        var series = Assert.Single(trend.Value!.ByProvider);
        Assert.All(series.Points.SkipLast(1), point => Assert.Equal(0m, point.Value));
        Assert.Equal(7m, series.Points[^1].Value);
    }

    [Fact]
    public async Task Spend_by_model_puts_both_providers_in_one_table_ordered_by_cost()
    {
        var costs = Costs(
            claude: new StubSpendSource
            {
                Report = new SpendReport(
                [
                    new SpendEntry(new DateOnly(2026, 8, 2), "opus", 1_000, new DashboardMoney(3m, "USD")),
                    new SpendEntry(new DateOnly(2026, 8, 3), "opus", 2_000, new DashboardMoney(4m, "USD")),
                    new SpendEntry(new DateOnly(2026, 8, 3), "haiku", 500, new DashboardMoney(1m, "USD"))
                ])
            },
            copilot: new StubSpendSource
            {
                Report = new SpendReport(
                    [new SpendEntry(new DateOnly(2026, 8, 4), "gpt-5", null, new DashboardMoney(9m, "USD"))])
            });

        var byModel = await costs.GetByModelAsync();

        Assert.True(byModel.HasValue);
        Assert.Collection(
            byModel.Value!.Rows,
            row =>
            {
                Assert.Equal("gpt-5", row.Name);
                Assert.Equal("Copilot", row.Detail);
                // Copilot meters in AI credits, not tokens, so the column stays
                // unreported rather than mixing two units under one heading.
                Assert.Null(row.Tokens);
            },
            row =>
            {
                Assert.Equal("opus", row.Name);
                Assert.Equal(7m, row.Cost!.Amount);
                Assert.Equal(3_000, row.Tokens);
            },
            row => Assert.Equal("haiku", row.Name));
    }

    [Fact]
    public async Task Both_providers_are_asked_once_and_the_three_parts_share_the_answer()
    {
        var claude = new StubSpendSource { Report = Spend(2m) };
        var copilot = new StubSpendSource { Report = Spend(3m) };
        var costs = Costs(claude, copilot);

        _ = await costs.GetThisMonthAsync();
        _ = await costs.GetByModelAsync();

        // Month and by-model read the same window, so they share one fetch; the
        // trend reads a longer one and fetches on its own.
        Assert.Equal(1, claude.Calls);

        _ = await costs.GetTrendAsync();

        Assert.Equal(2, claude.Calls);
        Assert.Equal(2, copilot.Calls);
    }

    private static CostInsights Costs(StubSpendSource claude, StubSpendSource copilot) =>
        new(new ClaudeAdapter(claude), new CopilotAdapter(copilot), new FixedClock(Now));

    /// <summary>A provider that cannot answer at all, so a test about one provider
    /// is about one provider. A stub left at its defaults is <em>available</em> with
    /// nothing to report, which is a different case and has its own test.</summary>
    private static StubSpendSource Silent() =>
        new() { Availability = InsightAvailability.Unavailable("Not configured in this test.") };

    private static SpendReport Spend(decimal amount) =>
        new([new SpendEntry(new DateOnly(2026, 8, 2), "opus", 1_000, new DashboardMoney(amount, "USD"))]);

    private sealed class StubSpendSource
    {
        public InsightAvailability Availability { get; init; } = InsightAvailability.Available;

        public SpendReport Report { get; init; } = SpendReport.Empty;

        public Exception? Throw { get; init; }

        public int Calls { get; private set; }

        public DateOnly From { get; private set; }

        public DateOnly To { get; private set; }

        public Task<InsightAvailability> GetAvailabilityAsync(CancellationToken cancellationToken) =>
            Throw is not null ? Task.FromException<InsightAvailability>(Throw) : Task.FromResult(Availability);

        public Task<SpendReport> GetSpendAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
        {
            Calls++;
            From = from;
            To = to;
            return Task.FromResult(Report);
        }
    }

    /// <summary>
    /// The two ports have the same shape but are separate interfaces on purpose, so
    /// one stub is wrapped twice rather than the test pretending they are one type.
    /// </summary>
    private sealed class ClaudeAdapter(StubSpendSource inner) : IClaudeSpendSource
    {
        public Task<InsightAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
            inner.GetAvailabilityAsync(cancellationToken);

        public Task<SpendReport> GetSpendAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
            inner.GetSpendAsync(from, to, cancellationToken);
    }

    private sealed class CopilotAdapter(StubSpendSource inner) : ICopilotSpendSource
    {
        public Task<InsightAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
            inner.GetAvailabilityAsync(cancellationToken);

        public Task<SpendReport> GetSpendAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
            inner.GetSpendAsync(from, to, cancellationToken);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
