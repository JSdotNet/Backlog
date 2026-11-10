namespace Backlog.Modules.Tasks.Abstractions.DataTransferObjects;

/// <summary>
/// The knowledge events in the Calendar's words: Tasks' own shape, so nothing of Events'
/// crosses into this module (local ADR 0050 §7). Neither record carries an event id — the
/// Calendar opens and writes nothing, so it keys what it draws by position.
/// </summary>
/// <param name="Events">Every decided event that has a start, in the order the events
/// were first saved.</param>
/// <param name="KeyDates">The key dates of every decided event, dated or not, event by
/// event in that order, each event's in its own order.</param>
/// <param name="Available">Whether there are events to show at all; false while the
/// Events area is switched off, when the Calendar offers no events.</param>
public sealed record CalendarEventsDto(
    IReadOnlyList<CalendarEventDto> Events,
    IReadOnlyList<CalendarKeyDateDto> KeyDates,
    bool Available = true)
{
    /// <summary>No events and no key dates.</summary>
    public static CalendarEventsDto Empty { get; } = new([], []);

    /// <summary>No events to read: the Events area is switched off.</summary>
    public static CalendarEventsDto Off { get; } = new([], [], Available: false);
}

/// <summary>One decided event across its days.</summary>
/// <param name="State">Where the event stands, as its word: Considering, Going, Attended
/// or Harvested.</param>
/// <param name="Place">The venue, <c>Online</c>, or null when no place is known.</param>
/// <param name="Start">First day, inclusive.</param>
/// <param name="End">Last day, inclusive; the start for a one-day event.</param>
/// <param name="Tentative">Whether the person is still weighing it up (Considering): drawn
/// dashed, where a decided event is drawn filled.</param>
public sealed record CalendarEventDto(
    string Title,
    string State,
    string? Place,
    DateOnly Start,
    DateOnly End,
    bool Tentative);

/// <summary>One key date: a single day that matters before an event.</summary>
/// <param name="Label">What happens that day, such as "Early bird ends".</param>
/// <param name="EventTitle">The event it belongs to.</param>
public sealed record CalendarKeyDateDto(
    string Label,
    string EventTitle,
    DateOnly On);
