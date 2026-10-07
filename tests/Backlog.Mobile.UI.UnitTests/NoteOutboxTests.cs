using System.Net;
using System.Net.Http.Json;

using Backlog.Mobile.UI.Notes;
using Backlog.Mobile.UI.Outbox;
using Backlog.Mobile.UI.TalkNotes;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// A note's outbox entry (.devbook/arc42/06-runtime-view.md#mobile-note-sync): the
/// files the edit added go up first, then the whole note document is pushed to the
/// task feed; a file the note already named is not sent again; and the payload reads
/// back as it was written.
/// </summary>
public sealed class NoteOutboxTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 9, 30, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly RecordingService _service = new();
    private readonly TalkNoteFiles _files = new(TestFolders.Create());
    private readonly DeviceOutbox _outbox;
    private readonly NoteViewProjection _notes;

    public NoteOutboxTests()
    {
        var sync = new CloudSyncClient(new HttpClient(_service) { BaseAddress = new Uri("https://sync.test") });
        _outbox = new DeviceOutbox(new InMemoryDeviceStore(), [new NoteOutboxKind(sync, _files)], _clock);
        _notes = new NoteViewProjection(new InMemoryNoteViewStore(), _outbox, sync, _clock);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _notes.Dispose();
        _outbox.Dispose();
    }

    [Fact]
    public async Task The_files_a_note_names_go_up_before_the_note_and_are_released_after()
    {
        var photo = await PlaceAsync("slide.jpg", "image/jpeg");

        await _notes.CreateAsync("Keynote", "notes", [photo], Cancellation);
        await _outbox.WhenIdleAsync();

        Assert.Equal(["PUT " + photo.Id, "POST note"], _service.Calls);
        var pushed = Assert.Single(_service.Pushed);
        Assert.Equal(photo, Assert.Single(pushed.Task.Attachments!));
        Assert.False(File.Exists(_files.OutboxPath(photo.Id)));
    }

    [Fact]
    public async Task An_edit_uploads_only_the_file_it_added()
    {
        var first = await PlaceAsync("first.pdf", "application/pdf");
        var note = await _notes.CreateAsync("Handouts", null, [first], Cancellation);
        await _outbox.WhenIdleAsync();

        var second = await PlaceAsync("second.pdf", "application/pdf");
        _clock.Advance(TimeSpan.FromMinutes(1));
        await _notes.EditAsync(note.Id, "Handouts", "both", [second], Cancellation);
        await _outbox.WhenIdleAsync();

        Assert.Equal(["PUT " + first.Id, "POST note", "PUT " + second.Id, "POST note"], _service.Calls);
        Assert.Equal([first.Id, second.Id], _service.Pushed[^1].Task.Attachments!.Select(file => file.Id));
    }

    [Fact]
    public async Task A_failed_push_keeps_the_upload_it_already_made()
    {
        var photo = await PlaceAsync("slide.jpg", "image/jpeg");
        _service.FailPushes = 1;

        await _notes.CreateAsync("Keynote", null, [photo], Cancellation);
        await _outbox.WhenIdleAsync();

        var waiting = Assert.Single(_outbox.Entries);
        Assert.Equal([photo.Id], NoteOutboxKind.Read(waiting).Uploaded);

        _clock.Advance(DeviceOutbox.FirstRetryDelay);
        await _outbox.WhenIdleAsync();

        Assert.Equal(["PUT " + photo.Id, "POST note", "POST note"], _service.Calls);
        Assert.Empty(_outbox.Entries);
    }

    [Fact]
    public void The_payload_reads_back_as_it_was_written()
    {
        var change = NoteViewProjection.NewNote(Guid.CreateVersion7(), "Standup", "text", [], Now);
        var json = NoteOutboxKind.Write(new NoteOutboxPayload(change, [], []));
        var entry = new OutboxEntry(Guid.CreateVersion7(), NoteOutboxKind.Token, json, 0, null, Now);

        Assert.Equal(json, NoteOutboxKind.Write(NoteOutboxKind.Read(entry)));
        Assert.Contains("\"type\":\"note\"", json, StringComparison.Ordinal);
    }

    private async Task<AttachmentMetadata> PlaceAsync(string name, string contentType)
    {
        Directory.CreateDirectory(_files.OutboxFolder);
        var id = Guid.CreateVersion7();
        var bytes = "bytes of "u8.ToArray().Concat(System.Text.Encoding.UTF8.GetBytes(name)).ToArray();
        await File.WriteAllBytesAsync(_files.OutboxPath(id), bytes, Cancellation);

        return new AttachmentMetadata(id, name, contentType, bytes.LongLength, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)));
    }

    /// <summary>The service's attachment upload and task push, recorded in the
    /// order they arrive.</summary>
    private sealed class RecordingService : HttpMessageHandler
    {
        private readonly Lock _lock = new();

        public List<string> Calls { get; } = [];

        public List<TaskChange> Pushed { get; } = [];

        public int FailPushes { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Put)
            {
                lock (_lock) Calls.Add("PUT " + request.RequestUri!.Segments[^1]);
                return new HttpResponseMessage(HttpStatusCode.Created);
            }

            var push = (await request.Content!.ReadFromJsonAsync<PushTasksRequest>(cancellationToken))!;

            lock (_lock)
            {
                Calls.Add("POST " + string.Join(",", push.Tasks.Select(change => change.Task.Type)));

                if (FailPushes > 0)
                {
                    FailPushes--;
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = JsonContent.Create(new { detail = "warming up" }) };
                }

                Pushed.AddRange(push.Tasks);
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new PushTasksResponse(push.Tasks.Count)) };
        }
    }
}
