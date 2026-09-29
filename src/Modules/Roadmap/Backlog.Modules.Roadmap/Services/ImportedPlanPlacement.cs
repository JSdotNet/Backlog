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
/// it wrote one; otherwise the length is the gathered effort over the velocity — a
/// week being seven days — rounded up and never under <see cref="MinimumSpanDays"/>, and
/// <see cref="DefaultSpanDays"/> when nothing estimated was gathered. Days are
/// calendar days, because the roadmap models no working week.
/// </para>
/// </summary>
public static class ImportedPlanPlacement
{
    /// <inheritdoc cref="EffortWindow.MinimumSpanDays"/>
    public const int MinimumSpanDays = EffortWindow.MinimumSpanDays;

    /// <inheritdoc cref="EffortWindow.DefaultSpanDays"/>
    public const int DefaultSpanDays = EffortWindow.DefaultSpanDays;

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
    /// <param name="velocity">Story points per week; always positive.</param>
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

        // The one formula every reader derives such a window with, so what the import
        // stores is what the roadmap draws until the effort or the pace changes.
        return (PlannedWindow.Of(start, EffortWindow.EndFrom(start, gatheredEffort, velocity)), ImportPlacement.Effort);
    }

    /// <inheritdoc cref="EffortWindow.Days"/>
    public static int Days(int gatheredEffort, decimal velocity) => EffortWindow.Days(gatheredEffort, velocity);
}
