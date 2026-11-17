using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Inbox.DomainModels;
using Backlog.Modules.Inbox.Features.ListPendingNotes;
using Backlog.Modules.Inbox.Features.ReceiveNote;
using Backlog.Modules.Inbox.Features.TranscribeRecordings;
using Backlog.Modules.Inbox.Services;
using Backlog.SharedKernel.Results;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Inbox.UnitTests;

/// <summary>
/// A meeting recording on a note, from download to removal (local ADR 0049): transcribed
/// once per sha256, the transcript and summary appended to the body as it is after the
/// transcription — never replacing it — the recording taken off the note in the same save
/// and pushed, nothing changed when anything fails, and a phone edit that undoes it
/// repaired from the kept sections.
/// </summary>
public sealed class TranscribeRecordingsTests
{
    private static readonly DateTimeOffset Now = Items.Noon.AddHours(1);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private readonly InMemoryInboxStore _store = new();
    private readonly FakeAttachmentFiles _files = new();
    private readonly FakeRecords _records = new();
    private readonly FakeTranscriber _transcriber = new();

    [Fact]
    public async Task A_downloaded_recording_is_appended_as_transcript_and_summary_and_removed_from_the_note()
    {
        var (note, recording) = NoteWithRecording(body: "Agenda: budget.");

        var written = await Handle(note.Id);

        Assert.Equal(1, written);
        var saved = _store.Items[note.Id];
        Assert.Equal(
            "Agenda: budget.\n\n## Transcript\n\n[00:00:00] We agreed the budget.\n\n## Summary\n\nThe budget was agreed.\n\n- [ ] Send the minutes",
            saved.BodyMd);
        Assert.DoesNotContain(saved.Attachments, attachment => attachment.Id == recording.Id);
        Assert.True(saved.NotePushPending);
        Assert.True(saved.EditedAt > Items.Noon);

        var record = Assert.Single(_records.Saved);
        Assert.Equal(recording.Sha256, record.Sha256);
        Assert.Equal(note.Id, record.NoteId);
        Assert.EndsWith("- [ ] Send the minutes", record.SectionsMd);
    }

    [Fact]
    public async Task The_audio_sent_is_the_file_as_it_lies_with_its_type()
    {
        var (note, recording) = NoteWithRecording();

        await Handle(note.Id);

        var sent = Assert.Single(_transcriber.Sent);
        Assert.Equal(recording.Bytes, sent.Audio.ToArray());
        Assert.Equal("audio/aac", sent.ContentType);
    }

    [Fact]
    public async Task An_edit_made_while_the_recording_was_out_is_kept_under_the_transcript()
    {
        var (note, _) = NoteWithRecording(body: "Before.");
        _transcriber.WhileTranscribing = () => _store.Items[note.Id].ApplyNote("Standup", "Before. And a phone edit.", Items.Noon.AddMinutes(30));

        await Handle(note.Id);

        Assert.StartsWith("Before. And a phone edit.\n\n## Transcript\n\n", _store.Items[note.Id].BodyMd);
    }

    [Fact]
    public async Task A_failed_transcription_leaves_the_body_and_the_file_and_records_nothing()
    {
        var (note, recording) = NoteWithRecording(body: "Agenda.");
        _transcriber.Answer = InboxErrors.TranscriptionFailed("Part 1 of 1 could not be transcribed.");

        var written = await Handle(note.Id);

        Assert.Equal(0, written);
        var kept = _store.Items[note.Id];
        Assert.Equal("Agenda.", kept.BodyMd);
        Assert.Contains(kept.Attachments, attachment => attachment.Id == recording.Id);
        Assert.False(kept.NotePushPending);
        Assert.Empty(_records.Saved);
    }

    [Fact]
    public async Task A_failed_write_records_nothing_so_the_next_start_tries_again()
    {
        var (note, _) = NoteWithRecording();
        _store.FailOnSave = 1;

        await Assert.ThrowsAsync<IOException>(() => Handle(note.Id));

        Assert.Empty(_records.Saved);
    }

    [Fact]
    public async Task Nothing_is_sent_while_foundry_is_not_configured()
    {
        var (note, recording) = NoteWithRecording();
        _transcriber.IsAvailable = false;

        Assert.Equal(0, await Handle(note.Id));

        Assert.Empty(_transcriber.Sent);
        Assert.Contains(_store.Items[note.Id].Attachments, attachment => attachment.Id == recording.Id);
    }

    [Fact]
    public async Task A_recording_not_yet_downloaded_and_a_photo_are_not_sent()
    {
        var (note, _) = NoteWithRecording(downloaded: false);
        var photo = TestFile.Jpeg();
        note.RecordAttachments([Attachment(photo)]);
        note.MarkAttachmentDownloaded(photo.Id, _files.PathFor(note.Id, photo.Name), Now);

        Assert.Equal(0, await Handle(note.Id));

        Assert.Empty(_transcriber.Sent);
    }

