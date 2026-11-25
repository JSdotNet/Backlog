using Backlog.Modules.Events.Abstractions;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Events.DomainModels;

/// <summary>
/// A calendar date, plus a time of day and its UTC offset when they are known
/// (<c>.devbook/domain/events/domain.md#event-date</c>). A conference listed as "26 - 28 OCT"
/// has dates only; a meetup from 16:30 in Nieuwegein has a time and the offset <c>+01:00</c>.
/// Equality is by value.
/// </summary>
public sealed record EventDate
{
    private EventDate(DateOnly date, TimeOnly? time, TimeSpan? utcOffset)
    {
        Date = date;
        Time = time;
        UtcOffset = utcOffset;
    }

    public DateOnly Date { get; }

    /// <summary>The time of day, or null when only the date is known.</summary>
    public TimeOnly? Time { get; }

    /// <summary>The offset from UTC the time is written in, or null.</summary>
    public TimeSpan? UtcOffset { get; }

    /// <summary>An event date. Refused for a time with no offset (<c>event_date.offset_missing</c>);
    /// an offset with no time is kept, since no rule forbids it.</summary>
    public static Result<EventDate> Create(DateOnly date, TimeOnly? time, TimeSpan? utcOffset) =>
        time is not null && utcOffset is null
            ? EventsErrors.EventDateOffsetMissing()
            : new EventDate(date, time, utcOffset);
}

/// <summary>
/// Where the event happens: online, or in person with a venue name in free text, an
/// <see cref="Address"/>, or both (<c>.devbook/domain/events/domain.md#place</c>). A place with an
/// address holds its <see cref="AddressLookup"/>, and its <see cref="MapPoint"/> when that lookup
/// placed it. Equality is by value.
/// </summary>
public sealed record Place
{
    private Place(string? venue, bool isOnline, Address? address, AddressLookup? lookup, MapPoint? point)
    {
        Venue = venue;
        IsOnline = isOnline;
        Address = address;
        Lookup = lookup;
        Point = point;
    }

    /// <summary>The venue name, trimmed, or null when there is none or the event is online.</summary>
    public string? Venue { get; }

    /// <summary>Whether the event happens online rather than in person.</summary>
    public bool IsOnline { get; }

    /// <summary>Where an in-person event is held, or null.</summary>
    public Address? Address { get; }

    /// <summary>What geocoding answered for <see cref="Address"/>; set exactly when there is one.</summary>
    public AddressLookup? Lookup { get; }

    /// <summary>The spot on the map; set exactly when <see cref="Lookup"/> is Placed.</summary>
    public MapPoint? Point { get; }

    /// <summary>An online event's place.</summary>
    public static Place Online { get; } = new(venue: null, isOnline: true, address: null, lookup: null, point: null);

    /// <summary>A venue name alone, trimmed. Refused for a blank one (<c>place.invalid</c>).</summary>
    public static Result<Place> AtVenue(string venue) => Create(venue, online: false);

    /// <summary>A venue name or online, with no address. Refused for both, for neither, and for a
    /// blank venue name (<c>place.invalid</c>).</summary>
    public static Result<Place> Create(string? venue, bool online) =>
        Create(venue, online, address: null, lookup: null, point: null);

    /// <summary>
    /// A place, its venue name trimmed and a blank one counted as none. Refused, in this order,
    /// for an online place with a venue name, an address, a lookup or a point, and for an
    /// in-person one with neither a venue name nor an address (<c>place.invalid</c>); then for a
    /// lookup without an address or an address without one, and for a point on a lookup that is
    /// not Placed or a Placed lookup without one (<c>place.lookup_mismatch</c>).
    /// </summary>
    public static Result<Place> Create(string? venue, bool online, Address? address, AddressLookup? lookup, MapPoint? point)
    {
        if (lookup is { } answer && !Enum.IsDefined(answer))
            throw new ArgumentOutOfRangeException(nameof(lookup), lookup, "An address is placed, not found, or not looked up yet.");

        var trimmed = string.IsNullOrWhiteSpace(venue) ? null : venue.Trim();

        if (online)
        {
            return trimmed is null && address is null && lookup is null && point is null
                ? Online
                : EventsErrors.PlaceInvalid();
        }

        if (trimmed is null && address is null) return EventsErrors.PlaceInvalid();

        if ((address is null) != (lookup is null)) return EventsErrors.PlaceLookupMismatch();

        if ((lookup == AddressLookup.Placed) != (point is not null)) return EventsErrors.PlaceLookupMismatch();

        return new Place(trimmed, isOnline: false, address, lookup, point);
    }

