using Backlog.Infrastructure.Claude;
using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;

namespace Backlog.Modules.Dashboard.UI.Adapters;

/// <summary>
/// Answers <see cref="IClaudeSpendSource"/> from Anthropic's Claude Code analytics,
/// narrowed to the configured actor of each configured account and added up.
/// </summary>
/// <remarks>
/// <para>
/// One person, several organizations: a personal Console and an employer's each
/// issue their own admin key, and the spend is the sum. Every account with a key
/// and an actor is read; one without either is skipped rather than being allowed
/// to blank the rest, because a half-filled second card is an ordinary state of
/// the Settings page rather than a fault.
/// </para>
/// <para>
/// The Claude Code report is the only one of Anthropic's three that carries an
/// actor, and it carries an estimated cost per model beside it. The organization
/// cost report holds the billed money but has no actor dimension at all, so a
/// personal figure has to come from here — and it is an estimate, which travels on
/// the report rather than being quietly presented as an invoice.
/// </para>
/// <para>
/// The endpoint covers exactly one day per call, so a window is a fan-out. Days are
/// requested with bounded concurrency: a month is thirty calls and issuing them all
/// at once is how an admin key meets a rate limit.
/// </para>
/// <para>
/// Most of that fan-out is avoided when a host composes a <paramref name="cache"/>.
/// A day more than <see cref="SettlementDays"/> behind today is read once and
/// remembered per account and actor, and a later window that covers it costs no
/// call at all; only the days Anthropic may still be aggregating are asked for on
/// every read. Optional because it is an optimization, and <paramref name="time"/>
/// exists only to say which days those are.
/// </para>
/// </remarks>
internal sealed class ClaudeSpendSource(
    IClaudeUsageClient usage,
    ClaudeSettingsStore settings,
    IClaudeCodeUsageCache? cache = null,
    TimeProvider? time = null) : IClaudeSpendSource
{
    /// <summary>
    /// How many day requests are in flight at once. Six keeps a month under a
    /// second on a warm connection without looking like a burst.
    /// </summary>
    private const int MaxConcurrentDays = 6;

    /// <summary>
    /// How many days behind today a day has to be before its report is treated as
    /// final. Anthropic aggregates the Claude Code report over the hours after a
    /// day ends, in the organization's own time zone, so yesterday can still be
    /// filling in this morning; the day before is done. Two days re-asks two calls
    /// per account per read, which is the whole remaining cost.
    /// </summary>
    internal const int SettlementDays = 2;

    /// <summary>
    /// The longest window this adapter will fan out over. Seven months of days is
    /// about two hundred calls, which is the trend window; anything beyond that is
    /// a caller mistake rather than a request worth making.
    /// </summary>
    private const int MaxDays = 240;

    public async Task<InsightAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        var available = await usage.GetAvailabilityAsync(cancellationToken).ConfigureAwait(false);

        if (!available.IsAvailable) return InsightAvailability.Unavailable(available.Reason);

        if (settings.Current.ReportingAccounts.Count == 0)
        {
            return InsightAvailability.Unavailable(
                "Add your Anthropic account in Settings. The Claude Code report covers the whole organization, so "
                + "Backlog needs to know which actor is you before it can show your spend rather than everyone's.");
        }

        return InsightAvailability.Available;
    }

    public async Task<SpendReport> GetSpendAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var accounts = settings.Current.ReportingAccounts;

        if (accounts.Count == 0) return SpendReport.Empty;

        var days = Days(from, to);
        var entries = new List<SpendEntry>();

        // Accounts in turn, days in bounded batches within each: the rate limit
        // is per organization, so fanning accounts out together would not go any
        // faster where it matters and would burst against each of them at once.
        foreach (var account in accounts)
        {
            foreach (var batch in days.Chunk(MaxConcurrentDays))
            {
                var reports = await Task
                    .WhenAll(batch.Select(day => ReadDayAsync(account, day, cancellationToken)))
                    .ConfigureAwait(false);

                entries.AddRange(reports.SelectMany(rows => rows).Where(entry => entry is not null).Select(entry => entry!));
            }
        }

        // Anthropic calls this figure estimated, so the report says so and every
        // part that renders it repeats the word.
        return new SpendReport(entries, Allowance: null, IsEstimate: true);

        async Task<IReadOnlyList<SpendEntry?>> ReadDayAsync(ClaudeAccount account, DateOnly day, CancellationToken token)
        {
            var actor = account.Actor!;
            var settled = cache is not null && IsSettled(day);

            if (settled && cache!.TryRead(account, actor, day) is { } remembered)
            {
                return Entries(day, remembered.Models);
            }

            ClaudeCodeReport report;
            try
            {
                report = await usage.GetClaudeCodeUsageAsync(account, day, token).ConfigureAwait(false);
            }
            catch (ClaudeException)
            {
                // One missing day is a gap in a trend, not a failure of the trend.
                // A day Anthropic has not finished aggregating answers this way —
                // and it is deliberately not remembered as empty, because a refusal
                // is not a figure.
                return [];
            }

            var models = report.Actors
                .Where(row => string.Equals(row.Actor, actor, StringComparison.OrdinalIgnoreCase))
                .SelectMany(row => row.Models)
                .ToList();

            // Written only once settled: a day that was still aggregating when it
            // was read would otherwise be frozen at whatever it had reached.
            if (settled) cache!.Write(account, actor, day, new ClaudeCodeSettledDay(models));

            return Entries(day, models);
        }

        static IReadOnlyList<SpendEntry?> Entries(DateOnly day, IReadOnlyList<ClaudeCodeModelUsage> models) =>
        [
            .. models.Select(model => new SpendEntry(
                day,
                model.Model,
                model.Tokens.TotalTokens,
                new DashboardMoney(model.EstimatedCost, model.Currency)))
        ];
    }

    /// <summary>Whether a day's report can no longer change: it is at least
    /// <see cref="SettlementDays"/> before today, by the clock this adapter was
    /// given and in UTC, which is the latest any organization's day can end.</summary>
    private bool IsSettled(DateOnly day)
    {
        var today = DateOnly.FromDateTime((time ?? TimeProvider.System).GetUtcNow().UtcDateTime);

        return day <= today.AddDays(-SettlementDays);
    }

    private static IReadOnlyList<DateOnly> Days(DateOnly from, DateOnly to)
    {
        var days = new List<DateOnly>();

        for (var day = from; day <= to && days.Count < MaxDays; day = day.AddDays(1))
        {
            days.Add(day);
        }

        return days;
    }
}
