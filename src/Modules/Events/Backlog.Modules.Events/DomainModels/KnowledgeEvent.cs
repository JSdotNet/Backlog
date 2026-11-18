using Backlog.Modules.Events.Abstractions;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Events.DomainModels;

/// <summary>
/// One edition of something the person attends to learn: a conference, a meetup, a webinar, a
/// workshop (<c>.devbook/domain/events/domain.md#knowledge-event</c>, local ADR 0050). The
/// consistency boundary for its decision and its harvest.
/// <para>
/// Its rules are <c>.devbook/domain/events/domain.invariants.md#knowledge-event</c>, each with a
/// unit test in <c>Backlog.Modules.Events.UnitTests</c>.
/// </para>
/// </summary>
public sealed class KnowledgeEvent
{
    private readonly List<KeyDate> _keyDates;
    private readonly List<NoteLink> _notes;
    private readonly List<EventTaskLink> _tasks;

    private KnowledgeEvent(Guid id, string title, List<KeyDate> keyDates, List<NoteLink> notes, List<EventTaskLink> tasks)
    {
        Id = id;
        Title = title;
        _keyDates = keyDates;
        _notes = notes;
        _tasks = tasks;
    }

    public Guid Id { get; }

    /// <summary>The only field a new event needs; never empty.</summary>
    public string Title { get; private set; }

    /// <summary>The Event Series it is an edition of, by id, or null.</summary>
    public Guid? Series { get; private set; }

    public EventDate? Start { get; private set; }

    /// <summary>The last day, or null: an event with a start and no end lasts its start day.</summary>
    public EventDate? End { get; private set; }

    /// <summary>Where the dates came from; set exactly when there is a start.</summary>
    public DateSource? DateSource { get; private set; }

    public Place? Place { get; private set; }

    /// <summary>The edition's page, or null.</summary>
    public string? Link { get; private set; }

    public KnowledgeEventState State { get; private set; } = KnowledgeEventState.Spotted;

    /// <summary>Why it was skipped; set once it is Skipped.</summary>
    public SkipReason? SkipReason { get; private set; }

    /// <summary>What the person wants to learn there, in free text, or null.</summary>
    public string? WhatToLearn { get; private set; }

    public IReadOnlyList<KeyDate> KeyDates => _keyDates;

    public TicketState? Ticket { get; private set; }

    public Cost? Cost { get; private set; }

    public IReadOnlyList<NoteLink> Notes => _notes;

    public IReadOnlyList<EventTaskLink> Tasks => _tasks;

    /// <summary>Whether it was worth it; only once it is Attended or Harvested.</summary>
    public Verdict? Verdict { get; private set; }

    /// <summary>A new Spotted event with only a title, trimmed. Refused for a blank title
    /// (<c>knowledge_event.title_missing</c>).</summary>
    public static Result<KnowledgeEvent> Create(Guid id, string title)
    {
        var trimmed = title?.Trim() ?? string.Empty;
        if (trimmed.Length == 0) return EventsErrors.TitleMissing();

        return new KnowledgeEvent(id, trimmed, [], [], []);
    }

    /// <summary>
    /// Rehydration from storage. Trusted, as every stored aggregate is: the rules held when it
    /// was written, so none is checked again. Blank texts read as absent.
    /// </summary>
    public static KnowledgeEvent Restore(
        Guid id,
        string title,
        Guid? series,
        EventDate? start,
        EventDate? end,
        DateSource? dateSource,
        Place? place,
        string? link,
        KnowledgeEventState state,
        string? skipReason,
        string? whatToLearn,
        IEnumerable<KeyDate> keyDates,
        TicketState? ticket,
        Cost? cost,
        IEnumerable<NoteLink> notes,
        IEnumerable<EventTaskLink> tasks,
        string? verdict)
    {
        ArgumentNullException.ThrowIfNull(keyDates);
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(tasks);

        return new KnowledgeEvent(id, title, [.. keyDates], [.. notes], [.. tasks])
        {
            Series = series,
            Start = start,
            End = end,
            DateSource = dateSource,
            Place = place,
            Link = Blank(link),
            State = state,
            SkipReason = Blank(skipReason) is { } reason ? new SkipReason(reason) : null,
            WhatToLearn = Blank(whatToLearn),
            Ticket = ticket,
            Cost = cost,
            Verdict = Blank(verdict) is { } text ? new Verdict(text) : null
        };
    }

