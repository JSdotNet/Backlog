using Backlog.Modules.Calendar.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Calendar.Abstractions.Services;

/// <summary>
/// The Events layer as the Calendar reads it (local ADR 0055 §3): the last read of
/// <see cref="ICalendarEvents"/>, read again only when one is owed — on arriving, after a
/// feature switched, on a new day, and when Show events is ticked — never on every render.
/// One per window.
/// </summary>
public interface ICalendarEventsLayer
{
    /// <summary>Raised when a read is owed: a feature switched — the Events area among
    /// them. It may arrive on any thread.</summary>
    event Action? Changed;

    /// <summary>Whether the host composed an Events port at all.</summary>
    bool Offered { get; }

    /// <summary>The events as last read: <see cref="CalendarEventsDto.Off"/> before the
    /// first read and from each arrival until its read lands, so no Show events box is
    /// offered before a read says whether the Events area is on, and
    /// <see cref="CalendarEventsDto.Empty"/> after one that failed.</summary>
    CalendarEventsDto Events { get; }

    /// <summary>Offered, and the last read found the Events area on
    /// (<see cref="CalendarEventsDto.Available"/>).</summary>
    bool Available { get; }

    /// <summary>The Calendar arrived on screen: the next <see cref="ReadIfOwedAsync"/> reads,
    /// and <see cref="Events"/> is Off until it lands.</summary>
    void Arrive();

    /// <summary>Reads when one is owed or the day moved on since the last read.</summary>
    Task ReadIfOwedAsync(DateOnly today, CancellationToken cancellationToken = default);

    /// <summary>Reads now, owed or not.</summary>
    Task ReadAsync(DateOnly today, CancellationToken cancellationToken = default);
}
