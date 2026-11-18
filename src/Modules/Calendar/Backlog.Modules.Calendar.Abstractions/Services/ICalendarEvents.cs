using Backlog.Modules.Calendar.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Calendar.Abstractions.Services;

/// <summary>
/// The Events layer port the Calendar owns (local ADR 0055 §2): the knowledge events as the
/// Calendar draws them — each decided event across its days, and each of their key dates
/// on its day. A sibling of <see cref="ICalendarPlans"/>, and read-only: the Calendar
/// changes no event.
/// <para>
/// A port on the Calendar's own surface rather than a reference to the Events context:
/// this module asks its own port, and an infrastructure adapter that may see both contexts
/// answers it through Events' published read contract — never its storage. So neither
/// module references the other's implementation, and no Calendar type holds an event id.
/// Whether the events are shown is the Calendar's own view choice, not this port's.
/// </para>
/// </summary>
public interface ICalendarEvents
{
    /// <summary>The decided events and their key dates as they read now, or
    /// <see cref="CalendarEventsDto.Off"/> while the Events area is switched off.</summary>
    Task<CalendarEventsDto> ReadAsync(CancellationToken cancellationToken = default);
}