    /// <summary>Names the Event Series this is an edition of, or clears it.</summary>
    public void SetSeries(Guid? series) => Series = series;

    /// <summary>Sets where the event happens, or clears it.</summary>
    public void SetPlace(Place? place) => Place = place;

    /// <summary>Sets the edition's page, trimmed; a blank one clears it.</summary>
    public void SetLink(string? link) => Link = Blank(link);

    /// <summary>Sets what the person wants to learn, trimmed; a blank one clears it.</summary>
    public void SetWhatToLearn(string? whatToLearn) => WhatToLearn = Blank(whatToLearn);

    /// <summary>Replaces the key dates, in the order given; none clears them.</summary>
    public void SetKeyDates(IEnumerable<KeyDate> keyDates)
    {
        ArgumentNullException.ThrowIfNull(keyDates);

        var replacement = keyDates.ToList();
        if (replacement.Exists(keyDate => keyDate is null)) throw new ArgumentException("A key date list holds no null.", nameof(keyDates));

        _keyDates.Clear();
        _keyDates.AddRange(replacement);
    }

    /// <summary>Sets whether the person holds a ticket, or clears it.</summary>
    public void SetTicket(TicketState? ticket)
    {
        if (ticket is { } state && !Enum.IsDefined(state))
            throw new ArgumentOutOfRangeException(nameof(ticket), ticket, "A ticket is not bought, bought, or not needed.");

        Ticket = ticket;
    }

    /// <summary>Sets what going costs, or clears it.</summary>
    public void SetCost(Cost? cost) => Cost = cost;

    /// <summary>Changes the title, trimmed. Refused for a blank one
    /// (<c>knowledge_event.title_missing</c>).</summary>
    public Result Rename(string title)
    {
        var trimmed = title?.Trim() ?? string.Empty;
        if (trimmed.Length == 0) return Result.Failure(EventsErrors.TitleMissing());

        Title = trimmed;
        return Result.Success();
    }

    /// <summary>
    /// Sets the start, the end and where they came from, or clears all three. Refused, in this
    /// order, for an end with no start or an end on an earlier calendar day than the start
    /// (<c>knowledge_event.dates_out_of_order</c>) — times on the same day are not compared — and
    /// for a start with no source or a source with no dates
    /// (<c>knowledge_event.date_source_mismatch</c>).
    /// </summary>
    public Result SetDates(EventDate? start, EventDate? end, DateSource? source)
    {
        if (source is { } named && !Enum.IsDefined(named))
            throw new ArgumentOutOfRangeException(nameof(source), source, "Dates are typed or read from the event page.");

        if (end is not null && (start is null || end.Date < start.Date))
            return Result.Failure(EventsErrors.DatesOutOfOrder());

        if ((start is null) != (source is null))
            return Result.Failure(EventsErrors.DateSourceMismatch());

        Start = start;
        End = end;
        DateSource = source;
        return Result.Success();
    }

    /// <summary>
    /// Moves the event to a later state on the path Spotted, Considering, Going, Attended,
    /// Harvested, skipping states on the way if need be. Refused from Harvested or Skipped
    /// (<c>knowledge_event.state_final</c>), and for the same or an earlier state
    /// (<c>knowledge_event.state_backward</c>). Skipped is no later state on that path:
    /// <see cref="Skip"/> is the only way in.
    /// </summary>
    public Result MoveTo(KnowledgeEventState target)
    {
        if (!Enum.IsDefined(target))
            throw new ArgumentOutOfRangeException(nameof(target), target, "A knowledge event moves to one of its six states.");

        if (IsFinal(State)) return Result.Failure(EventsErrors.StateFinal(State));

        if (PathPosition(target) is not { } to || to <= PathPosition(State))
            return Result.Failure(EventsErrors.StateBackward(State, target));

        State = target;
        return Result.Success();
    }

    /// <summary>
    /// Decides against the event, for <paramref name="reason"/>, trimmed. Refused from Harvested
    /// or Skipped (<c>knowledge_event.state_final</c>), from Going or Attended
    /// (<c>knowledge_event.cannot_skip</c>), and for a blank reason
    /// (<c>knowledge_event.skip_reason_missing</c>). A refusal leaves the event as it was.
    /// </summary>
    public Result Skip(string reason)
    {
        if (IsFinal(State)) return Result.Failure(EventsErrors.StateFinal(State));

        if (State is not (KnowledgeEventState.Spotted or KnowledgeEventState.Considering))
            return Result.Failure(EventsErrors.CannotSkip(State));

        var trimmed = reason?.Trim() ?? string.Empty;
        if (trimmed.Length == 0) return Result.Failure(EventsErrors.SkipReasonMissing());

        State = KnowledgeEventState.Skipped;
        SkipReason = new SkipReason(trimmed);
        return Result.Success();
    }

