using Backlog.Mobile.UI.Outbox;
using Backlog.Mobile.UI.Services;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions;

namespace Backlog.Mobile.UI.Tasks;

/// <summary>
/// The phone's view of the owner's tasks: the task feed folded into
/// <see cref="ITaskViewStore"/>, and the one write the phone makes — a task added
/// for today, pushed through the outbox.
/// <para>
/// A projection plus a push, and never a second task store: the phone keeps no
/// domain rules and decides nothing about a task it pulled. Whatever the replica
/// says last is what the row says.
/// </para>
/// <para>
/// One per device, a singleton, because there is one file under it and two pulls
/// folding into it at once would each write what the other had not seen.
/// </para>
/// </summary>
public sealed class TaskViewProjection : IDisposable
{
    private readonly ITaskViewStore _store;
    private readonly DeviceOutbox _outbox;
    private readonly CloudSyncClient _sync;
    private readonly TimeProvider _clock;

    /// <summary>Held by a pull and by an add, so an add's own row can never be
    /// written over the replica's copy of it by a pull that finished first.</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _rowsLock = new();
    private readonly Dictionary<Guid, TaskViewRow> _rows;

    public TaskViewProjection(ITaskViewStore store, DeviceOutbox outbox, CloudSyncClient sync, TimeProvider clock)
    {
        _store = store;
        _outbox = outbox;
        _sync = sync;
        _clock = clock;

        // Read once, here: the list is drawn from this before any pull answers.
        _rows = store.ReadRows().ToDictionary(row => row.Id);
    }

    /// <summary>Raised when a row was added, changed or deleted. May be raised off
    /// the render thread.</summary>
    public event Action? Changed;

    /// <summary>The phone's own date. My Day is picked for a day as the person
    /// lives it, so it is the local calendar and not UTC's.</summary>
    public DateOnly Today => DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);

    /// <summary>What is in My Day on <paramref name="today"/>, most urgent first.</summary>
    public IReadOnlyList<TaskViewRow> MyDay(DateOnly today)
    {
        lock (_rowsLock)
        {
            return
            [
                .. _rows.Values
                    .Where(row => row.IsInMyDay(today))
                    .OrderBy(row => PriorityRank(row.Task.Priority))
                    .ThenBy(row => row.Task.CreatedAt)
            ];
        }
    }

    public TaskViewRow? Find(Guid id)
    {
        lock (_rowsLock) return _rows.GetValueOrDefault(id);
    }

    /// <summary>
    /// Pulls the feed from the saved cursor until the service says it is drained,
    /// keeping each page and the cursor after it as one write — so a pull cut short
    /// resumes after the last page it kept.
    /// <para>
    /// A cursor the service will not take — not one it minted
    /// (<c>sync.cursor_malformed</c>), or one it no longer resumes from
    /// (<c>sync.cursor_expired</c>) — is dropped and the pull starts again from the
    /// beginning, once. That is a phone that has been away a while, not an error
    /// for the person: the full pull folds over the rows already kept to the same
    /// result. Any other failure is thrown for the screen to put in words.
    /// </para>
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
                catch (SyncUnavailableException ex) when (!restarted && IsCursorRejection(ex.Code))
                {
                    await _store.ResetCursorAsync(cancellationToken);
                    since = null;
                    restarted = true;
                    continue;
                }

                IReadOnlyList<TaskViewRow> changed;
                lock (_rowsLock) changed = TaskFold.Apply(_rows, page.Tasks ?? []);

                await _store.SaveAsync(changed, page.Since, cancellationToken);
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

    /// <summary>
    /// Adds a task picked for today. It is queued before anything else — the
    /// outbox is the promise to send it — and then written into the view, so it is
    /// in today's list the moment this returns, with or without a network. The
    /// next pull replaces the row with the replica's own copy.
    /// </summary>
    public async Task<TaskViewRow> AddAsync(string title, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        var change = NewTask(title.Trim(), _clock.GetUtcNow(), Today);
        var row = new TaskViewRow(change.Id, change.UpdatedAt, DeletedAt: null, ServerTimestamp: 0, change.Task);

        await _gate.WaitAsync(cancellationToken);

        try
        {
            await _outbox.EnqueueAsync(TaskOutboxKind.Token, change.Id, TaskOutboxKind.Write(change), cancellationToken);

            await _store.SaveAsync([row], cursor: null, cancellationToken);
            lock (_rowsLock) _rows[row.Id] = row;
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke();
        return row;
    }

    /// <summary>
    /// The task the phone creates: a new version-7 id, the typed title, and the
    /// Tasks module's defaults for a new entry — a <c>task</c>, <c>draft</c>,
    /// <c>medium</c> — picked for <paramref name="today"/>, because adding it here
    /// is picking it. Everything else is empty: the body, the repositories and the
    /// schedule are the desktop's to give it.
    /// </summary>
    public static TaskChange NewTask(string title, DateTimeOffset now, DateOnly today)
    {
        var payload = new TaskPayload(
            Title: title,
            ContentMd: string.Empty,
            Type: EnumMap.ToWire(EntryType.Task),
            Status: EnumMap.ToWire(EntryStatus.Draft),
            Priority: EnumMap.ToWire(Priority.Medium),
            Order: 0,
            Area: null,
            CreatedAt: now,
            SourceInboxId: null,
            RecurrenceSourceId: null,
            DueOn: null,
            RemindAt: null,
            Recurrence: null,
            InMyDayOn: today,
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
            ProjectionRefs: []);

        return new TaskChange(Guid.CreateVersion7(), now, DeletedAt: null, payload);
    }

    public void Dispose() => _gate.Dispose();

    private static bool IsCursorRejection(string? code) =>
        code is SyncErrorCodes.SyncCursorMalformed or SyncErrorCodes.SyncCursorExpired;

    private static int PriorityRank(string? priority) => priority?.ToLowerInvariant() switch
    {
        "critical" => 0,
        "high" => 1,
        "medium" => 2,
        "low" => 3,
        _ => 4
    };
}
