using Backlog.Modules.Calendar.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Calendar.Abstractions.Services;

/// <summary>
/// The Tasks layer port the Calendar owns (local ADR 0056 §2): the tasks due in a window
/// and the hours planned in it, and the two things the Calendar does to a task — move its
/// due date to a day, and set a day's planned hours.
/// <para>
/// A port on the Calendar's own surface, the shape <see cref="ICalendarPlans"/> takes: an
/// infrastructure adapter answers it through Tasks' published port, so neither module
/// references the other. Whether the tasks are shown is the Calendar's own view choice,
/// not this port's; which tasks the window's repository scope keeps is
/// <see cref="ICalendarWindowScope"/>'s.
/// </para>
/// </summary>
public interface ICalendarTasks
{
    /// <summary>Raised after a task was written, or the task store moved. It may arrive on
    /// any thread.</summary>
    event Action? Changed;

    /// <summary>The tasks due in <paramref name="window"/> and the hours planned in it, or
    /// <see cref="CalendarTasksDto.Off"/> in a host composed without Tasks.</summary>
    Task<CalendarTasksDto> ReadAsync(CalendarWindow window, CancellationToken cancellationToken = default);

    /// <summary>Moves the task's due date to <paramref name="dueOn"/>. Answers the refusal
    /// in words, or null.</summary>
    Task<string?> SetDueDateAsync(Guid taskId, DateOnly dueOn, CancellationToken cancellationToken = default);

    /// <summary>Sets <paramref name="hours"/> aside for the task on <paramref name="on"/>,
    /// or for null takes that day's block away; the task's other days stand. Answers the
    /// refusal in words, or null.</summary>
    Task<string?> SetPlannedHoursAsync(Guid taskId, DateOnly on, decimal? hours, CancellationToken cancellationToken = default);
}
