namespace Backlog.UI.Components.Tasks;

/// <summary>
/// The one way a bar across days is cut into a week row and given a lane, shared by
/// the plans and the events so the two layers stack by the same rule.
/// </summary>
internal static class CalendarLanes
{
    /// <summary>
    /// The pieces of <paramref name="items"/> that fall in the week starting on the
    /// Monday <paramref name="weekStart"/>, each made by <paramref name="segment"/> from
    /// its column, span, lane and whether it carries on from or into another week, and
    /// how many lanes the week needs.
    /// <para>
    /// Earliest first, a longer bar before a shorter one starting the same day, ties in
    /// the order given; each takes the first lane free over all its days. An item whose
    /// end is before its start draws nothing.
    /// </para>
    /// </summary>
    public static (IReadOnlyList<TSegment> Segments, int Lanes) Week<TItem, TSegment>(
        DateOnly weekStart,
        IReadOnlyList<TItem> items,
        Func<TItem, DateOnly> start,
        Func<TItem, DateOnly> end,
        Func<TItem, Placement, TSegment> segment)
    {
        ArgumentNullException.ThrowIfNull(items);

        var weekEnd = weekStart.AddDays(6);
        var cut = items
            .Select((item, order) => (item, order, start: start(item), end: end(item)))
            .Where(entry => entry.end >= entry.start && entry.start <= weekEnd && entry.end >= weekStart)
            .Select(entry =>
            {
                var from = entry.start < weekStart ? weekStart : entry.start;
                var to = entry.end > weekEnd ? weekEnd : entry.end;
                var column = from.DayNumber - weekStart.DayNumber + 1;
                return (entry.item, entry.order, entry.start, entry.end, column, span: to.DayNumber - from.DayNumber + 1);
            })
            .OrderBy(entry => entry.column)
            .ThenByDescending(entry => entry.span)
            .ThenBy(entry => entry.order)
            .ToList();

        // The last column each lane is taken up to.
        var lanes = new List<int>();
        var segments = new List<TSegment>(cut.Count);

        foreach (var (item, _, first, last, column, span) in cut)
        {
            var lane = lanes.FindIndex(taken => taken < column);
            if (lane < 0)
            {
                lanes.Add(0);
                lane = lanes.Count - 1;
            }

            lanes[lane] = column + span - 1;
            segments.Add(segment(item, new Placement(
                column,
                span,
                lane + 1,
                ContinuesBefore: first < weekStart,
                ContinuesAfter: last > weekEnd)));
        }

        return (segments, lanes.Count);
    }

    /// <summary>Where one piece sits in its week row.</summary>
    internal readonly record struct Placement(int Column, int Span, int Lane, bool ContinuesBefore, bool ContinuesAfter);
}
