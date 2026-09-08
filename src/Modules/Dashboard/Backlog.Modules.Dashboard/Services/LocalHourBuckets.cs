namespace Backlog.Modules.Dashboard.Services;

/// <summary>One (local date, local hour) cell while it is being filled.</summary>
internal readonly record struct HourCell(DateOnly Day, int Hour);

/// <summary>What one sweep found in one cell.</summary>
/// <param name="Peak">The most intervals overlapping at any instant inside the
/// cell.</param>
/// <param name="Total">Interval-time landing in the cell, with overlapping intervals
/// summed rather than merged.</param>
internal sealed record HourReading(int Peak, TimeSpan Total)
{
    /// <summary>What a cell nobody worked reads as. A zero rather than a gap: the
    /// sweep never stores one of these, and the grid supplies it for every cell the
    /// sweep did not mention, so "nothing happened" and "not reported" stay two
    /// different answers.</summary>
    internal static HourReading Nothing { get; } = new(0, TimeSpan.Zero);
}

/// <summary>
/// Intervals into hour-of-the-day cells on a given local clock: how many were running
/// at once in each cell, and how much interval-time landed in it.
/// </summary>
/// <remarks>
/// <para>
/// The zone is a parameter on every entry point and is never read from here, exactly as
/// <see cref="WeekBuckets"/> takes instants and never reads a clock. A grid asserted
/// against whatever zone CI happens to run in is a grid asserted against nothing, and a
/// class that fetched <c>TimeZoneInfo.Local</c> itself would be untestable in precisely
/// the way that matters.
/// </para>
/// <para>
/// Local hours are the only local thing on this surface. Everything else the dashboard
/// draws — the ISO week columns, the last-activity moment — is UTC. The mixture is a
/// genuine hazard and the defence is that the surface says so; it is not defended here.
/// </para>
/// <para>
/// <b>DST.</b> An interval is walked in UTC and cut at each local hour boundary, so a
/// cut that straddles a transition mis-attributes at most that transition's own length,
/// in one cell, twice a year. That is a stated limit rather than a hidden defect: the
/// alternative is a calendar-aware walk whose complexity would be paid every day to fix
/// two hours of it.
/// </para>
/// </remarks>
internal static class LocalHourBuckets
{
    /// <summary>How many dated days the grid covers. Seven, and fixed — see
    /// <see cref="Cells"/>.</summary>
    internal const int Days = 7;

    private const int HoursInADay = 24;

    /// <summary>
    /// The upper bound on how many hour-pieces one stretch may be cut into before the
    /// loop gives up. Bounded rather than trusted to terminate, the guard
    /// <see cref="WeekBuckets.Buckets"/> already carries and for its reason: a caller
    /// handing over an absurd window should get a short answer, not a hang. Set well
    /// past a year so no window this surface can ask for ever reaches it.
    /// </summary>
    private const int MaxPieces = HoursInADay * 400;

    /// <summary>
    /// The 168 cells the grid renders, oldest day first and hour 00 first within a day:
    /// the seven local dates ending on the one <paramref name="to"/> falls in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The literal last seven dated days, not seven days of the week folded across
    /// the window.</b> The fold was the other candidate and it is the more elegant one —
    /// it would make the grid a profile of habit, and it would let the grid follow the
    /// period control like every other figure in the part. It was rejected because the
    /// question is what actually happened, and a Tuesday that is really eleven Tuesdays
    /// added together answers a different one. A row here is one dated day and a cell is
    /// an hour that happened once, so there is no aggregation to disambiguate and no
    /// copy on screen explaining which of two aggregations produced which number.
    /// </para>
    /// <para>
    /// <b>Seven, whatever the period control says.</b> This is the asymmetry the caller
    /// has to state on screen: the tiles above the grid widen from four weeks to twelve
    /// and the grid does not move. Eighty-four dated rows is not a grid, and folding
    /// them back into seven is the option already rejected above — so the honest answer
    /// is a fixed seven days with the refusal said out loud, on the same rule the part
    /// already refuses the repository dimension under.
    /// </para>
    /// <para>
    /// Always all 168, so a cell with nothing in it is a zero and never a gap. That
    /// distinction is one a heatmap draws with its own step, and handing it a missing
    /// point where a zero belongs would say "not reported" about an hour nobody worked.
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<HourCell> Cells(DateTimeOffset to, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        var last = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(to, zone).DateTime);
        var first = last.AddDays(-(Days - 1));

