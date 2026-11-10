namespace Backlog.UI.Components.Tasks;

/// <summary>
/// One knowledge event as the month calendar draws it: a bar from its first day to its
/// last, across as many week rows as it reaches. The calendar only reads it — an event
/// bar is never dragged and never a drop.
/// </summary>
/// <param name="Id">The host's key for the bar, unique in what it hands in.</param>
/// <param name="Title">The event's name.</param>
/// <param name="State">Where the event stands, as its word — "Going", "Considering".</param>
/// <param name="Place">The venue, "Online", or null to leave it out.</param>
/// <param name="Start">First day, inclusive.</param>
/// <param name="End">Last day, inclusive; the start for a one-day event.</param>
/// <param name="Tentative">Still being weighed up: drawn dashed, where a decided event
/// is drawn filled. The state word says the same, so the shape is never alone.</param>
public sealed record CalendarEvent(
    string Id,
    string Title,
    string State,
    string? Place,
    DateOnly Start,
    DateOnly End,
    bool Tentative)
{
    /// <summary>What the bar, its tooltip and a screen reader say: "Techorama · Going ·
    /// Antwerp", without the place when none is known.</summary>
    public string Label =>
        string.IsNullOrWhiteSpace(Place) ? $"{Title} · {State}" : $"{Title} · {State} · {Place}";
}

/// <summary>A key date of an event — "Early bird ends" — drawn as a diamond chip on its
/// day, whether or not the event's own days are known.</summary>
/// <param name="Id">The host's key for the chip.</param>
/// <param name="Label">What happens that day.</param>
/// <param name="EventTitle">The event it belongs to, named in the tooltip.</param>
/// <param name="On">The day.</param>
public sealed record CalendarKeyDate(string Id, string Label, string EventTitle, DateOnly On);

/// <summary>
/// The part of one event's bar inside one week row.
/// </summary>
/// <param name="Column">The weekday it starts on, Monday 1 to Sunday 7 — a CSS grid
/// column.</param>
/// <param name="Span">How many days of the week it covers.</param>
/// <param name="Lane">Which row of event bars it sits in, from 1; overlapping events take
/// lanes of their own.</param>
/// <param name="ContinuesBefore">The event started in an earlier week; the bar opens
/// with "…".</param>
/// <param name="ContinuesAfter">The event runs on into a later week.</param>
public sealed record CalendarEventSegment(
    CalendarEvent Event,
    int Column,
    int Span,
    int Lane,
    bool ContinuesBefore,
    bool ContinuesAfter)
{
    /// <summary>The bar's text: the event's label, after "…" when it carries on from the
    /// week before.</summary>
    public string Label => ContinuesBefore ? $"… {Event.Label}" : Event.Label;
}

/// <summary>
/// Splits events into week rows and stacks the overlapping ones in lanes, by the rule
/// the plans follow (<see cref="CalendarPlanLanes"/>), apart from the markup so it can be
/// pinned down on its own.
/// </summary>
public static class CalendarEventLanes
{
    /// <summary>
    /// The segments of <paramref name="events"/> that fall in the week starting on the
    /// Monday <paramref name="weekStart"/>, with the lane each sits in, and how many
    /// lanes the week needs.
    /// <para>
    /// Earliest first, a longer bar before a shorter one starting the same day, ties in
    /// the order given; each takes the first lane free over all its days. An event whose
    /// end is before its start draws nothing.
    /// </para>
    /// </summary>
    public static (IReadOnlyList<CalendarEventSegment> Segments, int Lanes) Week(
        DateOnly weekStart,
        IReadOnlyList<CalendarEvent> events) =>
        CalendarLanes.Week(
            weekStart,
            events,
            @event => @event.Start,
            @event => @event.End,
            (@event, at) => new CalendarEventSegment(@event, at.Column, at.Span, at.Lane, at.ContinuesBefore, at.ContinuesAfter));
}
