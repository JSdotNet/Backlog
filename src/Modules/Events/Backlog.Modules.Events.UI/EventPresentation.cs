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

    /// <summary>Where the event is, in words, or null when not known.</summary>
    public static string? Where(PlaceDto? place) =>
        place is null ? null : place.IsOnline ? "Online" : place.Venue;

    /// <summary>The list row's second line: when, the series and where, as far as known.</summary>
    public static string RowLine(KnowledgeEventSummaryDto summary) =>
        string.Join(" · ", new[] { When(summary.Start, summary.End), summary.SeriesName, Where(summary.Place) }
            .Where(part => !string.IsNullOrWhiteSpace(part)));

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
