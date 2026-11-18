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
/// <param name="Venue">The venue, or null — for an online event, or one with no place.</param>
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
    DateTimeOffset UpdatedAt);

/// <summary>An event's start or end: a calendar date, plus a time of day and its UTC offset
/// when they are known.</summary>
public sealed record EventDateReplicaDto(DateOnly Date, TimeOnly? Time, TimeSpan? UtcOffset);

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