        return
        [
            .. Enumerable.Range(0, Days).SelectMany(day =>
                Enumerable.Range(0, HoursInADay).Select(hour => new HourCell(first.AddDays(day), hour)))
        ];
    }

    /// <summary>
    /// Which of the grid's cells an interval touches, in order, and none at all for an
    /// interval that falls outside it.
    /// <para>
    /// Hours rather than days, because the questions asked of it are hour-level: whether
    /// a session ran on a day is answered by the days these cells fall on, but whether it
    /// ran inside the working day is not — a session from four until eight was inside for
    /// one hour of it and outside for three, and a day-level answer could only have said
    /// one of those.
    /// </para>
    /// <para>
    /// Here rather than at the caller for the reason every other conversion in this class
    /// is: a second place that turned an instant into a local hour would be a second place
    /// for the grid's cells and the figures beside them to disagree about when something
    /// happened.
    /// </para>
    /// </summary>
    internal static IEnumerable<HourCell> HoursTouched(
        DateTimeOffset from,
        DateTimeOffset to,
        DateTimeOffset gridEnd,
        TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        if (to <= from) yield break;

        var last = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(gridEnd, zone).DateTime);
        var first = last.AddDays(-(Days - 1));

        foreach (var (cell, length) in Pieces(from, to, zone))
        {
            // A zero-length piece touches nothing. The same rule the sweep raises a peak
            // under, and for the same reason: an interval that ends exactly on an hour
            // boundary has not happened in the hour after it.
            if (length <= TimeSpan.Zero) continue;

            if (cell.Day < first || cell.Day > last) continue;

            yield return cell;
        }
    }

    /// <summary>
    /// Sweeps a set of intervals into the cells: the peak number overlapping at any
    /// instant in the cell, and the total interval-time landing in it with overlapping
    /// intervals summed rather than merged.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One sweep for both, because the total <em>is</em> the integral of the concurrency
    /// the peak reads off. Two passes would be two definitions of what "at once" means
    /// and they would drift, which is how a tile and the grid under it come to disagree
    /// about the same week.
    /// </para>
    /// <para>
    /// Sparse on purpose: only cells something landed in are present. A caller drawing a
    /// grid supplies <see cref="HourReading.Nothing"/> for the rest, so this method never
    /// has to be told which cells the caller means.
    /// </para>
    /// <para>
    /// Intervals are clipped to the window rather than dropped, so a stretch that began
    /// before it contributes the part inside — otherwise two readings of the same
    /// quarter would disagree depending on what came before it.
    /// </para>
    /// </remarks>
    internal static IReadOnlyDictionary<HourCell, HourReading> Sweep(
        IEnumerable<(DateTimeOffset From, DateTimeOffset To)> intervals,
        DateTimeOffset from,
        DateTimeOffset to,
        TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(intervals);
        ArgumentNullException.ThrowIfNull(zone);

        var readings = new Dictionary<HourCell, (int Peak, TimeSpan Total)>();

        // A reversed or empty window has no cells to fill. An empty answer rather than
        // a throw: the window comes from a control, and a control cannot be wrong.
        if (to <= from) return Freeze(readings);

        var endpoints = Endpoints(intervals, from, to);
        if (endpoints.Count == 0) return Freeze(readings);

        // At equal instants a close sorts before an open, so an interval that ends
        // exactly as another begins reads as one agent rather than two. Delta is -1 for
        // a close and +1 for an open, which is why the plain comparison is the rule.
        endpoints.Sort(static (left, right) =>
        {
            var byInstant = left.At.CompareTo(right.At);
            return byInstant != 0 ? byInstant : left.Delta.CompareTo(right.Delta);
        });

        var concurrency = 0;
        DateTimeOffset? previous = null;

        foreach (var endpoint in endpoints)
        {
            if (previous is { } opened && concurrency > 0 && endpoint.At > opened)
            {
                foreach (var (cell, length) in Pieces(opened, endpoint.At, zone))
                {
                    // Only a piece with length raises a peak. A zero-length one would
                    // otherwise stamp a peak on a cell where nothing ran for any time
                    // at all.
                    if (length <= TimeSpan.Zero) continue;

                    var current = readings.TryGetValue(cell, out var found) ? found : (Peak: 0, Total: TimeSpan.Zero);

                    readings[cell] = (
                        Math.Max(current.Peak, concurrency),

                        // Times the concurrency, not plus the length. This is where
                        // "twelve agents for an hour is twelve agent-hours" lives, and
                        // it is the same concurrency the peak above reads — which is the
                        // entire reason for one sweep instead of two.
                        current.Total + (length * concurrency));
                }
            }

            concurrency += endpoint.Delta;
            previous = endpoint.At;
        }

        return Freeze(readings);
    }

    /// <summary>
    /// Every interval's two ends, clipped to the window. An interval left with no length
    /// after clipping is dropped rather than carried as a pair of coincident endpoints:
    /// it contributes nothing either way, and dropping it keeps a zero-length stretch out
    /// of the peak.
    /// </summary>
    private static List<(DateTimeOffset At, int Delta)> Endpoints(
        IEnumerable<(DateTimeOffset From, DateTimeOffset To)> intervals,
        DateTimeOffset from,
        DateTimeOffset to)
    {
        var endpoints = new List<(DateTimeOffset At, int Delta)>();

        foreach (var (start, end) in intervals)
        {
            var opened = start > from ? start : from;
            var closed = end < to ? end : to;

            if (closed <= opened) continue;

            endpoints.Add((opened, 1));
            endpoints.Add((closed, -1));
        }

        return endpoints;
    }

    /// <summary>
    /// One stretch cut at local hour boundaries: which cell each piece belongs to, and
    /// how long that piece is.
    /// </summary>
    private static IEnumerable<(HourCell Cell, TimeSpan Length)> Pieces(
        DateTimeOffset from,
        DateTimeOffset to,
        TimeZoneInfo zone)
    {
        var cursor = from;

        for (var guard = 0; cursor < to && guard < MaxPieces; guard++)
        {
            var local = TimeZoneInfo.ConvertTime(cursor, zone);

            // How far into its local hour the cursor already is, so the step is what
            // remains of that hour. Always in (0, 1h], which is what makes the loop
            // move.
            var into = local.TimeOfDay - TimeSpan.FromHours(local.Hour);
            var next = cursor + (TimeSpan.FromHours(1) - into);

            if (next > to) next = to;

            yield return (new HourCell(DateOnly.FromDateTime(local.DateTime), local.Hour), next - cursor);

            cursor = next;
        }
    }

    private static IReadOnlyDictionary<HourCell, HourReading> Freeze(
        Dictionary<HourCell, (int Peak, TimeSpan Total)> readings) =>
        readings.ToDictionary(entry => entry.Key, entry => new HourReading(entry.Value.Peak, entry.Value.Total));
}
