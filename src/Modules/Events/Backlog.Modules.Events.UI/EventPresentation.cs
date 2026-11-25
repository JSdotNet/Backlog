using System.Globalization;

using global::Backlog.Modules.Events.Abstractions;
using global::Backlog.UI.Components.Selects;

namespace Backlog.Modules.Events.UI;

/// <summary>
/// How the Events pane words and draws what <see cref="IEventsPane"/> answers: state names, the
/// badge tone a state wears, dates as the list reads them, and the field text a date round-trips
/// through. No IO and no state.
/// </summary>
public static class EventPresentation
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>A state as the pane names it.</summary>
    public static string StateLabel(KnowledgeEventState state) => state switch
    {
        KnowledgeEventState.Spotted => "Spotted",
        KnowledgeEventState.Considering => "Considering",
        KnowledgeEventState.Going => "Going",
        KnowledgeEventState.Attended => "Attended",
        KnowledgeEventState.Harvested => "Harvested",
        _ => "Skipped"
    };

    /// <summary>A date section of the list as the pane heads it.</summary>
    public static string SectionLabel(EventListSection section) =>
        section == EventListSection.Past ? "Past" : "Upcoming";

    /// <summary>The one line the design gives each state, drawn beside the current step.</summary>
    public static string StateLine(KnowledgeEventState state) => state switch
    {
        KnowledgeEventState.Spotted => "Dates may be unknown",
        KnowledgeEventState.Considering => "Key dates start to matter",
        KnowledgeEventState.Going => "Ticket, travel and leave become tasks",
        KnowledgeEventState.Attended => "Talk notes and who you met",
        KnowledgeEventState.Harvested => "Takeaways in Brain, follow-ups in Tasks, a verdict",
        _ => "Decided against"
    };

    /// <summary>
    /// The status-badge value a state is drawn with: the status family's own tones
    /// (<c>.devbook/design/color-scheme.md#badge-and-chip-tones</c>), so the pane spends no
    /// colour of its own. Spotted is quiet, Considering is information, Going is live, Attended
    /// and Harvested are settled, Skipped is out of force. The badge reads the state's name.
    /// </summary>
    public static string StateTone(KnowledgeEventState state) => state switch
    {
        KnowledgeEventState.Spotted => "draft",
        KnowledgeEventState.Considering => "ready",
        KnowledgeEventState.Going => "inprogress",
        KnowledgeEventState.Attended or KnowledgeEventState.Harvested => "done",
        _ => "archived"
    };

    /// <summary>The state choice's value for Skip, which asks for a reason before it moves.</summary>
    public const string SkipChoice = "skip";

    /// <summary>The state choice: where the event stands, then the later states it may move to,
    /// then Skip… when it may still be skipped. Nothing earlier is offered: there is no way back.</summary>
    public static IReadOnlyList<SelectorOption> StateChoices(KnowledgeEventState state, KnowledgeEventMoveOffer moves) =>
    [
        new SelectorOption(state.ToString(), StateLabel(state)),
        .. moves.Forward.Select(target => new SelectorOption(target.ToString(), StateLabel(target))),
        .. moves.CanSkip ? [new SelectorOption(SkipChoice, "Skip…")] : Array.Empty<SelectorOption>()
    ];

    /// <summary>Where the dates came from, as the editor shows it beside them: the capture rules'
    /// "dates show their source", since a listing and the event page may disagree.</summary>
    public static string DateSourceLabel(DateSource source) => source switch
    {
        DateSource.EventPage => "Event page",
        _ => "Typed"
    };

    /// <summary>A ticket state as the pane names it.</summary>
    public static string TicketLabel(TicketState ticket) => ticket switch
    {
        TicketState.NotBought => "Not bought",
        TicketState.Bought => "Bought",
        _ => "Not needed"
    };

    /// <summary>Why a task is linked, in a word.</summary>
    public static string PurposeLabel(TaskPurpose purpose) => purpose switch
    {
        TaskPurpose.Prep => "Prep",
        TaskPurpose.Harvest => "Harvest",
        _ => "Linked"
    };

    /// <summary>A day as the list reads it: <c>27 Nov 2026</c>, with its time when known.</summary>
    public static string Day(EventDateDto day)
    {
        var date = day.Date.ToString("d MMM yyyy", Invariant);
        return day.Time is { } time ? $"{date} {time.ToString("HH:mm", Invariant)}" : date;
    }

    /// <summary>
    /// When the event runs, in words: a day, a range, or that it is not known yet. An end on the
    /// start's date never repeats the day: with no time or the start's time it reads as the start
    /// day alone (<c>27 Nov 2026 16:30</c>), with another time as that time after it
    /// (<c>27 Nov 2026 16:30 – 21:00</c>).
    /// </summary>
    public static string When(EventDateDto? start, EventDateDto? end)
    {
        if (start is null) return "Date not known";
        if (end is null) return Day(start);
        if (end.Date != start.Date) return $"{Day(start)} – {Day(end)}";
        if (end.Time is not { } endTime || endTime == start.Time) return Day(start);
        return $"{Day(start)} – {endTime.ToString("HH:mm", Invariant)}";
    }

    /// <summary>A day as a list row reads it, short: <c>27 Nov</c> in the year it is now,
    /// <c>18 May 2027</c> in any other, and <c>No date</c> while none is known.</summary>
    public static string ShortDate(EventDateDto? start, DateOnly today) =>
        start is null ? "No date"
        : start.Date.Year == today.Year ? start.Date.ToString("d MMM", Invariant)
        : start.Date.ToString("d MMM yyyy", Invariant);

    /// <summary>An address on one line, with only the parts it has: street, then postcode and
    /// town together, then country.</summary>
    public static string? AddressLine(AddressDto? address)
    {
        if (address is null) return null;

        var town = string.Join(" ", new[] { address.Postcode, address.Town }.Where(part => !string.IsNullOrWhiteSpace(part)));
        var line = string.Join(", ", new[] { address.Street, town, address.Country }.Where(part => !string.IsNullOrWhiteSpace(part)));
        return line.Length == 0 ? null : line;
    }

    /// <summary>Where the event is, in words: online, the venue's name, else its address; null
    /// while no place is known.</summary>
    public static string? Where(PlaceDto? place) =>
        place is null ? null
        : place.IsOnline ? "Online"
        : !string.IsNullOrWhiteSpace(place.Venue) ? place.Venue
        : AddressLine(place.Address);

    /// <summary>
    /// Why an event has no pin, in the four words <c>domain.md#not-on-the-map</c> keeps apart, or
    /// null when it has a map point. "No venue yet" covers an in-person place with no address
    /// too: the map has nothing to place it by.
    /// </summary>
    public static string? OffMapReason(PlaceDto? place) => place switch
    {
        { Point: not null } => null,
        { IsOnline: true } => "Online",
        { Address: not null, Lookup: AddressLookup.NotFound } => "Address not found",
        { Address: not null } => "Address not looked up yet",
        _ => "No venue yet"
    };

    /// <summary>The row's place line: where the event is, or why it is not on the map
    /// (<c>Online · not on the map</c>). Null for an event on the map with nothing to name.</summary>
    public static string? PlaceLine(PlaceDto? place) =>
        OffMapReason(place) is { } reason ? $"{reason} · not on the map" : Where(place);

    /// <summary>
    /// The mark at the start of a row (<c>color-scheme.md#events-map-marks</c>): the map number in
    /// its tone for an event on the map, the grey ring for one on the map but past or settled,
    /// <c>◎</c> for an online event and <c>–</c> for any other off the map.
    /// </summary>
    public static EventMark Mark(KnowledgeEventSummaryDto summary, EventListSection section)
    {
        ArgumentNullException.ThrowIfNull(summary);

        if (summary.Place is { IsOnline: true }) return new EventMark(EventMarkTone.Online, null);
        if (summary.Place?.Point is null) return new EventMark(EventMarkTone.Off, null);

        return new EventMark(Tone(summary.State, section), summary.MapNumber?.ToString(Invariant));
    }

    /// <summary>The tone of an event on the map: grey once past, Skipped, or Attended or
    /// Harvested while still upcoming; filled for Going; a dashed edge for Spotted and
    /// Considering.</summary>
    public static EventMarkTone Tone(KnowledgeEventState state, EventListSection section) =>
        section == EventListSection.Past ? EventMarkTone.Grey
        : state switch
        {
            KnowledgeEventState.Going => EventMarkTone.Filled,
            KnowledgeEventState.Spotted or KnowledgeEventState.Considering => EventMarkTone.Dashed,
            _ => EventMarkTone.Grey
        };

    /// <summary>A pin's accessible name: its number, title, state, date and venue, as far as
    /// known.</summary>
    public static string PinName(KnowledgeEventSummaryDto summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        return string.Join(", ", new[]
        {
            summary.MapNumber?.ToString(Invariant), summary.Title, StateLabel(summary.State),
            When(summary.Start, summary.End), Where(summary.Place)
        }.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    /// <summary>A pin's tooltip: <c>title · date</c>.</summary>
    public static string PinTooltip(KnowledgeEventSummaryDto summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        return $"{summary.Title} · {When(summary.Start, summary.End)}";
    }

    /// <summary>The venue hint while the event's address is being looked up: a save or Look up
    /// now sent it and its answer is not in yet.</summary>
    public const string LookingUpHint = "Looking up the address…";

    /// <summary>
    /// The line under the editor's venue field (<c>requirements.md</c>, "The venue field says where
    /// the event is on the map"): its number while on the map, out of view while its pin is off
    /// the map's view, that its address is being looked up, or why it is not on the map. Null for
    /// an event on the map that carries no number — past or Skipped — since none of the hints is
    /// about it.
    /// </summary>
    public static string? VenueHint(PlaceDto? place, int? mapNumber, bool? inView, bool lookingUp = false) => place switch
    {
        { IsOnline: false, Address: not null } when lookingUp => LookingUpHint,
        { IsOnline: true } => "Online: not on the map.",
        null or { Address: null } => "Add an address to put it on the map.",
        { Point: null, Lookup: AddressLookup.NotFound } => "Address not found: check it.",
        { Point: null } => "Not looked up yet: it will be looked up when you save again, or Look up now.",
        _ when mapNumber is not { } number => null,
        _ when inView == false => "Outside the map's view.",
        _ => $"On the map as {mapNumber.Value.ToString(Invariant)}."
    };

    /// <summary>A date as a date field holds it.</summary>
    public static string DateText(DateOnly? date) => date?.ToString("yyyy-MM-dd", Invariant) ?? string.Empty;

    /// <summary>A time as a time field holds it.</summary>
    public static string TimeText(TimeOnly? time) => time?.ToString("HH:mm", Invariant) ?? string.Empty;

    /// <summary>A date field's text, or null when it holds no whole date.</summary>
    public static DateOnly? ParseDate(string? text) =>
        DateOnly.TryParseExact(text?.Trim(), "yyyy-MM-dd", Invariant, DateTimeStyles.None, out var date) ? date : null;

    /// <summary>A time field's text, or null when it holds no time. Seconds are accepted, as a
    /// browser may send them.</summary>
    public static TimeOnly? ParseTime(string? text) =>
        TimeOnly.TryParseExact(text?.Trim(), ["HH:mm", "HH:mm:ss"], Invariant, DateTimeStyles.None, out var time) ? time : null;

    /// <summary>
    /// The day a pair of fields describe, with the offset of <paramref name="zone"/> on that date
    /// when a time is given (OQ-12): a typed time is the desktop's local time. Null when the date
    /// field holds no date.
    /// </summary>
    public static EventDateDto? DayOf(string? dateText, string? timeText, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        if (ParseDate(dateText) is not { } date) return null;
        if (ParseTime(timeText) is not { } time) return new EventDateDto(date);

        var offset = zone.GetUtcOffset(date.ToDateTime(time, DateTimeKind.Unspecified));
        return new EventDateDto(date, time, offset);
    }

    /// <summary>An offset as the time field's help text says it: <c>UTC+01:00</c>.</summary>
    public static string OffsetText(TimeSpan offset) =>
        "UTC" + (offset < TimeSpan.Zero ? "-" : "+") + offset.Duration().ToString(@"hh\:mm", Invariant);

    /// <summary>An amount as the cost field holds it.</summary>
    public static string AmountText(decimal? amount) => amount?.ToString("0.##", Invariant) ?? string.Empty;

    /// <summary>A cost field's text, or null when it holds no number. A comma is read as the
    /// decimal mark too, as a European keyboard types it.</summary>
    public static decimal? ParseAmount(string? text) =>
        decimal.TryParse(text?.Trim().Replace(',', '.'), NumberStyles.Number, Invariant, out var amount) ? amount : null;
}