    [Fact]
    public async Task A_sha256_already_transcribed_is_never_sent_again()
    {
        var (note, recording) = NoteWithRecording();
        _records.Seed(recording.Sha256, note.Id, recording.Id, "## Transcript\n\n[00:00:00] Earlier.\n\n## Summary\n\nEarlier.");

        await Handle(note.Id);

        Assert.Empty(_transcriber.Sent);
        Assert.DoesNotContain(_store.Items[note.Id].Attachments, attachment => attachment.Id == recording.Id);
    }

    [Fact]
    public async Task A_phone_edit_that_dropped_the_sections_and_named_the_file_again_is_repaired()
    {
        var (note, recording) = NoteWithRecording(body: "Agenda.");
        await Handle(note.Id);
        var sections = Assert.Single(_records.Saved).SectionsMd;

        // The phone's copy, edited on the body before the transcript, arrives later.
        var restored = _store.Items[note.Id];
        restored.ApplyNote("Standup", "Agenda. Edited on the phone.", restored.EditedAt.AddMinutes(1));
        restored.RecordAttachments([Attachment(recording)]);

        Assert.Equal(1, await Handle(note.Id));

        var repaired = _store.Items[note.Id];
        Assert.Equal("Agenda. Edited on the phone.\n\n" + sections, repaired.BodyMd);
        Assert.DoesNotContain(repaired.Attachments, attachment => attachment.Id == recording.Id);
        Assert.Single(_transcriber.Sent);
    }

    [Fact]
    public async Task A_repaired_note_that_is_already_whole_is_left_alone()
    {
        var (note, _) = NoteWithRecording();
        await Handle(note.Id);
        var edited = _store.Items[note.Id].EditedAt;

        Assert.Equal(0, await Handle(note.Id));

        Assert.Equal(edited, _store.Items[note.Id].EditedAt);
    }

