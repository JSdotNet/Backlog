namespace Backlog.UI.Components.Tasks;

/// <summary>
/// One task as the month calendar places it: by its due date, or in the tray of
/// tasks without one.
/// <para>
/// A record of its own rather than a <see cref="TaskRow"/>, because the calendar
/// needs three facts a row is not given — the due date as a date rather than as
/// words, the lifecycle state behind the status dot, and the host's repository
/// mark — and none of the row's metadata line. The host maps its entries onto
/// this once, the way it maps them onto rows for the list.
/// </para>
/// </summary>
/// <param name="Id">The host's id for the task; what a click and a drop report back.</param>
/// <param name="Title">What the chip says.</param>
/// <param name="DueOn">The day it is due, or null — then it is in the tray while it is
/// still open, and nowhere while it is finished.</param>
/// <param name="State">Where it is in its lifecycle; the status dot says it.</param>
/// <param name="Repeats">Whether it recurs. A recurring task is placed on its due
/// date only — the next occurrence is written when this one is ticked off, so the
/// calendar draws no series.</param>
/// <param name="CssClass">The host's repository identity classes, or null. The host
/// passes them only while its repository colours are showing, so the calendar never
/// decides whether the colours are on.</param>
/// <param name="Effort">The estimate as the tray says it — "3 pts" — or null.</param>
public sealed record CalendarTask(
    string Id,
    string Title,
    DateOnly? DueOn,
    CalendarTaskState State = CalendarTaskState.Draft,
    bool Repeats = false,
    string? CssClass = null,
    string? Effort = null)
{
    /// <summary>Finished: drawn struck through, and never overdue.</summary>
    public bool Done => State is CalendarTaskState.Done;

    /// <summary>Still to do — neither finished nor archived. Only an open task is
    /// overdue, and only an open one waits in the tray.</summary>
    public bool Open => State is not (CalendarTaskState.Done or CalendarTaskState.Archived);

    /// <summary>Open and due before <paramref name="today"/>.</summary>
    public bool IsOverdue(DateOnly today) => Open && DueOn is { } due && due < today;
}

/// <summary>The lifecycle states the status dot distinguishes.</summary>
public enum CalendarTaskState
{
    Draft,
    Ready,
    InProgress,
    Done,
    Archived
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
        var start = MondayOnOrBefore(first);

        var weeks = new List<DateOnly>(6);
        for (var week = start; week <= last; week = week.AddDays(7)) weeks.Add(week);
        return weeks;
    }

    /// <summary>The Monday on or before <paramref name="date"/>.</summary>
    public static DateOnly MondayOnOrBefore(DateOnly date) =>
        date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
}
