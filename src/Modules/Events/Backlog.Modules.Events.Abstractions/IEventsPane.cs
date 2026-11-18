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
    /// <summary>Every event, grouped by state: Going, Considering, Spotted, Attended, Harvested,
    /// Skipped. Within a group, events not over yet (ending, or else starting, today or later,
    /// so one still running today counts) come first, soonest start first, then undated ones,
    /// then past ones, most recent first. An empty group is left out.</summary>
    Task<IReadOnlyList<KnowledgeEventGroupDto>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>One event as the editor reads it; <c>knowledge_event.not_found</c> when none has
    /// that id.</summary>
    Task<Result<KnowledgeEventDto>> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Adds a Spotted event with <paramref name="title"/> and, optionally, its series.
    /// Refused for a blank title (<c>knowledge_event.title_missing</c>) and an unknown series.</summary>
    Task<Result<KnowledgeEventDto>> AddAsync(string title, Guid? seriesId = null, CancellationToken cancellationToken = default);

    Task<Result<KnowledgeEventDto>> RenameAsync(Guid id, string title, CancellationToken cancellationToken = default);

    /// <summary>Moves the event forward, skipping states if need be.</summary>
    Task<Result<KnowledgeEventDto>> MoveToAsync(Guid id, KnowledgeEventState target, CancellationToken cancellationToken = default);

    /// <summary>Skips a Spotted or Considering event, for <paramref name="reason"/>.</summary>
    Task<Result<KnowledgeEventDto>> SkipAsync(Guid id, string reason, CancellationToken cancellationToken = default);

    /// <summary>Sets the dates the person typed, with date source Typed, or clears them with a
    /// null start and end.</summary>
    Task<Result<KnowledgeEventDto>> SetDatesAsync(Guid id, EventDateDto? start, EventDateDto? end, CancellationToken cancellationToken = default);

    Task<Result<KnowledgeEventDto>> SetPlaceAsync(Guid id, PlaceDto? place, CancellationToken cancellationToken = default);

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
    /// read again. On the writer's thread; a screen marshals to its own.</summary>
    event Action? Changed;
}
