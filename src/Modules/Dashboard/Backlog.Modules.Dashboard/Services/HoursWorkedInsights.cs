using Backlog.Modules.Dashboard.Abstractions;
using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;

namespace Backlog.Modules.Dashboard.Services;

/// <summary>
/// The hours worked over the dashboard's window, per local day and per calendar week,
/// split by office hours (local ADR 0019, §7).
/// </summary>
/// <remarks>
/// <para>
/// The window starts on the Monday of the week the scope's window starts in, as the
/// weekly parts' first bucket does, so the oldest week is a whole week; it ends today,
/// so the week today falls in counts its planned hours so far and sets them beside the
/// hours worked so far. Weeks are calendar weeks, Monday first, on the person's local
/// calendar — where an hour worked lands is a local question, unlike the UTC weeks a
/// merged pull request is counted in.
/// </para>
/// <para>
/// The split itself is the adapter's, which may see the working week and the stretches
/// both. This only lays the dates out and adds them up. No cache: the source reads
/// through the activity cache, so a second read is cheap and a refresh is current.
/// </para>
/// </remarks>
internal sealed class HoursWorkedInsights(IHoursWorkedSource source, TimeProvider time) : IHoursWorkedInsights
{
    public async Task<InsightResult<HoursWorkedInsight>> GetHoursWorkedAsync(
        DashboardScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        var first = MondayOf(today.AddDays(-7 * scope.Weeks));

        IReadOnlyList<HoursWorkedDay>? read;
        try
        {
            read = await source.ReadAsync(first, today, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return InsightResult<HoursWorkedInsight>.Unavailable(
                $"Hours worked are unavailable: the agent activity could not be read. {exception.Message}");
        }

        if (read is null)
        {
            return InsightResult<HoursWorkedInsight>.Unavailable(
                "Hours worked are unavailable: the Sessions area is switched off, or this app reads no agent activity.");
        }

        var days = read
            .Where(day => day.Date >= first && day.Date <= today)
            .OrderBy(day => day.Date)
            .Select(day => new HoursWorkedPeriod(day.Date, day.Date, day.Inside, day.Outside, day.Planned))
            .ToList();

        var weeks = days
            .GroupBy(day => MondayOf(day.From))
            .Select(week => new HoursWorkedPeriod(
                week.Key,
                week.Max(day => day.Through),
                Sum(week, day => day.Inside),
                Sum(week, day => day.Outside),
                Sum(week, day => day.Planned)))
            .ToList();

        return InsightResult<HoursWorkedInsight>.Ready(new HoursWorkedInsight(days, weeks));
    }

    private static DateOnly MondayOf(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    private static TimeSpan Sum(IEnumerable<HoursWorkedPeriod> days, Func<HoursWorkedPeriod, TimeSpan> of) =>
        TimeSpan.FromTicks(days.Sum(day => of(day).Ticks));
}
