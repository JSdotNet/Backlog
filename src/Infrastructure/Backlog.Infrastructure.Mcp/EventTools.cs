using System.ComponentModel;
using System.Globalization;

using Backlog.Modules.Events.Abstractions;
using Backlog.SharedKernel.Results;

using ModelContextProtocol.Server;

namespace Backlog.Infrastructure.Mcp;

/// <summary>
/// <c>add_knowledge_event</c>: adds a knowledge event the way the Events pane's Add does, with
/// the editor's fields filled at once (<c>.devbook/domain/events/features.md#adding-an-event</c>).
/// <para>
/// <b>Through <see cref="IEventsPane"/>, never the database.</b> The event is built on the
/// aggregate and saved once by the module's own
/// <see cref="IEventsPane.AddAsync(NewKnowledgeEventDto, CancellationToken)"/>, so every rule the
/// pane meets refuses here with the same code, and a refusal writes nothing. The save announces
/// itself on the module's notice, so an open Events pane lists the event and the sync loop
/// pushes it, as it does a pane add. What this class owns is only the reading of the arguments
/// a model sends as text: ISO 8601 dates, a ticket word, a place from a venue and a flag.
/// </para>
/// <para>
/// Its own group behind <c>events</c> (local ADR 0050 §6, ADR 0012 §7): with the area off the
/// tool is absent from <c>tools/list</c>, and a call to it is an unknown tool.
/// </para>
/// </summary>
[McpServerToolType]
public sealed class EventTools(IEventsPane events, TimeProvider? clock = null)
{
    internal const string AddKnowledgeEvent = "add_knowledge_event";

    // Not idempotent: the pane's Add keeps no key a second call could match on, so the same
    // title sent twice is two events. Not destructive: nothing that was there changes. Closed:
    // the events are this machine's, and reach the phone only through the sync it has.
    [McpServerTool(Name = AddKnowledgeEvent, Title = "Add a knowledge event", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = false)]
    [Description(
        "Adds a knowledge event — a conference, meetup or course the person might go to, to learn something — as the "
        + "Events pane's Add does. It lands Spotted, whatever else is given, and dates given here carry the date source "
        + "Typed. Only the title is required. A series is named by its name: an existing series (matched without regard "
        + "to case) is joined, and an unknown name starts a new series. Calling this twice adds two events. Answers the "
        + "new event's id, title, state, series and dates. Refused, with nothing written, for a blank title "
        + "(knowledge_event.title_missing), an end before the start or an end with no start "
        + "(knowledge_event.dates_out_of_order), a venue and online together (place.invalid), a negative cost or one "
        + "with no currency or no amount (cost.invalid), a series name two series share (event_series.ambiguous), a "
        + "date that is not ISO 8601 (events.date_unreadable) and an unknown ticket word (events.unknown_ticket).")]
    public async Task<KnowledgeEventPayload> AddAsync(
        [Description("The event's title, as the person would say it, e.g. Techorama Belgium 2027.")]
        string title,
        [Description("Optional. The series' name, e.g. Techorama: an existing series is joined, a new name starts one.")]
        string? series = null,
        [Description("Optional. The first day, ISO 8601: yyyy-MM-dd, or with a time, yyyy-MM-ddTHH:mm with an offset "
            + "(+01:00 or Z). A time with no offset is read in this machine's time zone.")]
        string? start = null,
        [Description("Optional. The last day, in the same form as start; needs a start, on or after it.")]
        string? end = null,
        [Description("Optional. True for an online event. Not together with a venue.")]
        bool? online = null,
        [Description("Optional. Where the event happens, in free text, e.g. Kinepolis Antwerp. Not together with online.")]
        string? venue = null,
        [Description("Optional. The event's page.")]
        string? link = null,
        [Description("Optional. What the person hopes to learn there, in free text.")]
        string? whatToLearn = null,
        [Description("Optional. not_bought, bought, or not_needed.")]
        string? ticket = null,
        [Description("Optional. What going costs, zero or more; needs costCurrency.")]
        decimal? costAmount = null,
        [Description("Optional. The cost's currency code, e.g. EUR; needs costAmount.")]
        string? costCurrency = null,
        [Description("Your Claude session id, so this write joins your session's change set. Optional.")]
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        var zone = (clock ?? TimeProvider.System).LocalTimeZone;

        var request = new NewKnowledgeEventDto(
            title,
            series,
            ReadDate(start, nameof(start), zone).ValueOrThrow(),
            ReadDate(end, nameof(end), zone).ValueOrThrow(),
            PlaceOf(online, venue),
            link,
            whatToLearn,
            ReadTicket(ticket).ValueOrThrow(),
            CostOf(costAmount, costCurrency).ValueOrThrow());

        var added = await events.AddAsync(request, cancellationToken).ConfigureAwait(false);

        return KnowledgeEventPayload.From(added.ValueOrThrow());
    }

    private static readonly string[] DateTimeWithOffset =
    [
        "yyyy-MM-dd'T'HH:mmzzz",
        "yyyy-MM-dd'T'HH:mm:sszzz",
        "yyyy-MM-dd'T'HH:mm'Z'",
        "yyyy-MM-dd'T'HH:mm:ss'Z'"
    ];

