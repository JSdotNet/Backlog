using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.DomainModels;

namespace Backlog.Modules.Roadmap.Services;

/// <summary>
/// Where an import places an item's window (ADR 0013, ruling 4). Plain arithmetic
/// over the dates the plan already holds, the effort the tasks registered, and the
/// pace the reader set — nothing is estimated here.
/// <para>
/// The start is never read from the document: it is the day after the latest end
/// among the item's predecessors, or today. The end is the entry's <c>due:</c> when
/// it wrote one; otherwise the length is the gathered effort over the velocity,
/// rounded up and never under <see cref="MinimumSpanDays"/>, and
/// <see cref="DefaultSpanDays"/> when nothing estimated was gathered. Days are
/// calendar days, because the roadmap models no working week.
/// </para>
/// </summary>
public static class ImportedPlanPlacement
{
    /// <summary>The shortest window a length can make: a window is at least a day.</summary>
    public const int MinimumSpanDays = 1;

    /// <summary>The span when the total would be a number invented from nothing — a
    /// working week, the smallest span that reads as a plan rather than a day.</summary>
    public const int DefaultSpanDays = 5;

    /// <summary>The day after the latest of <paramref name="predecessorEnds"/>, or
    /// <paramref name="today"/> when the item waits on nothing.</summary>
    public static DateOnly StartAfter(IEnumerable<DateOnly> predecessorEnds, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(predecessorEnds);

        var ends = predecessorEnds.ToList();
        return ends.Count == 0 ? today : ends.Max().AddDays(1);
    }

    /// <summary>
    /// The window from <paramref name="start"/>, and the rule that placed it.
    /// <para>
    /// A <paramref name="due"/> before the start makes a one-day window on the due
    /// date: the person's date is kept, and the plan reports the contradiction with
    /// the predecessor the way it reports any other. Reported, never corrected.
    /// </para>
    /// </summary>
    /// <param name="gatheredEffort">The story points the plan's tasks registered;
    /// zero when nothing estimated was gathered.</param>
    /// <param name="velocity">Story points per day; always positive.</param>
    public static (PlannedWindow Window, ImportPlacement Placement) Place(
        DateOnly start,
        DateOnly? due,
        int gatheredEffort,
        decimal velocity)
    {
        if (due is { } end)
        {
            return (end < start ? PlannedWindow.Of(end, end) : PlannedWindow.Of(start, end), ImportPlacement.DueDate);
        }

        var days = Days(gatheredEffort, velocity);

        // Clamped so an absurd total cannot run the window off the calendar.
        var lastDay = Math.Min((long)start.DayNumber + days - 1, DateOnly.MaxValue.DayNumber);
        return (PlannedWindow.Of(start, DateOnly.FromDayNumber((int)lastDay)), ImportPlacement.Effort);
    }

    /// <summary>How many calendar days a gathered total spans at a velocity.</summary>
    public static int Days(int gatheredEffort, decimal velocity)
    {
        if (gatheredEffort <= 0) return DefaultSpanDays;
        if (velocity <= 0) throw new ArgumentOutOfRangeException(nameof(velocity), velocity, "Velocity is always positive.");

        var days = Math.Ceiling(gatheredEffort / velocity);
        return days >= int.MaxValue ? int.MaxValue : Math.Max(MinimumSpanDays, (int)days);
    }
}
