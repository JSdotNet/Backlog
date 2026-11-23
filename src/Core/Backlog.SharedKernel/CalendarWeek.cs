namespace Backlog.SharedKernel;

/// <summary>
/// The calendar week, Monday first — the week <see cref="WorkingHours.Week"/> reads in,
/// and the one the dashboard, the roadmap, iterations and the phone's goal page each
/// bucket by.
/// <para>
/// In the kernel so the Monday is worked out once: each of those contexts used to carry
/// its own copy of the arithmetic. A week that starts on another day is a different
/// question with its own parameter, which <c>RoadmapWindow.StartOfWeek</c> and
/// <see cref="UsageWeekReset"/> answer.
/// </para>
/// </summary>
public static class CalendarWeek
{
    /// <summary>The Monday on or before <paramref name="day"/>: the first day of the week
    /// it falls in.</summary>
    public static DateOnly MondayOf(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
}
