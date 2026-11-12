using Backlog.Mobile.UI.Components;
using Backlog.Mobile.UI.Components.Pages;
using Backlog.Mobile.UI.Notes;
using Backlog.Mobile.UI.Recordings;
using Backlog.Mobile.UI.Services;
using Backlog.Mobile.UI.TalkNotes;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// Recording a meeting into a note on the phone (features.md#meeting-recording): Record beside
/// Dictate and the photo, a bar with the dot, the timer, the level, Pause and Stop while it
/// runs, and the recording attached to the note on Stop as "Recording · 12:34", AAC audio.
/// Leaving the note does not stop it; a refused microphone is said in words and draws no bar.
/// </summary>
public sealed class NoteRecordingScreenTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 14, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Record_is_disabled_where_the_host_cannot_record()
    {
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now));
        host.MeetingRecorder.Supported = false;

        var app = OpenEditor(host, "notes/new");

        // Asked after the first render, as Dictate is. Record starts disabled, so the
        // assertion that counts is the one after the host has answered.
        app.WaitForAssertion(() => Assert.True(host.MeetingRecorder.SupportAsked > 0));
        app.Render();
        Assert.True(app.Find("[data-testid='note-record']").HasAttribute("disabled"));
        Assert.Empty(app.FindAll("[data-testid='note-recording']"));
    }

    [Fact]
    public void Record_sits_in_the_tools_beside_dictate_and_is_enabled_where_the_host_can_record()
    {
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now));

        var app = OpenEditor(host, "notes/new");

        app.WaitForAssertion(() => Assert.False(app.Find("[data-testid='note-record']").HasAttribute("disabled")));
        var tools = app.Find("[data-testid='note-editor-tools']");
        Assert.NotNull(tools.QuerySelector("[data-testid='note-speech-toggle']"));
        Assert.NotNull(tools.QuerySelector("[data-testid='note-record']"));
        Assert.Equal("Record", app.Find("[data-testid='note-record']").TextContent.Trim());
    }

    [Fact]
    public void Recording_shows_the_bar_pause_turns_to_resume_and_stop_attaches_the_recording()
    {
        var clock = new FakeTimeProvider(Now);
        var call = Note("Planning call", "Agenda.", Now.AddHours(-1));
        using var host = ShellHost.Paired(clock: clock, tasks: new ScriptedTaskService(call));
        var app = OpenNote(host, call.Id);
        host.MeetingRecorder.Elapsed = TimeSpan.FromSeconds(83);
        host.MeetingRecorder.Level = 0.5;

        PressRecord(app);

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='note-recording']")));
        Assert.Equal(call.Id, Assert.Single(host.MeetingRecorder.Started));
        Assert.Equal("recording", app.Find("[data-testid='note-recording']").GetAttribute("data-state"));
        Assert.NotNull(app.Find("[data-testid='note-recording-dot']"));
        Assert.Equal("1:23", app.Find("[data-testid='note-recording-timer']").TextContent);
        Assert.Equal("50", app.Find("[data-testid='note-recording-meter']").GetAttribute("aria-valuenow"));
        Assert.True(app.Find("[data-testid='note-record']").HasAttribute("disabled"));

        app.Find("[data-testid='note-recording-pause']").Click();
        app.WaitForAssertion(() => Assert.Equal("paused", app.Find("[data-testid='note-recording']").GetAttribute("data-state")));
        Assert.Empty(app.FindAll("[data-testid='note-recording-pause']"));

        app.Find("[data-testid='note-recording-resume']").Click();
        app.WaitForAssertion(() => Assert.Equal("recording", app.Find("[data-testid='note-recording']").GetAttribute("data-state")));

        app.Find("[data-testid='note-recording-stop']").Click();

        app.WaitForAssertion(() => Assert.Empty(app.FindAll("[data-testid='note-recording']")));
        app.WaitForAssertion(() =>
        {
            var tile = Assert.Single(app.FindAll("[data-testid='note-attachments'] [data-testid='attachment-tile']"));
            Assert.Contains("Recording · 12:34", tile.TextContent, StringComparison.Ordinal);
            Assert.Contains("AAC audio", tile.TextContent, StringComparison.Ordinal);
        });

        var attached = Assert.Single(Assert.Single(host.Service<NoteViewProjection>().Notes()).Attachments);
        Assert.Equal("audio/aac", attached.ContentType);
        Assert.False(app.Find("[data-testid='note-record']").HasAttribute("disabled"));
    }

    [Fact]
    public void Record_on_a_new_note_creates_the_note_first_and_records_into_it()
    {
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now));
        var app = OpenEditor(host, "notes/new");

        PressRecord(app);

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='note-recording']")));
        var note = Assert.Single(host.Service<NoteViewProjection>().Notes());
        Assert.Equal(note.Id, Assert.Single(host.MeetingRecorder.Started));

        app.Find("[data-testid='note-recording-stop']").Click();

        app.WaitForAssertion(() => Assert.Single(app.FindAll("[data-testid='note-attachments'] [data-testid='attachment-tile']")));
        Assert.Single(Assert.Single(host.Service<NoteViewProjection>().Notes()).Attachments);
    }

    [Fact]
    public void A_stopped_recording_plays_on_its_tile_while_its_bytes_are_on_the_phone()
    {
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now));
        host.JSInterop.Setup<string?>("backlogAudio.open", _ => true).SetResult("blob:recording");
        var app = OpenEditor(host, "notes/new");

        PressRecord(app);
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='note-recording']")));
        app.Find("[data-testid='note-recording-stop']").Click();

        app.WaitForAssertion(() => Assert.Equal("blob:recording",
            app.Find("[data-testid='note-attachments'] [data-testid='attachment-tile-player']").GetAttribute("src")));
        var tile = app.Find("[data-testid='note-attachments'] [data-testid='attachment-tile']");
        Assert.NotNull(tile.QuerySelector("[data-glyph='audio']"));
        Assert.StartsWith("AAC audio · ", tile.QuerySelector("[data-testid='attachment-tile-facts']")!.TextContent, StringComparison.Ordinal);
        Assert.StartsWith("Play Recording · ", tile.QuerySelector("[data-testid='attachment-tile-player']")!.GetAttribute("aria-label"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_delivered_recording_keeps_its_tile_without_a_player()
    {
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now));
        var app = OpenEditor(host, "notes/new");
        PressRecord(app);
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='note-recording']")));
        app.Find("[data-testid='note-recording-stop']").Click();
        app.WaitForAssertion(() => Assert.Single(app.FindAll("[data-testid='note-attachments'] [data-testid='attachment-tile']")));
        var note = Assert.Single(host.Service<NoteViewProjection>().Notes());

        // Delivered: the service holds the bytes now, and the phone let its copy go.
        host.Service<TalkNoteFiles>().Release(Assert.Single(note.Attachments).Id);
        host.Navigation.NavigateTo("notes");
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='note-row']")));
        host.Navigation.NavigateTo($"notes/{note.Id}");

        app.WaitForAssertion(() => Assert.NotNull(
            app.Find("[data-testid='note-attachments'] [data-testid='attachment-tile'] [data-glyph='audio']")));
        Assert.Empty(app.FindAll("[data-testid='attachment-tile-player']"));
    }

    [Fact]
    public void A_refused_microphone_is_said_in_words_and_draws_no_bar()
    {
        const string denied = "Microphone access was denied. Allow it for this site and try again.";
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now));
        host.MeetingRecorder.StartAnswer = MeetingRecorderResult.Failed(denied);
        var app = OpenEditor(host, "notes/new");

        PressRecord(app);

        app.WaitForAssertion(() => Assert.Equal(denied, app.Find("[data-testid='note-record-error']").TextContent));
        Assert.Empty(app.FindAll("[data-testid='note-recording']"));
        Assert.False(app.Find("[data-testid='note-record']").HasAttribute("disabled"));
    }

    [Fact]
    public void Typing_while_recording_still_saves_and_the_recording_is_kept_beside_it()
    {
        var clock = new FakeTimeProvider(Now);
        var call = Note("Planning call", "Agenda.", Now.AddHours(-1));
        var tasks = new ScriptedTaskService(call);
        using var host = ShellHost.Paired(clock: clock, tasks: tasks);
        var app = OpenNote(host, call.Id);

        PressRecord(app);
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='note-recording']")));

        app.Find("[data-testid='note-editor-body'] textarea").Input("Agenda.\n- decided on AAC");
        clock.Advance(NotePage.SaveDelay);

        app.WaitForAssertion(() => Assert.Equal(
            "Agenda.\n- decided on AAC",
            Assert.Single(host.Service<NoteViewProjection>().Notes()).Body));
        Assert.NotNull(app.Find("[data-testid='note-recording']"));

        app.Find("[data-testid='note-recording-stop']").Click();

        app.WaitForAssertion(() => Assert.Single(app.FindAll("[data-testid='note-attachments'] [data-testid='attachment-tile']")));
        var note = Assert.Single(host.Service<NoteViewProjection>().Notes());
        Assert.Equal("Agenda.\n- decided on AAC", note.Body);
        Assert.Equal("Recording · 12:34", Assert.Single(note.Attachments).Name);
    }

    [Fact]
    public void Leaving_the_note_keeps_the_recording_going_and_the_bar_is_back_on_return()
    {
        var clock = new FakeTimeProvider(Now);
        var call = Note("Planning call", "Agenda.", Now.AddHours(-1));
        using var host = ShellHost.Paired(clock: clock, tasks: new ScriptedTaskService(call));
        var app = OpenNote(host, call.Id);

        PressRecord(app);
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='note-recording']")));

        host.Navigation.NavigateTo("notes");
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='note-row']")));

        var session = host.Service<NoteRecordingSession>();
        Assert.Equal(call.Id, session.NoteId);
        Assert.Equal(MeetingRecorderState.Recording, host.MeetingRecorder.State);

        host.Navigation.NavigateTo($"notes/{call.Id}");
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='note-recording']")));

        app.Find("[data-testid='note-recording-stop']").Click();
        app.WaitForAssertion(() => Assert.Single(app.FindAll("[data-testid='note-attachments'] [data-testid='attachment-tile']")));
    }

    [Fact]
    public void A_failure_while_recording_attaches_what_was_written_and_says_why_on_the_bar()
    {
        const string full = "The recording could not be saved. The disk may be full.";
        var clock = new FakeTimeProvider(Now);
        var call = Note("Planning call", "Agenda.", Now.AddHours(-1));
        using var host = ShellHost.Paired(clock: clock, tasks: new ScriptedTaskService(call));
        var app = OpenNote(host, call.Id);

        PressRecord(app);
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='note-recording']")));

        host.MeetingRecorder.Fail(full);

        app.WaitForAssertion(() =>
        {
            Assert.Equal("failed", app.Find("[data-testid='note-recording']").GetAttribute("data-state"));
            Assert.Equal(full, app.Find("[data-testid='note-recording-error']").TextContent);
        });
        app.WaitForAssertion(() => Assert.Single(app.FindAll("[data-testid='note-attachments'] [data-testid='attachment-tile']")));

        app.Find("[data-testid='note-recording-dismiss']").Click();
        app.WaitForAssertion(() => Assert.Empty(app.FindAll("[data-testid='note-recording']")));
    }

    [Fact]
    public void While_one_note_records_record_is_disabled_on_every_other_note()
    {
        var clock = new FakeTimeProvider(Now);
        var call = Note("Planning call", "Agenda.", Now.AddHours(-1));
        var other = Note("Books to read", "Shape Up.", Now.AddHours(-2));
        using var host = ShellHost.Paired(clock: clock, tasks: new ScriptedTaskService(call, other));
        var app = OpenNote(host, call.Id);

        PressRecord(app);
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='note-recording']")));

        host.Navigation.NavigateTo($"notes/{other.Id}");
        app.WaitForAssertion(() => Assert.Equal(
            "Books to read",
            app.Find("[data-testid='note-editor-title'] input").GetAttribute("value")));

        // Another note's recording draws no bar here, and Record waits for it to stop.
        Assert.Empty(app.FindAll("[data-testid='note-recording']"));
        app.WaitForAssertion(() => Assert.True(app.Find("[data-testid='note-record']").HasAttribute("disabled")));
        Assert.Equal(call.Id, host.Service<NoteRecordingSession>().NoteId);
    }

    [Fact]
    public async Task A_recording_that_ticks_after_the_page_is_gone_disturbs_nothing()
    {
        var clock = new FakeTimeProvider(Now);
        var call = Note("Planning call", "Agenda.", Now.AddHours(-1));
        using var host = ShellHost.Paired(clock: clock, tasks: new ScriptedTaskService(call));
        var app = OpenNote(host, call.Id);

        PressRecord(app);
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='note-recording']")));

        host.Navigation.NavigateTo("notes");
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='note-row']")));

        // The disposed Note page and its bar both hear these; neither may draw.
        var session = host.Service<NoteRecordingSession>();
        await session.PauseAsync();
        await session.ResumeAsync();
        await session.StopAsync(TestContext.Current.CancellationToken);

        app.WaitForAssertion(() => Assert.Single(Assert.Single(host.Service<NoteViewProjection>().Notes()).Attachments));
        Assert.NotNull(app.Find("[data-testid='note-row']"));
        Assert.Empty(app.FindAll("[data-testid='note-recording']"));
    }

    [Fact]
    public void A_failure_while_the_note_was_not_open_is_said_when_it_is_next_opened()
    {
        const string full = "The recording could not be saved. The disk may be full.";
        var clock = new FakeTimeProvider(Now);
        var call = Note("Planning call", "Agenda.", Now.AddHours(-1));
        using var host = ShellHost.Paired(clock: clock, tasks: new ScriptedTaskService(call));
        var app = OpenNote(host, call.Id);

        PressRecord(app);
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='note-recording']")));

        host.Navigation.NavigateTo("notes");
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='note-row']")));

        var session = host.Service<NoteRecordingSession>();
        host.MeetingRecorder.Fail(full);
        app.WaitForAssertion(() => Assert.Null(session.NoteId));

        host.Navigation.NavigateTo($"notes/{call.Id}");
        app.WaitForAssertion(() =>
        {
            Assert.Equal("failed", app.Find("[data-testid='note-recording']").GetAttribute("data-state"));
            Assert.Equal(full, app.Find("[data-testid='note-recording-error']").TextContent);
        });
        Assert.Single(app.FindAll("[data-testid='note-attachments'] [data-testid='attachment-tile']"));

        app.Find("[data-testid='note-recording-dismiss']").Click();
        app.WaitForAssertion(() => Assert.Empty(app.FindAll("[data-testid='note-recording']")));
        Assert.Null(session.LastFailure);
    }

    [Fact]
    public void A_phone_call_pauses_the_bar_and_says_why()
    {
        var clock = new FakeTimeProvider(Now);
        var call = Note("Planning call", "Agenda.", Now.AddHours(-1));
        using var host = ShellHost.Paired(clock: clock, tasks: new ScriptedTaskService(call));
        var app = OpenNote(host, call.Id);

        PressRecord(app);
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='note-recording']")));

        host.MeetingRecorder.Interrupt(MeetingRecorderInterruption.PhoneCall);

        app.WaitForAssertion(() =>
        {
            Assert.Equal("paused", app.Find("[data-testid='note-recording']").GetAttribute("data-state"));
            Assert.Equal("Paused: a phone call has the microphone.", app.Find("[data-testid='note-recording-notice']").TextContent);
        });
        Assert.NotNull(app.Find("[data-testid='note-recording-resume']"));
    }

    [Fact]
    public void A_start_without_room_is_refused_beside_the_tools_and_draws_no_bar()
    {
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now));
        host.FreeSpace.Available = 120L * 1024 * 1024;
        var app = OpenEditor(host, "notes/new");

        PressRecord(app);

        app.WaitForAssertion(() => Assert.Equal(
            "There is not enough storage on this phone to record: it needs 248 MB free and has 120 MB.",
            app.Find("[data-testid='note-record-error']").TextContent));
        Assert.Empty(app.FindAll("[data-testid='note-recording']"));
    }

    [Fact]
    public async Task A_note_deleted_elsewhere_says_where_its_recording_went()
    {
        var clock = new FakeTimeProvider(Now);
        var call = Note("Planning call", "Agenda.", Now.AddHours(-1));
        var tasks = new ScriptedTaskService(call);
        using var host = ShellHost.Paired(clock: clock, tasks: tasks);
        var app = OpenNote(host, call.Id);

        PressRecord(app);
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='note-recording']")));

        var notes = host.Service<NoteViewProjection>();
        tasks.Append(NoteFoldTests.Note("Planning call", Now.AddMinutes(5), call.Id, deletedAt: Now.AddMinutes(5)));
        await notes.PullAsync(TestContext.Current.CancellationToken);
        Assert.Null(notes.Find(call.Id));

        app.Find("[data-testid='note-recording-stop']").Click();

        app.WaitForAssertion(() => Assert.Equal(
            "This note was deleted elsewhere. The recording was saved to a new note, Recording · 7 Oct 2026.",
            app.Find("[data-testid='note-recording-moved']").TextContent));
        Assert.Equal("Recording · 7 Oct 2026", Assert.Single(notes.Notes()).Title);
    }

    private static void PressRecord(IRenderedComponent<Routes> app)
    {
        app.WaitForAssertion(() => Assert.False(app.Find("[data-testid='note-record']").HasAttribute("disabled")));
        app.Find("[data-testid='note-record']").Click();
    }

    private static IRenderedComponent<Routes> OpenEditor(ShellHost host, string route)
    {
        var app = host.Open(route);
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='note-editor']")));
        return app;
    }

    /// <summary>Opens a note the pull brought, the way a person reaches it: from the list.</summary>
    private static IRenderedComponent<Routes> OpenNote(ShellHost host, Guid id)
    {
        var app = host.Open("notes");
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='note-row']")));

        host.Navigation.NavigateTo($"notes/{id}");
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='note-editor']")));
        return app;
    }

    private static TaskChange Note(string title, string body, DateTimeOffset at) =>
        NoteViewProjection.NewNote(Guid.CreateVersion7(), title, body, [], at);
}
