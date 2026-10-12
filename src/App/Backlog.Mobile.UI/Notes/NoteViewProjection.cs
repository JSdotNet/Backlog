using Backlog.Mobile.UI.Outbox;
using Backlog.Mobile.UI.Services;
using Backlog.Mobile.UI.TalkNotes;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.Notes;

/// <summary>
/// The phone's notes (<c>.devbook/arc42/06-runtime-view.md#mobile-note-sync</c>):
/// every note document on the task feed folded into <see cref="INoteViewStore"/>,
/// and the two writes the phone makes, creating a note and editing one, each
/// written into the view at once and queued through the <see cref="DeviceOutbox"/>.
/// <para>
/// A projection plus a push, as <see cref="Tasks.TaskViewProjection"/> is: the
/// phone keeps no domain rules. A note is an Inbox item, and whatever the replica
/// says last is what the row says. The phone never archives a note and never
/// sends a tombstone: triage stays on the desktop
/// (<c>.devbook/domain/inbox/features.md#triage-stays-on-the-desktop</c>).
/// </para>
/// <para>
/// One per device, a singleton, for the reason the task projection is one.
/// </para>
/// </summary>
public sealed class NoteViewProjection : IDisposable
{
    private readonly INoteViewStore _store;
    private readonly DeviceOutbox _outbox;
    private readonly CloudSyncClient _sync;
    private readonly TimeProvider _clock;

    /// <summary>Held by a pull and by a write, so a write's own row can never be
    /// written over by a pull that finished first.</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _rowsLock = new();
    private readonly Dictionary<Guid, NoteViewRow> _rows;

    public NoteViewProjection(INoteViewStore store, DeviceOutbox outbox, CloudSyncClient sync, TimeProvider clock)
    {
        _store = store;
        _outbox = outbox;
        _sync = sync;
        _clock = clock;

        // Read once, here: the list is drawn from this before any pull answers.
        _rows = store.ReadRows().ToDictionary(row => row.Id);
    }

    /// <summary>Raised when a row was added, changed or hidden. May be raised off
    /// the render thread.</summary>
    public event Action? Changed;

    /// <summary>Every note still on the phone, newest change first. A note the
    /// desktop archived or deleted is not among them.</summary>
    public IReadOnlyList<NoteViewRow> Notes()
    {
        lock (_rowsLock)
        {
            return [.. _rows.Values.Where(row => !row.IsHidden).OrderByDescending(row => row.UpdatedAt)];
        }
    }

    /// <summary>The note, or null when the phone has none by that id or the
    /// desktop has archived it.</summary>
    public NoteViewRow? Find(Guid id)
    {
        lock (_rowsLock) return _rows.GetValueOrDefault(id) is { IsHidden: false } row ? row : null;
    }

    /// <summary>
    /// Pulls the task feed from the note cursor until the service says it is
    /// drained, folding each page's notes and keeping them and the cursor after
    /// them as one write. Its own cursor, apart from the task view's, so neither
    /// projection's progress depends on the other's. A cursor the service will
    /// not take is dropped and the pull starts again from the beginning, once, as
    /// the task view's does; any other failure is thrown for the screen to word.
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

                // Folded into a copy and taken only once the page is kept: a save
                // that fails leaves the rows as the file has them, so the retry
                // folds the same page to the same changes and writes them.
                Dictionary<Guid, NoteViewRow> folded;
                lock (_rowsLock) folded = new Dictionary<Guid, NoteViewRow>(_rows);
                var changed = NoteFold.Apply(folded, page.Tasks ?? []);

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

    /// <summary>
    /// Creates a note. It is queued before anything else, since the outbox is the
    /// promise to send it, and then written into the view, so it is in the list the
    /// moment this returns, with or without a network.
    /// <paramref name="attachments"/> name files already placed in the outbox
    /// folder (<see cref="TalkNoteFiles.OutboxPath"/>), which the entry uploads
    /// ahead of the note. A blank title is made from the body or the files, as a
    /// talk note's is.
    /// </summary>
    public async Task<NoteViewRow> CreateAsync(
        string? title,
        string? body,
        IReadOnlyList<AttachmentMetadata>? attachments = null,
        CancellationToken cancellationToken = default)
    {
        var files = attachments ?? [];
        var change = NewNote(Guid.CreateVersion7(), TitleOf(title, body, files), Clean(body), files, _clock.GetUtcNow());

        var row = await WriteAsync(_ => (change, [.. files.Select(file => file.Id)]), cancellationToken);

        return row!;
    }

