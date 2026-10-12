using System.Globalization;
using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;

namespace Backlog.Modules.Dashboard.Services;

/// <summary>
/// Turns the assistants' personal spend reports into the cost parts.
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
    TimeProvider time,
    ISpendBudgetSettings? budgets = null) : ICostInsights
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
    /// How far back the weekly read reaches, in whole months before the current one:
    /// four, so the thirteen ISO weeks <see cref="GetByWeekAsync"/> draws are inside it
    /// whichever day of the month it is asked on.
    /// </summary>
    private const int WeeksMonths = 4;

    private readonly InsightCache _cache = new();

    public async Task<InsightResult<SpendThisMonthInsight>> GetThisMonthAsync(
        CancellationToken cancellationToken = default)
    {
        var window = MonthWindow();

        // This month first, and last month only once there is a figure to compare it
        // with: a part that is going to say why it has no number has no use for a
        // comparison, and asking the providers for one would be calls made for nothing.
        var current = await DeriveAsync("month", window, ThisMonth, cancellationToken).ConfigureAwait(false);
        if (!current.HasValue) return current;

        var previous = await LastMonthAsync(LastMonthWindow(window), cancellationToken).ConfigureAwait(false);

        return InsightResult<SpendThisMonthInsight>.Ready(new SpendThisMonthInsight(
            [.. current.Value!.Providers.Select(provider => provider with { PreviousSpend = PreviousOf(provider, previous) })]));
    }

    public Task<InsightResult<SpendByModelInsight>> GetByModelAsync(CancellationToken cancellationToken = default) =>
        DeriveAsync("month", MonthWindow(), ByModel, cancellationToken);

    /// <summary>
    /// Reads the same month as <see cref="GetThisMonthAsync"/>, so it shares that cached
    /// read; the budgets are read here, at derivation, so a changed budget counts at
    /// once. Optional so a host without budgets projects with none.
    /// </summary>
    public Task<InsightResult<SpendProjectionInsight>> GetProjectionAsync(
        CancellationToken cancellationToken = default) =>
        DeriveAsync("month", MonthWindow(), Projection, cancellationToken);

    /// <summary>
    /// Reads the same month as <see cref="GetThisMonthAsync"/> and shares that cached
    /// read. The budgets are read at derivation, as <see cref="GetProjectionAsync"/> reads
    /// them.
    /// </summary>
    public Task<InsightResult<SpendByDayInsight>> GetByDayAsync(CancellationToken cancellationToken = default) =>
        DeriveAsync("month", MonthWindow(), ByDay, cancellationToken);

    /// <summary>
    /// A read of its own, reaching <see cref="WeeksMonths"/> months back so it holds
    /// every day of the thirteen weeks drawn.
    /// </summary>
    public Task<InsightResult<SpendByWeekInsight>> GetByWeekAsync(CancellationToken cancellationToken = default) =>
        DeriveAsync("weeks", WeeksWindow(), ByWeek, cancellationToken);

    public void Invalidate() => _cache.Clear();

    /// <summary>Today's calendar month so far. Not a rolling thirty days: a bill
    /// arrives per calendar month, and "spent this month" has to mean the same
    /// thing the invoice will. Today is the person's local day, as the Devbook tab's
    /// "Checked" counts it: on the UTC day the chart said "Today, 12 Oct" after local
    /// midnight on the 13th.</summary>
    private (DateOnly From, DateOnly To) MonthWindow()
    {
        var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        return (new DateOnly(today.Year, today.Month, 1), today);
    }

    /// <summary>
    /// The same days of last month: its first day up to the same day of the month as
    /// today, or up to its own last day when it was shorter — the thirty-first of March
    /// is compared with the whole of February, which is every day February had.
    /// </summary>
    private static (DateOnly From, DateOnly To) LastMonthWindow((DateOnly From, DateOnly To) month)
    {
        var start = month.From.AddMonths(-1);
        var sameDay = start.AddDays(month.To.Day - 1);
        var lastDay = month.From.AddDays(-1);

        return (start, sameDay < lastDay ? sameDay : lastDay);
    }

    private (DateOnly From, DateOnly To) WeeksWindow()
    {
        var (monthStart, today) = MonthWindow();
        return (monthStart.AddMonths(-WeeksMonths), today);
    }

    /// <summary>
    /// The shape every cost part shares: ask every provider, and derive from
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

    /// <summary>
    /// Last month's reports for the providers that report by the day — Claude and Azure
    /// Foundry — under a cache key of their own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Copilot is not asked. Its billing reports a month as one entry, so the first
    /// nineteen days of last month are not something it can answer, and a whole month
    /// set beside nineteen days would be a comparison of two different things.
    /// </para>
    /// <para>
    /// Its own key rather than the month key with an earlier window, though the shape is
    /// the same: the prefix says what the entry is for, and the month parts and this one
    /// are cleared together by <see cref="Invalidate"/> either way. A provider failing
    /// here is an answer with no report, exactly as in <see cref="DeriveAsync{T}"/>,
    /// and that costs the comparison and nothing else.
    /// </para>
    /// </remarks>
    private Task<IReadOnlyList<SpendAnswer>> LastMonthAsync(
        (DateOnly From, DateOnly To) window,
        CancellationToken cancellationToken)
    {
        var key = "previous-month|" + window.From.ToString("O", CultureInfo.InvariantCulture)
            + "|" + window.To.ToString("O", CultureInfo.InvariantCulture);

        return _cache.GetOrAddAsync(key, async shared =>
        {
            var reads = new[]
            {
                ReadAsync(SpendProvider.Claude, claude.GetAvailabilityAsync,
                    token => claude.GetSpendAsync(window.From, window.To, token), shared),
                ReadAsync(SpendProvider.AzureFoundry, azureFoundry.GetAvailabilityAsync,
                    token => azureFoundry.GetSpendAsync(window.From, window.To, token), shared)
            };

            return (IReadOnlyList<SpendAnswer>)await Task.WhenAll(reads).ConfigureAwait(false);
        }, cancellationToken);
    }

    /// <summary>
    /// What a provider spent over the same days of last month, in this month's currency,
    /// or null when there is no like-for-like figure: a provider not asked, one that did
    /// not answer, or one whose answer was in another currency or mixed two. Nothing
    /// spent is a zero in this month's currency rather than in the default one, so an
    /// empty month never reads as a currency change.
    /// </summary>
    private static DashboardMoney? PreviousOf(MonthlySpend provider, IReadOnlyList<SpendAnswer> previous)
    {
        if (previous.FirstOrDefault(answer => answer.Provider == provider.Provider)?.Report is not { } report) return null;

        if (report.Entries.Count == 0) return DashboardMoney.Zero(provider.Spend.Currency);

        if (report.Entries.Any(entry =>
                !string.Equals(entry.Cost.Currency, provider.Spend.Currency, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        return Total(report.Entries);
    }

    private static string Reasons(IReadOnlyList<SpendAnswer> answers)
    {
        var reasons = answers.Select(answer => answer.Reason).Where(reason => !string.IsNullOrWhiteSpace(reason)).ToList();

        return reasons.Count == 0
            ? $"None of {SpendProvider.Claude.DisplayName()}, {SpendProvider.Copilot.DisplayName()} or {SpendProvider.AzureFoundry.DisplayName()} usage reporting is configured."
            : string.Join(" ", reasons);
    }

    /// <summary>The reports that answered, in <see cref="Providers"/> order.</summary>
    private static IEnumerable<(SpendProvider Provider, SpendReport Report)> Answered(IReadOnlyList<SpendAnswer> answers) =>
        Providers
            .Select(provider => (Provider: provider, answers.First(answer => answer.Provider == provider).Report))
            .Where(pair => pair.Report is not null)
            .Select(pair => (pair.Provider, pair.Report!));

    /// <summary>
    /// Each provider's month so far. A provider that answered with nothing is a zero in
    /// the month's currency — <see cref="QuietCurrency"/> — rather than in dollars, so a
    /// quiet card in a euro month does not read as a currency change.
    /// </summary>
    private static SpendThisMonthInsight ThisMonth(
        IReadOnlyList<SpendAnswer> answers,
        (DateOnly From, DateOnly To) window)
    {
        var quiet = QuietCurrency(answers);

        return new([.. Answered(answers).Select(pair => new MonthlySpend(
            pair.Provider,
            Total(pair.Report.Entries, quiet),
            pair.Report.Allowance,
            window.From,
            window.To,
            pair.Report.IsEstimate))]);
    }

    /// <summary>Each provider's month-end projection against its budget; a quiet one
    /// projects, and is budgeted, in the month's currency for the reason
    /// <see cref="ThisMonth"/> gives.</summary>
    private SpendProjectionInsight Projection(
        IReadOnlyList<SpendAnswer> answers,
        (DateOnly From, DateOnly To) window)
    {
        var quiet = QuietCurrency(answers);

        return new([.. Answered(answers).Select(pair => SpendProjections.Project(
            pair.Provider,
            pair.Report,
            window.To,
            budgets?.BudgetFor(pair.Provider),
            quiet))]);
    }

    /// <summary>
    /// The month day by day, across the providers that answered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A day up to today is what the providers reported for it, added up. A day after
    /// today is the providers' average daily spend added up — the same average
    /// <see cref="SpendProjections"/> projects the month-end from, so the last
    /// cumulative bar lands on the sum of the cards' projections.
    /// </para>
    /// <para>
    /// The adding goes through <see cref="DashboardMoney"/>, which refuses two
    /// currencies; the refusal reaches the part as its reason, as it does for every
    /// other cost figure that would otherwise have to pick one.
    /// </para>
    /// </remarks>
    private SpendByDayInsight ByDay(
        IReadOnlyList<SpendAnswer> answers,
        (DateOnly From, DateOnly To) window)
    {
        var answered = Answered(answers).ToList();
        var currency = CurrencyOf(answers);
        var monthEnd = window.From.AddMonths(1).AddDays(-1);

        // A provider that answered with nothing has no currency of its own, so it is
        // projected in the month's rather than making the others' total refuse over a
        // currency it never reported in.
        var quiet = QuietCurrency(answers);
        var projections = answered
            .Select(pair => SpendProjections.Project(pair.Provider, pair.Report, window.To, budgets?.BudgetFor(pair.Provider), quiet))
            .ToList();

        var spentOn = answered
            .SelectMany(pair => pair.Report.Entries)
            .Where(entry => entry.Date >= window.From && entry.Date <= window.To)
            .GroupBy(entry => entry.Date)
            .ToDictionary(group => group.Key, group => Total([.. group]));

        var averageDaily = projections.Aggregate(DashboardMoney.Zero(currency), (sum, projection) => sum + projection.AverageDaily);

        var days = new List<SpendDay>();
        for (var day = window.From; day <= monthEnd; day = day.AddDays(1))
        {
            days.Add(day <= window.To
                ? new SpendDay(day, spentOn.TryGetValue(day, out var spent) ? spent.Amount : 0m, IsProjected: false)
                : new SpendDay(day, averageDaily.Amount, IsProjected: true));
        }

        var withBudget = projections.Where(projection => projection.Budget is not null).ToList();
        var budget = withBudget.Count == 0
            ? null
            : withBudget.Skip(1).Aggregate(withBudget[0].Budget!, (sum, projection) => sum + projection.Budget!);

        return new SpendByDayInsight(days, currency, budget, answered.Any(pair => pair.Report.IsEstimate));
    }

    /// <summary>
    /// Spend per ISO week over the weeks a twelve-week window touches, the providers that
    /// report by the day added up.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The weeks are the productivity series' own — <see cref="WeekBuckets"/> over the
    /// twelve weeks back from today — so the brief's Cost block counts back from the same
    /// last bucket as its other blocks.
    /// </para>
    /// <para>
    /// Copilot is left out: it reports a month as one entry, which has no week. When it is
    /// the only provider that answered there is nothing to draw, and the refusal is that
    /// reason. Two currencies refuse through <see cref="DashboardMoney"/>, as everywhere
    /// else here; <see cref="DeriveAsync{T}"/> turns either refusal into the part's reason.
    /// </para>
    /// </remarks>
    private SpendByWeekInsight ByWeek(
        IReadOnlyList<SpendAnswer> answers,
        (DateOnly From, DateOnly To) window)
    {
        _ = window;

        var daily = Answered(answers).Where(pair => pair.Provider != SpendProvider.Copilot).ToList();

        if (daily.Count == 0)
        {
            var missing = answers
                .Where(answer => answer.Provider != SpendProvider.Copilot && answer.Report is null)
                .Select(answer => answer.Reason)
                .Where(reason => !string.IsNullOrWhiteSpace(reason));

            throw new InvalidOperationException(string.Join(" ",
                ["Only Copilot answered, and it reports spend by the month, so there is no weekly figure.", .. missing]));
        }

        var now = time.GetUtcNow();
        var buckets = WeekBuckets.Buckets(now.AddDays(-7 * 12), now);
        var keys = buckets.Select(bucket => bucket.Key).ToHashSet(StringComparer.Ordinal);

        var inWeeks = daily
            .SelectMany(pair => pair.Report.Entries.Select(entry => (pair.Report, entry)))
            .Select(pair => (Key: WeekBuckets.Of(new DateTimeOffset(pair.entry.Date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)).Key, pair.Report, pair.entry))
            .Where(pair => keys.Contains(pair.Key))
            .ToList();

        var spentIn = inWeeks
            .GroupBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => Total([.. group.Select(pair => pair.entry)]), StringComparer.Ordinal);

        var currency = spentIn.Values.Select(money => money.Currency).Distinct(StringComparer.OrdinalIgnoreCase).ToList() switch
        {
            [] => "USD",
            [var one] => one,
            [var first, var second, ..] => throw new InvalidOperationException(
                $"Cannot add {first} to {second}: Backlog holds no exchange rate, so spend by week is not drawn.")
        };

        return new SpendByWeekInsight(
            [.. buckets.Select(bucket => new InsightPoint(
                bucket.Label,
                spentIn.TryGetValue(bucket.Key, out var spent) ? spent.Amount : 0m))],
            currency,
            // An estimate only when an estimate went into the weeks drawn.
            inWeeks.Any(pair => pair.Report.IsEstimate));
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
            Add(provider.DisplayName(), report);
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
                    provider)
                {
                    // Null for a provider that does not split its tokens by direction,
                    // for the same reason as the total above.
                    InputTokens = group.Any(entry => entry.InputTokens is not null) ? group.Sum(entry => entry.InputTokens ?? 0) : null,
                    OutputTokens = group.Any(entry => entry.OutputTokens is not null) ? group.Sum(entry => entry.OutputTokens ?? 0) : null
                }));
        }
    }

    /// <summary>
    /// Sums entries, refusing to add across currencies. A provider that changed the
    /// currency it reports mid-window is not something to average away — the first
    /// currency wins and the mismatch throws, which surfaces as that part's
    /// unavailable reason rather than as a wrong total.
    /// </summary>
    private static DashboardMoney Total(IReadOnlyList<SpendEntry> entries, string emptyCurrency = SpendProjections.DefaultCurrency) =>
        entries.Count == 0
            ? DashboardMoney.Zero(emptyCurrency)
            : entries.Skip(1).Aggregate(entries[0].Cost, (sum, entry) => sum + entry.Cost);

    /// <summary>
    /// The currency a provider that answered with nothing is shown in: the one every
    /// provider that did report is in, or dollars when they reported nothing or
    /// disagree — there is no single month's currency to borrow then.
    /// </summary>
    private static string QuietCurrency(IReadOnlyList<SpendAnswer> answers) =>
        CurrencyOf(answers) is var currency && currency != Mixed ? currency : SpendProjections.DefaultCurrency;

    private const string Mixed = "mixed";

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
            0 => SpendProjections.DefaultCurrency,
            1 => currencies[0],
            _ => Mixed
        };
    }
}
