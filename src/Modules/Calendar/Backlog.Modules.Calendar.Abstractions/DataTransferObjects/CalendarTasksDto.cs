namespace Backlog.Modules.Calendar.Abstractions.DataTransferObjects;

/// <summary>The days the Calendar's grid shows, first and last inclusive — what a read of
/// the Tasks layer is asked for (local ADR 0056 §2).</summary>
public sealed record CalendarWindow(DateOnly First, DateOnly Last)
{
    /// <summary>Whether <paramref name="day"/> is one of the window's days.</summary>
    public bool Contains(DateOnly day) => day >= First && day <= Last;

    /// <summary>Whether every day of <paramref name="other"/> is one of this window's.</summary>
    public bool Covers(CalendarWindow other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return other.First >= First && other.Last <= Last;
    }
}

/// <summary>Where a task stands, in the Calendar's words: what its chip is drawn as. A
/// ticked task, or one whose status is Done, is <see cref="Done"/>.</summary>
public enum CalendarTaskStatus
{
    Draft,
    Ready,
    InProgress,
    Done,
}

/// <summary>
/// The tasks in a window, in the Calendar's words: its own shape, so nothing of Tasks
/// crosses into this module (local ADR 0056 §2).
/// </summary>
/// <param name="Tasks">Every live, unarchived task due on a day of the window, in the
/// backlog's rank order.</param>
/// <param name="Hours">Every block of planned hours on a day of the window, from every
/// live, unarchived task — dated or not, so a separate list from <paramref name="Tasks"/>.</param>
/// <param name="Available">Whether there are tasks to show at all; false in a host composed
/// without Tasks, when the Calendar offers no Show tasks.</param>
public sealed record CalendarTasksDto(
    IReadOnlyList<CalendarTaskDto> Tasks,
    IReadOnlyList<CalendarHoursBlockDto> Hours,
    bool Available = true)
{
    /// <summary>No task and no hours in the window.</summary>
    public static CalendarTasksDto Empty { get; } = new([], []);

    /// <summary>No tasks to read: the host composed no Tasks.</summary>
    public static CalendarTasksDto Off { get; } = new([], [], Available: false);
}

/// <summary>One task on its due day.</summary>
/// <param name="Repeats">Whether the task repeats.</param>
/// <param name="RepositoryIds">The repositories it is filed under, by id; the window's
/// scope narrows on them and the first gives the chip its colour.</param>
public sealed record CalendarTaskDto(
    Guid Id,
    string Title,
    DateOnly DueOn,
    CalendarTaskStatus Status,
    bool Repeats,
    IReadOnlyList<string> RepositoryIds);

/// <summary>The hours set aside for one task on one day.</summary>
public sealed record CalendarHoursBlockDto(
    Guid TaskId,
    string Title,
    DateOnly On,
    decimal Hours,
    IReadOnlyList<string> RepositoryIds);
