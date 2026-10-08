using Backlog.Mobile.UI.Notes;
using Backlog.Mobile.UI.Outbox;
using Backlog.Mobile.UI.TalkNotes;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The phone's notes (.devbook/arc42/06-runtime-view.md#mobile-note-sync): a note
/// created or edited here is in the list at once and goes out through the outbox as
/// a whole note document; a pull folds the desktop's edits in and drops a note the
/// desktop archived; and the phone never sends a tombstone.
/// </summary>
public sealed class NoteViewProjectionTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 9, 30, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly ScriptedTaskService _service = new();
    private readonly InMemoryNoteViewStore _store = new();
    private readonly DeviceOutbox _outbox;
    private readonly NoteViewProjection _notes;

    public NoteViewProjectionTests()
    {
        var sync = new CloudSyncClient(new HttpClient(new Handler(_service)) { BaseAddress = new Uri("https://sync.test") });
        _outbox = new DeviceOutbox(new InMemoryDeviceStore(), [new NoteOutboxKind(sync, new TalkNoteFiles(TestFolders.Create()))], _clock);
        _notes = new NoteViewProjection(_store, _outbox, sync, _clock);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _notes.Dispose();
        _outbox.Dispose();
    }

    [Fact]
    public async Task A_new_note_is_in_the_list_at_once_and_pushed_as_a_note_document()
    {
        var row = await _notes.CreateAsync("Standup", "- shipped sync", cancellationToken: Cancellation);

        Assert.Equal(row, Assert.Single(_notes.Notes()));
        Assert.Equal(0, row.ServerTimestamp);
        Assert.Equal(Now, row.UpdatedAt);

        await _outbox.WhenIdleAsync();

        var pushed = Assert.Single(_service.Pushed);
        Assert.Equal(row.Id, pushed.Id);
        Assert.Equal(NoteFold.NoteType, pushed.Task.Type);
        Assert.Equal("Standup", pushed.Task.Title);
        Assert.Equal("- shipped sync", pushed.Task.ContentMd);
        Assert.Equal(CaptureOutboxKind.Source, pushed.Task.SourceInboxId);
        Assert.Null(pushed.DeletedAt);
        Assert.Empty(_outbox.Entries);
    }

    [Fact]
    public async Task A_note_with_no_title_is_named_after_its_first_line()
    {
        var row = await _notes.CreateAsync("  ", "Call the venue\nabout the projector", cancellationToken: Cancellation);

        Assert.Equal("Call the venue", row.Title);
    }

    [Fact]
    public async Task An_edit_is_written_at_once_and_pushed_whole_under_a_later_stamp()
    {
        _service.State = InboxServiceState.Unreachable;
        var created = await _notes.CreateAsync("Standup", "first", cancellationToken: Cancellation);

        _clock.Advance(TimeSpan.FromMinutes(3));
        var edited = await _notes.EditAsync(created.Id, "Standup", "first\nsecond", cancellationToken: Cancellation);

        Assert.NotNull(edited);
        Assert.Equal("first\nsecond", Assert.Single(_notes.Notes()).Body);
        Assert.Equal(Now.AddMinutes(3), edited.UpdatedAt);

        // Offline, both wait in order: an edit is an entry of its own, not a
        // rewrite of the one ahead of it.
        Assert.Equal(2, _outbox.Entries.Count);
        Assert.All(_outbox.Entries, entry => Assert.Equal(NoteOutboxKind.Token, entry.Kind));
        Assert.Equal(["first", "first\nsecond"], _outbox.Entries.Select(entry => NoteOutboxKind.Read(entry).Change.Task.ContentMd));
        Assert.All(_outbox.Entries, entry => Assert.Equal(created.Id, NoteOutboxKind.Read(entry).Change.Id));

        _service.State = InboxServiceState.Answering;
        await _outbox.ResumeAsync(Cancellation);

        // The scripted service records the attempts it refused too; what landed
        // is the last two, in the order they were made.
        Assert.Equal(["first", "first\nsecond"], _service.Pushed.TakeLast(2).Select(change => change.Task.ContentMd));
        Assert.Empty(_outbox.Entries);
    }

    [Fact]
    public async Task A_pull_brings_the_desktops_edit_and_ignores_every_other_kind()
    {
        var id = Guid.CreateVersion7();
        _service.Append(NoteFoldTests.Note("Standup", Now, id, body: "phone text"));
        _service.Append(TestTasks.Task("A task", Now));
        _service.Append(NoteFoldTests.Note("Standup (edited)", Now.AddMinutes(10), id, body: "desktop text"));

        await _notes.PullAsync(Cancellation);

        var note = Assert.Single(_notes.Notes());
        Assert.Equal("Standup (edited)", note.Title);
        Assert.Equal("desktop text", note.Body);
        Assert.Equal("pos:3", _store.ReadCursor());
    }

    [Fact]
    public async Task A_note_the_desktop_archived_leaves_the_list_and_cannot_be_edited()
    {
        var created = await _notes.CreateAsync("Standup", "text", cancellationToken: Cancellation);
        await _outbox.WhenIdleAsync();
        _service.Append(NoteFoldTests.Note("Standup", Now.AddMinutes(5), created.Id, deletedAt: Now.AddMinutes(5)));

        await _notes.PullAsync(Cancellation);

        Assert.Empty(_notes.Notes());
        Assert.Null(_notes.Find(created.Id));
        Assert.Null(await _notes.EditAsync(created.Id, "Again", "nope", cancellationToken: Cancellation));
        Assert.All(_service.Pushed, change => Assert.Null(change.DeletedAt));
    }

    [Fact]
    public async Task An_edit_is_never_stamped_before_the_copy_it_replaces()
    {
        // A desktop clock ahead of the phone's: the pulled copy is stamped later
        // than the phone's now.
        var id = Guid.CreateVersion7();
        _service.Append(NoteFoldTests.Note("From the desktop", Now.AddHours(1), id));
        await _notes.PullAsync(Cancellation);

        var edited = await _notes.EditAsync(id, "Edited on the phone", null, cancellationToken: Cancellation);

        Assert.NotNull(edited);
        Assert.True(edited.UpdatedAt > Now.AddHours(1));
    }

    [Fact]
    public async Task Rows_kept_in_the_store_are_listed_before_any_pull()
    {
        var id = Guid.CreateVersion7();
        var kept = NoteFoldTests.Note("Kept", Now, id);
        await _store.SaveAsync([new NoteViewRow(id, Now, null, 4, kept.Task)], "pos:1", Cancellation);

        using var reopened = new NoteViewProjection(_store, _outbox, new CloudSyncClient(new HttpClient(new Handler(_service)) { BaseAddress = new Uri("https://sync.test") }), _clock);

        Assert.Equal("Kept", Assert.Single(reopened.Notes()).Title);
    }

    private sealed class Handler(ScriptedTaskService service) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            service.AnswerAsync(request, cancellationToken);
    }
}

/// <summary>The note view in memory, for the tests that are about the fold and the
/// writes rather than the file.</summary>
internal sealed class InMemoryNoteViewStore : INoteViewStore
{
    private readonly Lock _lock = new();
    private readonly Dictionary<Guid, NoteViewRow> _rows = [];
    private string? _cursor;

    public IReadOnlyList<NoteViewRow> ReadRows()
    {
        lock (_lock) return [.. _rows.Values];
    }

    public string? ReadCursor()
    {
        lock (_lock) return _cursor;
    }

    public Task SaveAsync(IReadOnlyList<NoteViewRow> rows, string? cursor = null, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            foreach (var row in rows) _rows[row.Id] = row;
            if (cursor is not null) _cursor = cursor;
        }

        return Task.CompletedTask;
    }

    public Task ResetCursorAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock) _cursor = null;
        return Task.CompletedTask;
    }
}
