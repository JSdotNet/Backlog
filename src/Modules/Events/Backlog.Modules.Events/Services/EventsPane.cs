using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.DomainModels;
using Backlog.SharedKernel.Results;

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
    TimeProvider clock) : IEventsPane
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

    public string? BrainUnavailableReason => brain.UnavailableReason;

    public event Action? Changed
    {
        add
        {
            tasks.Changed += value;
            brain.Changed += value;
        }
        remove
        {
            tasks.Changed -= value;
            brain.Changed -= value;
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
        if (seriesId is { } named && await series.GetAsync(named, cancellationToken).ConfigureAwait(false) is null)
            return EventsErrors.SeriesNotFound(named);

        var created = KnowledgeEvent.Create(Guid.CreateVersion7(clock.GetUtcNow()), title);
        if (created.IsFailure) return created.Error;

        created.Value.SetSeries(seriesId);
        await events.SaveAsync(created.Value, cancellationToken).ConfigureAwait(false);
        return await ReadAsync(created.Value, cancellationToken).ConfigureAwait(false);
    }

    public Task<Result<KnowledgeEventDto>> RenameAsync(Guid id, string title, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, e => e.Rename(title), cancellationToken);

    public Task<Result<KnowledgeEventDto>> MoveToAsync(Guid id, KnowledgeEventState target, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, e => e.MoveTo(target), cancellationToken);

    public Task<Result<KnowledgeEventDto>> SkipAsync(Guid id, string reason, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, e => e.Skip(reason), cancellationToken);

    public Task<Result<KnowledgeEventDto>> SetDatesAsync(Guid id, EventDayDto? start, EventDayDto? end, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, e =>
        {
            var from = ToDate(start);
            if (from.IsFailure) return Result.Failure(from.Error);

            var to = ToDate(end);
            if (to.IsFailure) return Result.Failure(to.Error);

            // Pane input is always typed; the event page is the capture route's alone.
            return e.SetDates(from.Value, to.Value, from.Value is null ? null : DateSource.Typed);
        }, cancellationToken);

    public Task<Result<KnowledgeEventDto>> SetPlaceAsync(Guid id, PlaceDto? place, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, e =>
        {
            if (place is null)
            {
                e.SetPlace(null);
                return Result.Success();
            }

            var made = Place.Create(place.Venue, place.IsOnline);
            if (made.IsFailure) return Result.Failure(made.Error);

            e.SetPlace(made.Value);
            return Result.Success();
        }, cancellationToken);

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
                if (one.IsFailure) return Result.Failure(one.Error);
                made.Add(one.Value);
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
        ChangeAsync(id, e =>
        {
            if (cost is null)
            {
                e.SetCost(null);
                return Result.Success();
            }

            var made = Cost.Create(cost.Amount, cost.Currency);
            if (made.IsFailure) return Result.Failure(made.Error);

            e.SetCost(made.Value);
            return Result.Success();
        }, cancellationToken);

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
        var created = EventSeries.Create(Guid.CreateVersion7(clock.GetUtcNow()), name, organiser, link);
        if (created.IsFailure) return created.Error;

        await series.SaveAsync(created.Value, cancellationToken).ConfigureAwait(false);
        return SeriesDto(created.Value);
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
        if (await events.GetAsync(id, cancellationToken).ConfigureAwait(false) is not { } found)
            return EventsErrors.KnowledgeEventNotFound(id);

        // The title alone: the task never names the event (ADR 0050 §3).
        var filed = await tasks.FileAsync(trimmed, cancellationToken).ConfigureAwait(false);
        if (filed.IsFailure) return filed.Error;

        return await SaveAsync(found, found.LinkTask(EventTaskLink.Create(filed.Value, TaskPurpose.Prep)), cancellationToken).ConfigureAwait(false);
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

    private async Task<Result<KnowledgeEventDto>> ChangeAsync(Guid id, Func<KnowledgeEvent, Result> change, CancellationToken cancellationToken)
    {
        if (await events.GetAsync(id, cancellationToken).ConfigureAwait(false) is not { } found)
            return EventsErrors.KnowledgeEventNotFound(id);

        return await SaveAsync(found, change(found), cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<KnowledgeEventDto>> SaveAsync(KnowledgeEvent changed, Result outcome, CancellationToken cancellationToken)
    {
        if (outcome.IsFailure) return outcome.Error;

        await events.SaveAsync(changed, cancellationToken).ConfigureAwait(false);
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
            others);
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

    private static EventDayDto? Day(EventDate? date) => date is null ? null : new EventDayDto(date.Date, date.Time, date.UtcOffset);

    private static Result<EventDate?> ToDate(EventDayDto? day)
    {
        if (day is null) return Result.Success<EventDate?>(null);

        var made = EventDate.Create(day.Date, day.Time, day.UtcOffset);
        return made.IsFailure ? Result.Failure<EventDate?>(made.Error) : Result.Success<EventDate?>(made.Value);
    }

    /// <summary>A Brain note's path as an event stores it: forward slashes, from the root, no
    /// leading slash. Null for a blank one.</summary>
    private static string? NotePath(string? path)
    {
        var normalized = path?.Trim().Replace('\\', '/').TrimStart('/');
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }
}
