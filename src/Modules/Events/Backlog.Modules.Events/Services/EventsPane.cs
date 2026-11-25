using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.DomainModels;
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
    IAddressGeocoder geocoder,
    KnowledgeEventSaved? saved = null,
    ILogger<EventsPane>? log = null) : IEventsPane
{
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

    /// <summary>The lookups running or queued per event, one at a time per event; guarded by
    /// <see cref="_gate"/>. An event leaves it once its last lookup is over.</summary>
    private readonly Dictionary<Guid, LookUpQueue> _lookUps = [];

    /// <summary>One lock per event, around every load-change-save this pane makes of it — the
    /// person's edits and a lookup's record alike — so a write that loaded before an answer was
    /// kept never writes over it. Never held across a geocoder call. Guarded by <see cref="_gate"/>.
    /// Writers in other windows and over MCP keep the store's last write wins.</summary>
    private readonly Dictionary<Guid, SemaphoreSlim> _eventLocks = [];

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
        NotifyHandlers();
    }

    /// <summary>Tells this pane's <see cref="Changed"/> handlers, each on its own.</summary>
    private void NotifyHandlers()
    {
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
        var mark = new WriteMark(_identity);
        Writing.Value = mark;
        return mark;
    }

    /// <summary>One write's self-write mark: whose it is, and whether the write is over.</summary>
    private sealed class WriteMark(object owner) : IDisposable
    {
        private volatile bool _over;

        public object Owner { get; } = owner;

        public bool IsOver => _over;

        public void Dispose() => _over = true;
    }

    public async Task<IReadOnlyList<KnowledgeEventSectionDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var all = await events.ListAsync(cancellationToken).ConfigureAwait(false);
        var names = (await series.ListAsync(cancellationToken).ConfigureAwait(false)).ToDictionary(s => s.Id, s => s.Name);
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

        var upcoming = all.Where(e => !IsPast(e, today))
            .OrderBy(e => e.Start is null)
            .ThenBy(e => e.Start?.Date)
            .ThenBy(e => e.Start?.Time ?? TimeOnly.MinValue)
            .ThenBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var past = all.Where(e => IsPast(e, today))
            .OrderByDescending(e => (e.End ?? e.Start)?.Date)
            .ThenByDescending(e => e.Start?.Date)
            .ThenBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase);

        KnowledgeEventSectionDto[] sections =
        [
            new(EventListSection.Upcoming, [.. upcoming.Zip(MapNumbers(upcoming), (e, number) => Summary(e, names, number))]),
            new(EventListSection.Past, [.. past.Select(e => Summary(e, names, mapNumber: null))])
        ];

        return [.. sections.Where(section => section.Events.Count > 0)];
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

        // The write is over once it is saved and announced: the geocoder call after it runs
        // unmarked, as after SetPlaceAsync, so work this save started and that announces a
        // change while the call is out is heard as made elsewhere. The answer's save marks itself.
        mark.Dispose();
        if (LookupDue(added) is not null) _ = QueueLookUp(added.Id, retry: false);

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
        using var held = await LockAsync(id, cancellationToken).ConfigureAwait(false);
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

    // Answered once saved: the address holds whatever the call does, and the person's save is
    // not held for as long as the geocoder takes. The call runs after it (QueueLookUp).
    public async Task<Result<KnowledgeEventDto>> SetPlaceAsync(Guid id, PlaceDto? place, CancellationToken cancellationToken = default)
    {
        KnowledgeEvent found;
        using (await LockAsync(id, cancellationToken).ConfigureAwait(false))
        {
            if (await events.GetAsync(id, cancellationToken).ConfigureAwait(false) is not { } loaded)
                return EventsErrors.KnowledgeEventNotFound(id);

            if (ApplyPlace(loaded, place) is Error error) return error;

            await SaveOnlyAsync(loaded, cancellationToken).ConfigureAwait(false);
            found = loaded;
        }

        if (LookupDue(found) is not null) _ = QueueLookUp(id, retry: false);

        return await ReadAsync(found, cancellationToken).ConfigureAwait(false);
    }

    // Waits for its answer, as the person asked for it, behind any lookup already running for
    // the event: one a save started may place the address, and then nothing is sent again.
    public async Task<Result<AddressLookUpDto>> LookUpAddressAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (await events.GetAsync(id, cancellationToken).ConfigureAwait(false) is not { } found)
            return EventsErrors.KnowledgeEventNotFound(id);

        if (found.Place is not { Address: not null } held) return EventsErrors.AddressMissing();
        if (held.Lookup == AddressLookup.Placed && !IsLookingUp(id))
            return new AddressLookUpDto(await ReadAsync(found, cancellationToken).ConfigureAwait(false), Answer: null);

        var answer = await QueueLookUp(id, retry: true).WaitAsync(cancellationToken).ConfigureAwait(false);

        return await events.GetAsync(id, cancellationToken).ConfigureAwait(false) is { } read
            ? new AddressLookUpDto(await ReadAsync(read, cancellationToken).ConfigureAwait(false), answer?.Lookup)
            : EventsErrors.KnowledgeEventNotFound(id);
    }

    // One call at a time, so a long list never bursts at the provider: each event in turn, through
    // the same per-event queue a save uses, so an address a save is already looking up is not sent
    // twice and each event the pass is on reads as being looked up. An event a running lookup
    // placed meanwhile counts as it then reads.
    public async Task<AddressLookupPassDto> LookUpPendingAsync(CancellationToken cancellationToken = default)
    {
        var pending = (await events.ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(e => LookupDue(e) is not null)
            .Select(e => e.Id)
            .ToList();

        int placed = 0, notFound = 0, notLookedUpYet = 0;
        foreach (var id in pending)
        {
            var answer = await QueueLookUp(id, retry: false).WaitAsync(cancellationToken).ConfigureAwait(false);
            var lookup = answer?.Lookup
                ?? (await events.GetAsync(id, cancellationToken).ConfigureAwait(false))?.Place?.Lookup
                ?? AddressLookup.NotLookedUpYet;
            switch (lookup)
            {
                case AddressLookup.Placed: placed++; break;
                case AddressLookup.NotFound: notFound++; break;
                default: notLookedUpYet++; break;
            }
        }

        return new AddressLookupPassDto(placed, notFound, notLookedUpYet);
    }

    public bool IsLookingUp(Guid id)
    {
        lock (_gate) return _lookUps.ContainsKey(id);
    }

    /// <summary>Waits for every lookup this pane has queued to be over — its answer kept and
    /// <see cref="Changed"/> told — the ones queued meanwhile too. It waits on the lookups' own
    /// tasks, not on <see cref="_lookUps"/>, which an event leaves just before it is told.</summary>
    internal async Task LookUpsSettledAsync()
    {
        while (true)
        {
            Task[] running;
            lock (_gate)
            {
                _queued.RemoveAll(lookUp => lookUp.IsCompleted);
                running = [.. _queued];
            }

            if (running.Length == 0) return;

            await Task.WhenAll(running).ConfigureAwait(false);
        }
    }

    /// <summary>Every lookup queued and not over, for <see cref="LookUpsSettledAsync"/>;
    /// guarded by <see cref="_gate"/>.</summary>
    private readonly List<Task> _queued = [];

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
        using var held = await LockAsync(id, cancellationToken).ConfigureAwait(false);
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

    /// <summary>Saves and announces, as this pane's own write.</summary>
    private async Task SaveOnlyAsync(KnowledgeEvent changed, CancellationToken cancellationToken)
    {
        using var mark = BeginWrite();

        await events.SaveAsync(changed, cancellationToken).ConfigureAwait(false);
        saved?.Raise();
    }

    /// <summary>The address a save of the event's place sends: one Not looked up yet — new,
    /// changed, or still waiting on an answer. A Placed or not-found address that did not change
    /// sends nothing (local ADR 0061 §5).</summary>
    private static Address? LookupDue(KnowledgeEvent e) =>
        e.Place is { Lookup: AddressLookup.NotLookedUpYet, Address: { } address } ? address : null;

    /// <summary>
    /// Queues a lookup of the event's address behind any already running for it, and answers
    /// what the geocoder said, or null when nothing was sent. It reads the event when its turn
    /// comes, so it sends the address the event holds then: one Not looked up yet, or with
    /// <paramref name="retry"/> (Look up now) one not found too. A Placed address, one changed
    /// to none, or a gone event sends nothing.
    /// <para>
    /// It runs on the thread pool with the flow's context left behind: no self-write mark and no
    /// caller's token reach it, so the person's save is over and a cancelled request still has
    /// its address looked up. The answer's own save marks itself; once the lookup is over the
    /// pane tells its own <see cref="Changed"/> handlers, which hear no own save otherwise.
    /// </para>
    /// </summary>
    private Task<GeocodeAnswer?> QueueLookUp(Guid id, bool retry)
    {
        lock (_gate)
        {
            if (!_lookUps.TryGetValue(id, out var queue)) _lookUps[id] = queue = new LookUpQueue();

            // One queued behind the running lookup and not started yet reads the event when its
            // turn comes, so it serves this ask too; a retry asked of it makes it a retry.
            if (queue.Waiting is { } waiting)
            {
                waiting.Retry |= retry;
                return waiting.Answer;
            }

            queue.Count++;
            var prior = queue.Tail;
            var entry = new QueuedLookUp { Retry = retry };
            entry.Answer = RunUnflowed(() => LookUpAfterAsync(prior, id, entry));
            _queued.Add(entry.Answer);
            queue.Waiting = entry;
            queue.Tail = entry.Answer;
            return entry.Answer;
        }
    }

    private static Task<T> RunUnflowed<T>(Func<Task<T>> work)
    {
        if (ExecutionContext.IsFlowSuppressed()) return Task.Run(work);

        using (ExecutionContext.SuppressFlow()) return Task.Run(work);
    }

    private async Task<GeocodeAnswer?> LookUpAfterAsync(Task prior, Guid id, QueuedLookUp entry)
    {
        try
        {
            await prior.ConfigureAwait(false);

            bool retry;
            lock (_gate)
            {
                if (_lookUps.TryGetValue(id, out var started) && ReferenceEquals(started.Waiting, entry)) started.Waiting = null;
                retry = entry.Retry;
            }

            if (await events.GetAsync(id, CancellationToken.None).ConfigureAwait(false) is not { } current) return null;

            var address = current.Place switch
            {
                { Lookup: AddressLookup.NotLookedUpYet, Address: { } due } => due,
                { Lookup: AddressLookup.NotFound, Address: { } again } when retry => again,
                _ => null
            };
            if (address is null) return null;

            var answer = await GeocodeAsync(address, CancellationToken.None).ConfigureAwait(false);
            await RecordAsync(id, address, answer, CancellationToken.None).ConfigureAwait(false);
            return answer;
        }
#pragma warning disable CA1031 // Nobody awaits a save's lookup: a store that fails here is logged, the address stays Not looked up yet.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            _log.LogWarning("Looking up an event's address failed with {ExceptionType}.", exception.GetType().Name);
            return GeocodeAnswer.Failed;
        }
        finally
        {
            lock (_gate)
            {
                if (_lookUps.TryGetValue(id, out var queue) && --queue.Count == 0) _lookUps.Remove(id);
            }

            NotifyHandlers();
        }
    }

    /// <summary>One event's lookups: the last one queued, the one waiting for its turn if any,
    /// and how many are not over.</summary>
    private sealed class LookUpQueue
    {
        public Task Tail { get; set; } = Task.CompletedTask;

        public QueuedLookUp? Waiting { get; set; }

        public int Count { get; set; }
    }

    /// <summary>A lookup queued for its turn: what it answers, and whether it retries an address
    /// not found (Look up now). Guarded by <see cref="_gate"/> until it starts.</summary>
    private sealed class QueuedLookUp
    {
        public Task<GeocodeAnswer?> Answer { get; set; } = Task.FromResult<GeocodeAnswer?>(null);

        public bool Retry { get; set; }
    }

    /// <summary>Takes the event's lock for one load-change-save; disposing the answer lets it go.</summary>
    private async Task<EventLock> LockAsync(Guid id, CancellationToken cancellationToken)
    {
        SemaphoreSlim held;
        lock (_gate)
        {
            if (!_eventLocks.TryGetValue(id, out held!)) _eventLocks[id] = held = new SemaphoreSlim(1, 1);
        }

        await held.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new EventLock(held);
    }

    private readonly struct EventLock(SemaphoreSlim held) : IDisposable
    {
        public void Dispose() => held.Release();
    }

    /// <summary>Keeps <paramref name="answer"/> on the event, loaded again: the call waits on the
    /// network, and a change saved elsewhere meanwhile is kept. The event decides whether the
    /// answer still applies — not to an address changed meanwhile, nor one it would move back —
    /// and a save is made only when it did. Null when the event is gone.</summary>
    private async Task<KnowledgeEvent?> RecordAsync(Guid id, Address address, GeocodeAnswer answer, CancellationToken cancellationToken)
    {
        using var held = await LockAsync(id, cancellationToken).ConfigureAwait(false);
        if (await events.GetAsync(id, cancellationToken).ConfigureAwait(false) is not { } current) return null;

        var before = current.Place;
        if (current.RecordLookup(address, answer.Lookup, answer.Point) is Error error)
            throw new InvalidOperationException($"A geocode answer carries a point exactly when it placed the address ({error.Code}).");

        if (current.Place != before) await SaveOnlyAsync(current, cancellationToken).ConfigureAwait(false);
        return current;
    }

    /// <summary>The geocoder's answer, or no answer when it throws: a geocoder that breaks its
    /// contract fails no save. Only the exception's type is logged, never its message, which
    /// may carry the request and with it the address (local ADR 0061 §5, guideline 0010).</summary>
    private async Task<GeocodeAnswer> GeocodeAsync(Address address, CancellationToken cancellationToken)
    {
        try
        {
            return await geocoder.GeocodeAsync(address, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // No answer back is Not looked up yet, whatever the adapter threw.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            _log.LogWarning("Geocoding an event's address failed with {ExceptionType}.", exception.GetType().Name);
            return GeocodeAnswer.Failed;
        }
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
            PlaceDtoOf(e.Place),
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

    private static KnowledgeEventSummaryDto Summary(KnowledgeEvent e, IReadOnlyDictionary<Guid, string> seriesNames, int? mapNumber) =>
        new(
            e.Id,
            e.Title,
            e.State,
            Day(e.Start),
            Day(e.End),
            e.Series is { } id && seriesNames.TryGetValue(id, out var name) ? name : null,
            PlaceDtoOf(e.Place),
            mapNumber);

    private static PlaceDto? PlaceDtoOf(Place? place) =>
        place is null
            ? null
            : new PlaceDto(
                place.Venue,
                place.IsOnline,
                place.Address is { } address ? new AddressDto(address.Street, address.Postcode, address.Town, address.Country) : null,
                place.Lookup,
                place.Point is { } point ? new MapPointDto(point.Latitude, point.Longitude) : null);

    /// <summary>Whether an event is over: its last day, the end or else the start, is before
    /// today. One still running today is not; an undated one never is.</summary>
    private static bool IsPast(KnowledgeEvent e, DateOnly today) =>
        (e.End ?? e.Start) is { } last && last.Date < today;

    /// <summary>The map number of each upcoming event, in list order: 1..n over those on the
    /// map, null for the rest (<c>domain.md#map-number</c>).</summary>
    private static IEnumerable<int?> MapNumbers(IEnumerable<KnowledgeEvent> upcoming)
    {
        var number = 0;
        foreach (var e in upcoming)
            yield return OnTheMap(e) ? ++number : null;
    }

    /// <summary>Whether an upcoming event takes a map number: it has a map point and is not
    /// Skipped (scope Q5). One already Attended or Harvested keeps its number (scope Q7).</summary>
    private static bool OnTheMap(KnowledgeEvent e) =>
        e.Place?.Point is not null && e.State != KnowledgeEventState.Skipped;

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

    /// <summary>Sets the place, or clears it with null. The lookup and point the caller sent are
    /// not read: an address goes in Not looked up yet, and the event keeps the lookup and point
    /// of one it already held.</summary>
    private static Result ApplyPlace(KnowledgeEvent e, PlaceDto? place)
    {
        if (place is null)
        {
            e.SetPlace(null);
            return Result.Success();
        }

        Address? address = null;
        if (place.Address is { } parts)
        {
            var addressed = Address.Create(parts.Street, parts.Postcode, parts.Town, parts.Country);
            if (addressed is not Ok<Address>(var madeAddress)) return (Error)addressed.Value!;
            address = madeAddress;
        }

        var made = Place.Create(place.Venue, place.IsOnline, address, address is null ? null : AddressLookup.NotLookedUpYet, point: null);
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