    private static readonly string[] DateTimeWithoutOffset = ["yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss"];

    /// <summary>
    /// A date as ISO 8601: a day alone; a day and a time with its offset, or <c>Z</c>; or a day and
    /// a time with none, which gets <paramref name="zone"/>'s offset for that day, as the editor's
    /// time field does (<c>EventPresentation.DayOf</c>, in the pane's UI project, which this
    /// adapter may not reference). Null for no text; <c>events.date_unreadable</c> for any other.
    /// </summary>
    internal static Result<EventDateDto?> ReadDate(string? text, string argument, TimeZoneInfo zone)
    {
        if (string.IsNullOrWhiteSpace(text)) return Result.Success<EventDateDto?>(null);

        var trimmed = text.Trim();
        var invariant = CultureInfo.InvariantCulture;

        if (DateOnly.TryParseExact(trimmed, "yyyy-MM-dd", invariant, DateTimeStyles.None, out var day))
            return Result.Success<EventDateDto?>(new EventDateDto(day));

        if (DateTimeOffset.TryParseExact(trimmed, DateTimeWithOffset, invariant, DateTimeStyles.AssumeUniversal, out var at))
            return Result.Success<EventDateDto?>(new EventDateDto(DateOnly.FromDateTime(at.DateTime), TimeOnly.FromDateTime(at.DateTime), at.Offset));

        if (DateTime.TryParseExact(trimmed, DateTimeWithoutOffset, invariant, DateTimeStyles.None, out var local))
            return Result.Success<EventDateDto?>(new EventDateDto(DateOnly.FromDateTime(local), TimeOnly.FromDateTime(local), zone.GetUtcOffset(local)));

        return Result.Failure<EventDateDto?>(Error.Validation(
            "events.date_unreadable",
            $"{argument} '{text}' is no ISO 8601 date: yyyy-MM-dd, or yyyy-MM-ddTHH:mm with or without an offset."));
    }

    /// <summary>A ticket word without regard to case, <c>_</c>, <c>-</c> or spaces: not_bought,
    /// bought, not_needed. Null for no word; <c>events.unknown_ticket</c> for any other.</summary>
    internal static Result<TicketState?> ReadTicket(string? word)
    {
        if (string.IsNullOrWhiteSpace(word)) return Result.Success<TicketState?>(null);

        var joined = string.Concat(word.Where(c => c is not ('_' or '-' or ' ')));

        return !int.TryParse(joined, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
            && Enum.TryParse<TicketState>(joined, ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed)
            ? Result.Success<TicketState?>(parsed)
            : Result.Failure<TicketState?>(Error.Validation(
                "events.unknown_ticket",
                $"'{word}' is no ticket state: not_bought, bought, or not_needed."));
    }

    /// <summary>Online, a venue, both — handed on for the module's <c>place.invalid</c> — or null
    /// for neither.</summary>
    private static PlaceDto? PlaceOf(bool? online, string? venue) =>
        online is true || !string.IsNullOrWhiteSpace(venue)
            ? new PlaceDto(string.IsNullOrWhiteSpace(venue) ? null : venue, online is true)
            : null;

    /// <summary>An amount and its currency. An amount with no currency is handed on for the
    /// module's <c>cost.invalid</c>; a currency with no amount is no cost a request can carry,
    /// so it is refused here in the module's words.</summary>
    private static Result<CostDto?> CostOf(decimal? amount, string? currency)
    {
        if (amount is { } value) return Result.Success<CostDto?>(new CostDto(value, currency ?? string.Empty));

        return string.IsNullOrWhiteSpace(currency)
            ? Result.Success<CostDto?>(null)
            : Result.Failure<CostDto?>(EventsErrors.CostInvalid("A cost names its amount."));
    }
}

/// <summary>The event <c>add_knowledge_event</c> added. <paramref name="Start"/> and
/// <paramref name="End"/> are ISO 8601 as the tool reads them: <c>yyyy-MM-dd</c>, or with the
/// time and its offset; null when the event has none.</summary>
public sealed record KnowledgeEventPayload(
    Guid Id,
    string Title,
    string State,
    string? Series,
    string? Start,
    string? End,
    string? DateSource)
{
    internal static KnowledgeEventPayload From(KnowledgeEventDto e) => new(
        e.Id,
        e.Title,
        e.State.ToString(),
        e.Series?.Name,
        Iso(e.Start),
        Iso(e.End),
        e.DateSource?.ToString());

    /// <summary>A date as <see cref="EventTools"/> reads one: the day, then the time with its
    /// offset when there is one, seconds only when they are not zero.</summary>
    internal static string? Iso(EventDateDto? date)
    {
        if (date is null) return null;

        var day = date.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (date.Time is not { } time) return day;

        var clock = time.ToString(time.Second == 0 ? "HH:mm" : "HH:mm:ss", CultureInfo.InvariantCulture);
        var offset = date.UtcOffset is { } utc
            ? (utc < TimeSpan.Zero ? "-" : "+") + utc.Duration().ToString(@"hh\:mm", CultureInfo.InvariantCulture)
            : string.Empty;

        return $"{day}T{clock}{offset}";
    }
}
