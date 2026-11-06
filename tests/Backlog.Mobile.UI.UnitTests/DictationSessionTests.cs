using Backlog.UI.Components.Shell;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// A page's side of dictation, shared by the capture sheet and the note page: the status line
/// each stage words while the turn is live, and what leaving the page does to a turn — the
/// device's words still arrive, and no recording leaves the phone (local ADR 0045 §7).
/// </summary>
public sealed class DictationSessionTests
{
    private readonly ScriptedAudioRecorder _recorder = new();
    private readonly ScriptedTranscriptionService _service = new();
    private readonly InMemorySpeechSettingsStore _settings = new() { Engine = DictationEngine.Cloud };
    private readonly List<string> _statuses = [];

    [Fact]
    public async Task The_status_line_follows_the_turn()
    {
        var dictation = new DictationSession(Cloud());

        var turn = dictation.ListenAsync(Dispatch, _statuses.Add);
        Assert.Equal(["Recording…"], _statuses);

        await dictation.StopAsync();
        await turn;

        Assert.Equal(["Recording…", "Transcribing…"], _statuses);
    }

    [Fact]
    public async Task An_on_device_turn_reads_listening()
    {
        var device = new ScriptedSpeechTranscriber { Supported = true };
        var dictation = new DictationSession(device);

        var turn = dictation.ListenAsync(Dispatch, _statuses.Add);
        device.Complete(SpeechTranscript.Heard("on the phone"));
        await turn;

        Assert.Equal(["Listening…"], _statuses);
    }

    [Fact]
    public async Task Leaving_a_cloud_turn_sends_nothing()
    {
        var dictation = new DictationSession(Cloud());

        var turn = dictation.ListenAsync(Dispatch, _statuses.Add);
        await dictation.LeaveAsync();

        Assert.True((await turn).IsError);
        Assert.Empty(_service.Uploads);
        Assert.Equal(["Recording…"], _statuses);
    }

    [Fact]
    public async Task Leaving_a_turn_no_engine_has_taken_yet_starts_none()
    {
        _recorder.SupportGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var device = new ScriptedSpeechTranscriber { Supported = true };
        var dictation = new DictationSession(Selecting(device));

        var turn = dictation.ListenAsync(Dispatch, _statuses.Add);
        await dictation.LeaveAsync();
        _recorder.SupportGate.SetResult(true);

        Assert.True((await turn).IsError);
        Assert.Equal(0, _recorder.Turns);
        Assert.False(device.IsListening);
        Assert.Empty(_statuses);
    }

    /// <summary>Nothing leaves the phone on a device turn, and the note page keeps the words
    /// heard after it closed: leaving stops the recogniser rather than throwing its turn away.</summary>
    [Fact]
    public async Task Leaving_an_on_device_turn_stops_it_and_its_words_still_arrive()
    {
        var device = new ScriptedSpeechTranscriber { Supported = true };
        var dictation = new DictationSession(device);

        var turn = dictation.ListenAsync(Dispatch, _statuses.Add);
        await dictation.LeaveAsync();
        device.Complete(SpeechTranscript.Heard("still mine"));

        Assert.Equal(1, device.StopCount);
        Assert.Equal("still mine", (await turn).Text);
    }

    [Fact]
    public async Task Leaving_with_no_turn_does_nothing()
    {
        var device = new ScriptedSpeechTranscriber { Supported = true };
        var dictation = new DictationSession(device);

        await dictation.LeaveAsync();

        Assert.Equal(0, device.StopCount);
    }

    [Fact]
    public async Task A_transcriber_that_throws_is_a_failed_turn()
    {
        var dictation = new DictationSession(new ThrowingTranscriber());

        var result = await dictation.ListenAsync(Dispatch, _statuses.Add);

        Assert.Equal(SpeechTranscript.Failed("The recogniser broke."), result);
    }

    private static Task Dispatch(Action work)
    {
        work();
        return Task.CompletedTask;
    }

    private CloudSpeechTranscriber Cloud() =>
        new(_recorder, _service.Client(), _settings, new CloudDictationAvailability());

    private SelectingSpeechTranscriber Selecting(ISpeechTranscriber device)
    {
        var availability = new CloudDictationAvailability();
        return new SelectingSpeechTranscriber(
            device,
            new CloudSpeechTranscriber(_recorder, _service.Client(), _settings, availability),
            _settings,
            availability,
            new SyncedStatus());
    }

    private sealed class SyncedStatus : ISyncStatusSource
    {
        public SyncStatusReading Current { get; } =
            SyncStatusReading.Synced(null);

        public event Action? Changed { add { } remove { } }
    }

    private sealed class ThrowingTranscriber : ISpeechTranscriber
    {
        public ValueTask<bool> IsSupportedAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(true);

        public Task<SpeechTranscript> ListenAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The recogniser broke.");

        public ValueTask StopAsync() => ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
