using Backlog.Modules.Calendar.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Calendar.Abstractions.Services;

/// <summary>
/// The Tasks layer as the Calendar reads it (local ADR 0056 §2, the read policy of local
/// ADR 0055 §3): the last read of <see cref="ICalendarTasks"/>, read again only when one is
/// owed — on arriving, after a task was written, on a new day, and when the grid shows days
/// the last read did not cover — never on every render. One per window.
/// </summary>
public interface ICalendarTasksLayer
{
    /// <summary>Raised when a read is owed: the port said the tasks changed. It may arrive
    /// on any thread.</summary>
    event Action? Changed;

    /// <summary>Whether the host composed a Tasks port at all.</summary>
    bool Offered { get; }

    /// <summary>Offered, and the last read found tasks to show
    /// (<see cref="CalendarTasksDto.Available"/>).</summary>
    bool Available { get; }

    /// <summary>The tasks as last read: <see cref="CalendarTasksDto.Off"/> before the first
    /// read and from each arrival until its read lands, and
    /// <see cref="CalendarTasksDto.Empty"/> after one that failed.</summary>
    CalendarTasksDto Tasks { get; }

    /// <summary>The Calendar arrived on screen: the next <see cref="ReadIfOwedAsync"/> reads,
    /// and <see cref="Tasks"/> is Off until it lands.</summary>
    void Arrive();

    /// <summary>Reads when one is owed, the day moved on since the last read, or
    /// <paramref name="window"/> holds days the last read did not.</summary>
    Task ReadIfOwedAsync(DateOnly today, CalendarWindow window, CancellationToken cancellationToken = default);

    /// <summary>Reads <paramref name="window"/> now, owed or not.</summary>
    Task ReadAsync(DateOnly today, CalendarWindow window, CancellationToken cancellationToken = default);

    /// <summary>Moves the task's due date through the port: the refusal in words, or null.
    /// Refused with no port.</summary>
    Task<string?> SetDueDateAsync(Guid taskId, DateOnly dueOn, CancellationToken cancellationToken = default);

    /// <summary>Sets or, for null, removes a day's planned hours through the port: the
    /// refusal in words, or null. Refused with no port.</summary>
    Task<string?> SetPlannedHoursAsync(Guid taskId, DateOnly on, decimal? hours, CancellationToken cancellationToken = default);
}
