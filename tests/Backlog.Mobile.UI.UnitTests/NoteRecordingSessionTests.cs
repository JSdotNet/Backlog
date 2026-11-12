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
    private const long Megabyte = 1024 * 1024;

    private readonly ScriptedMeetingRecorder _recorder = new();
    private readonly FakeFreeSpace _space = new();
    private readonly NoteRecordingSession _session;

    public NoteRecordingSessionTests() =>
        _session = new NoteRecordingSession(_recorder, _fixture.Files, _fixture.Composer, _fixture.Notes, _space, _fixture.Clock);

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
    public async Task A_note_deleted_elsewhere_while_recording_leaves_the_recording_running()
    {
        var note = await _fixture.Notes.CreateAsync("Standup", "notes", cancellationToken: Cancellation);
        await _session.StartAsync(note.Id, Cancellation);

        await DeleteElsewhereAsync(note.Id);

        Assert.Equal(MeetingRecorderState.Recording, _session.State);
        Assert.Equal(note.Id, _session.NoteId);
    }

    [Fact]
    public async Task A_recording_whose_note_was_deleted_elsewhere_gets_a_note_of_its_own_on_stop()
    {
        var note = await _fixture.Notes.CreateAsync("Standup", "notes", cancellationToken: Cancellation);
        _recorder.Parts.Add(ScriptedMeetingRecorder.AdtsPart(TimeSpan.FromSeconds(123)));
        await _session.StartAsync(note.Id, Cancellation);

        // The desktop archives the note mid-meeting.
        await DeleteElsewhereAsync(note.Id);
        await _session.StopAsync(Cancellation);

        var created = Assert.Single(_fixture.Notes.Notes(), row => row.Id != note.Id);
        Assert.Equal("Recording · 10 Nov 2026", created.Title);
        Assert.Equal(["Recording · 12:34", "Recording · part 2 · 2:03"], created.Attachments.Select(file => file.Name));
        Assert.Equal(created.Attachments.Select(file => file.Id), _session.LastAttached.Select(file => file.Id));
        Assert.Empty(Directory.GetFiles(_fixture.Files.Folder));
        Assert.Null(_session.Error);
        Assert.Equal(new RecordingMoved(note.Id, created.Id, "Recording · 10 Nov 2026"), _session.LastMoved);
    }

    [Fact]
    public async Task A_part_that_cannot_be_prepared_for_the_new_note_stays_in_the_recordings_folder()
    {
        var note = await _fixture.Notes.CreateAsync("Standup", "notes", cancellationToken: Cancellation);
        _recorder.Parts.Add(ScriptedMeetingRecorder.AdtsPart(TimeSpan.FromSeconds(30)));
        RecordingPartFile? second = null;
        _recorder.AfterPartWritten = part =>
        {
            if (part.Number != 2) return;
            second = part;
            Directory.CreateDirectory(_fixture.TalkNoteFiles.OutboxPath(part.AttachmentId));
        };
        await _session.StartAsync(note.Id, Cancellation);

        await DeleteElsewhereAsync(note.Id);
        await _session.StopAsync(Cancellation);

        var created = Assert.Single(_fixture.Notes.Notes(), row => row.Id != note.Id);
        Assert.Equal(["Recording · 12:34"], created.Attachments.Select(file => file.Name));
        Assert.Equal(NoteRecordingSession.PartKept, _session.Error);
        Assert.Equal([second!.Path], Directory.GetFiles(_fixture.Files.Folder));
    }

    [Fact]
    public async Task A_phone_call_pauses_the_recording_and_says_so()
    {
        var note = await _fixture.Notes.CreateAsync("Standup", "notes", cancellationToken: Cancellation);
        await _session.StartAsync(note.Id, Cancellation);

        _recorder.Interrupt(MeetingRecorderInterruption.PhoneCall);
        await WaitUntilAsync(() => _session.State == MeetingRecorderState.Paused);

        Assert.Equal("Paused: a phone call has the microphone.", _session.Notice);
        Assert.True(_session.IsRecording(note.Id));
    }

    [Fact]
    public async Task The_end_of_a_call_leaves_the_recording_paused_until_it_is_resumed()
    {
        var note = await _fixture.Notes.CreateAsync("Standup", "notes", cancellationToken: Cancellation);
        await _session.StartAsync(note.Id, Cancellation);
        _recorder.Interrupt(MeetingRecorderInterruption.PhoneCall);
        await WaitUntilAsync(() => _session.State == MeetingRecorderState.Paused);

        _recorder.Interrupt(MeetingRecorderInterruption.None);
        await Task.Delay(50, Cancellation);

        Assert.Equal(MeetingRecorderState.Paused, _session.State);
        Assert.Null(_session.Notice);
    }

    [Fact]
    public async Task Resume_after_a_call_takes_the_call_notice_away()
    {
        var note = await _fixture.Notes.CreateAsync("Standup", "notes", cancellationToken: Cancellation);
        await _session.StartAsync(note.Id, Cancellation);
        _recorder.Interrupt(MeetingRecorderInterruption.PhoneCall);
        await WaitUntilAsync(() => _session.State == MeetingRecorderState.Paused);
        _recorder.Interrupt(MeetingRecorderInterruption.None);

        await _session.ResumeAsync();

        Assert.Equal(MeetingRecorderState.Recording, _session.State);
        Assert.Null(_session.Notice);
    }

    [Fact]
    public async Task A_call_pauses_the_recording_once_and_off_the_thread_that_raised_it()
    {
        var note = await _fixture.Notes.CreateAsync("Standup", "notes", cancellationToken: Cancellation);
        await _session.StartAsync(note.Id, Cancellation);

        _recorder.Interrupt(MeetingRecorderInterruption.PhoneCall);
        await WaitUntilAsync(() => _session.State == MeetingRecorderState.Paused);
        _recorder.Interrupt(MeetingRecorderInterruption.PhoneCall);
        _recorder.Tick(0);
        await Task.Delay(50, Cancellation);

        Assert.Equal(1, _recorder.PauseCount);
        Assert.False(_recorder.PausedOnRaisingThread);
    }

    [Fact]
    public async Task Resume_during_the_call_records_on_and_says_the_capture_hears_nothing()
    {
        var note = await _fixture.Notes.CreateAsync("Standup", "notes", cancellationToken: Cancellation);
        await _session.StartAsync(note.Id, Cancellation);
        _recorder.Interrupt(MeetingRecorderInterruption.PhoneCall);
        await WaitUntilAsync(() => _session.State == MeetingRecorderState.Paused);

        await _session.ResumeAsync();
        _recorder.Tick(0);
        await Task.Delay(50, Cancellation);

        Assert.Equal(MeetingRecorderState.Recording, _session.State);
        Assert.Equal(RecordingNotices.Silenced, _session.Notice);
        Assert.Equal(1, _recorder.PauseCount);
    }

    [Fact]
    public async Task A_call_that_ended_before_its_pause_ran_does_not_pause()
    {
        var note = await _fixture.Notes.CreateAsync("Standup", "notes", cancellationToken: Cancellation);
        var queued = HoldBackground();
        await _session.StartAsync(note.Id, Cancellation);

        _recorder.Interrupt(MeetingRecorderInterruption.PhoneCall);
        _recorder.Interrupt(MeetingRecorderInterruption.None);
        foreach (var work in queued) await work();

        Assert.Equal(MeetingRecorderState.Recording, _session.State);
        Assert.Equal(0, _recorder.PauseCount);
    }

    [Fact]
    public async Task A_resume_pressed_before_the_call_pause_ran_does_not_pause()
    {
        var note = await _fixture.Notes.CreateAsync("Standup", "notes", cancellationToken: Cancellation);
        var queued = HoldBackground();
        await _session.StartAsync(note.Id, Cancellation);

        _recorder.Interrupt(MeetingRecorderInterruption.PhoneCall);
        await _session.ResumeAsync();
        foreach (var work in queued) await work();

        Assert.Equal(MeetingRecorderState.Recording, _session.State);
        Assert.Equal(0, _recorder.PauseCount);
    }

    [Fact]
    public async Task A_recorder_that_cannot_measure_the_level_gets_no_hint()
    {
        var note = await _fixture.Notes.CreateAsync("Standup", "notes", cancellationToken: Cancellation);
        _recorder.MeasuresLevel = false;
        await _session.StartAsync(note.Id, Cancellation);

        _recorder.Tick(0);
        _fixture.Clock.Advance(TimeSpan.FromSeconds(30));
        _recorder.Tick(0);

        Assert.Null(_session.Notice);
    }

    [Fact]
    public async Task Running_low_on_space_with_a_part_that_cannot_be_attached_says_both()
    {
        var note = await _fixture.Notes.CreateAsync("Standup", "notes", cancellationToken: Cancellation);
        _recorder.Parts.Add(ScriptedMeetingRecorder.AdtsPart(TimeSpan.FromSeconds(30)));
        _recorder.AfterPartWritten = part =>
        {
            if (part.Number == 2) Directory.CreateDirectory(_fixture.TalkNoteFiles.OutboxPath(part.AttachmentId));
        };
        await _session.StartAsync(note.Id, Cancellation);

        await RunOutOfSpaceAsync();

        Assert.Equal(
            "The recording stopped because the phone has less than 200 MB free. " + NoteRecordingSession.PartKept,
            _session.Error);
    }

    [Fact]
    public async Task Running_low_on_space_after_the_note_was_deleted_says_where_the_recording_went()
    {
        var note = await _fixture.Notes.CreateAsync("Standup", "notes", cancellationToken: Cancellation);
        await _session.StartAsync(note.Id, Cancellation);
        await DeleteElsewhereAsync(note.Id);

        await RunOutOfSpaceAsync();

        Assert.Equal(
            "The recording stopped because the phone has less than 200 MB free. What was recorded is on a new note, Recording · 10 Nov 2026.",
            _session.Error);
    }

    [Fact]
    public async Task A_capture_silenced_by_another_app_says_so_without_pausing_and_clears_when_it_is_back()
    {
        var note = await _fixture.Notes.CreateAsync("Standup", "notes", cancellationToken: Cancellation);
        _recorder.ReportsSilencing = true;
        await _session.StartAsync(note.Id, Cancellation);

        _recorder.Interrupt(MeetingRecorderInterruption.Silenced);

        Assert.Equal(MeetingRecorderState.Recording, _session.State);
        Assert.Equal("Another app has the microphone. Nothing is being recorded.", _session.Notice);

        _recorder.Interrupt(MeetingRecorderInterruption.None);
        Assert.Null(_session.Notice);
    }

    [Fact]
    public async Task Silence_held_for_ten_seconds_on_a_recorder_that_cannot_report_silencing_is_a_hint_until_the_next_sound()
    {
        var note = await _fixture.Notes.CreateAsync("Standup", "notes", cancellationToken: Cancellation);
        await _session.StartAsync(note.Id, Cancellation);

        _recorder.Tick(0);
        _fixture.Clock.Advance(TimeSpan.FromSeconds(10));
        _recorder.Tick(0);

        Assert.Equal("No sound is reaching the recording. Another app may have the microphone.", _session.Notice);

        _recorder.Tick(0.2);
        Assert.Null(_session.Notice);
    }

    [Fact]
    public async Task A_recorder_that_reports_silencing_gets_no_hint_from_a_quiet_room()
    {
        var note = await _fixture.Notes.CreateAsync("Standup", "notes", cancellationToken: Cancellation);
        _recorder.ReportsSilencing = true;
        await _session.StartAsync(note.Id, Cancellation);

        _recorder.Tick(0);
        _fixture.Clock.Advance(TimeSpan.FromSeconds(30));
        _recorder.Tick(0);

        Assert.Null(_session.Notice);
    }

    [Fact]
    public async Task A_start_without_room_for_the_recording_is_refused_in_words_and_records_nothing()
    {
        var note = await _fixture.Notes.CreateAsync("Standup", "notes", cancellationToken: Cancellation);
        _space.Available = 120 * Megabyte;

        var started = await _session.StartAsync(note.Id, Cancellation);

        Assert.False(started);
        Assert.Equal("There is not enough storage on this phone to record: it needs 248 MB free and has 120 MB.", _session.Error);
        Assert.Empty(_recorder.Started);
        Assert.Equal(MeetingRecorderState.Idle, _session.State);
    }

    [Fact]
    public async Task Running_low_on_space_while_recording_stops_it_attaches_what_was_recorded_and_says_why()
    {
        var note = await _fixture.Notes.CreateAsync("Standup", "notes", cancellationToken: Cancellation);
        await _session.StartAsync(note.Id, Cancellation);

        _space.Available = 200 * Megabyte;
        _fixture.Clock.Advance(TimeSpan.FromSeconds(10));
        _recorder.Tick(0.3);
        await WaitUntilAsync(() => _session.State == MeetingRecorderState.Idle);

        Assert.Equal(RecordingSpace.Stopped, _session.Error);
        Assert.Equal(RecordingSpace.Stopped, _session.LastFailure?.Error);
        Assert.Single(_fixture.Notes.Find(note.Id)!.Attachments);
    }

    [Fact]
    public async Task Free_space_is_looked_at_no_more_than_every_ten_seconds()
    {
        var note = await _fixture.Notes.CreateAsync("Standup", "notes", cancellationToken: Cancellation);
        await _session.StartAsync(note.Id, Cancellation);

        // Low the moment after the start's own look: the next look is ten seconds on.
        _space.Available = 100 * Megabyte;
        _fixture.Clock.Advance(TimeSpan.FromSeconds(9));
        _recorder.Tick(0.3);
        await Task.Delay(50, Cancellation);

        Assert.Equal(MeetingRecorderState.Recording, _session.State);
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

    /// <summary>Free space at the floor, seen on the next look ten seconds on; waits for the stop.</summary>
    private async Task RunOutOfSpaceAsync()
    {
        _space.Available = 200 * Megabyte;
        _fixture.Clock.Advance(NoteRecordingSession.SpaceCheckInterval);
        _recorder.Tick(0.3);
        await WaitUntilAsync(() => _session.State == MeetingRecorderState.Idle);
    }

    /// <summary>Holds what the session runs off the raising thread, for the test to run when it
    /// chooses: the order a pool thread would not promise.</summary>
    private List<Func<Task>> HoldBackground()
    {
        var queued = new List<Func<Task>>();
        _session.RunInBackground = work =>
        {
            queued.Add(work);
            return Task.CompletedTask;
        };
        return queued;
    }

    /// <summary>A pull bringing the note's tombstone, as a desktop delete or archive does.</summary>
    private async Task DeleteElsewhereAsync(Guid noteId)
    {
        var at = _fixture.Clock.GetUtcNow().AddMinutes(5);
        _fixture.Service.State = InboxServiceState.Answering;
        _fixture.Service.Append(NoteFoldTests.Note("Standup", at, noteId, deletedAt: at));
        await _fixture.Notes.PullAsync(Cancellation);
        Assert.Null(_fixture.Notes.Find(noteId));
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
