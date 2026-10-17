using Backlog.Mobile.UI.Outbox;
using Backlog.Mobile.UI.Services;
using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.Progress;

/// <summary>A goal as the phone reads it: the goal, when it was set, and what each pull
/// request attached to it said about itself when a desktop last read GitHub — its state,
/// checks and approvals — so the phone needs no GitHub access.</summary>
/// <param name="PullRequests">One per pull request attachment, in the order the goal holds
/// them; one no desktop has read yet is <see cref="GoalPullRequestDto.Unread"/>.</param>
public sealed record GoalView(GoalDto Goal, DateTimeOffset? CreatedAt, IReadOnlyList<GoalPullRequestDto> PullRequests);

/// <summary>
/// The phone's goals and impediments (local ADR 0035 §8,
/// <c>.devbook/domain/progress/features.md#goals-on-the-phone</c>): the task feed's goal,
/// impediment and stand-up context documents folded into <see cref="IProgressViewStore"/>,
/// read back as the published Progress DTOs, and the one change the phone makes — an edit to
/// an impediment's state, check-again date or note — queued through the
/// <see cref="DeviceOutbox"/> for a desktop to apply.
/// <para>
/// A projection plus a queue, never a second store: the phone keeps no domain rules and
/// decides nothing about a goal or an impediment. An edit changes nothing here until a
/// desktop has applied it and the pull that follows brings the impediment back.
/// </para>
/// <para>
/// One per device, a singleton, for the reason the task projection is one.
/// </para>
/// </summary>
public sealed class ProgressViewProjection : IDisposable
{
    private readonly IProgressViewStore _store;
    private readonly DeviceOutbox _outbox;
    private readonly CloudSyncClient _sync;
    private readonly TimeProvider _clock;

    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Serialises the edits the phone queues, apart from <see cref="_gate"/>, which a
    /// pull holds for as long as the network takes: an edit is the outbox's to send and needs
    /// nothing a pull is doing, so a slow pull never holds up a tap. The chains it reads and
    /// writes are under <see cref="_rowsLock"/>, as the pull's rows are.</summary>
    private readonly SemaphoreSlim _queueGate = new(1, 1);
    private readonly Lock _rowsLock = new();
    private readonly Dictionary<Guid, ProgressViewRow> _rows;

    /// <summary>The edits asked for each impediment's field, oldest first, under
    /// <see cref="_rowsLock"/>: what the next edit of the same field says the phone saw, so
    /// edits in a row apply in the order they were asked (<see cref="ImpedimentEditDto"/>).
    /// Seeded from the outbox, so the edits still waiting to leave outlive a restart.</summary>
    private readonly Dictionary<(Guid Impediment, ImpedimentEditKind Kind), List<ImpedimentEditDto>> _asked = [];

    /// <summary>For a chain whose every edit has left the outbox, the stamp of the copy of its
    /// impediment the phone held when the last of them left, under <see cref="_rowsLock"/>.
    /// Leaving the outbox is reaching cloud sync, not being applied: a desktop's verdict is a
    /// newer copy of the impediment arriving after that, and only that makes a chain the
    /// copy does not bear out give way.</summary>
    private readonly Dictionary<(Guid Impediment, ImpedimentEditKind Kind), DateTimeOffset> _leftAt = [];

    public ProgressViewProjection(IProgressViewStore store, DeviceOutbox outbox, CloudSyncClient sync, TimeProvider clock)
    {
        _store = store;
        _outbox = outbox;
        _sync = sync;
        _clock = clock;

        // Read once, here: the pages are drawn from this before any pull answers.
        _rows = store.ReadRows().ToDictionary(row => row.Id);

        foreach (var entry in outbox.Entries.Where(entry => entry.Kind == ImpedimentEditOutboxKind.Token))
        {
            if (Asked(entry) is { } edit) Remember(edit);
        }

        _outbox.Delivered += OnDelivered;
    }

    /// <summary>Raised when a goal, an impediment or the stand-up context changed. May be
    /// raised off the render thread.</summary>
    public event Action? Changed;

