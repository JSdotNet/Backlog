using Backlog.Mobile.UI.Outbox;
using Backlog.Mobile.UI.Services;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions;

namespace Backlog.Mobile.UI.Tasks;

/// <summary>
/// The phone's view of the owner's tasks: the task feed folded into
/// <see cref="ITaskViewStore"/>, and the writes the phone makes through the outbox —
/// a task added for today, and a whole task re-sent after one of its edits.
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

    /// <summary>What is in My Day on <paramref name="today"/>, in the order My Day
    /// keeps — see <see cref="InMyDayOrder"/>.</summary>
    public IReadOnlyList<TaskViewRow> MyDay(DateOnly today)
    {
        lock (_rowsLock)
        {
            return
            [
                .. InMyDayOrder(_rows.Values.Where(row => row.IsInMyDay(today)))
            ];
        }
    }

    /// <summary>
    /// Everything picked for <paramref name="today"/>, ticked or not, in the order
    /// <see cref="MyDay"/> keeps — what the Today screen groups. Unlike
    /// <see cref="MyDay"/> a done task stays in: it is ticked off today, which is
    /// the Done today group, and an unticked task whose status still reads done
    /// (the desktop's untick leaves the status alone) is open again. A deleted or
    /// archived task is gone.
    /// </summary>
    public IReadOnlyList<TaskViewRow> Day(DateOnly today)
    {
        lock (_rowsLock)
        {
            return
            [
                .. InMyDayOrder(_rows.Values
                    .Where(row => row.DeletedAt is null
                        && row.Task.InMyDayOn == today
                        && !string.Equals(row.Task.Status, "archived", StringComparison.OrdinalIgnoreCase)))
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

        TaskViewRow row;

        await _gate.WaitAsync(cancellationToken);

        try
        {
            // Ranked under the gate, so two adds — or an add and a pull folding
            // in — never read the same last rank.
            var change = NewTask(title.Trim(), _clock.GetUtcNow(), Today, NextOrder());
            row = new TaskViewRow(change.Id, change.UpdatedAt, DeletedAt: null, ServerTimestamp: 0, change.Task);

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
    /// Edits a task already in the view and pushes the whole of it.
    /// <para>
    /// The replica keeps one document per task and the later <c>UpdatedAt</c>
    /// wins whole (.devbook/arc42/adr/0005), so the phone cannot send the one
    /// field it changed: <paramref name="edit"/> is handed the task as the view
    /// holds it, and the document it answers with is stamped and queued entire,
    /// as outbox kind <c>task</c> under a new entry id of its own and the
    /// task's own id inside it. The same row is then written into the view at
    /// once, with server stamp 0, so the screen shows the edit before any network
    /// answers, and the replica's copy of the same write replaces it on the pull
    /// that follows delivery.
    /// </para>
    /// <para>
    /// The stamp is now, or one millisecond past the row's own when the phone's
    /// clock is behind the device that wrote it — an edit stamped earlier than the
    /// copy it was made from would lose to it on the replica and in this fold.
    /// </para>
    /// <para>
    /// Held under the pull's gate, as an add is, so a pull cannot fold an older
    /// copy over the edit between the queueing and the row.
    /// </para>
    /// </summary>
    /// <returns>The task's row as it now stands — unchanged when the edit
    /// changed nothing and nothing was queued — or null when the view holds no
    /// live task by that id.</returns>
    public async Task<TaskViewRow?> EditAsync(Guid id, Func<TaskPayload, TaskPayload?> edit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edit);

        TaskViewRow row;

        await _gate.WaitAsync(cancellationToken);

        try
        {
            TaskViewRow? current;
            lock (_rowsLock) current = _rows.GetValueOrDefault(id);

            if (current is null || current.DeletedAt is not null) return null;

            if (edit(current.Task) is not { } edited) return current;

            var now = _clock.GetUtcNow();
            var stamp = now > current.UpdatedAt ? now : current.UpdatedAt.AddMilliseconds(1);
            var change = new TaskChange(id, stamp, DeletedAt: null, edited);

            await _outbox.EnqueueAsync(TaskOutboxKind.Token, Guid.CreateVersion7(), TaskOutboxKind.Write(change), cancellationToken);

            row = new TaskViewRow(id, stamp, DeletedAt: null, ServerTimestamp: 0, edited);
            await _store.SaveAsync([row], cursor: null, cancellationToken);
            lock (_rowsLock) _rows[id] = row;
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
    /// is picking it, and ranked <paramref name="order"/>. Everything else is
    /// empty: the body, the repositories and the schedule are the desktop's to
    /// give it.
    /// </summary>
    public static TaskChange NewTask(string title, DateTimeOffset now, DateOnly today, int order = 0)
    {
        var payload = new TaskPayload(
            Title: title,
            ContentMd: string.Empty,
            Type: EnumMap.ToWire(EntryType.Task),
            Status: EnumMap.ToWire(EntryStatus.Draft),
            Priority: EnumMap.ToWire(Priority.Medium),
            Order: order,
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

    /// <summary>
    /// The rank a task the phone adds gets: one past the last live task the view
    /// holds, so it lands at the end of My Day — the capture sheet's "Lands in
    /// Anytime today" — on the phone and on the desktop alike, which ranks by the
    /// same number. The first task on an empty phone is ranked 0.
    /// <para>
    /// The end of what the phone has pulled: a task the desktop ranked last since
    /// that pull can share the rank, and the newer of the two then sorts first on
    /// both sides — the same order everywhere, one place off from the end.
    /// </para>
    /// </summary>
    private int NextOrder()
    {
        lock (_rowsLock)
        {
            var live = _rows.Values.Where(row => row.DeletedAt is null).ToList();
            return live.Count == 0 ? 0 : live.Max(row => row.Task.Order) + 1;
        }
    }

    /// <summary>
    /// The order My Day keeps, the same on the phone and the desktop
    /// (.devbook/domain/tasks/features.md#manual-ordering): the hand rank
    /// (<see cref="TaskPayload.Order"/>) first, and among tasks sharing a rank the
    /// newest first. The desktop's My Day is its task list narrowed to today's
    /// pick, and that list is read in this order — <c>ORDER BY sort_order,
    /// created_at DESC</c> in <c>SqliteTaskRepository.ListAsync</c> — and kept by
    /// the filter. The id breaks a tie the desktop leaves to the store, so the
    /// phone's list never reshuffles between pulls.
    /// </summary>
    private static IEnumerable<TaskViewRow> InMyDayOrder(IEnumerable<TaskViewRow> rows) =>
        rows
            .OrderBy(row => row.Task.Order)
            .ThenByDescending(row => row.Task.CreatedAt)
            .ThenByDescending(row => row.Id);
}
