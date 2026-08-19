using Backlog.Infrastructure.Claude;
using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;

namespace Backlog.Modules.Dashboard.UI.Adapters;

/// <summary>
/// Answers <see cref="IClaudeSpendSource"/> from Anthropic's Claude Code analytics,
/// narrowed to the configured actor.
/// </summary>
/// <remarks>
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
/// </remarks>
internal sealed class ClaudeSpendSource(IClaudeUsageClient usage, ClaudeSettingsStore settings) : IClaudeSpendSource
{
    /// <summary>
    /// How many day requests are in flight at once. Six keeps a month under a
    /// second on a warm connection without looking like a burst.
    /// </summary>
    private const int MaxConcurrentDays = 6;

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

        if (string.IsNullOrWhiteSpace(settings.Current.Actor))
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
        var actor = settings.Current.Actor;

        if (string.IsNullOrWhiteSpace(actor)) return SpendReport.Empty;

        var days = Days(from, to);
        var entries = new List<SpendEntry>();

        foreach (var batch in days.Chunk(MaxConcurrentDays))
        {
            var reports = await Task
                .WhenAll(batch.Select(day => ReadDayAsync(day, cancellationToken)))
                .ConfigureAwait(false);

            entries.AddRange(reports.SelectMany(rows => rows).Where(entry => entry is not null).Select(entry => entry!));
        }

        // Anthropic calls this figure estimated, so the report says so and every
        // part that renders it repeats the word.
        return new SpendReport(entries, Allowance: null, IsEstimate: true);

        async Task<IReadOnlyList<SpendEntry?>> ReadDayAsync(DateOnly day, CancellationToken token)
        {
            ClaudeCodeReport report;
            try
            {
                report = await usage.GetClaudeCodeUsageAsync(day, token).ConfigureAwait(false);
            }
            catch (ClaudeException)
            {
                // One missing day is a gap in a trend, not a failure of the trend.
                // A day Anthropic has not finished aggregating answers this way.
                return [];
            }

            return
            [
                .. report.Actors
                    .Where(row => string.Equals(row.Actor, actor, StringComparison.OrdinalIgnoreCase))
                    .SelectMany(row => row.Models)
                    .Select(model => new SpendEntry(
                        day,
                        model.Model,
                        model.Tokens.TotalTokens,
                        new DashboardMoney(model.EstimatedCost, model.Currency)))
            ];
        }
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