    /// <summary>This place with its address's lookup and point replaced. Only the aggregate calls
    /// it, for a place that holds an address, with a point exactly when the lookup is Placed.</summary>
    internal Place WithLookup(AddressLookup lookup, MapPoint? point) =>
        new(Venue, IsOnline, Address, lookup, point);
}

/// <summary>
/// Where an in-person event is held, in four optional parts — street, postcode, town and
/// country — each kept trimmed (<c>.devbook/domain/events/domain.md#address</c>). Equality is by
/// value, part by part.
/// </summary>
public sealed record Address
{
    private Address(string? street, string? postcode, string? town, string? country)
    {
        Street = street;
        Postcode = postcode;
        Town = town;
        Country = country;
    }

    public string? Street { get; }

    public string? Postcode { get; }

    public string? Town { get; }

    public string? Country { get; }

    /// <summary>An address with each part trimmed and a blank part kept as none. Refused when
    /// every part is blank (<c>address.empty</c>).</summary>
    public static Result<Address> Create(string? street, string? postcode, string? town, string? country)
    {
        var address = new Address(Part(street), Part(postcode), Part(town), Part(country));

        return address is { Street: null, Postcode: null, Town: null, Country: null }
            ? EventsErrors.AddressEmpty()
            : address;
    }

    private static string? Part(string? part) => string.IsNullOrWhiteSpace(part) ? null : part.Trim();
}

/// <summary>
/// The spot on the map where an in-person event is held, in degrees
/// (<c>.devbook/domain/events/domain.md#map-point</c>). Only a lookup that placed the address sets
/// it. Equality is by value.
/// </summary>
public sealed record MapPoint
{
    private MapPoint(double latitude, double longitude)
    {
        Latitude = latitude;
        Longitude = longitude;
    }

    public double Latitude { get; }

    public double Longitude { get; }

    /// <summary>A map point. Refused for a latitude outside -90 to 90 or a longitude outside -180 to
    /// 180, bounds included, and for a coordinate that is not a number
    /// (<c>map_point.out_of_range</c>).</summary>
    public static Result<MapPoint> Create(double latitude, double longitude) =>
        latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180
            ? new MapPoint(latitude, longitude)
            : EventsErrors.MapPointOutOfRange();
}

/// <summary>
/// A date that matters before the event, with its label: early bird ends, registration closes,
/// call for speakers closes (<c>.devbook/domain/events/domain.md#key-date</c>). A date only, with
/// no time. Equality is by value.
/// </summary>
public sealed record KeyDate
{
    private KeyDate(DateOnly date, string label)
    {
        Date = date;
        Label = label;
    }

    public DateOnly Date { get; }

    public string Label { get; }

    /// <summary>A key date with its label trimmed. Refused for a blank label
    /// (<c>key_date.label_missing</c>).</summary>
    public static Result<KeyDate> Create(DateOnly date, string label)
    {
        var trimmed = label?.Trim() ?? string.Empty;

        return trimmed.Length == 0 ? EventsErrors.KeyDateLabelMissing() : new KeyDate(date, trimmed);
    }
}

/// <summary>
/// What going costs, as one amount with its currency
/// (<c>.devbook/domain/events/domain.md#cost</c>). Nothing totals or reports it. Equality is by
/// value.
/// </summary>
public sealed record Cost
{
    private Cost(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }

    /// <summary>The currency code, trimmed and upper-cased, such as <c>EUR</c>.</summary>
    public string Currency { get; }

    /// <summary>A cost with its currency trimmed and upper-cased; no list of currencies is
    /// checked. Refused for a negative amount and a blank currency (<c>cost.invalid</c>).</summary>
    public static Result<Cost> Create(decimal amount, string currency)
    {
        if (amount < 0) return EventsErrors.CostInvalid("A cost is zero or more.");

        var code = currency?.Trim().ToUpperInvariant() ?? string.Empty;

        return code.Length == 0 ? EventsErrors.CostInvalid("A cost names its currency.") : new Cost(amount, code);
    }
}

