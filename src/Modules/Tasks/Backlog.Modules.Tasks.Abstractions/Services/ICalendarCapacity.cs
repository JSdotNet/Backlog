namespace Backlog.Modules.Tasks.Abstractions.Services;

/// <summary>
/// How many hours the person works on a day, as the Tasks Calendar sets a day's planned
/// hours against it.
/// <para>
/// A port on Tasks' own surface, the shape <see cref="ICalendarPlans"/> takes: an
/// infrastructure adapter answers it from the Roadmap's Hours and Days off settings
/// through Roadmap's own abstractions, read-only. Nothing the Calendar plans is handed
/// back the other way — the planned hours never reach the roadmap's pace or its
/// windows.
/// </para>
/// </summary>
public interface ICalendarCapacity
{
    /// <summary>Raised after the working week or a day off changed. It may arrive on any
    /// thread.</summary>
    event Action? Changed;

    /// <summary>The hours worked on <paramref name="day"/>: its weekday's hours in the
    /// working week, or zero for a day not worked — a weekend the week leaves off, or a
    /// day off.</summary>
    decimal HoursOn(DateOnly day);
}
