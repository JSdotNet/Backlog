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
/// Where the event happens: a venue, in free text, or online
/// (<c>.devbook/domain/events/domain.md#place</c>). Equality is by value.
/// </summary>
public sealed record Place
{
    private Place(string? venue, bool isOnline)
    {
        Venue = venue;
        IsOnline = isOnline;
    }

    /// <summary>The venue, trimmed, or null for an online event.</summary>
    public string? Venue { get; }

    /// <summary>Whether the event happens online rather than at a venue.</summary>
    public bool IsOnline { get; }

    /// <summary>An online event's place.</summary>
    public static Place Online { get; } = new(venue: null, isOnline: true);

    /// <summary>A venue, trimmed. Refused for a blank one (<c>place.invalid</c>).</summary>
    public static Result<Place> AtVenue(string venue) => Create(venue, online: false);

    /// <summary>A venue or online. Refused for both, for neither, and for a blank venue
    /// (<c>place.invalid</c>).</summary>
    public static Result<Place> Create(string? venue, bool online)
    {
        var trimmed = string.IsNullOrWhiteSpace(venue) ? null : venue.Trim();

        return (trimmed, online) switch
        {
            (null, true) => Online,
            (not null, false) => new Place(trimmed, isOnline: false),
            _ => EventsErrors.PlaceInvalid()
        };
    }
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

        return new NoteLink(kind, reference.Trim(), title?.Trim() ?? string.Empty);
    }

    public bool Equals(NoteLink? other) =>
        other is not null && Kind == other.Kind && string.Equals(Reference, other.Reference, StringComparison.Ordinal);

    public override int GetHashCode() => HashCode.Combine(Kind, StringComparer.Ordinal.GetHashCode(Reference));
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