    /// <summary>
    /// The last edit the phone asked for this impediment's field while it can still stand —
    /// what a page shows in place of the impediment's own value, and what the next edit of the
    /// field says the phone saw — or null when there is none: never asked, applied, changed on
    /// a desktop since, or left the phone and answered by a newer copy that does not bear it
    /// out. One rule for the page and for <see cref="QueueAsync"/>.
    /// </summary>
    public ImpedimentEditDto? AskedFor(Guid impedimentId, ImpedimentEditKind kind)
    {
        if (FindImpediment(impedimentId) is not { } impediment) return null;

        lock (_rowsLock) return Pending(impediment, kind);
    }

    /// <summary>Whether an edit the phone asked for this impediment's field is still in the
    /// outbox — not yet taken by cloud sync.</summary>
    public bool IsWaiting(Guid impedimentId, ImpedimentEditKind kind) =>
        _outbox.Entries
            .Where(entry => entry.Kind == ImpedimentEditOutboxKind.Token)
            .Select(Asked)
            .Any(edit => edit is not null && edit.ImpedimentId == impedimentId && edit.Kind == kind);

    /// <summary>The phone's own date, the local calendar.</summary>
    public DateOnly Today => DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);

    /// <summary>Every goal still set, in the order they were set — when set, then by id, so
    /// every phone lists them alike. A document this build cannot read is passed over.</summary>
    public IReadOnlyList<GoalView> Goals()
    {
        var context = Context();
        var statuses = new Dictionary<string, GoalPullRequestDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var pull in context?.PullRequests ?? []) statuses.TryAdd(GoalItems.Key(pull.Repository, pull.Number), pull);

        return
        [
            .. Live(ProgressFold.GoalType)
                .Select(row => ProgressDocuments.ReadGoal(row.Content, row.Id))
                .OfType<GoalDocumentDto>()
                .Where(document => !document.Deleted)
                .OrderBy(document => document.CreatedAt ?? DateTimeOffset.MaxValue)
                .ThenBy(document => document.Goal.Id)
                .Select(document => new GoalView(document.Goal, document.CreatedAt, PullRequestsOf(document.Goal, statuses)))
        ];
    }

    /// <summary>The goal, or null when the phone holds none by that id.</summary>
    public GoalView? FindGoal(Guid id) => Goals().FirstOrDefault(view => view.Goal.Id == id);

    /// <summary>Every impediment, open and resolved, in the order they were raised — the day,
    /// then when, then by id.</summary>
    public IReadOnlyList<ImpedimentDto> Impediments() =>
    [
        .. Live(ProgressFold.ImpedimentType)
            .Select(row => ProgressDocuments.ReadImpediment(row.Content, row.Id))
            .OfType<ImpedimentDocumentDto>()
            .OrderBy(document => document.Impediment.RaisedOn)
            .ThenBy(document => document.CreatedAt ?? DateTimeOffset.MaxValue)
            .ThenBy(document => document.Impediment.Id)
            .Select(document => document.Impediment)
    ];

    /// <summary>The impediment, or null when the phone holds none by that id.</summary>
    public ImpedimentDto? FindImpediment(Guid id) => Impediments().FirstOrDefault(impediment => impediment.Id == id);

    /// <summary>What a desktop last said about the pull requests, the pace and the working
    /// week, or null when no desktop has said anything yet.</summary>
    public StandUpContextDto? Context()
    {
        ProgressViewRow? row;
        lock (_rowsLock) row = _rows.GetValueOrDefault(StandUpContextId);

        return row is { DeletedAt: null } ? ProgressDocuments.ReadContext(row.Content) : null;
    }

    /// <summary>
    /// Whether the person has goals on (<c>.devbook/domain/progress/context.md#goals-area</c>),
    /// as far as the phone can tell. The phone keeps no feature settings of its own, and a
    /// desktop writes the stand-up context only while its <c>goals</c> flag is on, so a
    /// context the phone holds is the flag read from here: the Stand-up tab and its pages
    /// show while there is one, and say Stand-up is off while there is none.
    /// </summary>
    public bool GoalsOn => Context() is not null;

    /// <summary>
    /// Pulls the task feed from the Progress cursor until the service says it is drained,
    /// folding each page and keeping its rows and the cursor after it as one write. Its own
    /// cursor, apart from the task and note views', so none of them depends on another. A
    /// cursor the service will not take is dropped and the pull starts again from the
    /// beginning, once; any other failure is thrown for the screen to word.
    /// </summary>
    public async Task PullAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var since = _store.ReadCursor();
            var restarted = false;

            while (true)
            {
                PullTasksResponse page;

                try
                {
                    page = await _sync.PullTasksAsync(since, cancellationToken);
                }
                catch (SyncUnavailableException ex) when (!restarted
                    && ex.Code is SyncErrorCodes.SyncCursorMalformed or SyncErrorCodes.SyncCursorExpired)
                {
                    await _store.ResetCursorAsync(cancellationToken);
                    since = null;
                    restarted = true;
                    continue;
                }

                // Folded into a copy and taken only once the page is kept, as the note view
                // does: a save that fails leaves the rows as the file has them.
                Dictionary<Guid, ProgressViewRow> folded;
                lock (_rowsLock) folded = new Dictionary<Guid, ProgressViewRow>(_rows);
                var changed = ProgressFold.Apply(folded, page.Tasks ?? []);

                await _store.SaveAsync(changed, page.Since, cancellationToken);
                lock (_rowsLock)
                {
                    foreach (var row in changed) _rows[row.Id] = row;
                }

                since = page.Since;

                if (changed.Count > 0) Changed?.Invoke();
                if (!page.HasMore) return;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Asks for the impediment to move to <paramref name="state"/>, with
    /// <paramref name="with"/> as who it is with at that step.</summary>
    public Task<ImpedimentEditDto?> MoveImpedimentAsync(Guid impedimentId, ImpedimentState state, string? with = null, CancellationToken cancellationToken = default) =>
        QueueAsync(impedimentId, ImpedimentEditKind.State, state, with, null, null, cancellationToken);

    /// <summary>Asks for the impediment's check-again day to be <paramref name="checkAgain"/>,
    /// or cleared with null.</summary>
    public Task<ImpedimentEditDto?> SetCheckAgainAsync(Guid impedimentId, DateOnly? checkAgain, CancellationToken cancellationToken = default) =>
        QueueAsync(impedimentId, ImpedimentEditKind.CheckAgain, null, null, checkAgain, null, cancellationToken);

    /// <summary>Asks for the impediment's note to be <paramref name="note"/>, or cleared with
    /// null or blank text.</summary>
    public Task<ImpedimentEditDto?> EditNoteAsync(Guid impedimentId, string? note, CancellationToken cancellationToken = default) =>
        QueueAsync(impedimentId, ImpedimentEditKind.Note, null, null, null, string.IsNullOrWhiteSpace(note) ? null : note.Trim(), cancellationToken);

    /// <summary>
    /// The edit as the document it travels as: a new id of its own, the kind token
    /// <c>impediment-edit</c>, the edit as its text, stamped when the person asked — the shape
    /// every whole document on the task feed has, with the Tasks defaults for status and
    /// priority so no field but the type is unusual.
    /// </summary>
    public static TaskChange NewEdit(Guid id, ImpedimentEditDto edit, string title)
    {
        ArgumentNullException.ThrowIfNull(edit);

        return new TaskChange(id, edit.AskedAt, DeletedAt: null, new TaskPayload(
            title,
            ProgressDocuments.WriteEdit(edit),
            ProgressFold.ImpedimentEditType,
            Status: "draft",
            Priority: "medium",
            Order: 0,
            Area: null,
            CreatedAt: edit.AskedAt,
            SourceInboxId: null,
            RecurrenceSourceId: null,
            DueOn: null,
            RemindAt: null,
            Recurrence: null,
            InMyDayOn: null,
            View: null,
            Effort: null,
            ImportPlanId: null,
            ImportItemId: null,
            AttachmentPath: null,
            Tags: [],
            RepoIds: [],
            DependsOn: [],
            SubItems: [],
            UsageEvents: [],
            ProjectionRefs: []));
    }

    public void Dispose()
    {
        _outbox.Delivered -= OnDelivered;
        _gate.Dispose();
        _queueGate.Dispose();
    }

    /// <summary>The stand-up context's id, the literal the desktop writes it under. Persisted
    /// on every replica, so never changed.</summary>
    public static readonly Guid StandUpContextId = Guid.Parse("8b3f2d61-7c4e-4a95-b0d8-5e1c9a7f3b02");

    /// <summary>
    /// Queues one edit for an impediment the phone holds, open and readable; answers null and
    /// queues nothing for one it does not. The outbox is the promise to send it: the entry
    /// goes out as soon as there is a network, and a desktop applies it on its next pull.
    /// </summary>
    private async Task<ImpedimentEditDto?> QueueAsync(
        Guid impedimentId,
        ImpedimentEditKind kind,
        ImpedimentState? state,
        string? with,
        DateOnly? checkAgain,
        string? note,
        CancellationToken cancellationToken)
    {
        if (FindImpediment(impedimentId) is not { } impediment) return null;

        await _queueGate.WaitAsync(cancellationToken);

        try
        {
            var now = _clock.GetUtcNow();
            ImpedimentEditDto edit;

            lock (_rowsLock)
            {
                // What the phone saw: the value it last asked for this field while that chain
                // of edits can still apply, else the value the impediment holds.
                var pending = Pending(impediment, kind);
                var askedAt = pending is not null && now <= pending.AskedAt ? pending.AskedAt.AddTicks(1) : now;

                edit = new ImpedimentEditDto(
                    impedimentId,
                    kind,
                    state,
                    string.IsNullOrWhiteSpace(with) ? null : with.Trim(),
                    checkAgain,
                    note,
                    askedAt,
                    WasState: kind == ImpedimentEditKind.State ? pending?.State ?? impediment.State : null,
                    WasCheckAgain: kind == ImpedimentEditKind.CheckAgain ? (pending is null ? impediment.CheckAgain : pending.CheckAgain) : null,
                    WasNote: kind == ImpedimentEditKind.Note ? (pending is null ? impediment.Note : pending.Note) : null);
            }

            var id = Guid.CreateVersion7();
            await _outbox.EnqueueAsync(ImpedimentEditOutboxKind.Token, id, TaskOutboxKind.Write(NewEdit(id, edit, impediment.Title)), cancellationToken);

            lock (_rowsLock) Remember(edit);
            return edit;
        }
        finally
        {
            _queueGate.Release();
        }
    }

    /// <summary>
    /// The last edit asked for this field, while its chain can still apply: the impediment
    /// still holds what the first edit of the chain saw, or what one of the chain asked for —
    /// a desktop has applied part of it. Otherwise the field was changed elsewhere, the chain
    /// is forgotten, and null answers that the impediment's own value is what the phone saw.
    /// A chain that has left the outbox whole is forgotten too once a newer copy of the
    /// impediment has arrived and still does not say what it asked: the desktop refused it or
    /// found it stale. Under <see cref="_rowsLock"/>.
    /// </summary>
    private ImpedimentEditDto? Pending(ImpedimentDto impediment, ImpedimentEditKind kind)
    {
        var key = (impediment.Id, kind);
        if (_asked.GetValueOrDefault(key) is not { Count: > 0 } chain) return null;

        var live = kind switch
        {
            ImpedimentEditKind.State => chain[0].WasState is null || impediment.State == chain[0].WasState || chain.Any(edit => edit.State == impediment.State),
            ImpedimentEditKind.CheckAgain => impediment.CheckAgain == chain[0].WasCheckAgain || chain.Any(edit => edit.CheckAgain == impediment.CheckAgain),
            _ => SameNote(impediment.Note, chain[0].WasNote) || chain.Any(edit => SameNote(edit.Note, impediment.Note)),
        };

        // Applied whole: the impediment says what the last edit asked, so the chain is done.
        var done = kind switch
        {
            ImpedimentEditKind.State => impediment.State == chain[^1].State,
            ImpedimentEditKind.CheckAgain => impediment.CheckAgain == chain[^1].CheckAgain,
            _ => SameNote(impediment.Note, chain[^1].Note),
        };

        // Part of the chain applied: the impediment says what one of its edits asked.
        var partly = kind switch
        {
            ImpedimentEditKind.State => chain.Any(edit => edit.State == impediment.State),
            ImpedimentEditKind.CheckAgain => chain.Any(edit => edit.CheckAgain == impediment.CheckAgain),
            _ => chain.Any(edit => SameNote(edit.Note, impediment.Note)),
        };

        // A newer copy that bears out none of the chain is the desktop's answer to it; one
        // that bears out part of it is progress, and the rest waits for the copy after it.
        var answered = false;
        if (!chain.Any(InOutbox))
        {
            var stamp = StampOf(impediment.Id);
            if (!_leftAt.TryGetValue(key, out var left)) _leftAt[key] = stamp;
            else if (stamp > left && partly) _leftAt[key] = stamp;
            else answered = stamp > left;
        }

        if (live && !done && !answered) return chain[^1];

        _asked.Remove(key);
        _leftAt.Remove(key);
        return null;
    }

    /// <summary>Notes the moment a chain left the phone whole, before any pull can bring a
    /// desktop's answer to it.</summary>
    private void OnDelivered(OutboxEntry entry)
    {
        if (entry.Kind != ImpedimentEditOutboxKind.Token || Asked(entry) is not { } delivered) return;

        var key = (delivered.ImpedimentId, delivered.Kind);
        lock (_rowsLock)
        {
            if (_asked.GetValueOrDefault(key) is { Count: > 0 } chain && !chain.Any(InOutbox))
            {
                _leftAt.TryAdd(key, StampOf(delivered.ImpedimentId));
            }
        }
    }

    /// <summary>Whether the outbox still holds <paramref name="edit"/>.</summary>
    private bool InOutbox(ImpedimentEditDto edit) =>
        _outbox.Entries
            .Where(entry => entry.Kind == ImpedimentEditOutboxKind.Token)
            .Select(Asked)
            .Any(queued => queued is not null
                && queued.ImpedimentId == edit.ImpedimentId
                && queued.Kind == edit.Kind
                && queued.AskedAt == edit.AskedAt);

    /// <summary>The stamp of the copy of the impediment the phone holds. Under <see cref="_rowsLock"/>.</summary>
    private DateTimeOffset StampOf(Guid impedimentId) =>
        _rows.GetValueOrDefault(impedimentId)?.UpdatedAt ?? DateTimeOffset.MinValue;

    /// <summary>Adds an edit to its field's chain. Under <see cref="_rowsLock"/>, or in the constructor.</summary>
    private void Remember(ImpedimentEditDto edit)
    {
        var key = (edit.ImpedimentId, edit.Kind);
        if (!_asked.TryGetValue(key, out var chain)) _asked[key] = chain = [];
        chain.Add(edit);
        _leftAt.Remove(key);
        chain.Sort((left, right) => left.AskedAt.CompareTo(right.AskedAt));
    }

    /// <summary>The edit a queued outbox entry carries, or null for one this build cannot read.</summary>
    private static ImpedimentEditDto? Asked(OutboxEntry entry)
    {
        try
        {
            return ProgressDocuments.ReadEdit(TaskOutboxKind.Read(entry).Task.ContentMd);
        }
        catch (Exception failure) when (failure is System.Text.Json.JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private static bool SameNote(string? left, string? right) =>
        string.Equals(string.IsNullOrWhiteSpace(left) ? null : left.Trim(), string.IsNullOrWhiteSpace(right) ? null : right.Trim(), StringComparison.Ordinal);

    private List<ProgressViewRow> Live(string kind)
    {
        lock (_rowsLock)
        {
            return [.. _rows.Values.Where(row => row.DeletedAt is null && string.Equals(row.Kind, kind, StringComparison.Ordinal))];
        }
    }

    /// <summary>The statuses of the pull requests <paramref name="goal"/> holds as
    /// attachments, in their order — what the goal page's Pull requests section shows.</summary>
    private static List<GoalPullRequestDto> PullRequestsOf(GoalDto goal, Dictionary<string, GoalPullRequestDto> statuses) =>
    [
        .. goal.Attachments
            .Select(attachment => attachment.Item)
            .Where(item => item.Kind == GoalAttachmentKind.PullRequest && item.Repository is not null && item.Number is not null)
            .Select(item => new PullRequestRefDto(item.Repository!, item.Number!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            .Select(pull => statuses.TryGetValue(GoalItems.KeyOf(pull), out var status) ? status : GoalPullRequestDto.Unread(pull))
    ];
}
