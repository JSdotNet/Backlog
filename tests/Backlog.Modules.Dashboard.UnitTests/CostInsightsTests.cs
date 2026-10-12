using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Dashboard.Services;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Dashboard.UnitTests;

/// <summary>
/// The three cost parts. The behaviour worth most of these tests is what happens
/// when one of the providers cannot answer, because on a real machine that is the
/// normal case rather than the edge one.
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

        var month = await costs.GetThisMonthAsync(TestContext.Current.CancellationToken);

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

        var month = await costs.GetThisMonthAsync(TestContext.Current.CancellationToken);

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

        var month = await costs.GetThisMonthAsync(TestContext.Current.CancellationToken);

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

        var month = await Costs(claude, Silent()).GetThisMonthAsync(TestContext.Current.CancellationToken);

        Assert.True(month.HasValue);
        var provider = Assert.Single(month.Value!.Providers);
        Assert.Equal(new DateOnly(2026, 8, 1), provider.MonthStart);
        Assert.Equal(new DateOnly(2026, 8, 19), provider.Through);

        Assert.Contains((new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 19)), claude.Windows);
    }

    [Fact]
    public async Task An_estimated_figure_is_marked_as_one()
    {
        var costs = Costs(
            claude: new StubSpendSource { Report = Spend(9m) with { IsEstimate = true } },
            copilot: new StubSpendSource { Report = Spend(3m) });

        var month = await costs.GetThisMonthAsync(TestContext.Current.CancellationToken);

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

        var month = await costs.GetThisMonthAsync(TestContext.Current.CancellationToken);

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

        var month = await costs.GetThisMonthAsync(TestContext.Current.CancellationToken);

        Assert.True(month.HasValue);
        var provider = Assert.Single(month.Value!.Providers);
        Assert.Equal(0m, provider.Spend.Amount);
    }

    [Fact]
    public async Task The_trend_reaches_back_far_enough_to_have_six_months_to_compare_against()
    {
        var costs = Costs(new StubSpendSource { Report = Spend(1m) }, Silent());

        var trend = await costs.GetTrendAsync(TestContext.Current.CancellationToken);

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

        var trend = await costs.GetTrendAsync(TestContext.Current.CancellationToken);

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

        var byModel = await costs.GetByModelAsync(TestContext.Current.CancellationToken);

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

        _ = await costs.GetThisMonthAsync(TestContext.Current.CancellationToken);
        _ = await costs.GetByModelAsync(TestContext.Current.CancellationToken);

        // Month and by-model read the same window, so they share one fetch; the
        // trend reads a longer one and fetches on its own. Claude is asked a second
        // time, for the same days of last month the month tile compares with; Copilot
        // is not, because it reports a month as one entry.
        Assert.Equal(2, claude.Calls);
        Assert.Equal(1, copilot.Calls);

        _ = await costs.GetTrendAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, claude.Calls);
        Assert.Equal(2, copilot.Calls);
    }

    /// <summary>
    /// Azure Foundry answers only when the test says so. The provider was added
    /// after most of these tests were written, and each of them is about the two
    /// it names; a third one silently reporting would change what "only" means.
    /// </summary>
    [Fact]
    public async Task Azure_Foundry_is_a_third_tile_a_third_series_and_rows_of_its_own_beside_the_other_two()
    {
        var costs = Costs(
            claude: new StubSpendSource { Report = Spend(12m) },
            copilot: new StubSpendSource { Report = Spend(3m) },
            azureFoundry: new StubSpendSource { Report = AzureSpend(4.5m) });

        var month = await costs.GetThisMonthAsync(TestContext.Current.CancellationToken);
        var trend = await costs.GetTrendAsync(TestContext.Current.CancellationToken);
        var byModel = await costs.GetByModelAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [SpendProvider.Claude, SpendProvider.Copilot, SpendProvider.AzureFoundry],
            month.Value!.Providers.Select(provider => provider.Provider));
        var azure = month.Value.Providers[2];
        Assert.Equal(4.5m, azure.Spend.Amount);
        Assert.Equal("EUR", azure.Spend.Currency);
        Assert.False(azure.IsEstimate);
        Assert.Null(azure.Allowance);

        Assert.Equal(["Claude", "Copilot", "Azure Foundry"], trend.Value!.ByProvider.Select(series => series.Name));
        // Two currencies on one axis: the label says so rather than picking one.
        Assert.Equal("mixed", trend.Value.Currency);

        var row = Assert.Single(byModel.Value!.Rows, row => row.Detail == "Azure Foundry");
        Assert.Equal("gpt-5.4 Output Tokens", row.Name);
        Assert.Null(row.Tokens);
    }

    [Fact]
    public async Task Azure_Foundry_alone_is_enough_for_every_part()
    {
        var costs = Costs(Silent(), Silent(), azureFoundry: new StubSpendSource { Report = AzureSpend(2m) });

        var month = await costs.GetThisMonthAsync(TestContext.Current.CancellationToken);
        var trend = await costs.GetTrendAsync(TestContext.Current.CancellationToken);

        var provider = Assert.Single(month.Value!.Providers);
        Assert.Equal(SpendProvider.AzureFoundry, provider.Provider);
        Assert.Equal("EUR", trend.Value!.Currency);
    }

    [Fact]
    public async Task When_all_three_are_unavailable_the_reason_carries_all_three()
    {
        var costs = Costs(
            claude: new StubSpendSource { Availability = InsightAvailability.Unavailable("No Anthropic key.") },
            copilot: new StubSpendSource { Availability = InsightAvailability.Unavailable("No admin rights.") },
            azureFoundry: new StubSpendSource { Availability = InsightAvailability.Unavailable("No cost scope.") });

        var month = await costs.GetThisMonthAsync(TestContext.Current.CancellationToken);

        Assert.False(month.HasValue);
        Assert.Contains("No Anthropic key.", month.Availability.Reason, StringComparison.Ordinal);
        Assert.Contains("No admin rights.", month.Availability.Reason, StringComparison.Ordinal);
        Assert.Contains("No cost scope.", month.Availability.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// Nineteen days of August against the first nineteen of July, not against the
    /// whole of July: a month that is not over set beside one that is would read as
    /// spend falling every month until its last day.
    /// </summary>
    [Fact]
    public async Task The_previous_spend_is_the_same_days_of_last_month()
    {
        var claude = new StubSpendSource { ReportFor = ByMonth(thisMonth: 12m, lastMonth: 8m) };
        var azure = new StubSpendSource { ReportFor = ByMonth(thisMonth: 4.5m, lastMonth: 6m, currency: "EUR") };

        var month = await Costs(claude, Silent(), azure).GetThisMonthAsync(TestContext.Current.CancellationToken);

        Assert.True(month.HasValue);
        var claudeTile = Assert.Single(month.Value!.Providers, one => one.Provider == SpendProvider.Claude);
        Assert.Equal(12m, claudeTile.Spend.Amount);
        Assert.Equal(new DashboardMoney(8m, "USD"), claudeTile.PreviousSpend);

        var azureTile = Assert.Single(month.Value.Providers, one => one.Provider == SpendProvider.AzureFoundry);
        Assert.Equal(new DashboardMoney(6m, "EUR"), azureTile.PreviousSpend);

        Assert.Equal(
            [(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 19)), (new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 19))],
            claude.Windows);
    }

    /// <summary>
    /// The thirty-first of March has no thirty-first of February to stop at, so last
    /// month runs to its own last day: the whole of February against the whole of
    /// March so far.
    /// </summary>
    [Fact]
    public async Task A_shorter_last_month_is_read_to_its_last_day()
    {
        var claude = new StubSpendSource { Report = Spend(1m) };

        _ = await Costs(claude, Silent(), now: new DateTimeOffset(2026, 3, 31, 9, 0, 0, TimeSpan.Zero))
            .GetThisMonthAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31)), (new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28))],
            claude.Windows);
    }

    /// <summary>Copilot reports a month as one entry, so it cannot say what the first
    /// nineteen days of last month cost and is not asked: its tile has no comparison.</summary>
    [Fact]
    public async Task Copilot_has_no_previous_spend_and_is_not_asked_for_one()
    {
        var copilot = new StubSpendSource { Report = Spend(3m) };

        var month = await Costs(Silent(), copilot).GetThisMonthAsync(TestContext.Current.CancellationToken);

        var tile = Assert.Single(month.Value!.Providers);
        Assert.Null(tile.PreviousSpend);
        Assert.Equal([(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 19))], copilot.Windows);
    }

    /// <summary>
    /// The comparison is the one thing a failed read of last month may cost. This
    /// month's figure was read, and a reader who cannot see last month's still wants
    /// to know what this one is.
    /// </summary>
    [Fact]
    public async Task A_failed_read_of_last_month_leaves_this_months_figure_without_a_comparison()
    {
        var claude = new StubSpendSource
        {
            ReportFor = ByMonth(thisMonth: 12m, lastMonth: 8m),
            FailsFrom = from => from.Month == 7
        };

        var month = await Costs(claude, Silent()).GetThisMonthAsync(TestContext.Current.CancellationToken);

        Assert.True(month.HasValue);
        var tile = Assert.Single(month.Value!.Providers);
        Assert.Equal(12m, tile.Spend.Amount);
        Assert.Null(tile.PreviousSpend);
    }

    /// <summary>There is no exchange rate in this product, so a provider whose currency
    /// changed between the two months has no comparison rather than a converted one.</summary>
    [Fact]
    public async Task Last_month_in_another_currency_gives_no_comparison()
    {
        var claude = new StubSpendSource
        {
            ReportFor = (from, _) => new SpendReport(
            [
                new SpendEntry(from, "opus", 1, new DashboardMoney(5m, from.Month == 8 ? "USD" : "EUR"))
            ])
        };

        var month = await Costs(claude, Silent()).GetThisMonthAsync(TestContext.Current.CancellationToken);

        Assert.Null(Assert.Single(month.Value!.Providers).PreviousSpend);
    }

    /// <summary>Nothing spent over the same days of last month is a figure — zero, in
    /// this month's currency — and not an absence: the provider answered.</summary>
    [Fact]
    public async Task Nothing_spent_last_month_is_a_zero_in_this_months_currency()
    {
        var claude = new StubSpendSource
        {
            ReportFor = (from, _) => from.Month == 8 ? AzureSpend(2m) : SpendReport.Empty
        };

        var month = await Costs(claude, Silent()).GetThisMonthAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new DashboardMoney(0m, "EUR"), Assert.Single(month.Value!.Providers).PreviousSpend);
    }

    /// <summary>
    /// Last month is a comparison for a figure, so a part with no figure — every
    /// provider refused, or the month could not be totalled — does not ask for it.
    /// </summary>
    [Fact]
    public async Task Last_month_is_not_read_when_this_month_has_no_figure()
    {
        var claude = new StubSpendSource
        {
            Report = new SpendReport(
            [
                new SpendEntry(new DateOnly(2026, 8, 1), "opus", 10, new DashboardMoney(4m, "USD")),
                new SpendEntry(new DateOnly(2026, 8, 2), "opus", 10, new DashboardMoney(4m, "EUR"))
            ])
        };

        var month = await Costs(claude, Silent()).GetThisMonthAsync(TestContext.Current.CancellationToken);

        Assert.False(month.HasValue);
        Assert.Equal([(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 19))], claude.Windows);
    }

    [Fact]
    public async Task Last_month_is_read_once_and_kept_like_this_month()
    {
        var claude = new StubSpendSource { Report = Spend(2m) };
        var costs = Costs(claude, Silent());

        _ = await costs.GetThisMonthAsync(TestContext.Current.CancellationToken);
        _ = await costs.GetThisMonthAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, claude.Calls);
    }

    private static CostInsights Costs(
        StubSpendSource claude,
        StubSpendSource copilot,
        StubSpendSource? azureFoundry = null,
        DateTimeOffset? now = null) =>
        new(
            new ClaudeAdapter(claude),
            new CopilotAdapter(copilot),
            new AzureFoundryAdapter(azureFoundry ?? Silent()),
            new FakeTimeProvider(now ?? Now));

    /// <summary>A report that says which month it was asked about: this month's amount
    /// for a window opening in August, last month's otherwise.</summary>
    private static Func<DateOnly, DateOnly, SpendReport> ByMonth(decimal thisMonth, decimal lastMonth, string currency = "USD") =>
        (from, _) => new SpendReport(
        [
            new SpendEntry(from, "opus", 1_000, new DashboardMoney(from.Month == 8 ? thisMonth : lastMonth, currency))
        ]);

    private static SpendReport AzureSpend(decimal amount) =>
        new([new SpendEntry(new DateOnly(2026, 8, 3), "gpt-5.4 Output Tokens", null, new DashboardMoney(amount, "EUR"))]);

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

        /// <summary>When set, the report for a window, so a test can answer this month and
        /// last month differently. <see cref="Report"/> answers every window otherwise.</summary>
        public Func<DateOnly, DateOnly, SpendReport>? ReportFor { get; init; }

        /// <summary>When set, a spend read whose window opens on a day this answers true
        /// for throws after availability has said yes — a read that fails rather than
        /// one that refuses.</summary>
        public Func<DateOnly, bool>? FailsFrom { get; init; }

        public int Calls { get; private set; }

        /// <summary>Every window asked for, in order. A list rather than the last one,
        /// because the month part asks for two.</summary>
        public List<(DateOnly From, DateOnly To)> Windows { get; } = [];

        public Task<InsightAvailability> GetAvailabilityAsync(CancellationToken cancellationToken) =>
            Throw is not null ? Task.FromException<InsightAvailability>(Throw) : Task.FromResult(Availability);

        public Task<SpendReport> GetSpendAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
        {
            Calls++;
            Windows.Add((from, to));

            if (FailsFrom?.Invoke(from) == true)
            {
                return Task.FromException<SpendReport>(new InvalidOperationException("Anthropic answered 500."));
            }

            return Task.FromResult(ReportFor?.Invoke(from, to) ?? Report);
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

    private sealed class AzureFoundryAdapter(StubSpendSource inner) : IAzureFoundrySpendSource
    {
        public Task<InsightAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
            inner.GetAvailabilityAsync(cancellationToken);

        public Task<SpendReport> GetSpendAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
            inner.GetSpendAsync(from, to, cancellationToken);
    }
}
