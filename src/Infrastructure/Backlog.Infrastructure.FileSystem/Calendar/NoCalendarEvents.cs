using Backlog.Modules.Calendar.Abstractions.DataTransferObjects;
using Backlog.Modules.Calendar.Abstractions.Services;

namespace Backlog.Infrastructure.FileSystem.Calendar;

/// <summary>The Calendar's Events layer port in a host composed without the Events module:
/// always <see cref="CalendarEventsDto.Off"/>, as with the Events area switched off, so the
/// Calendar offers no Show events box and reads nothing.</summary>
internal sealed class NoCalendarEvents : ICalendarEvents
{
    public Task<CalendarEventsDto> ReadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(CalendarEventsDto.Off);
}
