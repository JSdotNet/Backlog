using Backlog.Modules.Tasks.Abstractions;

namespace Backlog.Mobile.UI.Tasks;

/// <summary>
/// Today's My Day as the phone's Today screen draws it: the card at the top, then
/// Agenda, Anytime today and Done today
/// (<c>.devbook/domain/tasks/features.md#my-day</c>).
/// <para>
/// Pure: handed the day's rows in My Day order and the phone's local time of day,
/// it decides nothing else. A task is done when it is ticked — it carries a
/// <c>completed_on</c> — because the tick is what the phone writes and unwrites;
/// its status plays no part.
/// </para>
/// </summary>
/// <param name="Card">The timed task the day is on now, or the next one to start;
/// null when no open timed task is current or still to come.</param>
/// <param name="Agenda">The open timed tasks other than the card's, by start time.</param>
/// <param name="Anytime">The open tasks with no agenda time, in My Day order.</param>
/// <param name="Done">The ticked tasks, in My Day order.</param>
public sealed record TodayPlan(
    TodayCard? Card,
    IReadOnlyList<TaskViewRow> Agenda,
    IReadOnlyList<TaskViewRow> Anytime,
    IReadOnlyList<TaskViewRow> Done)
{
    /// <summary>Every task picked for today, the card's included.</summary>
    public int Total => (Card is null ? 0 : 1) + Agenda.Count + Anytime.Count + Done.Count;

    public static TodayPlan Empty { get; } = new(null, [], [], []);

    /// <summary>
    /// Groups <paramref name="day"/> — the rows picked for today, in My Day order —
    /// at <paramref name="now"/>, the phone's local time of day.
    /// <para>
    /// The card is the open timed task whose block contains <paramref name="now"/>
    /// (the earliest-starting one when blocks overlap), or else the open timed
    /// task that starts soonest after it. A block that ended is no longer
    /// current and never next: it stays in Agenda until it is ticked.
    /// </para>
    /// </summary>
    public static TodayPlan For(IEnumerable<TaskViewRow> day, TimeOnly now)
    {
        ArgumentNullException.ThrowIfNull(day);

        var rows = day.ToList();
        var done = rows.Where(IsTicked).ToList();
        var open = rows.Where(row => !IsTicked(row)).ToList();

        var timed = open
            .Where(row => row.AgendaTime is not null)
            .Select((row, order) => (Row: row, Time: row.AgendaTime!, Order: order))
            .OrderBy(entry => entry.Time.Start)
            .ThenBy(entry => entry.Order)
            .ToList();

        TodayCard? card = null;

        if (timed.FirstOrDefault(entry => Contains(entry.Time, now)) is { Row: not null } current)
        {
            card = new TodayCard(current.Row, current.Time, IsNow: true);
        }
        else if (timed.FirstOrDefault(entry => entry.Time.Start > now) is { Row: not null } next)
        {
            card = new TodayCard(next.Row, next.Time, IsNow: false);
        }

        return new TodayPlan(
            card,
            [.. timed.Where(entry => entry.Row.Id != card?.Task.Id).Select(entry => entry.Row)],
            [.. open.Where(row => row.AgendaTime is null)],
            done);
    }

    /// <summary>Ticked off: the phone's done, and the desktop checkbox's.</summary>
    public static bool IsTicked(TaskViewRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return row.Task.CompletedOn is not null;
    }

    /// <summary>Whether the block runs at <paramref name="now"/>: from its start,
    /// up to but not including its end. A block that runs past midnight is
    /// current from its start to the end of the day — My Day reads no further.</summary>
    public static bool Contains(AgendaTime time, TimeOnly now)
    {
        ArgumentNullException.ThrowIfNull(time);

        return time.End > time.Start
            ? now >= time.Start && now < time.End
            : now >= time.Start;
    }
}

/// <summary>The task the Today screen's card shows, and whether it is on now or
/// next.</summary>
public sealed record TodayCard(TaskViewRow Task, AgendaTime Time, bool IsNow);
