namespace Backlog.UI.Components.Roadmap;

/// <summary>Which part of a bar a gesture has hold of.</summary>
public enum RoadmapDrag
{
    /// <summary>The whole bar: it keeps its length and changes when it happens,
    /// and may change which row it happens on.</summary>
    Move,

    /// <summary>The left edge: it starts earlier or later and still ends when it
    /// ended.</summary>
    ResizeStart,

    /// <summary>The right edge: it still starts when it started and runs for
    /// longer or shorter.</summary>
    ResizeEnd
}

/// <summary>
/// Where a bar ended up. Raised once, when the gesture settles.
/// <para>
/// The timeline never edits the plan it was given. It reports the new placement
/// and redraws from whatever the host hands back, for the same reason the task
/// list reports a reorder rather than performing one: only the host knows
/// whether the change survives a reload, whether it is allowed, and what else
/// has to move with it.
/// </para>
/// </summary>
/// <param name="BarId">Which bar moved.</param>
/// <param name="RowId">The row it is on now — unchanged unless the whole bar was
/// dragged to another row.</param>
/// <param name="Start">Its new first day, inclusive.</param>
/// <param name="End">Its new last day, inclusive.</param>
/// <param name="Kind">Which gesture produced it, so a host can tell a
/// rescheduling from a re-estimate without comparing dates.</param>
public sealed record RoadmapChange(
    string BarId,
    string RowId,
    DateOnly Start,
    DateOnly End,
    RoadmapDrag Kind)
{
    /// <summary>
    /// What a gesture of <paramref name="weekSteps"/> weeks does to a bar, or
    /// <see langword="null"/> when it does nothing at all.
    /// <para>
    /// Null is the important half. A drag that travelled less than half a week
    /// and let go, or an edge pulled past the opposite edge and clamped, has
    /// produced no change — and reporting one anyway would push a no-op into
    /// whatever the host does with these, including its undo stack. It would
    /// also mean that merely picking a bar up and putting it down snapped its
    /// dates to the nearest week, silently rewriting a plan the reader only
    /// wanted to look at.
    /// </para>
    /// </summary>
    /// <param name="bar">The bar as it stands.</param>
    /// <param name="kind">Which part of it is being dragged.</param>
    /// <param name="weekSteps">How many whole weeks, signed.</param>
    /// <param name="rowId">The row it would land on. Only consulted for a
    /// <see cref="RoadmapDrag.Move"/>; an edge cannot change rows.</param>
    /// <param name="weekStart">Which day a week begins on here.</param>
    public static RoadmapChange? For(
        RoadmapBar bar,
        RoadmapDrag kind,
        int weekSteps,
        string? rowId,
        DayOfWeek weekStart)
    {
        if (bar.Locked) return null;

        return kind switch
        {
            RoadmapDrag.Move => Moved(bar, weekSteps, rowId ?? bar.RowId, weekStart),
            RoadmapDrag.ResizeStart => StartMoved(bar, weekSteps, weekStart),
            _ => EndMoved(bar, weekSteps, weekStart)
        };
    }

    private static RoadmapChange? Moved(RoadmapBar bar, int weekSteps, string rowId, DayOfWeek weekStart)
    {
        var sameRow = string.Equals(rowId, bar.RowId, StringComparison.Ordinal);

        if (weekSteps == 0)
        {
            // Dropped on a different row without travelling in time. The dates
            // are left exactly alone — snapping them here would be a second,
            // unasked-for edit riding along with the one the reader made.
            return sameRow ? null : new RoadmapChange(bar.Id, rowId, bar.Start, bar.End, RoadmapDrag.Move);
        }

        var start = RoadmapWindow.SnapToWeek(bar.Start.AddDays(weekSteps * 7), weekStart);

        // Length is carried, not recomputed. A bar dragged across the year is
        // the same piece of work; only when it happens has changed.
        return new RoadmapChange(bar.Id, rowId, start, start.AddDays(bar.Days - 1), RoadmapDrag.Move);
    }

    private static RoadmapChange? StartMoved(RoadmapBar bar, int weekSteps, DayOfWeek weekStart)
    {
        if (weekSteps == 0) return null;

        var start = RoadmapWindow.SnapToWeek(bar.Start.AddDays(weekSteps * 7), weekStart);

        // Pulled past its own end. Clamped to the week the end is in rather than
        // refused, so the reader gets the shortest bar the grid allows instead
        // of a gesture that appeared to do nothing.
        if (start > bar.End) start = RoadmapWindow.SnapToWeek(bar.End, weekStart);
        if (start > bar.End) start = bar.End;

        return start == bar.Start ? null : new RoadmapChange(bar.Id, bar.RowId, start, bar.End, RoadmapDrag.ResizeStart);
    }

    private static RoadmapChange? EndMoved(RoadmapBar bar, int weekSteps, DayOfWeek weekStart)
    {
        if (weekSteps == 0) return null;

        var end = SnapWeekEnd(bar.End.AddDays(weekSteps * 7), weekStart);

        if (end < bar.Start) end = SnapWeekEnd(bar.Start, weekStart);
        if (end < bar.Start) end = bar.Start;

        return end == bar.End ? null : new RoadmapChange(bar.Id, bar.RowId, bar.Start, end, RoadmapDrag.ResizeEnd);
    }

    /// <summary>
    /// The end of the week a date falls nearest to — the day before a week
    /// begins, because <see cref="RoadmapBar.End"/> is the last day inclusive.
    /// <para>
    /// Snapping an end date the same way as a start date would land it on a
    /// Monday and draw a bar that stops the moment its last week opens. Bars
    /// here begin on the first day of a week and finish on the last, which is
    /// how the weeks under them read.
    /// </para>
    /// </summary>
    public static DateOnly SnapWeekEnd(DateOnly date, DayOfWeek weekStart) =>
        RoadmapWindow.SnapToWeek(date.AddDays(1), weekStart).AddDays(-1);

    /// <summary>The bar as it would be if this change were applied. What the
    /// timeline draws while a drag is still in flight, and what a host can use
    /// to apply the change without restating the arithmetic.</summary>
    public RoadmapBar ApplyTo(RoadmapBar bar) =>
        bar with { RowId = RowId, Start = Start, End = End };
}
