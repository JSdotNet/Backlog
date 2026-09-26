using System.Globalization;

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
        Columns = [.. Quarters.Select(quarter => new RoadmapColumn(
            RoadmapColumnScale.Quarter, quarter.Start, quarter.End, quarter.Label, quarter.Year.ToString(CultureInfo.InvariantCulture), quarter.LongLabel))];
    }

    private RoadmapWindow(IReadOnlyList<RoadmapColumn> columns)
    {
        Start = columns[0].Start;
        End = columns[^1].End;
        Quarters = QuartersBetween(Start, End);
        Columns = columns;
        IsGraduated = true;
    }

    /// <summary>First day of the window, inclusive.</summary>
    public DateOnly Start { get; }

    /// <summary>Last day of the window, inclusive.</summary>
    public DateOnly End { get; }

    /// <summary>The quarter columns the axis is ruled with, in order.</summary>
    public IReadOnlyList<RoadmapQuarter> Quarters { get; }

    /// <summary>The columns the axis is actually ruled with, in order. For a plain
    /// window these are <see cref="Quarters"/>; for a <see cref="Graduated"/> one
    /// they run from weeks to months to quarters as they get further from today.</summary>
    public IReadOnlyList<RoadmapColumn> Columns { get; }

    /// <summary>Whether the columns are of mixed length, so a day is not the same
    /// width everywhere along the track.</summary>
    public bool IsGraduated { get; }

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

    /// <summary>How many weeks a graduated window rules finer than a month on either
    /// side of today, counting this week — which is itself ruled in days.</summary>
    public const int GraduatedWeeks = 4;

    /// <summary>
    /// A window ruled coarser the further it reaches from today: a column per day
    /// for this week, a column per week for the <see cref="GraduatedWeeks"/> minus
    /// one after it, a column per month for roughly three months after that, and a
    /// column per quarter beyond.
    /// <para>
    /// The near term is what gets planned in detail and rescheduled by the week, so
    /// that is where the ruler is fine enough to read a week off — and this week
    /// finer still, because it is where the work in flight sits, and a single week
    /// column stacked every bar that started this week on the same few pixels.
    /// Next quarter is a month-level commitment, and anything further out is an
    /// intention, which a weekly ruler would only make look more certain than it is.
    /// </para>
    /// <para>
    /// The weeks are whole, so they rarely end on a month start: the month they end
    /// in is drawn as a column covering only its remaining days, and every month
    /// after it is whole. The months run up to a quarter start, so every quarter is
    /// whole too.
    /// </para>
    /// <para>
    /// Before this week is history, reached by scrolling back — finished plans are
    /// drawn where their work actually happened. It is ruled in weeks for the
    /// <see cref="GraduatedWeeks"/> before this one, where recently finished work
    /// sits, and in months before that, the last clipped to meet the first week;
    /// and only as far back as the earliest date given.
    /// </para>
    /// <para>
    /// It always reaches at least to the end of the monthly tier, so the horizon's
    /// shape is visible even for a plan that stops next month, and further when
    /// the plan does.
    /// </para>
    /// </summary>
    public static RoadmapWindow Graduated(IEnumerable<DateOnly> dates, DateOnly today, DayOfWeek weekStart)
    {
        var days = dates as ICollection<DateOnly> ?? [.. dates];

        var thisWeek = StartOfWeek(today, weekStart);
        var weeksFrom = thisWeek.AddDays(7);
        var monthsFrom = thisWeek.AddDays(7 * GraduatedWeeks);
        var quartersFrom = FirstOfQuarterOnOrAfter(monthsFrom.AddMonths(3));
        var pastWeeksFrom = thisWeek.AddDays(-7 * GraduatedWeeks);

        var first = days.Count == 0 ? thisWeek : days.Min();
        var last = days.Count == 0 ? quartersFrom.AddDays(-1) : days.Max();
        var end = last < quartersFrom ? quartersFrom.AddDays(-1) : EndOfQuarter(last);

        var columns = new List<RoadmapColumn>();
        var previousYear = 0;
        var previousMonth = 0;

        // Long before this week: months, the last one clipped to meet the first week.
        for (var cursor = first < pastWeeksFrom ? new DateOnly(first.Year, first.Month, 1) : pastWeeksFrom; cursor < pastWeeksFrom; cursor = cursor.AddMonths(1))
        {
            columns.Add(Month(cursor, Min(cursor.AddMonths(1).AddDays(-1), pastWeeksFrom.AddDays(-1)), previousYear != cursor.Year));
            previousYear = cursor.Year;
            previousMonth = cursor.Month;
        }

        // Just before it: weeks, from the one the earliest date falls in.
        for (var cursor = first < pastWeeksFrom ? pastWeeksFrom : StartOfWeek(Min(first, thisWeek), weekStart); cursor < thisWeek; cursor = cursor.AddDays(7))
        {
            columns.Add(Week(cursor, cursor.Month != previousMonth));
            previousMonth = cursor.Month;
            previousYear = cursor.Year;
        }

        // This week, a column a day. The first carries the week's number, so the
        // reader still knows which week this is without a column of its own.
        for (var cursor = thisWeek; cursor < weeksFrom; cursor = cursor.AddDays(1))
        {
            columns.Add(new RoadmapColumn(
                RoadmapColumnScale.Day,
                cursor,
                cursor,
                cursor.ToString("ddd d", CultureInfo.CurrentCulture),
                cursor == thisWeek ? $"W{WeekNumber(thisWeek, weekStart)}" : null,
                cursor.ToString("dddd d MMMM yyyy", CultureInfo.CurrentCulture)));
        }

        previousMonth = thisWeek.Month;
        previousYear = thisWeek.Year;

        for (var cursor = weeksFrom; cursor < monthsFrom && cursor <= end; cursor = cursor.AddDays(7))
        {
            columns.Add(Week(cursor, cursor.Month != previousMonth));
            previousMonth = cursor.Month;
            previousYear = cursor.Year;
        }

        for (var cursor = monthsFrom; cursor < quartersFrom && cursor <= end; cursor = new DateOnly(cursor.Year, cursor.Month, 1).AddMonths(1))
        {
            columns.Add(Month(cursor, new DateOnly(cursor.Year, cursor.Month, 1).AddMonths(1).AddDays(-1), previousYear != cursor.Year));
            previousYear = cursor.Year;
        }

        for (var cursor = quartersFrom; cursor <= end; cursor = cursor.AddMonths(3))
        {
            columns.Add(new RoadmapColumn(
                RoadmapColumnScale.Quarter,
                cursor,
                EndOfQuarter(cursor),
                $"Q{QuarterOf(cursor)}",
                cursor.Year.ToString(CultureInfo.InvariantCulture),
                $"Q{QuarterOf(cursor)} {cursor.Year}"));
        }

        return new RoadmapWindow(columns);

        RoadmapColumn Week(DateOnly start, bool showMonth) => new(
            RoadmapColumnScale.Week,
            start,
            start.AddDays(6),
            $"W{WeekNumber(start, weekStart)}",
            showMonth ? start.ToString("MMM", CultureInfo.CurrentCulture) : null,
            $"Week {WeekNumber(start, weekStart)}, from {start.ToString("d MMM yyyy", CultureInfo.CurrentCulture)}");

        static RoadmapColumn Month(DateOnly start, DateOnly end, bool showYear) => new(
            RoadmapColumnScale.Month,
            start,
            end,
            start.ToString("MMM", CultureInfo.CurrentCulture),
            showYear ? start.Year.ToString(CultureInfo.InvariantCulture) : null,
            start.ToString("MMMM yyyy", CultureInfo.CurrentCulture));
    }

    /// <summary>
    /// The week number a week is known by: ISO 8601 when weeks start on Monday,
    /// which is the numbering a planner in most of Europe already uses, and the
    /// calendar's first-four-day rule from the given start day otherwise.
    /// </summary>
    public static int WeekNumber(DateOnly weekStartDay, DayOfWeek weekStart) =>
        weekStart == DayOfWeek.Monday
            ? ISOWeek.GetWeekOfYear(weekStartDay.ToDateTime(TimeOnly.MinValue))
            : CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(
                weekStartDay.AddDays(3).ToDateTime(TimeOnly.MinValue), CalendarWeekRule.FirstFourDayWeek, weekStart);

    /// <summary>The first day of the week a date falls in.</summary>
    public static DateOnly StartOfWeek(DateOnly date, DayOfWeek weekStart) =>
        date.AddDays(-(((int)date.DayOfWeek - (int)weekStart + 7) % 7));

    private static DateOnly FirstOfQuarterOnOrAfter(DateOnly date) =>
        StartOfQuarter(date) == date ? date : StartOfQuarter(date).AddMonths(3);

    private static DateOnly Min(DateOnly left, DateOnly right) => left < right ? left : right;

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

