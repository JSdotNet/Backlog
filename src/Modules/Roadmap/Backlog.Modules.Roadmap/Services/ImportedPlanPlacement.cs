using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.DomainModels;
using Backlog.SharedKernel;

namespace Backlog.Modules.Roadmap.Services;

/// <summary>
/// Where an import places an item's window (ADR 0013, ruling 4, as amended by local ADR
/// 0019). Plain arithmetic over the dates the plan already holds, the effort the tasks
/// registered, the pace the reader set and the week they work — nothing is estimated
/// here.
/// <para>
/// The start is never read from the document: it is the day after the latest end
/// among the item's predecessors, or today — moved to the next worked day when it is
/// not one. The end is the entry's <c>due:</c> when it wrote one; otherwise it is the day
/// the hours the gathered effort needs at the velocity run out, counted through the
/// working week (<see cref="EffortWindow"/>), and one working week of hours when nothing
/// estimated was gathered.
/// </para>
/// </summary>
public static class ImportedPlanPlacement
{
    /// <summary>The day after the latest of <paramref name="predecessorEnds"/>, or
    /// <paramref name="today"/> when the item waits on nothing. Not yet moved to a
    /// worked day: <see cref="Place"/> does that, for every caller alike. The keep-up
    /// projection's own rule (<see cref="RoadmapProjection.StartAfter"/>), so the import
    /// and the projection floor an item alike.</summary>
    public static DateOnly StartAfter(IEnumerable<DateOnly> predecessorEnds, DateOnly today) =>
        RoadmapProjection.StartAfter(predecessorEnds, today);

    /// <summary>
    /// The window from <paramref name="start"/>, and the rule that placed it.
    /// <para>
    /// The window starts on the first worked day on or after <paramref name="start"/>
    /// (local ADR 0019, §1). A <paramref name="due"/> before that makes a one-day window
    /// on the due date: the person's date is kept, and the plan reports the
    /// contradiction with the predecessor the way it reports any other. Reported, never
    /// corrected.
    /// </para>
    /// </summary>
    /// <param name="gatheredEffort">The story points the plan's tasks registered;
    /// zero when nothing estimated was gathered.</param>
    /// <param name="velocity">Story points per working week; always positive.</param>
    /// <param name="week">The working week the effort is counted through.</param>
    public static (PlannedWindow Window, ImportPlacement Placement) Place(
        DateOnly start,
        DateOnly? due,
        int gatheredEffort,
        decimal velocity,
        WorkingHours week)
    {
        ArgumentNullException.ThrowIfNull(week);

        var from = EffortWindow.FirstWorkedDay(start, week);

        if (due is { } end)
        {
            return (end < from ? PlannedWindow.Of(end, end) : PlannedWindow.Of(from, end), ImportPlacement.DueDate);
        }

        // The one formula every reader derives such a window with, so what the import
        // stores is what the roadmap draws until the effort, the pace or the week changes.
        return (PlannedWindow.Of(from, EffortWindow.EndFrom(from, gatheredEffort, velocity, week)), ImportPlacement.Effort);
    }
}
