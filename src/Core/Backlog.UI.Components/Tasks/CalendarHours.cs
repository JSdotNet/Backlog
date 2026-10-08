using System.Globalization;

namespace Backlog.UI.Components.Tasks;

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
/// <param name="TaskId">The host's id for the task, as on its <see cref="CalendarTask"/>.</param>
/// <param name="Title">What the chip says before the hours.</param>
/// <param name="On">The day.</param>
/// <param name="Hours">How many hours.</param>
/// <param name="CssClass">The host's repository identity classes, or null — passed only
/// while its repository colours are showing, as on a <see cref="CalendarTask"/>.</param>
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
/// <param name="Capacity">The hours worked that day, or null when the host keeps no
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
}

/// <summary>The hours arithmetic and wording, apart from the markup so it can be
/// pinned down on its own.</summary>
public static class CalendarHours
{
    /// <summary>The most one block can hold: the whole day.</summary>
    public const decimal MaxHours = 24m;

    /// <summary>"3h", "2.5h", "0.75h" — the shortest figure that is exact, in the
    /// reader's culture.</summary>
    public static string Format(decimal hours) =>
        string.Create(CultureInfo.CurrentCulture, $"{hours.ToString("0.##", CultureInfo.CurrentCulture)}h");

    /// <summary>The figure an hours field starts from: "3", "2.5". Invariant, because a
    /// number field's value is written with a point whatever the reader's culture, and
    /// one written with a comma reads as empty.</summary>
    public static string Edit(decimal hours) => hours.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>
    /// The hours a typed figure means, or null when it means none to keep: not a number,
    /// zero or less, or more than <see cref="MaxHours"/>. Read invariant first — a number
    /// field hands its value over with a point whatever the reader's culture — and then
    /// in the reader's culture, so "2.5" and, in a culture that writes it so, "2,5" are
    /// both two and a half. No group separators, so "1.5" is never fifteen anywhere. An
    /// "h" after the number is allowed. Rounded to quarters of an hour, as the task keeps
    /// it.
    /// </summary>
    public static decimal? Parse(string? typed)
    {
        if (string.IsNullOrWhiteSpace(typed)) return null;

        var text = typed.Trim();
        if (text.EndsWith('h') || text.EndsWith('H')) text = text[..^1].TrimEnd();

        const NumberStyles figure = NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign;
        if (!decimal.TryParse(text, figure, CultureInfo.InvariantCulture, out var hours)
            && !decimal.TryParse(text, figure, CultureInfo.CurrentCulture, out hours))
        {
            return null;
        }

        // Quarters of an hour, as the task keeps them, so a figure that would round to
        // nothing is refused here rather than by the store.
        var quarters = Math.Round(hours * 4m, MidpointRounding.AwayFromZero) / 4m;
        return quarters is > 0m and <= MaxHours ? quarters : null;
    }

    /// <summary>A day's blocks added up against <paramref name="capacity"/>, or null for a
    /// day with none.</summary>
    public static CalendarDayHours? Day(IEnumerable<CalendarHoursBlock> blocks, decimal? capacity)
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

/// <summary>One block of a task's planned hours as the detail panel lists it: the day
/// and how many hours.</summary>
public sealed record PlannedHoursDay(DateOnly On, decimal Hours);

/// <summary>Hours set or changed on a day, or — for a null <see cref="Hours"/> — the
/// day's block taken away.</summary>
public sealed record PlannedHoursDayChange(DateOnly On, decimal? Hours);
