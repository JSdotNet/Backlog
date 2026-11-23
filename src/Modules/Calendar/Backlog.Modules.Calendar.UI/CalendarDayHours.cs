using global::Backlog.UI.Components.Tasks;

namespace Backlog.Modules.Calendar.UI;

/// <summary>
/// Hours set aside for a task on one day, as the month calendar draws them: a chip on
/// the day — "Refactor sync · 3h" — apart from the task's due-date chip, and a part of
/// the day's total.
/// <para>
/// A date and a number of hours, never a start or an end: the calendar has no time
/// grid. The person's own plan; the calendar adds it up against the day's capacity and
/// hands it nowhere else.
/// </para>
/// </summary>
/// <param name="TaskId">The task's id, as on its <see cref="CalendarTask"/>.</param>
/// <param name="Title">What the chip says before the hours.</param>
/// <param name="On">The day.</param>
/// <param name="Hours">How many hours.</param>
/// <param name="CssClass">The repository identity classes, or null — passed only
/// while the window's repository colours are showing, as on a <see cref="CalendarTask"/>.</param>
public sealed record CalendarHoursBlock(
    string TaskId,
    string Title,
    DateOnly On,
    decimal Hours,
    string? CssClass = null);

/// <summary>Hours set, changed or taken away for a task on a day: <see cref="Hours"/>
/// is the new figure, or null to remove the day's block.</summary>
public sealed record CalendarHoursChange(string TaskId, DateOnly On, decimal? Hours);

/// <summary>A day's planned hours against its capacity, as the day head says it.</summary>
/// <param name="Planned">Every block on the day, added up.</param>
/// <param name="Capacity">The hours worked that day, or null when the Calendar reads no
/// working week to set the day against.</param>
public sealed record CalendarDayHours(decimal Planned, decimal? Capacity)
{
    /// <summary>More hours planned than the day has: drawn in the error colour.</summary>
    public bool Over => Capacity is { } capacity && Planned > capacity;

    /// <summary>What the day head shows: "3h / 8h", or "3h" with no capacity.</summary>
    public string Label => Capacity is { } capacity
        ? $"{CalendarHours.Format(Planned)} / {CalendarHours.Format(capacity)}"
        : CalendarHours.Format(Planned);

    /// <summary>The tooltip, naming the numbers: what is planned, what the day holds,
    /// and by how much it is over.</summary>
    public string Summary => Capacity switch
    {
        null => $"{CalendarHours.Format(Planned)} planned",
        0m => $"{CalendarHours.Format(Planned)} planned on a day with no working hours — {CalendarHours.Format(Planned)} over",
        { } capacity when Planned > capacity =>
            $"{CalendarHours.Format(Planned)} planned, {CalendarHours.Format(capacity)} of working hours — {CalendarHours.Format(Planned - capacity)} over",
        { } capacity => $"{CalendarHours.Format(Planned)} planned of {CalendarHours.Format(capacity)} working hours"
    };

    /// <summary>A day's blocks added up against <paramref name="capacity"/>, or null for a
    /// day with none.</summary>
    public static CalendarDayHours? Of(IEnumerable<CalendarHoursBlock> blocks, decimal? capacity)
    {
        ArgumentNullException.ThrowIfNull(blocks);

        var any = false;
        var planned = 0m;
        foreach (var block in blocks)
        {
            any = true;
            planned += block.Hours;
        }

        return any ? new CalendarDayHours(planned, capacity) : null;
    }
}