/// <summary>
/// Why the person decided against an event, in their own words
/// (<c>.devbook/domain/events/domain.md#skip-reason</c>). Plain text, never empty. Equality is by
/// value. Only the aggregate makes one, from <see cref="KnowledgeEvent.Skip"/> or a restore.
/// </summary>
public sealed record SkipReason
{
    internal SkipReason(string text) => Text = text;

    public string Text { get; }
}

/// <summary>
/// Whether the event was worth it, in the person's own words
/// (<c>.devbook/domain/events/domain.md#verdict</c>). Equality is by value. Only the aggregate
/// makes one, from <see cref="KnowledgeEvent.SetVerdict"/> or a restore.
/// </summary>
public sealed record Verdict
{
    internal Verdict(string text) => Text = text;

    public string Text { get; }
}

/// <summary>
/// A note the event points at: a Brain note by its path from the Brain's root, or a Talk note
/// by its Inbox id (<c>.devbook/domain/events/domain.md#note-link</c>). It keeps the note's title
/// as it read when linked. Two links are equal when they name the same note — the same kind and
/// reference — whatever their titles.
/// </summary>
public sealed record NoteLink
{
    private NoteLink(NoteLinkKind kind, string reference, string title)
    {
        Kind = kind;
        Reference = reference;
        Title = title;
    }

    public NoteLinkKind Kind { get; }

    /// <summary>The Brain note's path from the Brain's root, or the Inbox note's id.</summary>
    public string Reference { get; }

    /// <summary>The note's title as it read when linked; empty when none was given.</summary>
    public string Title { get; }

    /// <summary>A link to a note. A blank reference is a caller bug rather than a rule the
    /// person can break, so it throws.</summary>
    public static NoteLink Create(NoteLinkKind kind, string reference, string? title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind), kind, "A note link names a Brain note or a Talk note.");

        var named = kind == NoteLinkKind.Brain ? BrainPath(reference) ?? reference.Trim() : reference.Trim();
        return new NoteLink(kind, named, title?.Trim() ?? string.Empty);
    }

    /// <summary>A Brain note's path as a link stores it: forward slashes, from the root, with no
    /// leading slash, no <c>./</c> and no empty segment, so <c>./areas//talk.md</c> is
    /// <c>areas/talk.md</c>. Null for one that names nothing. Applied on every link made, read
    /// back or replicated, so a path stored before the rule names the same note too.</summary>
    public static string? BrainPath(string? path)
    {
        var segments = (path ?? string.Empty).Trim().Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(segment => segment != ".");
        var normalized = string.Join('/', segments);
        return normalized.Length == 0 ? null : normalized;
    }

    /// <summary>Equal when they name the same note. References compare without regard to case: the
    /// Brain's folder is read case-blind, so <c>Areas/Talk.md</c> and <c>areas/talk.md</c> are one
    /// note, and an Inbox id is a GUID.</summary>
    public bool Equals(NoteLink? other) =>
        other is not null && Kind == other.Kind && string.Equals(Reference, other.Reference, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() => HashCode.Combine(Kind, StringComparer.OrdinalIgnoreCase.GetHashCode(Reference));
}

/// <summary>
/// A task the event points at, named by its id, with its purpose
/// (<c>.devbook/domain/events/domain.md#event-task-link</c>). The task never names the event
/// back. Two links are equal when they name the same task, whatever their purposes.
/// </summary>
public sealed record EventTaskLink
{
    private EventTaskLink(string taskId, TaskPurpose purpose)
    {
        TaskId = taskId;
        Purpose = purpose;
    }

    public string TaskId { get; }

    public TaskPurpose Purpose { get; }

    /// <summary>A link to a task. A blank task id is a caller bug rather than a rule the person
    /// can break, so it throws.</summary>
    public static EventTaskLink Create(string taskId, TaskPurpose purpose)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        if (!Enum.IsDefined(purpose)) throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "A task link is for prep, the harvest, or linked.");

        return new EventTaskLink(taskId.Trim(), purpose);
    }

    public bool Equals(EventTaskLink? other) =>
        other is not null && string.Equals(TaskId, other.TaskId, StringComparison.Ordinal);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(TaskId);
}
