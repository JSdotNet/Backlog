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

    /// <summary>How many weeks either side of today a graduated window rules in days:
    /// this week, and this many before and after it.</summary>
    public const int GraduatedDayWeeks = 1;

    /// <summary>How many weeks a graduated window rules a column a week on either
    /// side, beyond the weeks it rules in days.</summary>
    public const int GraduatedWeeks = 3;

    /// <summary>
    /// A window ruled coarser the further it reaches from today, the same way in
    /// both directions: a column per day for this week and the
    /// <see cref="GraduatedDayWeeks"/> either side of it, a column per week for the
    /// <see cref="GraduatedWeeks"/> beyond those, a column per month for roughly a
    /// quarter beyond that, and a column per quarter beyond.
    /// <para>
    /// The near term is what gets planned in detail and rescheduled by the week, so
    /// that is where the ruler is fine enough to read a week off — and the days
    /// around today finer still, because that is where the work in flight sits, and
    /// a single week column stacked every bar that started in it on the same few
    /// pixels. A day ruler from the start of last week to the end of next means
    /// today always has at least a full week of days on both sides. Next quarter is
    /// a month-level commitment, and anything further out is an intention, which a
    /// weekly ruler would only make look more certain than it is.
    /// </para>
    /// <para>
    /// The weeks are whole, so they rarely meet a month boundary: the month nearest
    /// them on either side is a column clipped to the days it has left, and every
    /// month beyond is whole. The months run to a quarter boundary, so every
    /// quarter is whole too.
    /// </para>
    /// <para>
    /// Before this week is history, reached by scrolling back — finished plans are
    /// drawn where their work actually happened. Its days and weeks are always
    /// there, so a reader can look back at the last month whatever is drawn; its
    /// months and quarters reach only as far back as the earliest date given.
    /// </para>
    /// <para>
    /// After it, the window always reaches at least to the end of the monthly tier,
    /// so the horizon's shape is visible even for a plan that stops next month, and
    /// further when the plan does.
    /// </para>
    /// </summary>
    public static RoadmapWindow Graduated(IEnumerable<DateOnly> dates, DateOnly today, DayOfWeek weekStart)
    {
        var days = dates as ICollection<DateOnly> ?? [.. dates];

        var thisWeek = StartOfWeek(today, weekStart);

        // Forward: days to weeksFrom, weeks to monthsFrom, months to quartersFrom.
        var weeksFrom = thisWeek.AddDays(7 * (GraduatedDayWeeks + 1));
        var monthsFrom = weeksFrom.AddDays(7 * GraduatedWeeks);
        var quartersFrom = FirstOfQuarterOnOrAfter(monthsFrom.AddMonths(3));

        // Backward, the mirror: days from pastDaysFrom, weeks from pastWeeksFrom,
        // months from pastMonthsFrom, quarters before that.
        var pastDaysFrom = thisWeek.AddDays(-7 * GraduatedDayWeeks);
        var pastWeeksFrom = pastDaysFrom.AddDays(-7 * GraduatedWeeks);
        var pastMonthsFrom = StartOfQuarter(pastWeeksFrom.AddMonths(-3));

        var first = days.Count == 0 ? thisWeek : days.Min();
        var last = days.Count == 0 ? quartersFrom.AddDays(-1) : days.Max();
        var end = last < quartersFrom ? quartersFrom.AddDays(-1) : EndOfQuarter(last);

        var columns = new List<RoadmapColumn>();
        var previousYear = 0;
        var previousMonth = 0;

        // Long before this week: quarters, as far back as the earliest date.
        for (var cursor = StartOfQuarter(first); cursor < pastMonthsFrom; cursor = cursor.AddMonths(3))
        {
            columns.Add(Quarter(cursor));
            previousYear = cursor.Year;
        }

        // Then months, the last one clipped to meet the first week.
        var monthsStart = first < pastMonthsFrom ? pastMonthsFrom
            : first < pastWeeksFrom ? new DateOnly(first.Year, first.Month, 1)
            : pastWeeksFrom;
        for (var cursor = monthsStart; cursor < pastWeeksFrom; cursor = cursor.AddMonths(1))
        {
            columns.Add(Month(cursor, Min(cursor.AddMonths(1).AddDays(-1), pastWeeksFrom.AddDays(-1)), previousYear != cursor.Year));
            previousYear = cursor.Year;
            previousMonth = cursor.Month;
        }

        // Just before the days: weeks, always all of them, so there is recent
        // history to scroll back into even when nothing drawn began before this week.
        for (var cursor = pastWeeksFrom; cursor < pastDaysFrom; cursor = cursor.AddDays(7))
        {
            columns.Add(Week(cursor, cursor.Month != previousMonth));
            previousMonth = cursor.Month;
            previousYear = cursor.Year;
        }

        // Last week, this week and next, a column a day. The first day of each week
        // carries its number, so the reader still knows which week it is without a
        // column of its own.
        for (var cursor = pastDaysFrom; cursor < weeksFrom; cursor = cursor.AddDays(1))
        {
            var week = StartOfWeek(cursor, weekStart);

            columns.Add(new RoadmapColumn(
                RoadmapColumnScale.Day,
                cursor,
                cursor,
                cursor.ToString("ddd d", CultureInfo.CurrentCulture),
                cursor == week ? $"W{WeekNumber(week, weekStart)}" : null,
                cursor.ToString("dddd d MMMM yyyy", CultureInfo.CurrentCulture)));
        }

        // The days name no month, so the first week after them names its own unless
        // the last day-week already began in it.
        previousMonth = weeksFrom.AddDays(-7).Month;
        previousYear = weeksFrom.AddDays(-7).Year;

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
            columns.Add(Quarter(cursor));
        }

        return new RoadmapWindow(columns);

        static RoadmapColumn Quarter(DateOnly start) => new(
            RoadmapColumnScale.Quarter,
            start,
            EndOfQuarter(start),
            $"Q{QuarterOf(start)}",
            start.Year.ToString(CultureInfo.InvariantCulture),
            $"Q{QuarterOf(start)} {start.Year}");

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
