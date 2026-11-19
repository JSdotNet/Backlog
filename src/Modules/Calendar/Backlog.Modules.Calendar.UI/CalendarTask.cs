using global::Backlog.Modules.Calendar.Abstractions.DataTransferObjects;
using global::Backlog.SharedKernel;

namespace Backlog.Modules.Calendar.UI;

/// <summary>
/// One task as the Calendar's grid places it: on its due date.
/// <para>
/// The grid's own shape, mapped by the Calendar surface from what its Tasks layer read
/// (<c>CalendarTaskDto</c>): the due date, the lifecycle state behind the status dot, and
/// the repository mark the window's scope gives it. A task with no due date is not on the
/// Calendar — a due date is set in Tasks (local ADR 0056 §2).
/// </para>
/// </summary>
/// <param name="Id">The task's id; what a click and a drop report back.</param>
/// <param name="Title">What the chip says.</param>
/// <param name="DueOn">The day it is due.</param>
/// <param name="State">Where it is in its lifecycle; the status dot says it.</param>
/// <param name="Repeats">Whether it recurs. A recurring task is placed on its due
/// date only — the next occurrence is written when this one is ticked off, so the
/// calendar draws no series.</param>
/// <param name="CssClass">The repository identity classes, or null. The surface passes
/// them only while the window's repository colours are showing, so the grid never
/// decides whether the colours are on.</param>
public sealed record CalendarTask(
    string Id,
    string Title,
    DateOnly DueOn,
    CalendarTaskState State = CalendarTaskState.Draft,
    bool Repeats = false,
    string? CssClass = null)
{
    /// <summary>Finished: drawn struck through, and never overdue.</summary>
    public bool Done => State is CalendarTaskState.Done;

    /// <summary>Still to do. Only an open task is overdue, and only an open one is
    /// offered to a new block of hours.</summary>
    public bool Open => !Done;

    /// <summary>Open and due before <paramref name="today"/>.</summary>
    public bool IsOverdue(DateOnly today) => Open && DueOn < today;
}

/// <summary>The lifecycle states the status dot distinguishes.</summary>
public enum CalendarTaskState
{
    Draft,
    Ready,
    InProgress,
    Done
}

/// <summary>A task dropped on a day: give it that due date.</summary>
public sealed record CalendarDueChange(string TaskId, DateOnly DueOn);

/// <summary>
/// The month grid's arithmetic, apart from the markup so it can be pinned down
/// on its own.
/// </summary>
public static class CalendarMonth
{
    /// <summary>The first of <paramref name="date"/>'s month.</summary>
    public static DateOnly FirstOf(DateOnly date) => new(date.Year, date.Month, 1);

    /// <summary>
    /// The Monday of every week the month touches, in order: from the Monday on or
    /// before the 1st to the week holding the last day. Four to six weeks, and the
    /// first and last usually reach into the neighbouring months.
    /// <para>
    /// Monday-first whatever the culture says, because the design draws it so and
    /// a week that starts on a different day for different readers of the same
    /// shared backlog would place the same task in different rows.
    /// </para>
    /// </summary>
    public static IReadOnlyList<DateOnly> WeekStarts(DateOnly month)
    {
        var first = FirstOf(month);
        var last = first.AddMonths(1).AddDays(-1);
        var start = CalendarWeek.MondayOf(first);

        var weeks = new List<DateOnly>(6);
        for (var week = start; week <= last; week = week.AddDays(7)) weeks.Add(week);
        return weeks;
    }

    /// <summary>The days the Month range shows for <paramref name="month"/>: from the
    /// Monday of its first week to the Sunday of its last.</summary>
    public static CalendarWindow Window(DateOnly month)
    {
        var weeks = WeekStarts(month);
        return new CalendarWindow(weeks[0], weeks[^1].AddDays(6));
    }

    /// <summary>
    /// The Monday of the week the Week range opens on from <paramref name="month"/>:
    /// the week holding <paramref name="today"/> when today is in that month, else the
    /// month's first week — the one holding the 1st.
    /// </summary>
    public static DateOnly OpeningWeek(DateOnly month, DateOnly today)
    {
        var first = FirstOf(month);
        return FirstOf(today) == first ? CalendarWeek.MondayOf(today) : CalendarWeek.MondayOf(first);
    }

    /// <summary>
    /// The first of the month the Month range shows after the week starting on
    /// <paramref name="weekStart"/>: the month holding its Thursday, the ISO rule, so a
    /// week that spans two months lands in the one holding most of its days.
    /// </summary>
    public static DateOnly MonthOfWeek(DateOnly weekStart) =>
        FirstOf(CalendarWeek.MondayOf(weekStart).AddDays(3));
}
