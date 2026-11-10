using Backlog.Mobile.UI.Recordings;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// A stopped recording (.devbook/domain/inbox/requirements.md#recordings-on-a-note): Stop adds
/// each part to the note it was started on, in order, as "Recording · &lt;duration&gt;" in AAC;
/// whatever goes wrong is said in words rather than thrown; and the recording belongs to the
/// session, not the page, so it is attached however the page was left.
/// </summary>
public sealed class NoteRecordingSessionTests : IAsyncDisposable
{
    private readonly RecordingFixture _fixture = new();
    private readonly ScriptedMeetingRecorder _recorder = new();
    private readonly NoteRecordingSession _session;

    public NoteRecordingSessionTests() =>
        _session = new NoteRecordingSession(_recorder, _fixture.Files, _fixture.Composer, _fixture.Notes);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        await _session.DisposeAsync();
        _fixture.Dispose();
    }

    [Fact]
    public async Task Stop_attaches_the_recording_to_the_note_it_was_started_on()
    {
        var note = await _fixture.Notes.CreateAsync("Standup", "notes", cancellationToken: Cancellation);
        var (bytes, _) = _recorder.Parts[0];

        Assert.True(await _session.StartAsync(note.Id, Cancellation));
        await _session.StopAsync(Cancellation);

        var attached = Assert.Single(_fixture.Notes.Find(note.Id)!.Attachments);
        Assert.Equal("Recording · 12:34", attached.Name);
        Assert.Equal("audio/aac", attached.ContentType);
        Assert.Equal(bytes.Length, attached.SizeBytes);
        Assert.Equal(bytes, File.ReadAllBytes(_fixture.TalkNoteFiles.OutboxPath(attached.Id)));
        Assert.Empty(Directory.GetFiles(_fixture.Files.Folder));
        Assert.Equal(MeetingRecorderState.Idle, _session.State);
        Assert.Null(_session.NoteId);
        Assert.Null(_session.Error);
    }

    [Fact]
    public async Task A_recording_that_ended_on_a_failure_is_kept_against_its_note_until_dismissed_or_a_new_start()
    {
        var note = await _fixture.Notes.CreateAsync("Standup", "notes", cancellationToken: Cancellation);
        _recorder.Elapsed = TimeSpan.FromSeconds(61);

        await _session.StartAsync(note.Id, Cancellation);
        _recorder.Fail("The disk may be full.");
        await _session.StopAsync(Cancellation);

        var failure = Assert.IsType<RecordingFailure>(_session.LastFailure);
        Assert.Equal(note.Id, failure.NoteId);
        Assert.Equal("The disk may be full.", failure.Error);
        Assert.Equal(TimeSpan.FromSeconds(61), failure.Elapsed);

        // Another note's dismiss leaves it; this note's takes it away.
        _session.DismissFailure(Guid.NewGuid());
        Assert.NotNull(_session.LastFailure);
        _session.DismissFailure(note.Id);
        Assert.Null(_session.LastFailure);

        await _session.StartAsync(note.Id, Cancellation);
        _recorder.Fail("Again.");
        await _session.StopAsync(Cancellation);
        Assert.NotNull(_session.LastFailure);

        // A new recording supersedes it; a refused start is no recording and sets none.
        await _session.StartAsync(note.Id, Cancellation);
        Assert.Null(_session.LastFailure);
        await _session.StopAsync(Cancellation);
        _recorder.StartAnswer = MeetingRecorderResult.Failed("Microphone access was denied.");
        Assert.False(await _session.StartAsync(note.Id, Cancellation));
        Assert.Null(_session.LastFailure);
    }

    [Fact]
    public async Task A_recording_that_rolled_over_attaches_every_part_in_order()
    {
        var note = await _fixture.Notes.CreateAsync("All-hands", "notes", cancellationToken: Cancellation);
        _recorder.Parts.Clear();
        _recorder.Parts.Add(ScriptedMeetingRecorder.AdtsPart(new TimeSpan(1, 44, 51)));
        _recorder.Parts.Add(ScriptedMeetingRecorder.AdtsPart(TimeSpan.FromSeconds(123)));

        await _session.StartAsync(note.Id, Cancellation);
        await _session.StopAsync(Cancellation);

        Assert.Equal(
            ["Recording · 1:44:51", "Recording · part 2 · 2:03"],
            _fixture.Notes.Find(note.Id)!.Attachments.Select(file => file.Name));
    }

    [Fact]
    public async Task A_denied_microphone_is_said_in_words_and_records_nothing()
    {
        var note = await _fixture.Notes.CreateAsync("Standup", "notes", cancellationToken: Cancellation);
        _recorder.StartAnswer = MeetingRecorderResult.Failed("Backlog may not use the microphone.");

        var started = await _session.StartAsync(note.Id, Cancellation);

        Assert.False(started);
        Assert.Equal("Backlog may not use the microphone.", _session.Error);
        Assert.Equal(MeetingRecorderState.Idle, _session.State);
        Assert.Null(_session.NoteId);
    }

    [Fact]
    public async Task A_recorder_that_throws_on_stop_is_said_in_words()
    {
        var note = await _fixture.Notes.CreateAsync("Standup", "notes", cancellationToken: Cancellation);
        await _session.StartAsync(note.Id, Cancellation);
        _recorder.StopThrows = new IOException("There is not enough space on the disk.");

        await _session.StopAsync(Cancellation);

        Assert.Equal("There is not enough space on the disk.", _session.Error);
        Assert.Equal(MeetingRecorderState.Idle, _session.State);
    }

    [Fact]
    public async Task A_failure_while_recording_attaches_what_was_written_and_says_why()
    {
        var note = await _fixture.Notes.CreateAsync("Standup", "notes", cancellationToken: Cancellation);
        await _session.StartAsync(note.Id, Cancellation);

        _recorder.Fail("The phone is out of space. The recording so far is on the note.");
        await WaitUntilAsync(() => _session.State == MeetingRecorderState.Idle);

        Assert.Equal("The phone is out of space. The recording so far is on the note.", _session.Error);
        Assert.Single(_fixture.Notes.Find(note.Id)!.Attachments);
    }

    [Fact]
    public async Task Words_typed_while_recording_are_kept_beside_the_recording()
    {
        var note = await _fixture.Notes.CreateAsync("Standup", "first", cancellationToken: Cancellation);
        await _session.StartAsync(note.Id, Cancellation);

        await _fixture.Notes.EditAsync(note.Id, "Standup", "first\nsecond", cancellationToken: Cancellation);
        await _session.StopAsync(Cancellation);

        var kept = _fixture.Notes.Find(note.Id)!;
        Assert.Equal("first\nsecond", kept.Body);
        Assert.Single(kept.Attachments);
    }

    [Fact]
    public async Task A_recording_outlives_the_page_that_started_it()
    {
        var note = await _fixture.Notes.CreateAsync("Standup", "notes", cancellationToken: Cancellation);
        var raised = 0;
        void Page() => raised++;

        _session.Changed += Page;
        await _session.StartAsync(note.Id, Cancellation);
        _session.Changed -= Page;

        // The page is gone; the notification's Stop reaches the same session.
        await _session.StopAsync(Cancellation);

        Assert.True(raised > 0);
        Assert.Single(_fixture.Notes.Find(note.Id)!.Attachments);
    }

    [Fact]
    public async Task Pause_and_resume_hold_the_recording_on_its_note()
    {
        var note = await _fixture.Notes.CreateAsync("Standup", "notes", cancellationToken: Cancellation);
        await _session.StartAsync(note.Id, Cancellation);

        await _session.PauseAsync();
        Assert.Equal(MeetingRecorderState.Paused, _session.State);
        Assert.True(_session.IsRecording(note.Id));

        await _session.ResumeAsync();
        Assert.Equal(MeetingRecorderState.Recording, _session.State);
    }

    [Fact]
    public async Task A_second_note_cannot_start_while_one_is_recording()
    {
        var first = await _fixture.Notes.CreateAsync("Standup", "notes", cancellationToken: Cancellation);
        var second = await _fixture.Notes.CreateAsync("Retro", "notes", cancellationToken: Cancellation);
        await _session.StartAsync(first.Id, Cancellation);

        var started = await _session.StartAsync(second.Id, Cancellation);

        Assert.False(started);
        Assert.Equal(NoteRecordingSession.AlreadyRecording, _session.Error);
        Assert.Equal(first.Id, _session.NoteId);
        Assert.Equal([first.Id], _recorder.Started);
    }

    [Fact]
    public async Task A_note_that_is_gone_when_recording_stops_keeps_its_file_for_later()
    {
        var note = await _fixture.Notes.CreateAsync("Standup", "notes", cancellationToken: Cancellation);
        await _session.StartAsync(note.Id, Cancellation);

        // The desktop archives the note mid-meeting.
        _fixture.Service.State = InboxServiceState.Answering;
        _fixture.Service.Append(NoteFoldTests.Note("Standup", _fixture.Clock.GetUtcNow().AddMinutes(5), note.Id, deletedAt: _fixture.Clock.GetUtcNow().AddMinutes(5)));
        await _fixture.Notes.PullAsync(Cancellation);

        await _session.StopAsync(Cancellation);

        Assert.NotNull(_session.Error);
        Assert.Single(Directory.GetFiles(_fixture.Files.Folder));
    }

    [Fact]
    public async Task A_part_that_cannot_be_attached_is_kept_for_the_next_start_and_the_rest_attach()
    {
        var note = await _fixture.Notes.CreateAsync("Standup", "notes", cancellationToken: Cancellation);
        _recorder.Parts.Add(ScriptedMeetingRecorder.AdtsPart(TimeSpan.FromSeconds(30)));
        RecordingPartFile? second = null;

        // Part 2 cannot reach the outbox folder: something already holds its name there.
        _recorder.AfterPartWritten = part =>
        {
            if (part.Number != 2) return;
            second = part;
            Directory.CreateDirectory(_fixture.TalkNoteFiles.OutboxPath(part.AttachmentId));
        };

        await _session.StartAsync(note.Id, Cancellation);
        await _session.StopAsync(Cancellation);

        Assert.Equal(["Recording · 12:34"], _fixture.Notes.Find(note.Id)!.Attachments.Select(file => file.Name));
        Assert.Equal(NoteRecordingSession.PartKept, _session.Error);
        Assert.Equal([second!.Path], Directory.GetFiles(_fixture.Files.Folder));

        Directory.Delete(_fixture.TalkNoteFiles.OutboxPath(second.AttachmentId));
        Assert.Equal(1, await _fixture.Recovery.RecoverAsync(Cancellation));
        Assert.Equal(2, _fixture.Notes.Find(note.Id)!.Attachments.Count);
    }

    [Fact]
    public async Task A_stop_cut_off_before_the_note_is_written_leaves_every_part_for_the_next_start()
    {
        var note = await _fixture.Notes.CreateAsync("Standup", "notes", cancellationToken: Cancellation);
        _recorder.Parts.Add(ScriptedMeetingRecorder.AdtsPart(TimeSpan.FromSeconds(30)));
        using var cut = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        _recorder.AfterStop = cut.Cancel;
        var raised = 0;
        _session.Changed += () => raised++;

        await _session.StartAsync(note.Id, Cancellation);
        raised = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _session.StopAsync(cut.Token));

        Assert.Empty(_fixture.Notes.Find(note.Id)!.Attachments);
        Assert.Equal(MeetingRecorderState.Idle, _session.State);
        Assert.True(raised > 0);
        Assert.Equal(2, Directory.GetFiles(_fixture.Files.Folder).Length);

        Assert.Equal(2, await _fixture.Recovery.RecoverAsync(Cancellation));
    }

    [Fact]
    public async Task A_fault_raised_as_the_recording_starts_is_not_lost()
    {
        var note = await _fixture.Notes.CreateAsync("Standup", "notes", cancellationToken: Cancellation);
        _recorder.FailOnStart = "The microphone was taken by another app.";

        await _session.StartAsync(note.Id, Cancellation);
        await WaitUntilAsync(() => _session.State == MeetingRecorderState.Idle);

        Assert.Equal("The microphone was taken by another app.", _session.Error);
    }

    [Fact]
    public async Task A_fault_is_stopped_off_the_thread_that_raised_it()
    {
        var note = await _fixture.Notes.CreateAsync("Standup", "notes", cancellationToken: Cancellation);
        await _session.StartAsync(note.Id, Cancellation);

        _recorder.Fail("The phone is out of space.");
        await WaitUntilAsync(() => _session.State == MeetingRecorderState.Idle);

        Assert.False(_recorder.StoppedOnRaisingThread);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The session never got there.");
            await Task.Delay(10, Cancellation);
        }
    }
}
