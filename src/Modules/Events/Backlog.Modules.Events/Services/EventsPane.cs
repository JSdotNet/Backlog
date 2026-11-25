using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.DomainModels;
using Backlog.SharedKernel.Changes;
using Backlog.SharedKernel.Results;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Modules.Events.Services;

/// <summary>
/// <see cref="IEventsPane"/> over the two repositories, the task target and the Brain notes.
/// Each change is one load, one aggregate call and one save; the aggregate holds every rule.
/// </summary>
internal sealed class EventsPane(
    IKnowledgeEventRepository events,
    IEventSeriesRepository series,
    IEventTaskTarget tasks,
    IEventBrainNotes brain,
    HarvestTaskPolicy harvest,
    TimeProvider clock,
    KnowledgeEventSaved? saved = null,
    ILogger<EventsPane>? log = null) : IEventsPane
{
    /// <summary>The order the list draws its groups in: decided, upcoming work first.</summary>
    private static readonly KnowledgeEventState[] GroupOrder =
    [
        KnowledgeEventState.Going,
        KnowledgeEventState.Considering,
        KnowledgeEventState.Spotted,
        KnowledgeEventState.Attended,
        KnowledgeEventState.Harvested,
        KnowledgeEventState.Skipped
    ];

    /// <summary>
    /// The write running on this flow: the self-write mark. Begun at the top of each write method,
    /// so it reaches <see cref="KnowledgeEventSaved"/>'s raise, and the policy the move into
    /// Harvested calls, and ended when the method returns. A save this pane made is then not
    /// heard back as one made elsewhere (<see cref="OnSaved"/>), while every other pane still
    /// hears it.
    /// <para>
    /// Work the save starts carries the flow's execution context, and with it the mark: the sync
    /// loop's settle timer, a <c>Task.Run</c>. So the mark holds the pane's identity token rather
    /// than the pane, which keeps no window's pane alive, and it stops counting once its write
    /// is over, so a save announced from that later work is heard as made elsewhere.
    /// </para>
    /// </summary>
    private static readonly AsyncLocal<WriteMark?> Writing = new();

    private readonly ILogger _log = log ?? NullLogger<EventsPane>.Instance;

    private readonly Lock _gate = new();

    /// <summary>This pane's identity in a <see cref="WriteMark"/>; a pane is one per window scope.</summary>
    private readonly object _identity = new();

    /// <summary>The <see cref="Changed"/> handlers that hear saves made elsewhere; subscribed to
    /// the singleton notice only while there is one, so a disposed window's pane is not kept.</summary>
    private Action? _savedElsewhere;

    public string? BrainUnavailableReason => brain.UnavailableReason;

    // A task or the Brain changing underneath, and an event or a series saved anywhere but here:
    // another window, the MCP server, the To Event route, the Harvest Task Policy's worker.
    public event Action? Changed
    {
        add
        {
            tasks.Changed += value;
            brain.Changed += value;
            lock (_gate)
            {
                if (_savedElsewhere is null && saved is not null) saved.Changed += OnSaved;
                _savedElsewhere += value;
            }
        }
        remove
        {
            tasks.Changed -= value;
            brain.Changed -= value;
            lock (_gate)
            {
                _savedElsewhere -= value;
                if (_savedElsewhere is null && saved is not null) saved.Changed -= OnSaved;
            }
        }
    }

    // Runs on the writer's call, inside its save: each window's handler on its own, so one that
    // throws neither stops the others nor fails a save that already landed.
    private void OnSaved()
    {
        if (Writing.Value is { IsOver: false } mark && ReferenceEquals(mark.Owner, _identity)) return;
        if (Volatile.Read(ref _savedElsewhere) is not { } handlers) return;

        foreach (var handler in handlers.GetInvocationList().Cast<Action>())
        {
            try
            {
                handler();
            }
#pragma warning disable CA1031 // A window's handler is not the writer's failure; it is logged and the rest still hear.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                _log.LogWarning(exception, "An Events pane handler failed on an event saved elsewhere.");
            }
        }
    }

    /// <summary>Announces a save on the way out of a failed write. A subscriber that throws here
    /// is logged, so the store's failure stays the one the caller sees.</summary>
    private void AnnounceBestEffort()
    {
        try
        {
            saved?.Raise();
        }
#pragma warning disable CA1031 // The write's own failure is rethrown; this one must not replace it.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            _log.LogWarning(exception, "Announcing an event series saved before a failed event save failed.");
        }
    }

    /// <summary>Marks the calling write as this pane's own until the returned mark is disposed.
    /// Called only from async methods, whose end also restores the flow's mark.</summary>
    private WriteMark BeginWrite()
    {
        // The pane's write is the person's change set (local ADR 0061 §2), unless an MCP
        // call already named an agent's. Set here, in a synchronous method, so the caller's
        // flow carries it until the mark is disposed.
        var mark = new WriteMark(_identity, ChangeContext.BeginIfNone(ChangeActor.Person, "Events pane edit"));
        Writing.Value = mark;
        return mark;
    }

    /// <summary>One write's self-write mark: whose it is, and whether the write is over.</summary>
    private sealed class WriteMark(object owner, ChangeContext.Scope change) : IDisposable
    {
        private volatile bool _over;

        public object Owner { get; } = owner;

        public bool IsOver => _over;

        public void Dispose()
        {
            _over = true;
            change.Dispose();
        }
    }

    public async Task<IReadOnlyList<KnowledgeEventGroupDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var all = await events.ListAsync(cancellationToken).ConfigureAwait(false);
        var names = (await series.ListAsync(cancellationToken).ConfigureAwait(false)).ToDictionary(s => s.Id, s => s.Name);
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

        return
        [
            .. GroupOrder
                .Select(state => new KnowledgeEventGroupDto(state, [.. Ordered(all.Where(e => e.State == state), today).Select(e => Summary(e, names))]))
                .Where(group => group.Events.Count > 0)
        ];
    }

    public async Task<Result<KnowledgeEventDto>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (await events.GetAsync(id, cancellationToken).ConfigureAwait(false) is not { } found)
            return EventsErrors.KnowledgeEventNotFound(id);

        return await ReadAsync(found, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<KnowledgeEventDto>> AddAsync(string title, Guid? seriesId = null, CancellationToken cancellationToken = default)
    {
        using var mark = BeginWrite();
        if (seriesId is { } named && await series.GetAsync(named, cancellationToken).ConfigureAwait(false) is null)
            return EventsErrors.SeriesNotFound(named);

        var created = KnowledgeEvent.Create(Guid.CreateVersion7(clock.GetUtcNow()), title);
        if (created is not Ok<KnowledgeEvent>(var added)) return (Error)created.Value!;

        added.SetSeries(seriesId);
        await events.SaveAsync(added, cancellationToken).ConfigureAwait(false);
        saved?.Raise();
        return await ReadAsync(added, cancellationToken).ConfigureAwait(false);
    }

    // Built whole on the aggregate before anything is saved, so a field the rules refuse leaves
    // no half-filled event behind, and a series its name would start is saved only once the
    // event holds. One announcement for the lot: the sync loop pushes whatever changed.
    public async Task<Result<KnowledgeEventDto>> AddAsync(NewKnowledgeEventDto request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var mark = BeginWrite();

        var created = KnowledgeEvent.Create(Guid.CreateVersion7(clock.GetUtcNow()), request.Title);
        if (created is not Ok<KnowledgeEvent>(var added)) return (Error)created.Value!;

        if (Fill(added, request) is Error filledError) return filledError;

        var joined = await SeriesNamedAsync(request.SeriesName, cancellationToken).ConfigureAwait(false);
        if (joined is not Ok<(EventSeries Series, bool IsNew)?>(var seriesNamed)) return (Error)joined.Value!;

        // The stores have no transaction and a series no delete, so a series started here stays
        // if the event's save then fails. It is announced either way, so the sync loop pushes it
        // within seconds rather than on its next tick.
        var startedSeries = false;
        try
        {
            if (seriesNamed is { } named)
            {
                if (named.IsNew)
                {
                    await series.SaveAsync(named.Series, cancellationToken).ConfigureAwait(false);
                    startedSeries = true;
                }

                added.SetSeries(named.Series.Id);
            }

            await events.SaveAsync(added, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (startedSeries) AnnounceBestEffort();
            throw;
        }

        saved?.Raise();
        return await ReadAsync(added, cancellationToken).ConfigureAwait(false);
    }

    public Task<Result<KnowledgeEventDto>> RenameAsync(Guid id, string title, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, e => e.Rename(title), cancellationToken);

    // Into Harvested, the harvest task is completed after the save and before the read, so the
    // move holds whatever Tasks answers and the event reads its task as Done. The policy is best
    // effort: a completion that fails is not the move's failure.
    public Task<Result<KnowledgeEventDto>> MoveToAsync(Guid id, KnowledgeEventState target, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, e => e.MoveTo(target), cancellationToken,
            target == KnowledgeEventState.Harvested ? e => harvest.OnHarvestedAsync(e, cancellationToken) : null);

    public Task<Result<KnowledgeEventDto>> SkipAsync(Guid id, string reason, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, e => e.Skip(reason), cancellationToken);

    public async Task<Result> RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var mark = BeginWrite();
        if (await events.GetAsync(id, cancellationToken).ConfigureAwait(false) is not { } found)
            return Result.Failure(EventsErrors.KnowledgeEventNotFound(id));

        var removed = found.Remove();
        if (removed is Error) return removed;

        await events.SaveAsync(found, cancellationToken).ConfigureAwait(false);
        saved?.Raise();
        return Result.Success();
    }

    public Task<Result<KnowledgeEventDto>> SetDatesAsync(Guid id, EventDateDto? start, EventDateDto? end, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, e => ApplyTypedDates(e, start, end), cancellationToken);

    public Task<Result<KnowledgeEventDto>> SetPlaceAsync(Guid id, PlaceDto? place, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, e => ApplyPlace(e, place), cancellationToken);

    public Task<Result<KnowledgeEventDto>> SetLinkAsync(Guid id, string? link, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, e =>
        {
            e.SetLink(link);
            return Result.Success();
        }, cancellationToken);

    public Task<Result<KnowledgeEventDto>> SetWhatToLearnAsync(Guid id, string? whatToLearn, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, e =>
        {
            e.SetWhatToLearn(whatToLearn);
            return Result.Success();
        }, cancellationToken);

    public Task<Result<KnowledgeEventDto>> SetKeyDatesAsync(Guid id, IReadOnlyList<KeyDateDto> keyDates, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keyDates);

        return ChangeAsync(id, e =>
        {
            var made = new List<KeyDate>(keyDates.Count);
            foreach (var keyDate in keyDates)
            {
                var one = KeyDate.Create(keyDate.Date, keyDate.Label);
                if (one is not Ok<KeyDate>(var madeOne)) return (Error)one.Value!;
                made.Add(madeOne);
            }

            e.SetKeyDates(made);
            return Result.Success();
        }, cancellationToken);
    }

    public Task<Result<KnowledgeEventDto>> SetTicketAsync(Guid id, TicketState? ticket, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, e =>
        {
            e.SetTicket(ticket);
            return Result.Success();
        }, cancellationToken);

    public Task<Result<KnowledgeEventDto>> SetCostAsync(Guid id, CostDto? cost, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, e => ApplyCost(e, cost), cancellationToken);

    public Task<Result<KnowledgeEventDto>> SetVerdictAsync(Guid id, string? verdict, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, e => e.SetVerdict(verdict), cancellationToken);

    public async Task<IReadOnlyList<EventSeriesDto>> ListSeriesAsync(CancellationToken cancellationToken = default) =>
        [
            .. (await series.ListAsync(cancellationToken).ConfigureAwait(false))
                .OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(SeriesDto)
        ];

    public async Task<Result<EventSeriesDto>> StartSeriesAsync(string name, string? organiser = null, string? link = null, CancellationToken cancellationToken = default)
    {
        using var mark = BeginWrite();
        var created = EventSeries.Create(Guid.CreateVersion7(clock.GetUtcNow()), name, organiser, link);
        if (created is not Ok<EventSeries>(var started)) return (Error)created.Value!;

        await series.SaveAsync(started, cancellationToken).ConfigureAwait(false);
        saved?.Raise();
        return SeriesDto(started);
    }

    public async Task<Result<KnowledgeEventDto>> SetSeriesAsync(Guid id, Guid? seriesId, CancellationToken cancellationToken = default)
    {
        if (seriesId is { } named && await series.GetAsync(named, cancellationToken).ConfigureAwait(false) is null)
            return EventsErrors.SeriesNotFound(named);

        return await ChangeAsync(id, e =>
        {
            e.SetSeries(seriesId);
            return Result.Success();
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<KnowledgeEventDto>> LinkBrainNoteAsync(Guid id, string path, string? title = null, CancellationToken cancellationToken = default)
    {
        if (NotePath(path) is not { } normalized) return EventsErrors.NotePathMissing();

        var link = await BrainLinkAsync(normalized, title, cancellationToken).ConfigureAwait(false);
        return await ChangeAsync(id, e => e.LinkNote(link), cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<KnowledgeEventDto>> RelinkBrainNoteAsync(Guid id, string oldPath, string newPath, string? title = null, CancellationToken cancellationToken = default)
    {
        if (NotePath(oldPath) is not { } from || NotePath(newPath) is not { } to) return EventsErrors.NotePathMissing();

        var replacement = await BrainLinkAsync(to, title, cancellationToken).ConfigureAwait(false);
        return await ChangeAsync(id, e => e.ReplaceNoteLink(NoteLink.Create(NoteLinkKind.Brain, from, null), replacement), cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<Result<KnowledgeEventDto>> UnlinkNoteAsync(Guid id, NoteLinkKind kind, string reference, CancellationToken cancellationToken = default)
    {
        var named = kind == NoteLinkKind.Brain ? NotePath(reference) : reference?.Trim();
        if (string.IsNullOrEmpty(named)) return Task.FromResult<Result<KnowledgeEventDto>>(EventsErrors.NotePathMissing());

        return ChangeAsync(id, e =>
        {
            e.UnlinkNote(NoteLink.Create(kind, named, null));
            return Result.Success();
        }, cancellationToken);
    }

    public async Task<Result<KnowledgeEventDto>> AddPrepTaskAsync(Guid id, string title, CancellationToken cancellationToken = default)
    {
        var trimmed = title?.Trim() ?? string.Empty;
        if (trimmed.Length == 0) return EventsErrors.PrepTaskTitleMissing();

        // Loaded first, so a task is never filed for an event that is not there.
        if (await events.GetAsync(id, cancellationToken).ConfigureAwait(false) is null)
            return EventsErrors.KnowledgeEventNotFound(id);

        // The title alone: the task never names the event (ADR 0050 §3).
        var filed = await tasks.FileAsync(trimmed, cancellationToken).ConfigureAwait(false);
        if (filed is not Ok<string>(var taskId)) return (Error)filed.Value!;

        // Loaded again: filing waits on Tasks, and a change saved elsewhere meanwhile is kept.
        return await ChangeAsync(id, e => e.LinkTask(EventTaskLink.Create(taskId, TaskPurpose.Prep)), cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<KnowledgeEventDto>> LinkExistingTaskAsync(Guid id, string taskId, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(taskId?.Trim(), out var parsed) || parsed == Guid.Empty) return EventsErrors.TaskNotInBacklog();

        var normalized = parsed.ToString("D");
        var held = await tasks.LookupAsync([normalized], cancellationToken).ConfigureAwait(false);
        if (!held.ContainsKey(normalized)) return EventsErrors.TaskNotInBacklog();

        return await ChangeAsync(id, e => e.LinkTask(EventTaskLink.Create(normalized, TaskPurpose.Linked)), cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<EventBrainNoteDto>> BrainNoteCandidatesAsync(CancellationToken cancellationToken = default) =>
        brain.CandidatesAsync(cancellationToken);

    private async Task<Result<KnowledgeEventDto>> ChangeAsync(Guid id, Func<KnowledgeEvent, Result> change, CancellationToken cancellationToken, Func<KnowledgeEvent, Task>? afterSave = null)
    {
        if (await events.GetAsync(id, cancellationToken).ConfigureAwait(false) is not { } found)
            return EventsErrors.KnowledgeEventNotFound(id);

        return await SaveAsync(found, change(found), cancellationToken, afterSave).ConfigureAwait(false);
    }

    private async Task<Result<KnowledgeEventDto>> SaveAsync(KnowledgeEvent changed, Result outcome, CancellationToken cancellationToken, Func<KnowledgeEvent, Task>? afterSave = null)
    {
        if (outcome is Error error) return error;

        using var mark = BeginWrite();

        await events.SaveAsync(changed, cancellationToken).ConfigureAwait(false);
        saved?.Raise();
        if (afterSave is not null) await afterSave(changed).ConfigureAwait(false);
        return await ReadAsync(changed, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The link to the Brain note at <paramref name="path"/>, titled as the Brain has
    /// it, else as given, else by its file name.</summary>
    private async Task<NoteLink> BrainLinkAsync(string path, string? title, CancellationToken cancellationToken)
    {
        var found = await brain.ResolveAsync([path], cancellationToken).ConfigureAwait(false);
        var named = found.TryGetValue(path, out var note) && note.Title.Length > 0
            ? note.Title
            : string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(path) : title;

        return NoteLink.Create(NoteLinkKind.Brain, path, named);
    }

    private async Task<KnowledgeEventDto> ReadAsync(KnowledgeEvent e, CancellationToken cancellationToken)
    {
        var edition = e.Series is { } seriesId ? await series.GetAsync(seriesId, cancellationToken).ConfigureAwait(false) : null;
        var others = edition is null
            ? []
            : (await events.ListAsync(cancellationToken).ConfigureAwait(false))
                .Where(other => other.Series == edition.Id && other.Id != e.Id)
                .OrderBy(other => other.Start is null)
                .ThenBy(other => other.Start?.Date)
                .Select(other => new EventEditionDto(other.Id, other.Title, Day(other.Start), other.State))
                .ToList();

        var brainPaths = e.Notes.Where(note => note.Kind == NoteLinkKind.Brain).Select(note => note.Reference).ToList();
        var notes = brainPaths.Count == 0
            ? new Dictionary<string, EventBrainNoteDto>()
            : await brain.ResolveAsync(brainPaths, cancellationToken).ConfigureAwait(false);

        var taskIds = e.Tasks.Select(task => task.TaskId).ToList();
        var facts = taskIds.Count == 0
            ? new Dictionary<string, EventTaskFactsDto>()
            : await tasks.LookupAsync(taskIds, cancellationToken).ConfigureAwait(false);

        return new KnowledgeEventDto(
            e.Id,
            e.Title,
            edition is null ? null : SeriesDto(edition),
            Day(e.Start),
            Day(e.End),
            e.DateSource,
            e.Place is { } place ? new PlaceDto(place.Venue, place.IsOnline) : null,
            e.Link,
            e.State,
            e.SkipReason?.Text,
            e.WhatToLearn,
            [.. e.KeyDates.Select(keyDate => new KeyDateDto(keyDate.Date, keyDate.Label))],
            e.Ticket,
            e.Cost is { } cost ? new CostDto(cost.Amount, cost.Currency) : null,
            [.. e.Notes.Select(note => NoteDto(note, notes))],
            [.. e.Tasks.Select(task => TaskDto(task, facts))],
            e.Verdict?.Text,
            others,
            RemoveOffered: !e.HoldsHarvestTask);
    }

    private static EventNoteLinkDto NoteDto(NoteLink note, IReadOnlyDictionary<string, EventBrainNoteDto> found)
    {
        if (note.Kind != NoteLinkKind.Brain) return new EventNoteLinkDto(note.Kind, note.Reference, note.Title, Missing: false);

        return found.TryGetValue(note.Reference, out var there)
            ? new EventNoteLinkDto(note.Kind, note.Reference, there.Title.Length > 0 ? there.Title : note.Title, Missing: false)
            : new EventNoteLinkDto(note.Kind, note.Reference, note.Title, Missing: true);
    }

    private static EventTaskDto TaskDto(EventTaskLink task, IReadOnlyDictionary<string, EventTaskFactsDto> facts) =>
        facts.TryGetValue(task.TaskId, out var held)
            ? new EventTaskDto(task.TaskId, task.Purpose, held.Title, held.Status, InBacklog: true)
            : new EventTaskDto(task.TaskId, task.Purpose, null, null, InBacklog: false);

    private static KnowledgeEventSummaryDto Summary(KnowledgeEvent e, IReadOnlyDictionary<Guid, string> seriesNames) =>
        new(
            e.Id,
            e.Title,
            e.State,
            Day(e.Start),
            Day(e.End),
            e.Series is { } id && seriesNames.TryGetValue(id, out var name) ? name : null,
            e.Place is { } place ? new PlaceDto(place.Venue, place.IsOnline) : null);

    /// <summary>Upcoming (not over yet: its last day, the end or else the start, is today or
    /// later) soonest start first, then undated, then past, most recent first; the title breaks
    /// a tie.</summary>
    private static IEnumerable<KnowledgeEvent> Ordered(IEnumerable<KnowledgeEvent> group, DateOnly today) =>
        group
            .OrderBy(e => e.Start is null ? 1 : Upcoming(e, today) ? 0 : 2)
            .ThenBy(e => e.Start is { } start && Upcoming(e, today) ? start.Date.DayNumber : 0)
            .ThenBy(e => e.Start is { } start && Upcoming(e, today) ? start.Time ?? TimeOnly.MinValue : TimeOnly.MinValue)
            .ThenByDescending(e => e.Start is { } start && !Upcoming(e, today) ? start.Date.DayNumber : 0)
            .ThenBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase);

    /// <summary>Whether a dated event is not over yet: one still running today is upcoming.</summary>
    private static bool Upcoming(KnowledgeEvent e, DateOnly today) =>
        (e.End ?? e.Start) is { } last && last.Date >= today;

    private static EventSeriesDto SeriesDto(EventSeries s) => new(s.Id, s.Name, s.Organiser, s.Link);

    private static EventDateDto? Day(EventDate? date) => date is null ? null : new EventDateDto(date.Date, date.Time, date.UtcOffset);

    private static Result<EventDate?> ToDate(EventDateDto? day)
    {
        if (day is null) return Result.Success<EventDate?>(null);

        return EventDate.Create(day.Date, day.Time, day.UtcOffset) switch
        {
            Ok<EventDate>(var made) => Result.Success<EventDate?>(made),
            Error error => Result.Failure<EventDate?>(error),
        };
    }

    /// <summary>Sets every field a new event's request carries, in the editor's terms: the dates
    /// typed, the place, link, what to learn, ticket and cost. The first rule that refuses
    /// answers.</summary>
    private static Result Fill(KnowledgeEvent e, NewKnowledgeEventDto request)
    {
        var dated = ApplyTypedDates(e, request.Start, request.End);
        if (dated is Error) return dated;

        var placed = ApplyPlace(e, request.Place);
        if (placed is Error) return placed;

        var costed = ApplyCost(e, request.Cost);
        if (costed is Error) return costed;

        e.SetLink(request.Link);
        e.SetWhatToLearn(request.WhatToLearn);
        e.SetTicket(request.Ticket);
        return Result.Success();
    }

    /// <summary>Sets the dates the person typed, with date source Typed, or clears them with a
    /// null start and end. Pane input is always typed; the event page is the capture route's alone.</summary>
    private static Result ApplyTypedDates(KnowledgeEvent e, EventDateDto? start, EventDateDto? end)
    {
        var from = ToDate(start);
        if (from is not Ok<EventDate?>(var fromDate)) return (Error)from.Value!;

        var to = ToDate(end);
        if (to is not Ok<EventDate?>(var toDate)) return (Error)to.Value!;

        return e.SetDates(fromDate, toDate, fromDate is null ? null : DateSource.Typed);
    }

    /// <summary>Sets the place, or clears it with null.</summary>
    private static Result ApplyPlace(KnowledgeEvent e, PlaceDto? place)
    {
        if (place is null)
        {
            e.SetPlace(null);
            return Result.Success();
        }

        var made = Place.Create(place.Venue, place.IsOnline);
        if (made is not Ok<Place>(var placed)) return (Error)made.Value!;

        e.SetPlace(placed);
        return Result.Success();
    }

    /// <summary>Sets the cost, or clears it with null.</summary>
    private static Result ApplyCost(KnowledgeEvent e, CostDto? cost)
    {
        if (cost is null)
        {
            e.SetCost(null);
            return Result.Success();
        }

        var made = Cost.Create(cost.Amount, cost.Currency);
        if (made is not Ok<Cost>(var priced)) return (Error)made.Value!;

        e.SetCost(priced);
        return Result.Success();
    }

    /// <summary>The series a new event names by <paramref name="name"/>: the one series with that
    /// name, trimmed and without regard to case; a new one, not saved yet, when none has it; or
    /// <c>event_series.ambiguous</c> when two or more have it. Null for no name.</summary>
    private async Task<Result<(EventSeries Series, bool IsNew)?>> SeriesNamedAsync(string? name, CancellationToken cancellationToken)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return Result.Success<(EventSeries, bool)?>(null);

        var matches = (await series.ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(s => string.Equals(s.Name.Trim(), trimmed, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToList();

        if (matches.Count > 1) return Result.Failure<(EventSeries, bool)?>(EventsErrors.SeriesNameAmbiguous(trimmed));
        if (matches.Count == 1) return Result.Success<(EventSeries, bool)?>((matches[0], false));

        var started = EventSeries.Create(Guid.CreateVersion7(clock.GetUtcNow()), trimmed, organiser: null, link: null);
        return started switch
        {
            Ok<EventSeries>(var newSeries) => Result.Success<(EventSeries, bool)?>((newSeries, true)),
            Error error => Result.Failure<(EventSeries, bool)?>(error),
        };
    }

    /// <summary>A Brain note's path as an event stores it (<see cref="NoteLink.BrainPath"/>).
    /// Null for a blank one.</summary>
    private static string? NotePath(string? path) => NoteLink.BrainPath(path);
}
