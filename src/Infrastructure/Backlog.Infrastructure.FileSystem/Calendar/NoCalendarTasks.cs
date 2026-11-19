using Backlog.Modules.Calendar.Abstractions.DataTransferObjects;
using Backlog.Modules.Calendar.Abstractions.Services;

namespace Backlog.Infrastructure.FileSystem.Calendar;

/// <summary>The Calendar's Tasks layer port in a host composed without Tasks (local ADR 0056
/// §2): always <see cref="CalendarTasksDto.Off"/>, so the Calendar draws its plans and events
/// and offers no Show tasks box, refuses every write, and never says anything changed.</summary>
internal sealed class NoCalendarTasks : ICalendarTasks
{
    private const string NoTasks = "There are no tasks on this Calendar.";

    public event Action? Changed { add { } remove { } }

    public Task<CalendarTasksDto> ReadAsync(CalendarWindow window, CancellationToken cancellationToken = default) =>
        Task.FromResult(CalendarTasksDto.Off);

    public Task<string?> SetDueDateAsync(Guid taskId, DateOnly dueOn, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(NoTasks);

    public Task<string?> SetPlannedHoursAsync(Guid taskId, DateOnly on, decimal? hours, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(NoTasks);
}
