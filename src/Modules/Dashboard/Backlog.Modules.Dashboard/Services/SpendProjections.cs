using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;

namespace Backlog.Modules.Dashboard.Services;

/// <summary>
/// Where one provider's spend is heading by the end of the calendar month.
/// </summary>
/// <remarks>
/// <para>
/// Month-to-date spend plus the average daily spend over the last seven days times
/// the days left in the month. The days left are the ones after today: today is
/// already under way and counted in the month-to-date figure.
/// </para>
/// <para>
/// The seven days never reach back into the previous month. The window the spend is
/// read for is this month's, and a month that began on a quiet holiday week should not
/// be projected from last month's crunch. When fewer than seven days of the month have
/// passed, the average is over the days there are — today included — so the first of
/// the month projects from the first day alone.
/// </para>
/// </remarks>
public static class SpendProjections
{
    /// <summary>How many days the average daily spend is taken over.</summary>
    public const int AverageDays = 7;

    public static SpendProjection Project(
        SpendProvider provider,
        SpendReport report,
        DateOnly today,
        decimal? budget)
    {
        ArgumentNullException.ThrowIfNull(report);

        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        var currency = report.Entries.Count == 0 ? "USD" : report.Entries[0].Cost.Currency;

        var thisMonth = report.Entries
            .Where(entry => entry.Date >= monthStart && entry.Date <= today)
            .ToList();

        var monthToDate = Sum(thisMonth, currency);

        var averageFrom = today.AddDays(-(AverageDays - 1));
        if (averageFrom < monthStart) averageFrom = monthStart;

        var averageDays = today.DayNumber - averageFrom.DayNumber + 1;
        var recent = Sum([.. thisMonth.Where(entry => entry.Date >= averageFrom)], currency);
        var averageDaily = recent.Amount / averageDays;

        var daysLeft = monthEnd.DayNumber - today.DayNumber;

        return new SpendProjection(
            provider,
            monthToDate,
            new DashboardMoney(averageDaily, currency),
            new DashboardMoney(monthToDate.Amount + (averageDaily * daysLeft), currency),
            budget is { } amount ? new DashboardMoney(amount, currency) : null,
            today,
            daysLeft,
            report.IsEstimate);
    }

    /// <summary>Sums in the report's currency, throwing on a mismatch rather than
    /// adding unlike amounts — the same refusal the other cost parts make.</summary>
    private static DashboardMoney Sum(IReadOnlyList<SpendEntry> entries, string currency) =>
        entries.Aggregate(DashboardMoney.Zero(currency), (sum, entry) => sum + entry.Cost);
}