    /// <summary>
    /// Writes whether the event was worth it, trimmed, or clears it for a blank one. Refused, a
    /// clear included, until the event is Attended or Harvested
    /// (<c>knowledge_event.verdict_too_early</c>).
    /// </summary>
    public Result SetVerdict(string? verdict)
    {
        if (State is not (KnowledgeEventState.Attended or KnowledgeEventState.Harvested))
            return Result.Failure(EventsErrors.VerdictTooEarly(State));

        Verdict = string.IsNullOrWhiteSpace(verdict) ? null : new Verdict(verdict.Trim());
        return Result.Success();
    }

    /// <summary>Links <paramref name="taskId"/> as the event's one harvest task. The same task
    /// again changes nothing; a different one while a harvest task is linked is refused
    /// (<c>knowledge_event.harvest_task_exists</c>).</summary>
    public Result LinkHarvestTask(string taskId) => LinkTask(EventTaskLink.Create(taskId, TaskPurpose.Harvest));

    /// <summary>
    /// Links a task. A task already linked, by id, is held once: linking it again changes
    /// nothing and succeeds, which is what makes linking twice harmless — except that a task
    /// held for another purpose becomes the harvest task when it is linked as one and the event
    /// holds none yet. A second harvest task is refused
    /// (<c>knowledge_event.harvest_task_exists</c>).
    /// </summary>
    public Result LinkTask(EventTaskLink link)
    {
        ArgumentNullException.ThrowIfNull(link);

        var held = _tasks.FindIndex(task => task.Equals(link));
        if (held >= 0 && (link.Purpose != TaskPurpose.Harvest || _tasks[held].Purpose == TaskPurpose.Harvest))
            return Result.Success();

        if (link.Purpose == TaskPurpose.Harvest && _tasks.Exists(task => task.Purpose == TaskPurpose.Harvest))
            return Result.Failure(EventsErrors.HarvestTaskExists());

        if (held >= 0) _tasks[held] = link;
        else _tasks.Add(link);
        return Result.Success();
    }

    /// <summary>Links a note. A note already linked — the same kind and reference, whatever its
    /// title — changes nothing, so applying an attachment request twice is harmless.</summary>
    public Result LinkNote(NoteLink link)
    {
        ArgumentNullException.ThrowIfNull(link);

        if (!_notes.Contains(link)) _notes.Add(link);
        return Result.Success();
    }

    /// <summary>
    /// Points a link at another note, in the same place in the list: how a Brain note that
    /// moved is linked again. A replacement the event already links is kept once, so the old
    /// link just goes. Refused when the event does not link <paramref name="linked"/>
    /// (<c>note_link.not_linked</c>).
    /// </summary>
    public Result ReplaceNoteLink(NoteLink linked, NoteLink replacement)
    {
        ArgumentNullException.ThrowIfNull(linked);
        ArgumentNullException.ThrowIfNull(replacement);

        var at = _notes.IndexOf(linked);
        if (at < 0) return Result.Failure(EventsErrors.NoteNotLinked());

        var held = _notes.IndexOf(replacement);
        if (held >= 0 && held != at) _notes.RemoveAt(at);
        else _notes[at] = replacement;

        return Result.Success();
    }

    /// <summary>Removes a note link. A note the event does not link changes nothing.</summary>
    public void UnlinkNote(NoteLink link)
    {
        ArgumentNullException.ThrowIfNull(link);

        _notes.Remove(link);
    }

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static bool IsFinal(KnowledgeEventState state) =>
        state is KnowledgeEventState.Harvested or KnowledgeEventState.Skipped;

    /// <summary>Where a state sits on the forward path, or null for Skipped, which is off it.</summary>
    private static int? PathPosition(KnowledgeEventState state) => state switch
    {
        KnowledgeEventState.Spotted => 0,
        KnowledgeEventState.Considering => 1,
        KnowledgeEventState.Going => 2,
        KnowledgeEventState.Attended => 3,
        KnowledgeEventState.Harvested => 4,
        KnowledgeEventState.Skipped => null
    };
}
