using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Inbox.DomainModels;
using Backlog.Modules.Inbox.Features.ListPendingNotes;
using Backlog.Modules.Inbox.Features.GetInbox;
using Backlog.Modules.Inbox.Features.ReceiveNote;
using Backlog.Modules.Inbox.Features.RetryTranscription;
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
    public async Task The_transcriber_is_handed_the_downloaded_file_with_its_type()
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
    public async Task A_failed_transcription_leaves_the_body_and_the_file_and_counts_an_attempt()
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

        var failure = await _records.FindFailureAsync(recording.Sha256, Cancellation);
        Assert.Equal(new InboxTranscriptionFailureDto(recording.Sha256, note.Id, recording.Id, 1, "Part 1 of 1 could not be transcribed.", Now), failure);
    }

    [Fact]
    public async Task A_failed_save_marks_nothing_done_and_counts_an_attempt_so_the_next_start_tries_again()
    {
        var (note, recording) = NoteWithRecording();
        _store.FailOnSave = 1;

        Assert.Equal(0, await Handle(note.Id));

        Assert.Empty(_records.Saved);
        var failure = await _records.FindFailureAsync(recording.Sha256, Cancellation);
        Assert.Equal(1, failure?.Attempts);
        Assert.Equal(TranscribeRecordingsCommandHandler.SaveFailedReason, failure?.Reason);
    }

    [Fact]
    public async Task A_transcriber_that_throws_counts_an_attempt_with_a_fixed_reason_and_never_its_message()
    {
        var (note, recording) = NoteWithRecording(body: "Agenda.");
        _transcriber.Throw = new HttpRequestException("Foundry said: the launch code is 0000");

        Assert.Equal(0, await Handle(note.Id));

        var failure = await _records.FindFailureAsync(recording.Sha256, Cancellation);
        Assert.Equal(1, failure?.Attempts);
        Assert.Equal(TranscribeRecordingsCommandHandler.TranscriberFailedReason, failure?.Reason);
        Assert.DoesNotContain("launch code", failure!.Reason, StringComparison.Ordinal);
        Assert.Equal("Agenda.", _store.Items[note.Id].BodyMd);
    }

    [Fact]
    public async Task A_cancelled_take_up_counts_no_attempt()
    {
        var (note, recording) = NoteWithRecording();
        using var cancellation = new CancellationTokenSource();
        _transcriber.WhileTranscribing = cancellation.Cancel;
        _transcriber.Throw = new OperationCanceledException(cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new TranscribeRecordingsCommandHandler(_store, new FakeTimeProvider(Now), _records, _transcriber, _files)
                .Handle(new TranscribeRecordingsCommand(note.Id), cancellation.Token));

        Assert.Null(await _records.FindFailureAsync(recording.Sha256, Cancellation));
    }

    [Fact]
    public async Task A_downloaded_file_that_cannot_be_read_counts_an_attempt()
    {
        var (note, recording) = NoteWithRecording();
        _files.Files.Clear();

        Assert.Equal(0, await Handle(note.Id));

        Assert.Empty(_transcriber.Sent);
        Assert.Equal(TranscribeRecordingsCommandHandler.UnreadableReason, (await _records.FindFailureAsync(recording.Sha256, Cancellation))?.Reason);
    }

    [Fact]
    public async Task Nothing_is_sent_while_foundry_is_not_configured()
    {
        var (note, recording) = NoteWithRecording();
        _transcriber.IsAvailable = false;

        Assert.Equal(0, await Handle(note.Id));

        Assert.Empty(_transcriber.Sent);
        Assert.Contains(_store.Items[note.Id].Attachments, attachment => attachment.Id == recording.Id);
        Assert.Empty(await _records.ListFailuresAsync(Cancellation));
    }

    [Fact]
    public async Task A_picked_audio_file_is_neither_sent_nor_removed()
    {
        var (note, _) = NoteWithRecording(downloaded: false);
        var picked = new TestFile(Guid.CreateVersion7(), "standup.m4a", "audio/mp4", [0, 0, 0, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p']);
        note.RecordAttachments([Attachment(picked)]);
        note.MarkAttachmentDownloaded(picked.Id, Path(note, picked), Now);
        _files.Files[Path(note, picked)] = picked.Bytes;

        Assert.Equal(0, await Handle(note.Id));

        Assert.Empty(_transcriber.Sent);
        Assert.Contains(_store.Items[note.Id].Attachments, attachment => attachment.Id == picked.Id);
        Assert.Empty(await _records.ListFailuresAsync(Cancellation));
    }

    [Fact]
    public async Task The_third_failure_leaves_the_recording_waiting_for_retry_and_the_next_take_up_sends_nothing()
    {
        var (note, recording) = NoteWithRecording(body: "Agenda.");
        _transcriber.Answer = InboxErrors.TranscriptionFailed("Part 1 of 1 could not be transcribed.");

        for (var attempt = 0; attempt < InboxTranscriptionFailureDto.MaxAttempts; attempt++) await Handle(note.Id);
        Assert.True((await _records.FindFailureAsync(recording.Sha256, Cancellation))!.WaitsForRetry);

        Assert.Equal(0, await Handle(note.Id));

        Assert.Equal(InboxTranscriptionFailureDto.MaxAttempts, _transcriber.Sent.Count);
        Assert.Equal(3, (await _records.FindFailureAsync(recording.Sha256, Cancellation))!.Attempts);
        Assert.Equal("Agenda.", _store.Items[note.Id].BodyMd);
    }

    [Fact]
    public async Task A_recording_waiting_for_retry_holds_back_the_files_after_it()
    {
        var (note, first) = NoteWithRecording();
        var second = Recording(seed: 2, name: "Recording · part 2 · 3:00");
        note.RecordAttachments([Attachment(second)]);
        note.MarkAttachmentDownloaded(second.Id, Path(note, second), Now);
        _files.Files[Path(note, second)] = second.Bytes;
        _records.SeedFailure(first.Sha256, note.Id, first.Id, attempts: 3);

        Assert.Equal(0, await Handle(note.Id));

        Assert.Empty(_transcriber.Sent);
        Assert.Equal(2, _store.Items[note.Id].Attachments.Count);
    }

    [Fact]
    public async Task Retry_forgets_the_failures_and_queues_the_note_so_a_success_writes_it()
    {
        var (note, recording) = NoteWithRecording();
        _records.SeedFailure(recording.Sha256, note.Id, recording.Id, attempts: 3);
        var queue = new FakeQueue();

        var retried = await new RetryTranscriptionCommandHandler(_store, _records, queue)
            .Handle(new RetryTranscriptionCommand(note.Id, recording.Id), Cancellation);

        Assert.True(retried.IsSuccess);
        Assert.Equal([note.Id], queue.Requested);
        Assert.Null(await _records.FindFailureAsync(recording.Sha256, Cancellation));

        Assert.Equal(1, await Handle(note.Id));
        Assert.DoesNotContain(_store.Items[note.Id].Attachments, attachment => attachment.Id == recording.Id);
    }

    [Fact]
    public async Task A_success_after_a_failure_forgets_the_failure()
    {
        var (note, recording) = NoteWithRecording();
        _records.SeedFailure(recording.Sha256, note.Id, recording.Id, attempts: 2);

        Assert.Equal(1, await Handle(note.Id));

        Assert.Null(await _records.FindFailureAsync(recording.Sha256, Cancellation));
    }

    [Fact]
    public async Task Retry_on_a_file_the_note_does_not_hold_is_refused()
    {
        var (note, _) = NoteWithRecording();

        var retried = await new RetryTranscriptionCommandHandler(_store, _records, new FakeQueue())
            .Handle(new RetryTranscriptionCommand(note.Id, Guid.CreateVersion7()), Cancellation);

        Assert.Equal(InboxErrors.AttachmentNotFound, retried.Error);
    }

    [Fact]
    public async Task The_snapshot_carries_the_failure_of_a_recording_waiting_for_retry_only()
    {
        var (note, recording) = NoteWithRecording();
        var (other, early) = NoteWithRecording(seed: 7);
        _records.SeedFailure(recording.Sha256, note.Id, recording.Id, attempts: 3);
        _records.SeedFailure(early.Sha256, other.Id, early.Id, attempts: 1);

        var snapshot = await new GetInboxQueryHandler(_store, _store, _records).Handle(new GetInboxQuery(), Cancellation);

        var row = snapshot.Items.Single(item => item.Id == note.Id).Attachments.Single();
        Assert.Equal(3, row.TranscriptionFailure?.Attempts);
        Assert.Null(snapshot.Items.Single(item => item.Id == other.Id).Attachments.Single().TranscriptionFailure);
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
        var second = Recording(seed: 2, name: "Recording · part 2 · 3:00");
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
        var second = Recording(seed: 2, name: "Recording · part 2 · 3:00");
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
        Assert.Equal(isRecording, InboxAttachment.Named(Guid.CreateVersion7(), "Recording · 0:12", contentType, 1, new string('a', 64)).IsRecording);

    [Theory]
    [InlineData("Recording · 12:34", true)]
    [InlineData("Recording · part 2 · 1:02:03", true)]
    [InlineData("Recording (interrupted)", true)]
    [InlineData("standup.m4a", false)]
    [InlineData("Recording", false)]
    [InlineData("Recording.aac", false)]
    [InlineData("recording · 12:34", false)]
    [InlineData("Recording - 12:34", false)]
    public void Only_the_names_the_recorder_gives_are_recordings(string name, bool isRecording) =>
        Assert.Equal(isRecording, InboxAttachment.Named(Guid.CreateVersion7(), name, "audio/aac", 1, new string('a', 64)).IsRecording);

    private async Task<int> Handle(Guid noteId) =>
        (await new TranscribeRecordingsCommandHandler(_store, new FakeTimeProvider(Now), _records, _transcriber, _files)
            .Handle(new TranscribeRecordingsCommand(noteId), Cancellation)).Value;

    private (InboxItem Note, TestFile Recording) NoteWithRecording(string? body = null, bool downloaded = true, int seed = 1)
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

        var recording = Recording(seed);
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

    /// <summary>A recording the app made, by the recorder's name (local ADR 0049 §1).</summary>
    private static TestFile Recording(int seed = 1, string name = "Recording · 12:34") =>
        new(Guid.CreateVersion7(), name, "audio/aac", [0xFF, 0xF1, 0x60, 0x40, (byte)seed, 0x1F, 0xFC]);

    private static InboxAttachment Attachment(TestFile file) =>
        InboxAttachment.Named(file.Id, file.Name, file.ContentType, file.Bytes.Length, file.Sha256);

    private sealed class FakeTranscriber : IInboxRecordingTranscriber
    {
        public bool IsAvailable { get; set; } = true;

        public Result<InboxRecordingTranscriptDto> Answer { get; set; } =
            new InboxRecordingTranscriptDto("[00:00:00] We agreed the budget.", "The budget was agreed.\n\n- [ ] Send the minutes");

        public Action? WhileTranscribing { get; set; }

        public Exception? Throw { get; set; }

        public List<InboxRecordingDto> Sent { get; } = [];

        public Task<Result<InboxRecordingTranscriptDto>> TranscribeAsync(InboxRecordingDto recording, CancellationToken cancellationToken = default)
        {
            Sent.Add(recording);
            WhileTranscribing?.Invoke();
            return Throw is null ? Task.FromResult(Answer) : Task.FromException<Result<InboxRecordingTranscriptDto>>(Throw);
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

        private readonly Dictionary<string, InboxTranscriptionFailureDto> _failures = [];

        public void SeedFailure(string sha256, Guid noteId, Guid attachmentId, int attempts) =>
            _failures[sha256] = new(sha256, noteId, attachmentId, attempts, "Earlier.", Now);

        public Task<InboxTranscriptionFailureDto?> FindFailureAsync(string sha256, CancellationToken cancellationToken = default) =>
            Task.FromResult(_failures.GetValueOrDefault(sha256));

        public Task<InboxTranscriptionFailureDto> RecordFailureAsync(
            string sha256, Guid noteId, Guid attachmentId, string reason, DateTimeOffset failedAt, CancellationToken cancellationToken = default)
        {
            var failure = new InboxTranscriptionFailureDto(sha256, noteId, attachmentId, (_failures.GetValueOrDefault(sha256)?.Attempts ?? 0) + 1, reason, failedAt);
            _failures[sha256] = failure;
            return Task.FromResult(failure);
        }

        public Task ClearFailureAsync(string sha256, CancellationToken cancellationToken = default)
        {
            _failures.Remove(sha256);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<InboxTranscriptionFailureDto>> ListFailuresAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<InboxTranscriptionFailureDto>>([.. _failures.Values]);
    }

    private sealed class FakeQueue : IInboxRecordingQueue
    {
        public List<Guid> Requested { get; } = [];

        public void Request(Guid noteId) => Requested.Add(noteId);
    }
}
