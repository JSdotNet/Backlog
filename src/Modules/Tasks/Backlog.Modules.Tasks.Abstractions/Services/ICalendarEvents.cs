using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Tasks.Abstractions.Services;

/// <summary>
/// The knowledge events as the Tasks Calendar draws them: each decided event across its
/// days, and each of their key dates on its day. A sibling of <c>ICalendarPlans</c> (the Calendar's Plans port),
/// and read-only: the Calendar changes no event.
/// <para>
/// A port on Tasks' own surface rather than a reference to the Events context (local ADR
/// 0050 §7): this module asks its own port, and an infrastructure adapter that may see both
/// contexts answers it through Events' published read contract — never its storage. So
/// neither module references the other's implementation, and no Tasks type holds an event
/// id.
/// </para>
/// </summary>
public interface ICalendarEvents
{
    /// <summary>Whether the Calendar draws the events — its "Show events" box. Off until
    /// the reader turns it on on this device.</summary>
    bool Shown { get; }

    /// <summary>Remembers the "Show events" box on this device. A choice the host cannot
    /// keep for next time still holds for this run.</summary>
    void SetShown(bool shown);

    /// <summary>The decided events and their key dates as they read now, or
    /// <see cref="CalendarEventsDto.Off"/> while the Events area is switched off.</summary>
    Task<CalendarEventsDto> ReadAsync(CancellationToken cancellationToken = default);
}
