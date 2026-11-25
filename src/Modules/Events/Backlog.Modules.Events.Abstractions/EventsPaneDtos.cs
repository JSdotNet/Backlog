namespace Backlog.Modules.Events.Abstractions;

/// <summary>
/// Where the event happens: online, or in person with a venue name, an address, or both
/// (<c>domain.md#place</c>). Read back, an address carries its <paramref name="Lookup"/>, and its
/// <paramref name="Point"/> when that lookup placed it.
/// <para>
/// Sent to <see cref="IEventsPane.SetPlaceAsync"/> or in a <see cref="NewKnowledgeEventDto"/>,
/// <paramref name="Lookup"/> and <paramref name="Point"/> are not read: the event decides them.
/// An address that differs from the one held is Not looked up yet and is sent to the geocoder;
/// an unchanged one keeps its lookup and point. Send null for no address, never one whose parts
/// are all blank (<c>address.empty</c>).
/// </para>
/// </summary>
public sealed record PlaceDto(
    string? Venue,
    bool IsOnline,
    AddressDto? Address = null,
    AddressLookup? Lookup = null,
    MapPointDto? Point = null);

/// <summary>An in-person event's address in four optional parts, each trimmed and a blank one
/// read as none (<c>domain.md#address</c>).</summary>
public sealed record AddressDto(string? Street, string? Postcode, string? Town, string? Country);

/// <summary>The spot on the map an address was placed at, in degrees (<c>domain.md#map-point</c>).</summary>
public sealed record MapPointDto(double Latitude, double Longitude);

/// <summary>
/// What Look up now (<see cref="IEventsPane.LookUpAddressAsync"/>) did: the event as it reads
/// afterwards, and the geocoder's <paramref name="Answer"/>. Placed or NotFound is what it
/// answered; NotLookedUpYet means no answer came back (offline, no map key, the call failed), so
/// nothing was decided about the address, whatever lookup the event still holds; null means
/// nothing was sent, because the address was already placed. The event's own lookup never moves
/// back on a missing answer (local ADR 0061 §5), so only <paramref name="Answer"/> tells "could not
/// look it up" from "looked up, still not found".
/// </summary>
public sealed record AddressLookUpDto(KnowledgeEventDto Event, AddressLookup? Answer);

/// <summary>What <see cref="IEventsPane.LookUpPendingAsync"/> got back for the addresses it
/// sent: how many were placed, how many were not found, and how many got no answer and stay Not
/// looked up yet.</summary>
public sealed record AddressLookupPassDto(int Placed, int NotFound, int NotLookedUpYet);

/// <summary>A date that matters before the event, with its label (<c>domain.md#key-date</c>).</summary>
public sealed record KeyDateDto(DateOnly Date, string Label);

/// <summary>What going costs, as one amount and its currency code (<c>domain.md#cost</c>).</summary>
public sealed record CostDto(decimal Amount, string Currency);

/// <summary>
/// A new event with every field the pane's editor sets, added in one save: what
/// <see cref="IEventsPane.AddAsync(NewKnowledgeEventDto, CancellationToken)"/> takes. Only the
/// title is required. <paramref name="SeriesName"/> names a series by its name: one there is,
/// matched trimmed and without regard to case, is joined; an unknown one is started.
/// <paramref name="Start"/> and <paramref name="End"/> are the dates the person typed, so their
/// date source is Typed.
/// </summary>
public sealed record NewKnowledgeEventDto(
    string Title,
    string? SeriesName = null,
    EventDateDto? Start = null,
    EventDateDto? End = null,
    PlaceDto? Place = null,
    string? Link = null,
    string? WhatToLearn = null,
    TicketState? Ticket = null,
    CostDto? Cost = null);

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
/// <param name="MapNumber">Its map number (<c>domain.md#map-number</c>): 1, 2, 3 in list order
/// for an upcoming event with a map point that is not Skipped; null for every other event.
/// Derived each time the list is read, so it changes as events are added or pass.</param>
public sealed record KnowledgeEventSummaryDto(
    Guid Id,
    string Title,
    KnowledgeEventState State,
    EventDateDto? Start,
    EventDateDto? End,
    string? SeriesName,
    PlaceDto? Place,
    int? MapNumber = null);

/// <summary>The events of one date section, in the order the list draws them.</summary>
public sealed record KnowledgeEventSectionDto(EventListSection Section, IReadOnlyList<KnowledgeEventSummaryDto> Events);

/// <summary>
/// One event as the pane's editor reads it, with its series, its note and task links resolved,
/// the other editions of its series, and the moves its state choice offers.
/// </summary>
/// <param name="RemoveOffered">Whether Remove is offered: while the event holds no harvest
/// task. Read off the aggregate, the same rule its <c>Remove()</c> refuses by.</param>
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
    IReadOnlyList<EventEditionDto> OtherEditions,
    bool RemoveOffered)
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
