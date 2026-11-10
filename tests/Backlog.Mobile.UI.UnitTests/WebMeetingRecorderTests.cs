using Backlog.Mobile.UI.Recordings;

using Microsoft.JSInterop;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The browser harness's meeting recorder: slices from <c>meeting-recorder.js</c> are appended
/// to one <c>.m4a</c> part in the recordings folder as they come, a stop hands that part back as
/// <c>audio/mp4</c> with its size on disk, and a refused microphone is an answer in words that
/// leaves no file behind.
/// </summary>
public sealed class WebMeetingRecorderTests
{
    private const string ModulePath = "./_content/Backlog.Mobile.UI/meeting-recorder.js";

    [Fact]
    public async Task A_browser_without_audio_mp4_cannot_record()
    {
        using var context = new BunitContext();
        var module = Recorder(context);
        module.Setup<bool>("isSupported").SetResult(false);
        await using var recorder = new WebMeetingRecorder(context.JSInterop.JSRuntime);

        Assert.False(await recorder.IsSupportedAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_refused_microphone_is_an_answer_in_words_and_leaves_no_file()
    {
        using var context = new BunitContext();
        var module = Recorder(context);
        module.Setup<string?>("start", _ => true).SetResult("NotAllowedError");
        var folder = TestFolders.Create();
        await using var recorder = new WebMeetingRecorder(context.JSInterop.JSRuntime);

        var started = await recorder.StartAsync(new MeetingRecordingTarget(Guid.NewGuid(), folder), TestContext.Current.CancellationToken);

        Assert.True(started.IsError);
        Assert.Contains("denied", started.Error, StringComparison.Ordinal);
        Assert.Equal(MeetingRecorderState.Idle, recorder.State);
        Assert.Empty(Directory.GetFiles(folder));
    }

    [Fact]
    public async Task Slices_are_appended_to_one_part_and_a_stop_hands_it_back_as_audio_mp4()
    {
        using var context = new BunitContext();
        var module = Recorder(context);
        module.Setup<string?>("start", _ => true).SetResult(null);
        var folder = TestFolders.Create();
        var noteId = Guid.NewGuid();
        await using var recorder = new WebMeetingRecorder(context.JSInterop.JSRuntime);
        var changes = 0;
        recorder.Changed += () => changes++;

        var started = await recorder.StartAsync(new MeetingRecordingTarget(noteId, folder), TestContext.Current.CancellationToken);
        Assert.False(started.IsError);
        Assert.Equal(MeetingRecorderState.Recording, recorder.State);

        await recorder.OnChunkAsync(new Chunk([1, 2, 3]));
        recorder.OnLevel(0.6);
        Assert.Equal(0.6, recorder.Level);
        await recorder.PauseAsync();
        Assert.Equal(MeetingRecorderState.Paused, recorder.State);
        await recorder.ResumeAsync();
        await recorder.OnChunkAsync(new Chunk([4, 5]));

        var stopped = await recorder.StopAsync(TestContext.Current.CancellationToken);

        Assert.False(stopped.IsError);
        var part = Assert.Single(stopped.Parts);
        Assert.Equal("audio/mp4", part.ContentType);
        Assert.Equal(1, part.Number);
        Assert.Equal(5, part.SizeBytes);
        Assert.Equal(".m4a", Path.GetExtension(part.Path));
        Assert.Equal([1, 2, 3, 4, 5], await File.ReadAllBytesAsync(part.Path, TestContext.Current.CancellationToken));
        Assert.True(RecordingFiles.TryRead(part.Path, out var file));
        Assert.Equal(noteId, file.NoteId);
        Assert.Equal(MeetingRecorderState.Idle, recorder.State);
        Assert.True(changes >= 5);
        module.VerifyInvoke("stop");
    }

    [Fact]
    public async Task A_stop_with_nothing_recorded_leaves_no_file()
    {
        using var context = new BunitContext();
        var module = Recorder(context);
        module.Setup<string?>("start", _ => true).SetResult(null);
        var folder = TestFolders.Create();
        await using var recorder = new WebMeetingRecorder(context.JSInterop.JSRuntime);

        await recorder.StartAsync(new MeetingRecordingTarget(Guid.NewGuid(), folder), TestContext.Current.CancellationToken);
        var stopped = await recorder.StopAsync(TestContext.Current.CancellationToken);

        Assert.True(stopped.IsError);
        Assert.Empty(stopped.Parts);
        Assert.Empty(Directory.GetFiles(folder));
    }

    [Fact]
    public async Task A_microphone_lost_mid_recording_is_a_fault_and_the_stop_keeps_what_was_written()
    {
        using var context = new BunitContext();
        var module = Recorder(context);
        module.Setup<string?>("start", _ => true).SetResult(null);
        var folder = TestFolders.Create();
        await using var recorder = new WebMeetingRecorder(context.JSInterop.JSRuntime);

        await recorder.StartAsync(new MeetingRecordingTarget(Guid.NewGuid(), folder), TestContext.Current.CancellationToken);
        await recorder.OnChunkAsync(new Chunk([9, 9]));
        await recorder.OnFaultAsync("track-ended");

        Assert.Equal("The microphone stopped recording.", recorder.Fault);

        var stopped = await recorder.StopAsync(TestContext.Current.CancellationToken);
        Assert.Equal("The microphone stopped recording.", stopped.Error);
        Assert.Equal(2, Assert.Single(stopped.Parts).SizeBytes);
    }

    [Fact]
    public async Task A_torn_down_circuit_abandons_the_recording_and_keeps_its_file_for_recovery()
    {
        using var context = new BunitContext();
        var module = Recorder(context);
        module.Setup<string?>("start", _ => true).SetResult(null);
        var folder = TestFolders.Create();
        var recorder = new WebMeetingRecorder(context.JSInterop.JSRuntime);

        await recorder.StartAsync(new MeetingRecordingTarget(Guid.NewGuid(), folder), TestContext.Current.CancellationToken);
        await recorder.OnChunkAsync(new Chunk([7]));
        await recorder.DisposeAsync();

        module.VerifyInvoke("abort");
        module.VerifyNotInvoke("stop");
        Assert.Single(Directory.GetFiles(folder));
    }

    [Fact]
    public async Task A_level_reported_while_paused_is_ignored_and_raises_nothing()
    {
        using var context = new BunitContext();
        var module = Recorder(context);
        module.Setup<string?>("start", _ => true).SetResult(null);
        await using var recorder = new WebMeetingRecorder(context.JSInterop.JSRuntime);

        await recorder.StartAsync(new MeetingRecordingTarget(Guid.NewGuid(), TestFolders.Create()), TestContext.Current.CancellationToken);
        await recorder.PauseAsync();
        var changes = 0;
        recorder.Changed += () => changes++;

        recorder.OnLevel(0.9);

        Assert.Equal(0, recorder.Level);
        Assert.Equal(0, changes);
    }

    [Fact]
    public async Task A_part_file_that_cannot_be_opened_is_no_space_and_the_browser_is_not_started()
    {
        using var context = new BunitContext();
        var module = Recorder(context);
        var parent = TestFolders.Create();
        // A file where the recordings folder should be: creating the folder fails.
        var blocked = Path.Combine(parent, "recordings");
        await File.WriteAllTextAsync(blocked, "not a folder", TestContext.Current.CancellationToken);
        await using var recorder = new WebMeetingRecorder(context.JSInterop.JSRuntime);

        var started = await recorder.StartAsync(new MeetingRecordingTarget(Guid.NewGuid(), blocked), TestContext.Current.CancellationToken);

        Assert.Equal(WebMeetingRecorder.NoSpace, started.Error);
        Assert.Equal(MeetingRecorderState.Idle, recorder.State);
        module.VerifyNotInvoke("start");
    }

    [Fact]
    public async Task The_slice_still_arriving_when_the_microphone_ends_is_kept()
    {
        using var context = new BunitContext();
        var module = Recorder(context);
        module.Setup<string?>("start", _ => true).SetResult(null);
        await using var recorder = new WebMeetingRecorder(context.JSInterop.JSRuntime);

        await recorder.StartAsync(new MeetingRecordingTarget(Guid.NewGuid(), TestFolders.Create()), TestContext.Current.CancellationToken);
        await recorder.OnChunkAsync(new Chunk([1, 1]));
        await recorder.OnFaultAsync("track-ended");
        // The browser hands over the last slice after it has said the track ended.
        await recorder.OnChunkAsync(new Chunk([2, 2, 2]));

        var stopped = await recorder.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(5, Assert.Single(stopped.Parts).SizeBytes);
    }

    [Fact]
    public async Task A_stop_cancelled_before_it_began_leaves_the_recording_running()
    {
        using var context = new BunitContext();
        var module = Recorder(context);
        module.Setup<string?>("start", _ => true).SetResult(null);
        await using var recorder = new WebMeetingRecorder(context.JSInterop.JSRuntime);
        await recorder.StartAsync(new MeetingRecordingTarget(Guid.NewGuid(), TestFolders.Create()), TestContext.Current.CancellationToken);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => recorder.StopAsync(new CancellationToken(canceled: true)));

        Assert.Equal(MeetingRecorderState.Recording, recorder.State);
        module.VerifyNotInvoke("stop");

        // And a stop with nothing cancelled still ends it.
        await recorder.StopAsync(TestContext.Current.CancellationToken);
        Assert.Equal(MeetingRecorderState.Idle, recorder.State);
    }

    private static BunitJSModuleInterop Recorder(BunitContext context)
    {
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var module = context.JSInterop.SetupModule(ModulePath);
        module.Mode = JSRuntimeMode.Loose;
        return module;
    }

    /// <summary>One slice as the module hands it over.</summary>
    private sealed class Chunk(byte[] bytes) : IJSStreamReference
    {
        public long Length => bytes.Length;

        public ValueTask<Stream> OpenReadStreamAsync(long maxAllowedSize = 512000, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<Stream>(new MemoryStream(bytes, writable: false));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
