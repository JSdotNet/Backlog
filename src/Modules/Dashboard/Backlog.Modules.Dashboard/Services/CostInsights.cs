using System.Globalization;
using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;

namespace Backlog.Modules.Dashboard.Services;

/// <summary>
/// Turns the assistants' personal spend reports into the three cost parts.
/// </summary>
/// <remarks>
/// <para>
/// Every provider is asked, and any one being unavailable is survivable: a part
/// renders whichever answered and says nothing about the ones that did not,
/// because a total labelled as a total while silently missing some of its inputs
/// is the worst thing this dashboard could show. Only when none can answer does a
/// part go unavailable, and then it carries every reason.
/// </para>
/// <para>
/// Nothing here takes a <see cref="Backlog.Modules.Dashboard.Abstractions.DashboardScope"/>.
/// No provider reports spend per repository, so there is no narrowing to
/// honour — see <see cref="ICostInsights"/>.
/// </para>
/// </remarks>
public sealed class CostInsights(
    IClaudeSpendSource claude,
    ICopilotSpendSource copilot,
    IAzureFoundrySpendSource azureFoundry,
    TimeProvider time) : ICostInsights
{
    /// <summary>The providers in the order the parts list them. The order is a
    /// screen decision — Claude first because its tile is the emphasised one —
    /// and it lives here once so the three parts cannot disagree on it.</summary>
    private static readonly SpendProvider[] Providers =
    [
        SpendProvider.Claude,
        SpendProvider.Copilot,
        SpendProvider.AzureFoundry
    ];

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
    /// The shape all three parts share: ask every provider, and derive from
    /// whichever answered.
    /// </summary>
    private async Task<InsightResult<T>> DeriveAsync<T>(
        string keyPrefix,
        (DateOnly From, DateOnly To) window,
        Func<IReadOnlyList<SpendAnswer>, (DateOnly From, DateOnly To), T> derive,
        CancellationToken cancellationToken)
    {
        var key = keyPrefix + "|" + window.From.ToString("O", CultureInfo.InvariantCulture)
            + "|" + window.To.ToString("O", CultureInfo.InvariantCulture);

        var answers = await _cache.GetOrAddAsync(key, async shared =>
        {
            var reads = new[]
            {
                ReadAsync(SpendProvider.Claude, claude.GetAvailabilityAsync,
                    token => claude.GetSpendAsync(window.From, window.To, token), shared),
                ReadAsync(SpendProvider.Copilot, copilot.GetAvailabilityAsync,
                    token => copilot.GetSpendAsync(window.From, window.To, token), shared),
                ReadAsync(SpendProvider.AzureFoundry, azureFoundry.GetAvailabilityAsync,
                    token => azureFoundry.GetSpendAsync(window.From, window.To, token), shared)
            };

            // Concurrently: they share no credential and no endpoint, so there is
            // no reason the slowest one should decide when the others are read.
            return (IReadOnlyList<SpendAnswer>)await Task.WhenAll(reads).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        if (answers.All(answer => answer.Report is null))
        {
            return InsightResult<T>.Unavailable(Reasons(answers));
        }

        try
        {
            return InsightResult<T>.Ready(derive(answers, window));
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

    /// <summary>One provider's report, or — when <see cref="Report"/> is null —
    /// the reason there is none.</summary>
    private sealed record SpendAnswer(SpendProvider Provider, SpendReport? Report, string Reason);

    /// <summary>
    /// One provider's report, or the reason there is none. A throw becomes a reason
    /// rather than travelling, because one provider failing must leave the others'
    /// figures on screen.
    /// </summary>
    private static async Task<SpendAnswer> ReadAsync(
        SpendProvider provider,
        Func<CancellationToken, Task<InsightAvailability>> availabilityOf,
        Func<CancellationToken, Task<SpendReport>> spendOf,
        CancellationToken cancellationToken)
    {
        try
        {
            var availability = await availabilityOf(cancellationToken).ConfigureAwait(false);

            if (!availability.IsAvailable) return new SpendAnswer(provider, null, availability.Reason);

            return new SpendAnswer(provider, await spendOf(cancellationToken).ConfigureAwait(false), string.Empty);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new SpendAnswer(provider, null, exception.Message);
        }
    }

    private static string Reasons(IReadOnlyList<SpendAnswer> answers)
    {
        var reasons = answers.Select(answer => answer.Reason).Where(reason => !string.IsNullOrWhiteSpace(reason)).ToList();

        return reasons.Count == 0
            ? "None of Claude, Copilot or Azure Foundry usage reporting is configured."
            : string.Join(" ", reasons);
    }

    /// <summary>The reports that answered, in <see cref="Providers"/> order.</summary>
    private static IEnumerable<(SpendProvider Provider, SpendReport Report)> Answered(IReadOnlyList<SpendAnswer> answers) =>
        Providers
            .Select(provider => (Provider: provider, answers.First(answer => answer.Provider == provider).Report))
            .Where(pair => pair.Report is not null)
            .Select(pair => (pair.Provider, pair.Report!));

    private static SpendThisMonthInsight ThisMonth(
        IReadOnlyList<SpendAnswer> answers,
        (DateOnly From, DateOnly To) window) =>
        new([.. Answered(answers).Select(pair => new MonthlySpend(
            pair.Provider,
            Total(pair.Report.Entries),
            pair.Report.Allowance,
            window.From,
            window.To,
            pair.Report.IsEstimate))]);

    /// <summary>The provider's name as the parts print it. Here rather than in
    /// each part because the trend's series and the model table's detail column
    /// have to say the same word for the same tile.</summary>
    private static string Name(SpendProvider provider) => provider switch
    {
        SpendProvider.Claude => "Claude",
        SpendProvider.Copilot => "Copilot",
        SpendProvider.AzureFoundry => "Azure Foundry",
        _ => provider.ToString()
    };

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
        IReadOnlyList<SpendAnswer> answers,
        (DateOnly From, DateOnly To) window)
    {
        var buckets = MonthBuckets(window.From, window.To);
        var series = new List<InsightSeries>();

        foreach (var (provider, report) in Answered(answers))
        {
            Add(Name(provider), report);
        }

        return new SpendTrendInsight(series, CurrencyOf(answers), SpendBucket.Month);

        void Add(string name, SpendReport report)
        {
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
    /// Where the money went by model, across every provider.
    /// </summary>
    /// <remarks>
    /// One table rather than one per provider, ordered by spend, with the provider
    /// in each row's detail. The question here is which model costs the most;
    /// splitting it up would make the reader do that merge by eye.
    /// </remarks>
    private static SpendByModelInsight ByModel(
        IReadOnlyList<SpendAnswer> answers,
        (DateOnly From, DateOnly To) window)
    {
        _ = window;

        var rows = new List<InsightRow>();

        foreach (var (provider, report) in Answered(answers))
        {
            Add(Name(provider), report);
        }

        return new SpendByModelInsight([.. rows.OrderByDescending(row => row.Cost?.Amount ?? 0m)]);

        void Add(string provider, SpendReport report)
        {
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
    /// The currency to label an axis with. Anthropic and GitHub report United
    /// States dollars; Azure bills in the subscription's currency, which for a
    /// European subscription is not that. When they disagree the axis says
    /// "mixed" rather than picking one and hoping.
    /// </summary>
    private static string CurrencyOf(IReadOnlyList<SpendAnswer> answers)
    {
        var currencies = answers
            .Where(answer => answer.Report is not null)
            .SelectMany(answer => answer.Report!.Entries)
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
