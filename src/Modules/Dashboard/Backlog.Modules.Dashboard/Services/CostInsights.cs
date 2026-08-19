using System.Globalization;
using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;

namespace Backlog.Modules.Dashboard.Services;

/// <summary>
/// Turns the two assistants' personal spend reports into the three cost parts.
/// </summary>
/// <remarks>
/// <para>
/// Both providers are asked, and either one being unavailable is survivable: a
/// part renders whichever answered and says nothing about the one that did not,
/// because a total labelled as a total while silently missing half its inputs is
/// the worst thing this dashboard could show. Only when neither can answer does a
/// part go unavailable, and then it carries both reasons.
/// </para>
/// <para>
/// Nothing here takes a <see cref="Backlog.Modules.Dashboard.Abstractions.DashboardScope"/>.
/// Neither provider reports spend per repository, so there is no narrowing to
/// honour — see <see cref="ICostInsights"/>.
/// </para>
/// </remarks>
public sealed class CostInsights(
    IClaudeSpendSource claude,
    ICopilotSpendSource copilot,
    TimeProvider time) : ICostInsights
{
    /// <summary>
    /// How far back the trend reaches. Six whole months plus the current one, so
    /// the monthly view always has the six comparison buckets the acceptance
    /// criteria ask for even on the first of a month.
    /// </summary>
    private const int TrendMonths = 6;

    private readonly InsightCache _cache = new();

    public Task<InsightResult<SpendThisMonthInsight>> GetThisMonthAsync(
        CancellationToken cancellationToken = default) =>
        DeriveAsync("month", MonthWindow(), ThisMonth, cancellationToken);

    public Task<InsightResult<SpendTrendInsight>> GetTrendAsync(CancellationToken cancellationToken = default) =>
        DeriveAsync("trend", TrendWindow(), Trend, cancellationToken);

    public Task<InsightResult<SpendByModelInsight>> GetByModelAsync(CancellationToken cancellationToken = default) =>
        DeriveAsync("month", MonthWindow(), ByModel, cancellationToken);

    public void Invalidate() => _cache.Clear();

    /// <summary>Today's calendar month so far. Not a rolling thirty days: a bill
    /// arrives per calendar month, and "spent this month" has to mean the same
    /// thing the invoice will.</summary>
    private (DateOnly From, DateOnly To) MonthWindow()
    {
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        return (new DateOnly(today.Year, today.Month, 1), today);
    }

    private (DateOnly From, DateOnly To) TrendWindow()
    {
        var (monthStart, today) = MonthWindow();
        return (monthStart.AddMonths(-TrendMonths), today);
    }

    /// <summary>
    /// The shape all three parts share: ask both providers, and derive from
    /// whichever answered.
    /// </summary>
    private async Task<InsightResult<T>> DeriveAsync<T>(
        string keyPrefix,
        (DateOnly From, DateOnly To) window,
        Func<SpendReport?, SpendReport?, (DateOnly From, DateOnly To), T> derive,
        CancellationToken cancellationToken)
    {
        var key = keyPrefix + "|" + window.From.ToString("O", CultureInfo.InvariantCulture)
            + "|" + window.To.ToString("O", CultureInfo.InvariantCulture);

        var both = await _cache.GetOrAddAsync(key, async () =>
        {
            var claudeReport = ReadAsync(
                claude.GetAvailabilityAsync,
                token => claude.GetSpendAsync(window.From, window.To, token),
                cancellationToken);

            var copilotReport = ReadAsync(
                copilot.GetAvailabilityAsync,
                token => copilot.GetSpendAsync(window.From, window.To, token),
                cancellationToken);

            // Concurrently: they share no credential and no endpoint, so there is
            // no reason the slower one should decide when the faster one is read.
            await Task.WhenAll(claudeReport, copilotReport).ConfigureAwait(false);

            return (Claude: await claudeReport.ConfigureAwait(false), Copilot: await copilotReport.ConfigureAwait(false));
        }).ConfigureAwait(false);

        if (both.Claude.Report is null && both.Copilot.Report is null)
        {
            return InsightResult<T>.Unavailable(Reasons(both.Claude.Reason, both.Copilot.Reason));
        }

        try
        {
            return InsightResult<T>.Ready(derive(both.Claude.Report, both.Copilot.Report, window));
        }
        catch (Exception exception)
        {
            // The derivation can refuse as well as the fetch: summing two
            // currencies throws rather than picking one, because there is no
            // exchange rate in this product. Reaching the reader as that part's
            // reason is the whole point of refusing — a throw that escaped here
            // would take the surface down over a figure it declined to invent.
            return InsightResult<T>.Unavailable(exception.Message);
        }
    }

    /// <summary>
    /// One provider's report, or the reason there is none. A throw becomes a reason
    /// rather than travelling, because one provider failing must leave the other's
    /// figures on screen.
    /// </summary>
    private static async Task<(SpendReport? Report, string Reason)> ReadAsync(
        Func<CancellationToken, Task<InsightAvailability>> availabilityOf,
        Func<CancellationToken, Task<SpendReport>> spendOf,
        CancellationToken cancellationToken)
    {
        try
        {
            var availability = await availabilityOf(cancellationToken).ConfigureAwait(false);

            if (!availability.IsAvailable) return (null, availability.Reason);

            return (await spendOf(cancellationToken).ConfigureAwait(false), string.Empty);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return (null, exception.Message);
        }
    }

    private static string Reasons(string claude, string copilot)
    {
        var reasons = new[] { claude, copilot }.Where(reason => !string.IsNullOrWhiteSpace(reason)).ToList();

        return reasons.Count == 0
            ? "Neither Claude nor Copilot usage reporting is configured."
            : string.Join(" ", reasons);
    }

    private static SpendThisMonthInsight ThisMonth(
        SpendReport? claude,
        SpendReport? copilot,
        (DateOnly From, DateOnly To) window)
    {
        var providers = new List<MonthlySpend>();

        Add(SpendProvider.Claude, claude);
        Add(SpendProvider.Copilot, copilot);

        return new SpendThisMonthInsight(providers);

        void Add(SpendProvider provider, SpendReport? report)
        {
            if (report is null) return;

            providers.Add(new MonthlySpend(
                provider,
                Total(report.Entries),
                report.Allowance,
                window.From,
                window.To,
                report.IsEstimate));
        }
    }

    /// <summary>
    /// Spend over time, one series per provider that answered.
    /// </summary>
    /// <remarks>
    /// Monthly buckets, because the window is seven months and two hundred daily
    /// bars in that space is a texture rather than a chart. The bucket kind travels
    /// on the DTO so the axis can say which it is instead of leaving the reader to
    /// infer it from the labels.
    /// </remarks>
    private static SpendTrendInsight Trend(
        SpendReport? claude,
        SpendReport? copilot,
        (DateOnly From, DateOnly To) window)
    {
        var buckets = MonthBuckets(window.From, window.To);
        var series = new List<InsightSeries>();

        Add("Claude", claude);
        Add("Copilot", copilot);

        return new SpendTrendInsight(series, CurrencyOf(claude, copilot), SpendBucket.Month);

        void Add(string name, SpendReport? report)
        {
            if (report is null) return;

            var byMonth = report.Entries
                .GroupBy(entry => MonthLabel(entry.Date), StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Sum(entry => entry.Cost.Amount), StringComparer.Ordinal);

            series.Add(new InsightSeries(
                name,
                [.. buckets.Select(bucket => new InsightPoint(
                    bucket,
                    byMonth.TryGetValue(bucket, out var amount) ? amount : 0m))]));
        }
    }

    /// <summary>
    /// Where the money went by model, across both providers.
    /// </summary>
    /// <remarks>
    /// One table rather than two, ordered by spend, with the provider in each row's
    /// detail. The question here is which model costs the most; splitting it in two
    /// would make the reader do that merge by eye.
    /// </remarks>
    private static SpendByModelInsight ByModel(
        SpendReport? claude,
        SpendReport? copilot,
        (DateOnly From, DateOnly To) window)
    {
        _ = window;

        var rows = new List<InsightRow>();

        Add("Claude", claude);
        Add("Copilot", copilot);

        return new SpendByModelInsight([.. rows.OrderByDescending(row => row.Cost?.Amount ?? 0m)]);

        void Add(string provider, SpendReport? report)
        {
            if (report is null) return;

            rows.AddRange(report.Entries
                .GroupBy(entry => entry.Model ?? "Not reported", StringComparer.OrdinalIgnoreCase)
                .Select(group => new InsightRow(
                    group.Key,
                    // Null rather than zero when the provider reported money but no
                    // tokens: an em dash says "not reported", a zero says "none".
                    group.Any(entry => entry.Tokens is not null) ? group.Sum(entry => entry.Tokens ?? 0) : null,
                    Total(group.ToList()),
                    provider)));
        }
    }

    /// <summary>
    /// Sums entries, refusing to add across currencies. A provider that changed the
    /// currency it reports mid-window is not something to average away — the first
    /// currency wins and the mismatch throws, which surfaces as that part's
    /// unavailable reason rather than as a wrong total.
    /// </summary>
    private static DashboardMoney Total(IReadOnlyList<SpendEntry> entries) =>
        entries.Count == 0
            ? DashboardMoney.Zero("USD")
            : entries.Skip(1).Aggregate(entries[0].Cost, (sum, entry) => sum + entry.Cost);

    /// <summary>
    /// The currency to label an axis with. Both providers report United States
    /// dollars today; if one ever does not, the axis says so rather than picking
    /// one and hoping.
    /// </summary>
    private static string CurrencyOf(SpendReport? claude, SpendReport? copilot)
    {
        var currencies = new[] { claude, copilot }
            .Where(report => report is not null)
            .SelectMany(report => report!.Entries)
            .Select(entry => entry.Cost.Currency)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return currencies.Count switch
        {
            0 => "USD",
            1 => currencies[0],
            _ => "mixed"
        };
    }

    private static string MonthLabel(DateOnly date) =>
        date.ToString("MMM yy", CultureInfo.InvariantCulture);

    private static IReadOnlyList<string> MonthBuckets(DateOnly from, DateOnly to)
    {
        var labels = new List<string>();
        var cursor = new DateOnly(from.Year, from.Month, 1);
        var last = new DateOnly(to.Year, to.Month, 1);

        for (var guard = 0; cursor <= last && guard < 120; guard++)
        {
            labels.Add(MonthLabel(cursor));
            cursor = cursor.AddMonths(1);
        }

        return labels;
    }
}
