using System.Security.Cryptography;

using Backlog.Mobile.UI.Notes;
using Backlog.Mobile.UI.Outbox;
using Backlog.Mobile.UI.Recordings;
using Backlog.Mobile.UI.TalkNotes;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The phone ends the app mid-meeting (.devbook/domain/inbox/requirements.md#recordings-on-a-note):
/// on the next start, every file left in <c>notes/recordings/</c> is attached to its note as
/// "Recording (interrupted)" and queued, and none is left behind — unless the phone does not
/// hold its note, when it stays where it is.
/// </summary>
public sealed class RecordingRecoveryTests : IDisposable
{
    private readonly RecordingFixture _fixture = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task A_file_left_by_a_killed_app_is_attached_to_its_note_as_interrupted()
    {
        var note = await _fixture.Notes.CreateAsync("Standup", "notes", cancellationToken: Cancellation);
        var bytes = AdtsStream.Of([200, 150, 300]);
        var left = _fixture.LeaveRecording(note.Id, 1, ".aac", bytes);

        var recovered = await _fixture.Recovery.RecoverAsync(Cancellation);

        Assert.Equal(1, recovered);
        var attached = Assert.Single(_fixture.Notes.Find(note.Id)!.Attachments);
        Assert.Equal(left.AttachmentId, attached.Id);
        Assert.Equal("Recording (interrupted)", attached.Name);
        Assert.Equal("audio/aac", attached.ContentType);
        Assert.Equal(bytes.Length, attached.SizeBytes);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), attached.Sha256);
        Assert.Equal(bytes, File.ReadAllBytes(_fixture.TalkNoteFiles.OutboxPath(attached.Id)));
        Assert.Empty(Directory.GetFiles(_fixture.Files.Folder));
    }

    [Fact]
    public async Task The_attached_recording_is_queued_with_its_note()
    {
        var note = await _fixture.Notes.CreateAsync("Standup", "notes", cancellationToken: Cancellation);
        var left = _fixture.LeaveRecording(note.Id, 1, ".aac", AdtsStream.Of([100]));

        await _fixture.Recovery.RecoverAsync(Cancellation);

        var queued = NoteOutboxKind.Read(_fixture.Outbox.Entries[^1]);
        Assert.Equal(note.Id, queued.Change.Id);
        Assert.Contains(queued.Change.Task.Attachments!, file => file.Id == left.AttachmentId);
    }

    [Fact]
    public async Task Every_part_a_killed_app_left_is_attached_in_order()
    {
        var note = await _fixture.Notes.CreateAsync("Standup", "notes", cancellationToken: Cancellation);
        var first = _fixture.LeaveRecording(note.Id, 1, ".aac", AdtsStream.Of([100]));
        var second = _fixture.LeaveRecording(note.Id, 2, ".aac", AdtsStream.Of([120]));

        Assert.Equal(2, await _fixture.Recovery.RecoverAsync(Cancellation));

        Assert.Equal([first.AttachmentId, second.AttachmentId], _fixture.Notes.Find(note.Id)!.Attachments.Select(file => file.Id));
    }

    [Fact]
    public async Task A_file_whose_note_the_phone_does_not_hold_stays_where_it_is()
    {
        var left = _fixture.LeaveRecording(Guid.CreateVersion7(), 1, ".aac", AdtsStream.Of([100]));

        var recovered = await _fixture.Recovery.RecoverAsync(Cancellation);

        Assert.Equal(0, recovered);
        Assert.True(File.Exists(left.Path));
        Assert.Empty(_fixture.Outbox.Entries);
    }

    [Fact]
    public async Task Recovering_twice_attaches_once()
    {
        var note = await _fixture.Notes.CreateAsync("Standup", "notes", cancellationToken: Cancellation);
        _fixture.LeaveRecording(note.Id, 1, ".aac", AdtsStream.Of([100]));

        await _fixture.Recovery.RecoverAsync(Cancellation);
        var again = await _fixture.Recovery.RecoverAsync(Cancellation);

        Assert.Equal(0, again);
        Assert.Single(_fixture.Notes.Find(note.Id)!.Attachments);
    }

    [Fact]
    public async Task A_note_whose_file_cannot_be_attached_does_not_hold_up_the_others()
    {
        var stuck = await _fixture.Notes.CreateAsync("Standup", "notes", cancellationToken: Cancellation);
        var fine = await _fixture.Notes.CreateAsync("Retro", "notes", cancellationToken: Cancellation);
        var blocked = _fixture.LeaveRecording(stuck.Id, 1, ".aac", AdtsStream.Of([100]));
        _fixture.LeaveRecording(fine.Id, 1, ".aac", AdtsStream.Of([100]));
        Directory.CreateDirectory(_fixture.TalkNoteFiles.OutboxPath(blocked.AttachmentId));

        var recovered = await _fixture.Recovery.RecoverAsync(Cancellation);

        Assert.Equal(1, recovered);
        Assert.Single(_fixture.Notes.Find(fine.Id)!.Attachments);
        Assert.Empty(_fixture.Notes.Find(stuck.Id)!.Attachments);
        Assert.True(File.Exists(blocked.Path));
    }

    [Fact]
    public async Task No_recordings_folder_is_nothing_to_recover()
    {
        Assert.False(Directory.Exists(_fixture.Files.Folder));

        Assert.Equal(0, await _fixture.Recovery.RecoverAsync(Cancellation));
        Assert.Empty(_fixture.Outbox.Entries);
    }
}

/// <summary>
/// The phone's notes over memory, with a sync service that does not answer, so whatever the
/// recording queues stays in the outbox to be read; and the recordings folder in a temp
/// folder of its own.
/// </summary>
internal sealed class RecordingFixture : IDisposable
{
    public RecordingFixture()
    {
        Clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero));
        Service = new ScriptedTaskService { State = InboxServiceState.Unreachable };

        var root = TestFolders.Create();
        TalkNoteFiles = new TalkNoteFiles(Path.Combine(root, "talk-notes"));
        Files = new RecordingFiles(Path.Combine(root, "notes", "recordings"));

        var sync = new CloudSyncClient(new HttpClient(new Handler(Service)) { BaseAddress = new Uri("https://sync.test") });
        Outbox = new DeviceOutbox(new InMemoryDeviceStore(), [new NoteOutboxKind(sync, TalkNoteFiles)], Clock);
        Notes = new NoteViewProjection(new InMemoryNoteViewStore(), Outbox, sync, Clock);
        Composer = new TalkNoteComposer(TalkNoteFiles, Outbox, Clock);
        Recovery = new RecordingRecovery(Files, Composer, Notes);
    }

    public FakeTimeProvider Clock { get; }

    public ScriptedTaskService Service { get; }

    public TalkNoteFiles TalkNoteFiles { get; }

    public RecordingFiles Files { get; }

    public DeviceOutbox Outbox { get; }

    public NoteViewProjection Notes { get; }

    public TalkNoteComposer Composer { get; }

    public RecordingRecovery Recovery { get; }

    /// <summary>A part file as a recorder that was killed mid-write leaves it.</summary>
    public RecordingPartFile LeaveRecording(Guid noteId, int part, string extension, byte[] bytes)
    {
        var file = Files.Target(noteId).NewPart(part, extension);
        File.WriteAllBytes(file.Path, bytes);
        return file;
    }

    public void Dispose()
    {
        Notes.Dispose();
        Outbox.Dispose();
    }

    private sealed class Handler(ScriptedTaskService service) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            service.AnswerAsync(request, cancellationToken);
    }
}
