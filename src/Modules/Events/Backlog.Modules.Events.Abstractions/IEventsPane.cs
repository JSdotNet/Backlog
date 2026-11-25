using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Events.Abstractions;

/// <summary>
/// What the Events pane reads and changes through (<c>.devbook/domain/events/features.md#events-pane</c>,
/// <c>#prep-tasks</c>). Every change loads the event, applies it on the aggregate, saves it, and
/// answers the event as the editor reads it, or the aggregate's refusal with its
/// <see cref="EventsErrors"/> code. A refusal saves nothing.
/// <para>
/// Scoped: it holds the scoped task target. The latest write wins; nothing is locked.
/// </para>
/// </summary>
public interface IEventsPane
{
    /// <summary>Every event, by date and whatever its state, in two sections: Upcoming, the
    /// events not over yet (ending, or else starting, today or later on the desktop clock, so one
    /// still running today counts), soonest start first, then the undated ones; then Past, the
    /// rest, most recent first: the latest last day first, then the latest start. An empty section is left out. Each upcoming event with a map point
    /// that is not Skipped carries its map number, 1..n in that order.</summary>
    Task<IReadOnlyList<KnowledgeEventSectionDto>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>One event as the editor reads it; <c>knowledge_event.not_found</c> when none has
    /// that id.</summary>
    Task<Result<KnowledgeEventDto>> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Adds a Spotted event with <paramref name="title"/> and, optionally, its series.
    /// Refused for a blank title (<c>knowledge_event.title_missing</c>) and an unknown series.</summary>
    Task<Result<KnowledgeEventDto>> AddAsync(string title, Guid? seriesId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a Spotted event with every field <paramref name="request"/> carries, in one save, its
    /// dates with date source Typed: the editor's fields filled at once, as
    /// <c>add_knowledge_event</c> does over MCP. Every rule the aggregate holds refuses with its
    /// own code, and a refusal writes nothing — no event, and no series.
    /// <para>
    /// The series is named by its name. One series with that name, trimmed and without regard to
    /// case, is joined; none starts one with that name, saved only once the event is known to
    /// be valid; two or more are refused (<c>event_series.ambiguous</c>).
    /// </para>
    /// <para>
    /// An address is looked up after the save, as <see cref="SetPlaceAsync"/> does: the answer is
    /// the event as saved, Not looked up yet.
    /// </para>
    /// </summary>
    Task<Result<KnowledgeEventDto>> AddAsync(NewKnowledgeEventDto request, CancellationToken cancellationToken = default);

    Task<Result<KnowledgeEventDto>> RenameAsync(Guid id, string title, CancellationToken cancellationToken = default);

    /// <summary>Moves the event forward, skipping states if need be.</summary>
    Task<Result<KnowledgeEventDto>> MoveToAsync(Guid id, KnowledgeEventState target, CancellationToken cancellationToken = default);

    /// <summary>Skips a Spotted, Considering or Going event, for <paramref name="reason"/>: a
    /// Going event that is called off is skipped too.</summary>
    Task<Result<KnowledgeEventDto>> SkipAsync(Guid id, string reason, CancellationToken cancellationToken = default);

    /// <summary>Removes an event added by mistake, from any state; afterwards neither
    /// <see cref="ListAsync"/> nor <see cref="GetAsync"/> answers it. Refused for an event holding
    /// its harvest task (<c>knowledge_event.has_harvest_task</c>), and for an unknown or removed
    /// one (<c>knowledge_event.not_found</c>). Its tasks and notes stay where they are; the
    /// removal travels to the other devices like any other write.</summary>
    Task<Result> RemoveAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Sets the dates the person typed, with date source Typed, or clears them with a
    /// null start and end.</summary>
    Task<Result<KnowledgeEventDto>> SetDatesAsync(Guid id, EventDateDto? start, EventDateDto? end, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets where the event happens, or clears it with null, and answers the event as saved —
    /// before any geocoder call. A place whose address is new or changed, or still Not looked up
    /// yet, is then sent to the geocoder once, after the save and outside the caller's call: one
    /// lookup at a time per event, its answer kept on the event as it reads by then — Placed with
    /// its point, Address not found, or, with no answer back (offline, no map key, the call
    /// failed), Not looked up yet. <see cref="IsLookingUp"/> is true meanwhile, and the pane
    /// raises <see cref="Changed"/> once the lookup is over, whatever it answered. An unchanged
    /// address that is Placed or not found, an online place, and a venue name alone send nothing
    /// (local ADR 0061 §5). The caller's cancellation token covers the save only.
    /// </summary>
    Task<Result<KnowledgeEventDto>> SetPlaceAsync(Guid id, PlaceDto? place, CancellationToken cancellationToken = default);

    /// <summary>
    /// Look up now: sends the event's address to the geocoder again when it is Not looked up yet
    /// or Address not found, and keeps the answer, after any lookup already running for the
    /// event; this one waits for its answer, as the person asked for it. A Placed address — one a
    /// lookup that was running placed — sends nothing. No answer back
    /// leaves the lookup as it was, and the result's <see cref="AddressLookUpDto.Answer"/> says so
    /// (NotLookedUpYet), apart from a lookup that found nothing (NotFound). Refused for an event
    /// that is online or has no address (<c>address.missing</c>), and for an unknown one
    /// (<c>knowledge_event.not_found</c>).
    /// </summary>
    Task<Result<AddressLookUpDto>> LookUpAddressAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The pass the person's first save of the map key starts
    /// (<c>.devbook/domain/events/domain.md#address-lookup</c>): sends each address Not looked up
    /// yet to the geocoder once, one at a time, and keeps each answer. Placed and not-found
    /// addresses are skipped. Nothing else calls it: there is no background retry.
    /// </summary>
    Task<AddressLookupPassDto> LookUpPendingAsync(CancellationToken cancellationToken = default);

    /// <summary>Whether this pane is looking up the event's address now: a save or Look up now
    /// sent it and its answer is not kept yet.</summary>
    bool IsLookingUp(Guid id);

    Task<Result<KnowledgeEventDto>> SetLinkAsync(Guid id, string? link, CancellationToken cancellationToken = default);

    Task<Result<KnowledgeEventDto>> SetWhatToLearnAsync(Guid id, string? whatToLearn, CancellationToken cancellationToken = default);

    /// <summary>Replaces the key dates, in the order given. Refused whole for a blank label.</summary>
    Task<Result<KnowledgeEventDto>> SetKeyDatesAsync(Guid id, IReadOnlyList<KeyDateDto> keyDates, CancellationToken cancellationToken = default);

    Task<Result<KnowledgeEventDto>> SetTicketAsync(Guid id, TicketState? ticket, CancellationToken cancellationToken = default);

    Task<Result<KnowledgeEventDto>> SetCostAsync(Guid id, CostDto? cost, CancellationToken cancellationToken = default);

    /// <summary>Writes the verdict, or clears it; only once the event is Attended or Harvested.</summary>
    Task<Result<KnowledgeEventDto>> SetVerdictAsync(Guid id, string? verdict, CancellationToken cancellationToken = default);

    /// <summary>Every series, in name order.</summary>
    Task<IReadOnlyList<EventSeriesDto>> ListSeriesAsync(CancellationToken cancellationToken = default);

    /// <summary>Starts a series. Refused for a blank name (<c>event_series.name_missing</c>).</summary>
    Task<Result<EventSeriesDto>> StartSeriesAsync(string name, string? organiser = null, string? link = null, CancellationToken cancellationToken = default);

    /// <summary>Names the event's series, or clears it with null.</summary>
    Task<Result<KnowledgeEventDto>> SetSeriesAsync(Guid id, Guid? seriesId, CancellationToken cancellationToken = default);

    /// <summary>Links a Brain note by its path from the Brain's root. A note linked already
    /// changes nothing. The title is the note's own when the Brain has it, else
    /// <paramref name="title"/>, else the file name.</summary>
    Task<Result<KnowledgeEventDto>> LinkBrainNoteAsync(Guid id, string path, string? title = null, CancellationToken cancellationToken = default);

    /// <summary>Points the link at <paramref name="oldPath"/> to the note at
    /// <paramref name="newPath"/>, in one save: how a missing note is linked again.</summary>
    Task<Result<KnowledgeEventDto>> RelinkBrainNoteAsync(Guid id, string oldPath, string newPath, string? title = null, CancellationToken cancellationToken = default);

    /// <summary>Removes a note link; one the event does not hold changes nothing.</summary>
    Task<Result<KnowledgeEventDto>> UnlinkNoteAsync(Guid id, NoteLinkKind kind, string reference, CancellationToken cancellationToken = default);

    /// <summary>Files a task with <paramref name="title"/> alone, under the Brain scope, and links
    /// it as a prep task. A blank title is refused before anything is filed; a refused filing
    /// links nothing.</summary>
    Task<Result<KnowledgeEventDto>> AddPrepTaskAsync(Guid id, string title, CancellationToken cancellationToken = default);

    /// <summary>Links a task this backlog holds, by id, as purpose Linked. Writes nothing to the
    /// task. Refused for an id the backlog does not hold (<c>event_task.not_in_backlog</c>).</summary>
    Task<Result<KnowledgeEventDto>> LinkExistingTaskAsync(Guid id, string taskId, CancellationToken cancellationToken = default);

    /// <summary>The Brain notes the note picker offers.</summary>
    Task<IReadOnlyList<EventBrainNoteDto>> BrainNoteCandidatesAsync(CancellationToken cancellationToken = default);

    /// <summary>Why no Brain note can be linked right now, in words; null while one can.</summary>
    string? BrainUnavailableReason { get; }

    /// <summary>Raised when a task or the Brain changed underneath, so an open event's chips are
    /// read again, and when an event or a series was saved anywhere but through this instance —
    /// another window's pane, the MCP server, the To Event route, the Harvest Task Policy's
    /// worker — so an open list shows it. This instance's own saves are not raised: their answer
    /// is the event as saved. A copy pulled from another device is not raised either. On the
    /// writer's thread; a screen marshals to its own.</summary>
    event Action? Changed;
}
