namespace Backlog.Modules.Events.Abstractions.DataTransferObjects;

/// <summary>
/// One knowledge event as it travels between devices (local ADR 0050 §2): the whole event, so
/// a desktop that takes another's copy loses nothing of it, and the shape the phone reads to show
/// the event. Plain data; the sync client serializes it, and no type here carries a
/// serialization attribute (guideline ADR 0014).
/// <para>
/// The phone shows the title, state, dates, place, link, key dates, what to learn and notes, and
/// ignores the rest; the desktops need every field. A note link's <see cref="EventNoteLinkReplicaDto.Reference"/>
/// is a Brain note's path or a Talk note's Inbox id.
/// </para>
/// </summary>
/// <param name="Id">The event's id.</param>
/// <param name="Title">Never empty.</param>
/// <param name="State">Where the event stands.</param>
/// <param name="Start">The first day, with a time and offset when known, or null.</param>
/// <param name="End">The last day, or null: an event with a start and no end lasts its start day.</param>
/// <param name="DateSource">Where the dates came from; set exactly when there is a start.</param>
/// <param name="Venue">The venue name, or null — for an online event, one with no place, or
/// one held at an address alone.</param>
/// <param name="Online">Whether the event happens online.</param>
/// <param name="Link">The edition's page, or null.</param>
/// <param name="KeyDates">The dates that matter before the event, in the person's order.</param>
/// <param name="WhatToLearn">What the person wants to learn there, or null.</param>
/// <param name="SeriesId">The event series it is an edition of, or null.</param>
/// <param name="Notes">The Brain and Talk notes the event links, in order.</param>
/// <param name="SkipReason">Why it was skipped, once it is Skipped.</param>
/// <param name="Ticket">Whether the person holds a ticket, or null.</param>
/// <param name="Cost">What going costs, or null.</param>
/// <param name="Tasks">The tasks the event points at, with their purposes.</param>
/// <param name="Verdict">Whether it was worth it, once Attended or Harvested.</param>
/// <param name="UpdatedAt">When a desktop last changed the event, on that desktop's clock: the
/// stamp the later copy wins by.</param>
/// <param name="Removed">Whether the person removed the event: the copy is its tombstone, and
/// every device leaves it out. Absent in a document from an older build, which reads as not
/// removed.</param>
/// <param name="Address">Where an in-person event is held, or null (local ADR 0061). Absent in
/// a document from an older desktop build, which reads as the venue alone. The phone ignores
/// it.</param>
/// <param name="Lookup">What geocoding answered for <paramref name="Address"/>; with an address
/// and none, it reads as not looked up yet.</param>
/// <param name="Point">The spot on the map, exactly when <paramref name="Lookup"/> is Placed.</param>
public sealed record KnowledgeEventReplicaDto(
    Guid Id,
    string Title,
    KnowledgeEventState State,
    EventDateReplicaDto? Start,
    EventDateReplicaDto? End,
    DateSource? DateSource,
    string? Venue,
    bool Online,
    string? Link,
    IReadOnlyList<EventKeyDateReplicaDto> KeyDates,
    string? WhatToLearn,
    Guid? SeriesId,
    IReadOnlyList<EventNoteLinkReplicaDto> Notes,
    string? SkipReason,
    TicketState? Ticket,
    EventCostReplicaDto? Cost,
    IReadOnlyList<EventTaskLinkReplicaDto> Tasks,
    string? Verdict,
    DateTimeOffset UpdatedAt,
    bool Removed = false,
    EventAddressReplicaDto? Address = null,
    AddressLookup? Lookup = null,
    EventMapPointReplicaDto? Point = null);

/// <summary>An event's start or end: a calendar date, plus a time of day and its UTC offset
/// when they are known.</summary>
public sealed record EventDateReplicaDto(DateOnly Date, TimeOnly? Time, TimeSpan? UtcOffset);

/// <summary>Where an in-person event is held, in up to four parts, each null when it has none.</summary>
public sealed record EventAddressReplicaDto(string? Street, string? Postcode, string? Town, string? Country);

/// <summary>A spot on the map, in degrees. Each is nullable so a document that leaves one out
/// reads with it absent rather than as zero, and the copy is refused rather than pinned on the
/// equator or Null Island.</summary>
public sealed record EventMapPointReplicaDto(double? Latitude, double? Longitude);

/// <summary>A date that matters before the event, with its label.</summary>
public sealed record EventKeyDateReplicaDto(DateOnly Date, string Label);

/// <summary>A note the event links: a Brain note by its path from the Brain's root, or a Talk
/// note by its Inbox id, with the note's title as it read when linked.</summary>
public sealed record EventNoteLinkReplicaDto(NoteLinkKind Kind, string Reference, string Title);

/// <summary>A task the event points at, by its id, with its purpose.</summary>
public sealed record EventTaskLinkReplicaDto(string TaskId, TaskPurpose Purpose);

/// <summary>What going costs: one amount and its currency code.</summary>
public sealed record EventCostReplicaDto(decimal Amount, string Currency);

/// <summary>
/// One event series as it travels beside the events that name it — so a phone that holds an
/// event can show its series by name.
/// </summary>
/// <param name="Id">The series' id, the one its editions' <see cref="KnowledgeEventReplicaDto.SeriesId"/> names.</param>
/// <param name="Name">Never empty.</param>
/// <param name="Organiser">Who runs the series, or null.</param>
/// <param name="Link">The series' page, or null.</param>
/// <param name="UpdatedAt">When a desktop last changed it.</param>
public sealed record EventSeriesReplicaDto(Guid Id, string Name, string? Organiser, string? Link, DateTimeOffset UpdatedAt);

/// <summary>
/// The <c>events</c> switch as the phone reads it (local ADR 0050 §6): one per owner, at
/// <see cref="WellKnownId"/>, written by the desktop whose switch last moved. The latest write
/// wins, so the phone honours the latest switch any desktop made. The phone offers its Events
/// screen unless the copy it holds says off; holding none is the area on.
/// </summary>
/// <param name="Id">Always <see cref="WellKnownId"/>.</param>
/// <param name="On">Whether the area is on.</param>
/// <param name="UpdatedAt">When the switch moved, on that desktop's clock.</param>
public sealed record EventsAreaReplicaDto(Guid Id, bool On, DateTimeOffset UpdatedAt)
{
    /// <summary>The one id an owner's switch document has, on a container that is per owner.</summary>
    public static readonly Guid WellKnownId = new("e7e47a5e-0000-4000-8000-0000000000a1");
}