/// <summary>How much time one column of the axis stands for.</summary>
public enum RoadmapColumnScale
{
    Day,
    Week,
    Month,
    Quarter
}

/// <summary>One column of the axis, whatever length of time it rules.</summary>
/// <param name="Scale">Week, month or quarter.</param>
/// <param name="Start">First day drawn.</param>
/// <param name="End">Last day drawn.</param>
/// <param name="Label">The column head.</param>
/// <param name="Caption">The smaller line under it — the year, or the month a run
/// of weeks enters — or null where it would only repeat the column before.</param>
/// <param name="LongLabel">The unambiguous form, for a tooltip or a test.</param>
public sealed record RoadmapColumn(
    RoadmapColumnScale Scale,
    DateOnly Start,
    DateOnly End,
    string Label,
    string? Caption,
    string LongLabel)
{
    public int TotalDays => End.DayNumber - Start.DayNumber + 1;

    /// <summary>How many days the whole week, month or quarter this column belongs
    /// to has, clipped or not — what a column's width is shared over.</summary>
    public int NominalDays => Scale switch
    {
        RoadmapColumnScale.Day => 1,
        RoadmapColumnScale.Week => 7,
        RoadmapColumnScale.Month => DateTime.DaysInMonth(Start.Year, Start.Month),
        _ => RoadmapWindow.EndOfQuarter(Start).DayNumber - RoadmapWindow.StartOfQuarter(Start).DayNumber + 1
    };
}
