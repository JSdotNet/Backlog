namespace Backlog.Modules.Events.Abstractions;

/// <summary>Where the event happens: a venue, or online (<c>domain.md#place</c>).</summary>
public sealed record PlaceDto(string? Venue, bool IsOnline);

/// <summary>A date that matters before the event, with its label (<c>domain.md#key-date</c>).</summary>
public sealed record KeyDateDto(DateOnly Date, string Label);

/// <summary>What going costs, as one amount and its currency code (<c>domain.md#cost</c>).</summary>
public sealed record CostDto(decimal Amount, string Currency);

/// <summary>An event series: the organiser's recurring run of events (<c>domain.md#event-series</c>).</summary>
public sealed record EventSeriesDto(Guid Id, string Name, string? Organiser, string? Link);

/// <summary>Another edition of the open event's series, as the editor lists it.</summary>
public sealed record EventEditionDto(Guid Id, string Title, EventDateDto? Start, KnowledgeEventState State);

/// <summary>
/// A note the event links (<c>domain.md#note-link</c>). <paramref name="Title"/> is the Brain
/// note's current title when it is there, else the title stored when it was linked.
/// <paramref name="Missing"/> is true for a Brain note no longer at its path: moved, renamed, or a
/// Brain this desktop cannot read. A Talk note is never missing here; it shows by its stored title.
/// </summary>
public sealed record EventNoteLinkDto(NoteLinkKind Kind, string Reference, string Title, bool Missing);

/// <summary>
/// A task the event links, by id (<c>domain.md#event-task-link</c>). <paramref name="InBacklog"/>
/// is false when this backlog holds no task with that id; title and status are then null.
/// </summary>
public sealed record EventTaskDto(string TaskId, TaskPurpose Purpose, string? Title, string? Status, bool InBacklog);

/// <summary>One event as the pane's list draws it.</summary>
public sealed record KnowledgeEventSummaryDto(
    Guid Id,
    string Title,
    KnowledgeEventState State,
    EventDateDto? Start,
    EventDateDto? End,
    string? SeriesName,
    PlaceDto? Place);

/// <summary>The events in one state, in the order the list draws them.</summary>
public sealed record KnowledgeEventGroupDto(KnowledgeEventState State, IReadOnlyList<KnowledgeEventSummaryDto> Events);

/// <summary>
/// One event as the pane's editor reads it, with its series, its note and task links resolved,
/// the other editions of its series, and the moves its state choice offers.
/// </summary>
public sealed record KnowledgeEventDto(
    Guid Id,
    string Title,
    EventSeriesDto? Series,
    EventDateDto? Start,
    EventDateDto? End,
    DateSource? DateSource,
    PlaceDto? Place,
    string? Link,
    KnowledgeEventState State,
    string? SkipReason,
    string? WhatToLearn,
    IReadOnlyList<KeyDateDto> KeyDates,
    TicketState? Ticket,
    CostDto? Cost,
    IReadOnlyList<EventNoteLinkDto> Notes,
    IReadOnlyList<EventTaskDto> Tasks,
    string? Verdict,
    IReadOnlyList<EventEditionDto> OtherEditions)
{
    /// <summary>What the state choice offers from <see cref="State"/>.</summary>
    public KnowledgeEventMoveOffer Moves => KnowledgeEventMoves.Offered(State);

    /// <summary>Whether a verdict may be written: once the event is Attended or Harvested.</summary>
    public bool VerdictOffered => State is KnowledgeEventState.Attended or KnowledgeEventState.Harvested;
}

/// <summary>A Brain note the note picker offers: its path from the Brain's root and its title.</summary>
public sealed record EventBrainNoteDto(string Path, string Title);

/// <summary>A task as the task target reads it: its title and its status in words.</summary>
public sealed record EventTaskFactsDto(string TaskId, string Title, string Status);
