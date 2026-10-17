using Backlog.SharedKernel;

namespace Backlog.Modules.Progress.Abstractions;

/// <summary>
/// One window over the calendar (<c>.devbook/domain/progress/domain.md#iteration</c>): its
/// length and its first and last day, both included. Nobody creates or closes one; it is
/// read from today on every reading. Equality is by length and first day, the last day
/// following from both.
/// </summary>
public readonly record struct IterationWindow(IterationLength Length, DateOnly First, DateOnly Last)
{
    public bool Contains(DateOnly day) => day >= First && day <= Last;
}

/// <summary>
/// Builds the iteration windows: <see cref="Current"/> is the window the reading calls
/// <b>next</b> — the one holding today, or the first after today when today falls in none —
/// and <see cref="Previous"/> of it is <b>last</b>. Pure, and in the module's published
/// surface so every head reads the same windows.
/// <list type="bullet">
/// <item>Day: one worked day of the person's working week; stepping skips the days not worked.
/// A week with no worked day at all reads as Monday to Friday, so a step always ends.</item>
/// <item>Week: Monday to Friday.</item>
/// <item>Two weeks: fourteen days from the two-week start, or a whole number of fourteen days
/// before or after it; without a start, from the Monday of today's week.</item>
/// <item>Month: one calendar month.</item>
/// </list>
/// </summary>
public static class Iterations
{
    /// <summary>How far a Day step looks for a worked date: further than the longest
    /// run of days off a person can block, with a week either side.</summary>
    private const int DaySearchLimit = WorkingHours.MaxDaysOffRange + 14;

    /// <summary>The window holding <paramref name="today"/>, or the first after it.</summary>
    public static IterationWindow Current(DateOnly today, IterationLength length, DateOnly? twoWeekStart, WorkingHours week)
    {
        ArgumentNullException.ThrowIfNull(week);

        switch (length)
        {
            case IterationLength.Day:
                var day = Worked(today, Usable(week), forward: true);
                return new IterationWindow(length, day, day);

            case IterationLength.Week:
                var monday = MondayOf(today);
                if (today > monday.AddDays(4)) monday = monday.AddDays(7);
                return new IterationWindow(length, monday, monday.AddDays(4));

            case IterationLength.TwoWeeks:
                var anchor = twoWeekStart ?? MondayOf(today);
                var offset = today.DayNumber - anchor.DayNumber;
                var first = anchor.AddDays((int)Math.Floor(offset / 14d) * 14);
                return new IterationWindow(length, first, first.AddDays(13));

            case IterationLength.Month:
                var start = new DateOnly(today.Year, today.Month, 1);
                return new IterationWindow(length, start, start.AddMonths(1).AddDays(-1));

            default:
                throw new ArgumentOutOfRangeException(nameof(length), length, "An iteration is a day, a week, two weeks or a month.");
        }
    }

    /// <summary>The window just before <paramref name="window"/>.</summary>
    public static IterationWindow Previous(IterationWindow window, WorkingHours week) => Step(window, week, forward: false);

    /// <summary>The window just after <paramref name="window"/>.</summary>
    public static IterationWindow Following(IterationWindow window, WorkingHours week) => Step(window, week, forward: true);

    /// <summary>The <paramref name="count"/> windows ending with <paramref name="last"/>,
    /// oldest first — the history a forecast reads its done rate from.</summary>
    public static IReadOnlyList<IterationWindow> History(IterationWindow last, int count, WorkingHours week)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);

        var windows = new List<IterationWindow> { last };
        while (windows.Count < count) windows.Insert(0, Previous(windows[0], week));
        return windows;
    }

    /// <summary>How many dates of <paramref name="window"/> the working week works.</summary>
    public static int WorkedDays(IterationWindow window, WorkingHours week) => WorkedDays(window.First, window.Last, week);

    /// <summary>How many dates from <paramref name="from"/> through <paramref name="through"/>,
    /// both included, the working week works; 0 when <paramref name="through"/> is before
    /// <paramref name="from"/>.</summary>
    public static int WorkedDays(DateOnly from, DateOnly through, WorkingHours week)
    {
        ArgumentNullException.ThrowIfNull(week);

        var usable = Usable(week);
        var count = 0;
        for (var day = from; day <= through; day = day.AddDays(1))
        {
            if (usable.IsWorked(day)) count++;
        }

        return count;
    }

    /// <summary>
    /// A pace in points a week scaled to <paramref name="window"/>
    /// (<c>.devbook/domain/progress/domain.md#pace</c>): the pace times the window's worked
    /// days, divided by the worked weekdays of the week's pattern, rounded to whole points
    /// with halves away from zero. 18 a week reads 18 for a Week and 4 for a Day.
    /// </summary>
    public static int Capacity(decimal pointsPerWeek, IterationWindow window, WorkingHours week)
    {
        ArgumentNullException.ThrowIfNull(week);

        var usable = Usable(week);
        var perWeek = WorkingHours.Week.Count(usable.IsWorked);
        return (int)Math.Round(pointsPerWeek * WorkedDays(window, usable) / perWeek, MidpointRounding.AwayFromZero);
    }

    /// <summary>The Monday of <paramref name="day"/>'s week, Monday first.</summary>
    public static DateOnly MondayOf(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    private static IterationWindow Step(IterationWindow window, WorkingHours week, bool forward)
    {
        ArgumentNullException.ThrowIfNull(week);

        var sign = forward ? 1 : -1;
        switch (window.Length)
        {
            case IterationLength.Day:
                var day = Worked(forward ? window.Last.AddDays(1) : window.First.AddDays(-1), Usable(week), forward);
                return window with { First = day, Last = day };

            case IterationLength.Week:
                return window with { First = window.First.AddDays(7 * sign), Last = window.Last.AddDays(7 * sign) };

            case IterationLength.TwoWeeks:
                return window with { First = window.First.AddDays(14 * sign), Last = window.Last.AddDays(14 * sign) };

            case IterationLength.Month:
                var first = window.First.AddMonths(sign);
                return window with { First = first, Last = first.AddMonths(1).AddDays(-1) };

            default:
                throw new ArgumentOutOfRangeException(nameof(window), window.Length, "An iteration is a day, a week, two weeks or a month.");
        }
    }

    /// <summary>The first worked date from <paramref name="from"/>, that date included, in
    /// the direction asked. A run of blocked dates longer than any a person can set falls
    /// back to Monday to Friday, so the walk always ends.</summary>
    private static DateOnly Worked(DateOnly from, WorkingHours week, bool forward)
    {
        var step = forward ? 1 : -1;
        var day = from;
        for (var i = 0; i < DaySearchLimit; i++, day = day.AddDays(step))
        {
            if (week.IsWorked(day)) return day;
        }

        return ReferenceEquals(week, WorkingHours.Default) ? from : Worked(from, WorkingHours.Default, forward);
    }

    /// <summary>The week as given, or Monday to Friday when it works no weekday at all.</summary>
    private static WorkingHours Usable(WorkingHours week) =>
        WorkingHours.Week.Any(week.IsWorked) ? week : WorkingHours.Default;
}
