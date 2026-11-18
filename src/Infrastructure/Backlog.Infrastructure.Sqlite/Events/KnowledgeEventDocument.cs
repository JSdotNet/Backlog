using System.Text.Json;
using System.Text.Json.Serialization;

using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.DomainModels;
using Backlog.SharedKernel.Results;

namespace Backlog.Infrastructure.Sqlite.Events;

/// <summary>
/// One knowledge event as the JSON document its row holds: the owner's local copy of the
/// module's owned document (local ADR 0050 §2, the shape ADR 0039 §3 gives an owned document).
/// Versioned so a later shape can be told from this one. The JSON mapping lives here and only
/// here: no domain type carries a serialization attribute (guideline ADR 0014).
/// </summary>
internal sealed record KnowledgeEventDocument(
    int Version,
    Guid Id,
    string Title,
    Guid? Series,
    EventDateDocument? Start,
    EventDateDocument? End,
    DateSource? DateSource,
    PlaceDocument? Place,
    string? Link,
    KnowledgeEventState State,
    string? SkipReason,
    string? WhatToLearn,
    IReadOnlyList<KeyDateDocument>? KeyDates,
    TicketState? Ticket,
    CostDocument? Cost,
    IReadOnlyList<NoteLinkDocument>? Notes,
    IReadOnlyList<TaskLinkDocument>? Tasks,
    string? Verdict)
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>The only shape written so far.</summary>
    public const int CurrentVersion = 1;

    public static KnowledgeEventDocument From(KnowledgeEvent knowledgeEvent) => new(
        CurrentVersion,
        knowledgeEvent.Id,
        knowledgeEvent.Title,
        knowledgeEvent.Series,
        EventDateDocument.From(knowledgeEvent.Start),
        EventDateDocument.From(knowledgeEvent.End),
        knowledgeEvent.DateSource,
        knowledgeEvent.Place is { } place ? new PlaceDocument(place.Venue, place.IsOnline) : null,
        knowledgeEvent.Link,
        knowledgeEvent.State,
        knowledgeEvent.SkipReason?.Text,
        knowledgeEvent.WhatToLearn,
        [.. knowledgeEvent.KeyDates.Select(keyDate => new KeyDateDocument(keyDate.Date, keyDate.Label))],
        knowledgeEvent.Ticket,
        knowledgeEvent.Cost is { } cost ? new CostDocument(cost.Amount, cost.Currency) : null,
        [.. knowledgeEvent.Notes.Select(note => new NoteLinkDocument(note.Kind, note.Reference, note.Title))],
        [.. knowledgeEvent.Tasks.Select(task => new TaskLinkDocument(task.TaskId, task.Purpose))],
        knowledgeEvent.Verdict?.Text);

    /// <summary>The knowledge event this document holds. Throws <see cref="JsonException"/> for
    /// one that parsed but does not read as an event — a later shape, no id or title, a value
    /// outside its fixed set, or a value object its own rules refuse — so the store skips the
    /// row rather than handing the module an event without them.</summary>
    public KnowledgeEvent ToKnowledgeEvent()
    {
        if (Version != CurrentVersion) throw new JsonException($"A knowledge event document of version {Version} is not one this build reads.");
        if (Id == Guid.Empty) throw new JsonException("A knowledge event document names its id.");
        if (string.IsNullOrWhiteSpace(Title)) throw new JsonException("A knowledge event document has a title.");
        if (!Enum.IsDefined(State)
            || DateSource is { } source && !Enum.IsDefined(source)
            || Ticket is { } ticket && !Enum.IsDefined(ticket))
            throw new JsonException("A knowledge event document's state, date source and ticket come from their fixed sets.");

        return KnowledgeEvent.Restore(
            Id,
            Title.Trim(),
            Series,
            Start?.ToEventDate(),
            End?.ToEventDate(),
            DateSource,
            Place?.ToPlace(),
            Link,
            State,
            SkipReason,
            WhatToLearn,
            (KeyDates ?? []).Select(keyDate => Required(keyDate, "key date").ToKeyDate()),
            Ticket,
            Cost?.ToCost(),
            (Notes ?? []).Select(note => Required(note, "note link").ToNoteLink()),
            (Tasks ?? []).Select(task => Required(task, "task link").ToTaskLink()),
            Verdict);
    }

    private static T Required<T>(T? value, string what) where T : class =>
        value ?? throw new JsonException($"A knowledge event document's {what} list holds no null.");

    /// <summary>The value a stored value object's own rules give, or the reason they refuse it,
    /// as the exception that skips the row.</summary>
    internal static T Valid<T>(Result<T> result, string what) =>
        result.IsSuccess ? result.Value : throw new JsonException($"A knowledge event document's {what} is not valid: {result.Error}.");
}

/// <summary>An <see cref="EventDate"/> as stored.</summary>
internal sealed record EventDateDocument(DateOnly Date, TimeOnly? Time, TimeSpan? UtcOffset)
{
    public static EventDateDocument? From(EventDate? date) =>
        date is null ? null : new EventDateDocument(date.Date, date.Time, date.UtcOffset);

    public EventDate ToEventDate() => KnowledgeEventDocument.Valid(EventDate.Create(Date, Time, UtcOffset), "date");
}

/// <summary>A <see cref="Modules.Events.DomainModels.Place"/> as stored.</summary>
internal sealed record PlaceDocument(string? Venue, bool Online)
{
    public Place ToPlace() => KnowledgeEventDocument.Valid(Place.Create(Venue, Online), "place");
}

/// <summary>A <see cref="KeyDate"/> as stored.</summary>
internal sealed record KeyDateDocument(DateOnly Date, string? Label)
{
    public KeyDate ToKeyDate() => KnowledgeEventDocument.Valid(KeyDate.Create(Date, Label ?? string.Empty), "key date");
}

/// <summary>A <see cref="Modules.Events.DomainModels.Cost"/> as stored.</summary>
internal sealed record CostDocument(decimal Amount, string? Currency)
{
    public Cost ToCost() => KnowledgeEventDocument.Valid(Cost.Create(Amount, Currency ?? string.Empty), "cost");
}

/// <summary>A <see cref="NoteLink"/> as stored. A blank reference or a kind outside the set is
/// a document the store cannot read, not a caller bug, so it skips the row.</summary>
internal sealed record NoteLinkDocument(NoteLinkKind Kind, string? Reference, string? Title)
{
    public NoteLink ToNoteLink()
    {
        if (string.IsNullOrWhiteSpace(Reference) || !Enum.IsDefined(Kind))
            throw new JsonException("A knowledge event document's note link names a Brain or Talk note.");

        return NoteLink.Create(Kind, Reference, Title);
    }
}

/// <summary>An <see cref="EventTaskLink"/> as stored, on the same terms as a note link.</summary>
internal sealed record TaskLinkDocument(string? TaskId, TaskPurpose Purpose)
{
    public EventTaskLink ToTaskLink()
    {
        if (string.IsNullOrWhiteSpace(TaskId) || !Enum.IsDefined(Purpose))
            throw new JsonException("A knowledge event document's task link names a task and its purpose.");

        return EventTaskLink.Create(TaskId, Purpose);
    }
}