    /// <summary>
    /// Edits a note the phone holds: a new title and body, and files added to the
    /// ones it already names. Written into the view at once and queued whole under
    /// a stamp of now, so it wins over every earlier copy. Answers null for a note
    /// the phone does not hold, or one the desktop has archived: an archived note
    /// has stopped syncing.
    /// </summary>
    public Task<NoteViewRow?> EditAsync(
        Guid id,
        string? title,
        string? body,
        IReadOnlyList<AttachmentMetadata>? addedAttachments = null,
        CancellationToken cancellationToken = default) =>
        // Built under the gate, from the row as it stands then: a pull that hid
        // the note or brought a newer copy cannot land between the read and the
        // write.
        WriteAsync(
            rows =>
            {
                if (rows.GetValueOrDefault(id) is not { IsHidden: false } current) return null;

                var added = (addedAttachments ?? []).Where(file => current.Attachments.All(held => held.Id != file.Id)).ToList();
                IReadOnlyList<AttachmentMetadata> files = [.. current.Attachments, .. added];
                var now = _clock.GetUtcNow();

                var payload = current.Note with
                {
                    Title = TitleOf(title, body, files),
                    ContentMd = Clean(body),
                    Type = NoteFold.NoteType,
                    Attachments = files.Count > 0 ? files : null,
                };

                // Never earlier than the copy it replaces, even on a phone whose
                // clock is behind the desktop's: an edit that lost to its own
                // original would be undone on the next pull.
                var stamp = now > current.UpdatedAt ? now : current.UpdatedAt.AddTicks(1);

                return (new TaskChange(id, stamp, DeletedAt: null, payload), [.. added.Select(file => file.Id)]);
            },
            cancellationToken);

    /// <summary>
    /// The note document the phone creates: a new id, the note kind token, the
    /// body as <c>ContentMd</c>, the phone's channel in <c>SourceInboxId</c> as a
    /// capture's is, its files as metadata, and the Tasks defaults for status and
    /// priority, so no field but the type is unusual.
    /// </summary>
    public static TaskChange NewNote(Guid id, string title, string body, IReadOnlyList<AttachmentMetadata> attachments, DateTimeOffset now)
    {
        var payload = new TaskPayload(
            Title: title,
            ContentMd: body,
            Type: NoteFold.NoteType,
            Status: "draft",
            Priority: "medium",
            Order: 0,
            Area: null,
            CreatedAt: now,
            SourceInboxId: CaptureOutboxKind.Source,
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
            ProjectionRefs: [],
            Attachments: attachments.Count > 0 ? attachments : null);

        return new TaskChange(id, now, DeletedAt: null, payload);
    }

    public void Dispose() => _gate.Dispose();

    /// <summary>Builds the write from the rows under the gate, queues it, and
    /// writes its row. Answers null, and writes nothing, when the build does.</summary>
    private async Task<NoteViewRow?> WriteAsync(
        Func<IReadOnlyDictionary<Guid, NoteViewRow>, (TaskChange Change, IReadOnlyList<Guid> ToUpload)?> build,
        CancellationToken cancellationToken)
    {
        NoteViewRow row;

        await _gate.WaitAsync(cancellationToken);

        try
        {
            (TaskChange Change, IReadOnlyList<Guid> ToUpload)? write;
            lock (_rowsLock) write = build(_rows);

            if (write is not { } built) return null;

            var (change, toUpload) = built;

            row = new NoteViewRow(change.Id, change.UpdatedAt, DeletedAt: null, ServerTimestamp: 0, change.Task);

            await _outbox.EnqueueAsync(
                NoteOutboxKind.Token,
                Guid.CreateVersion7(),
                NoteOutboxKind.Write(new NoteOutboxPayload(change, toUpload, [])),
                cancellationToken);

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

    private string TitleOf(string? title, string? body, IReadOnlyList<AttachmentMetadata> files) =>
        TalkNoteTitle.For(
            title,
            body,
            [.. files.Select(file => (file.Name, file.ContentType))],
            DateOnly.FromDateTime(_clock.GetLocalNow().DateTime));

    private static string Clean(string? body) => body?.Trim() ?? string.Empty;

    private static bool IsCursorRejection(string? code) =>
        code is SyncErrorCodes.SyncCursorMalformed or SyncErrorCodes.SyncCursorExpired;
}
