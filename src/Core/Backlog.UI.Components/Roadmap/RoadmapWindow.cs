namespace Backlog.UI.Components.Roadmap;

/// <summary>
/// The stretch of time the timeline draws, and the arithmetic that turns a date
/// into a position along it.
/// <para>
/// The window always begins on a quarter boundary and ends on the last day of a
/// quarter, because the axis above it is ruled in quarters and a window that
/// started mid-February would put the first tick somewhere with no meaning.
/// <see cref="Covering"/> is what does the widening.
/// </para>
/// <para>
/// Positions are day-proportional rather than one equal column per quarter. Q1
/// is ninety days and Q3 ninety-two, so equal columns would make identical work
/// draw two percent wider in the summer — and the width of a bar is the one
/// thing on this chart a reader measures by eye.
/// </para>
/// </summary>
public sealed record RoadmapWindow
{
    /// <summary>A quarter's nominal length, used to turn "how wide is a quarter
    /// on screen" into "how wide is a day". Quarters are 90 to 92 days long, so
    /// any single number is a rounding; this one is the average, which keeps a
    /// year exactly four column-widths wide.</summary>
    public const double NominalQuarterDays = 365.25 / 4;

    public RoadmapWindow(DateOnly start, DateOnly end)
    {
        // An end before its start would make every fraction negative and every
        // bar draw off the left edge. Ordering them is a kinder answer than an
        // exception the caller cannot act on from inside a render.
        Start = start <= end ? start : end;
        End = start <= end ? end : start;
        Quarters = QuartersBetween(Start, End);
    }

    /// <summary>First day of the window, inclusive.</summary>
    public DateOnly Start { get; }

    /// <summary>Last day of the window, inclusive.</summary>
    public DateOnly End { get; }

    /// <summary>The quarter columns the axis is ruled with, in order.</summary>
    public IReadOnlyList<RoadmapQuarter> Quarters { get; }

    /// <summary>How many days the window spans, counting both ends. Never zero,
    /// so it is always safe to divide by.</summary>
    public int TotalDays => End.DayNumber - Start.DayNumber + 1;

    /// <summary>How far into the window a date falls, 0 at the first day and 1
    /// at the day after the last. Not clamped: a caller asking where an
    /// out-of-window date would go gets the honest answer, and the timeline
    /// decides separately whether to draw it.</summary>
    public double FractionAt(DateOnly date) =>
        (date.DayNumber - Start.DayNumber) / (double)TotalDays;

    /// <summary>The inverse: which day sits at a fraction of the window.</summary>
    public DateOnly DateAt(double fraction) =>
        Start.AddDays((int)Math.Round(fraction * TotalDays, MidpointRounding.AwayFromZero));

    public bool Contains(DateOnly date) => date >= Start && date <= End;

    /// <summary>
    /// The narrowest quarter-aligned window that holds every date given, with
    /// nothing to show falling back to the quarter <paramref name="fallback"/>
    /// is in.
    /// </summary>
    /// <remarks>
    /// A caller may set the window itself instead. This exists because the
    /// common case — "draw my plan" — otherwise makes every caller write the
    /// same quarter-rounding by hand, and get it subtly wrong at year ends.
    /// </remarks>
    public static RoadmapWindow Covering(IEnumerable<DateOnly> dates, DateOnly fallback)
    {
        var days = dates as ICollection<DateOnly> ?? [.. dates];

        if (days.Count == 0) return new RoadmapWindow(StartOfQuarter(fallback), EndOfQuarter(fallback));

        var first = days.Min();
        var last = days.Max();

        return new RoadmapWindow(StartOfQuarter(first), EndOfQuarter(last));
    }

    /// <summary>The first day of the quarter a date falls in.</summary>
    public static DateOnly StartOfQuarter(DateOnly date) =>
        new(date.Year, (QuarterOf(date) - 1) * 3 + 1, 1);

    /// <summary>The last day of the quarter a date falls in.</summary>
    public static DateOnly EndOfQuarter(DateOnly date) =>
        StartOfQuarter(date).AddMonths(3).AddDays(-1);

    /// <summary>Which quarter a date is in, 1 through 4.</summary>
    public static int QuarterOf(DateOnly date) => (date.Month - 1) / 3 + 1;

    /// <summary>
    /// The week boundary a date belongs to once a drag has let go of it —
    /// nearest, not floor.
    /// <para>
    /// Nearest is the difference between a bar that follows the pointer and one
    /// that always lags behind it. Flooring would mean a bar dragged five days
    /// forward moves nothing at all, and the reader would conclude the drag was
    /// broken rather than that it had been rounded down.
    /// </para>
    /// <para>
    /// A week has an even number of days either side only if you count one of
    /// them twice, so the split is three days back and four forward: land on
    /// the first four days of a week and it snaps to that week, land on the
    /// last three and it snaps to the next.
    /// </para>
    /// </summary>
    public static DateOnly SnapToWeek(DateOnly date, DayOfWeek weekStart)
    {
        var into = ((int)date.DayOfWeek - (int)weekStart + 7) % 7;

        return into <= 3 ? date.AddDays(-into) : date.AddDays(7 - into);
    }

    private static IReadOnlyList<RoadmapQuarter> QuartersBetween(DateOnly start, DateOnly end)
    {
        var quarters = new List<RoadmapQuarter>();

        for (var cursor = StartOfQuarter(start); cursor <= end; cursor = cursor.AddMonths(3))
        {
            var close = EndOfQuarter(cursor);

            // Clipped to the window rather than drawn whole. A caller that set
            // the window by hand may well have cut a quarter in half, and a
            // column running past the last day would rule time that is not here.
            quarters.Add(new RoadmapQuarter(
                cursor.Year,
                QuarterOf(cursor),
                cursor < start ? start : cursor,
                close > end ? end : close));
        }

        return quarters;
    }
}

/// <summary>One column of the axis.</summary>
/// <param name="Year">The calendar year it belongs to.</param>
/// <param name="Number">1 through 4.</param>
/// <param name="Start">First day drawn, after clipping to the window.</param>
/// <param name="End">Last day drawn, after clipping to the window.</param>
public sealed record RoadmapQuarter(int Year, int Number, DateOnly Start, DateOnly End)
{
    /// <summary>What the column head reads. The year is drawn beside it as its
    /// own smaller line rather than folded in here, so four quarters of one year
    /// do not repeat it four times.</summary>
    public string Label => $"Q{Number}";

    /// <summary>The unambiguous form, for anything that will be read out of
    /// context — a screen reader, a tooltip, a test.</summary>
    public string LongLabel => $"Q{Number} {Year}";

    public int TotalDays => End.DayNumber - Start.DayNumber + 1;
}