    [Fact]
    public async Task A_rolled_over_recordings_second_file_appends_as_part_2()
    {
        var (note, first) = NoteWithRecording();
        var second = Recording(seed: 2, name: "Recording_p2.aac");
        note.RecordAttachments([Attachment(second)]);
        note.MarkAttachmentDownloaded(second.Id, Path(note, second), Now);
        _files.Files[Path(note, second)] = second.Bytes;

        Assert.Equal(2, await Handle(note.Id));

        var body = _store.Items[note.Id].BodyMd;
        Assert.Contains("## Transcript\n\n", body);
        Assert.Contains("## Summary (part 2)\n\n", body);
        Assert.True(body.IndexOf("## Transcript (part 2)", StringComparison.Ordinal) > body.IndexOf("## Summary\n", StringComparison.Ordinal));
        Assert.Empty(_store.Items[note.Id].Attachments);
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public async Task A_first_file_that_fails_holds_back_the_second_so_the_parts_keep_their_order()
    {
        var (note, first) = NoteWithRecording();
        var second = Recording(seed: 2, name: "Recording_p2.aac");
        note.RecordAttachments([Attachment(second)]);
        note.MarkAttachmentDownloaded(second.Id, Path(note, second), Now);
        _files.Files[Path(note, second)] = second.Bytes;
        _transcriber.Answer = InboxErrors.TranscriptionFailed("Part 1 of 1 could not be transcribed.");

        Assert.Equal(0, await Handle(note.Id));

        Assert.Single(_transcriber.Sent);
        Assert.Equal(2, _store.Items[note.Id].Attachments.Count);
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void Sections_already_in_a_body_saved_with_crlf_are_not_appended_again()
    {
        var (note, recording) = NoteWithRecording(body: "Agenda.\r\n\r\n## Transcript\r\n\r\nx");

        Assert.True(note.AppendTranscript(recording.Id, "## Transcript\n\nx", Now));

        Assert.Equal("Agenda.\r\n\r\n## Transcript\r\n\r\nx", note.BodyMd);
        Assert.Empty(note.Attachments);
    }

    [Fact]
    public async Task An_archived_note_is_not_written()
    {
        var (note, _) = NoteWithRecording();
        note.Archive(Now);

        Assert.Equal(0, await Handle(note.Id));
        Assert.Empty(_transcriber.Sent);
    }

    [Fact]
    public async Task A_pulled_note_with_a_recording_is_handed_to_the_queue()
    {
        var queue = new FakeQueue();
        var recording = Recording();
        var source = new FakeAttachmentSource().Holding(recording);
        var replication = new InboxNoteReplication(
            new ReceiveNoteCommandHandler(_store, new FakeTimeProvider(Now), source, _files, recordings: queue),
            new ListPendingNotesQueryHandler(_store),
            new MarkNotesPushedCommandHandler(_store));
        var id = Guid.CreateVersion7();

        await replication.ReceiveAsync(
            new InboxNoteDto(id, "Standup", null, "mobile", Items.Noon, Items.Noon, Attachments: [recording.Named]),
            Cancellation);
        await replication.ReceiveAsync(new InboxNoteDto(Guid.CreateVersion7(), "No audio", null, "mobile", Items.Noon, Items.Noon), Cancellation);

        Assert.Equal([id], queue.Requested);
        Assert.True(_store.Items[id].Attachments.Single().IsDownloaded);
    }

    [Fact]
    public void Appending_a_transcript_never_replaces_the_body_and_a_second_call_changes_nothing()
    {
        var (note, recording) = NoteWithRecording(body: "Keep me.");

        Assert.True(note.AppendTranscript(recording.Id, "## Transcript\n\nx", Now));
        Assert.False(note.AppendTranscript(recording.Id, "## Transcript\n\nx", Now.AddMinutes(1)));

        Assert.Equal("Keep me.\n\n## Transcript\n\nx", note.BodyMd);
    }

    [Theory]
    [InlineData("audio/aac", true)]
    [InlineData("audio/mp4", true)]
    [InlineData("AUDIO/X-M4A", true)]
    [InlineData("audio/webm", false)]
    [InlineData("image/jpeg", false)]
    public void Only_the_recording_types_are_recordings(string contentType, bool isRecording) =>
        Assert.Equal(isRecording, InboxAttachment.Named(Guid.CreateVersion7(), "f", contentType, 1, new string('a', 64)).IsRecording);

    private async Task<int> Handle(Guid noteId) =>
        (await new TranscribeRecordingsCommandHandler(_store, new FakeTimeProvider(Now), _records, _transcriber, _files)
            .Handle(new TranscribeRecordingsCommand(noteId), Cancellation)).Value;

    private (InboxItem Note, TestFile Recording) NoteWithRecording(string? body = null, bool downloaded = true)
    {
        var note = InboxItem.FromCapture(
            Guid.CreateVersion7(),
            "Standup",
            new InboxSource("mobile", null),
            sourceUrl: null,
            ContentKind.Note,
            capturedAt: Items.Noon,
            receivedAt: Items.Noon.AddMinutes(5),
            bodyMd: body,
            replicaBacked: true);
        note.StampEdited(Items.Noon);

        var recording = Recording();
        note.RecordAttachments([Attachment(recording)]);
        if (downloaded)
        {
            note.MarkAttachmentDownloaded(recording.Id, Path(note, recording), Now);
            _files.Files[Path(note, recording)] = recording.Bytes;
        }

        _store.Seed(note);
        return (note, recording);
    }

    private string Path(InboxItem note, TestFile file) => _files.PathFor(note.Id, file.Name);

    private static TestFile Recording(int seed = 1, string name = "Recording.aac") =>
        new(Guid.CreateVersion7(), name, "audio/aac", [0xFF, 0xF1, 0x60, 0x40, (byte)seed, 0x1F, 0xFC]);

    private static InboxAttachment Attachment(TestFile file) =>
        InboxAttachment.Named(file.Id, file.Name, file.ContentType, file.Bytes.Length, file.Sha256);

    private sealed class FakeTranscriber : IInboxRecordingTranscriber
    {
        public bool IsAvailable { get; set; } = true;

        public Result<InboxRecordingTranscriptDto> Answer { get; set; } =
            new InboxRecordingTranscriptDto("[00:00:00] We agreed the budget.", "The budget was agreed.\n\n- [ ] Send the minutes");

        public Action? WhileTranscribing { get; set; }

        public List<InboxRecordingDto> Sent { get; } = [];

        public Task<Result<InboxRecordingTranscriptDto>> TranscribeAsync(InboxRecordingDto recording, CancellationToken cancellationToken = default)
        {
            Sent.Add(recording);
            WhileTranscribing?.Invoke();
            return Task.FromResult(Answer);
        }
    }

    private sealed class FakeRecords : IInboxTranscriptionRecords
    {
        private readonly Dictionary<string, InboxTranscriptionRecordDto> _records = [];

        public List<InboxTranscriptionRecordDto> Saved { get; } = [];

        public void Seed(string sha256, Guid noteId, Guid attachmentId, string sections) =>
            _records[sha256] = new(sha256, noteId, attachmentId, sections, Now);

        public Task<InboxTranscriptionRecordDto?> FindAsync(string sha256, CancellationToken cancellationToken = default) =>
            Task.FromResult(_records.GetValueOrDefault(sha256));

        public Task SaveAsync(InboxTranscriptionRecordDto record, CancellationToken cancellationToken = default)
        {
            Saved.Add(record);
            _records[record.Sha256] = record;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeQueue : IInboxRecordingQueue
    {
        public List<Guid> Requested { get; } = [];

        public void Request(Guid noteId) => Requested.Add(noteId);
    }
}
