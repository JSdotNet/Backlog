using System.Net;

using Backlog.Modules.Sync.Abstractions;
using Backlog.UI.Components.Shell;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// Which engine takes a dictation turn (local ADR 0045 §6): the one the Settings sheet names,
/// read each turn; the device whenever the phone is offline or unpaired; and the device for
/// the rest of the app run once the cloud said it cannot transcribe here.
/// </summary>
public sealed class SelectingSpeechTranscriberTests
{
    private readonly ScriptedSpeechTranscriber _onDevice = new() { Supported = true };
    private readonly ScriptedAudioRecorder _recorder = new();
    private readonly ScriptedTranscriptionService _service = new();
    private readonly InMemorySpeechSettingsStore _settings = new();
    private readonly CloudDictationAvailability _availability = new();
    private readonly FixedSyncStatus _status = new(SyncStatusReading.Synced(null));

    [Fact]
    public async Task On_device_is_the_engine_until_the_person_chooses_the_cloud()
    {
        await using var speech = Selecting();

        var turn = speech.ListenAsync(TestContext.Current.CancellationToken);
        _onDevice.Complete(SpeechTranscript.Heard("on the phone"));

        Assert.Equal("on the phone", (await turn).Text);
        Assert.Equal(0, _recorder.Turns);
        Assert.Empty(_service.Uploads);
    }

    [Fact]
    public async Task The_cloud_takes_the_turn_when_chosen_and_the_phone_is_online()
    {
        _settings.Engine = DictationEngine.Cloud;
        await using var speech = Selecting();

        var turn = speech.ListenAsync(TestContext.Current.CancellationToken);
        await speech.StopAsync();

        Assert.Equal("hello from the cloud", (await turn).Text);
        Assert.False(_onDevice.IsListening);
        Assert.Single(_service.Uploads);
    }

    [Fact]
    public async Task A_change_of_engine_applies_from_the_next_turn()
    {
        await using var speech = Selecting();

        var first = speech.ListenAsync(TestContext.Current.CancellationToken);
        _onDevice.Complete(SpeechTranscript.Heard("one"));
        await first;

        _settings.Engine = DictationEngine.Cloud;
        var second = speech.ListenAsync(TestContext.Current.CancellationToken);
        await speech.StopAsync();

        Assert.Equal("hello from the cloud", (await second).Text);
    }

    [Theory]
    [InlineData(SyncStatusKind.Offline)]
    [InlineData(SyncStatusKind.NotPaired)]
    public async Task An_offline_or_unpaired_phone_listens_on_the_device_without_a_word(SyncStatusKind kind)
    {
        _settings.Engine = DictationEngine.Cloud;
        _status.Current = new SyncStatusReading(kind);
        await using var speech = Selecting();

        var turn = speech.ListenAsync(TestContext.Current.CancellationToken);
        _onDevice.Complete(SpeechTranscript.Heard("on the phone"));

        Assert.Equal(SpeechTranscript.Heard("on the phone"), await turn);
        Assert.Equal(0, _recorder.Turns);
    }

    [Fact]
    public async Task Stop_reaches_the_engine_that_took_the_turn()
    {
        await using var speech = Selecting();

        _ = speech.ListenAsync(TestContext.Current.CancellationToken);
        await speech.StopAsync();

        Assert.Equal(1, _onDevice.StopCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, null)]
    [InlineData(HttpStatusCode.ServiceUnavailable, SyncErrorCodes.TranscriptionUnavailable)]
    public async Task A_cloud_that_cannot_transcribe_here_says_so_once_and_the_device_takes_every_later_turn(HttpStatusCode status, string? code)
    {
        _settings.Engine = DictationEngine.Cloud;
        _service.Answer = (_, _) => Task.FromResult(ScriptedTranscriptionService.Problem(status, code));
        await using var speech = Selecting();

        var first = speech.ListenAsync(TestContext.Current.CancellationToken);
        await speech.StopAsync();
        Assert.Equal(SpeechTranscript.Failed("Cloud dictation is unavailable here; using on-device."), await first);

        var second = speech.ListenAsync(TestContext.Current.CancellationToken);
        _onDevice.Complete(SpeechTranscript.Heard("on the phone"));

        Assert.Equal("on the phone", (await second).Text);
        Assert.Equal(1, _recorder.Turns);
    }

    [Fact]
    public async Task An_unreachable_cloud_falls_back_to_the_device_for_later_turns()
    {
        _settings.Engine = DictationEngine.Cloud;
        _service.Answer = (_, _) => throw new HttpRequestException("No route to host.");
        await using var speech = Selecting();

        var first = speech.ListenAsync(TestContext.Current.CancellationToken);
        await speech.StopAsync();
        Assert.Equal(SpeechTranscript.Failed("Cloud dictation is unavailable here; using on-device."), await first);

        var second = speech.ListenAsync(TestContext.Current.CancellationToken);
        _onDevice.Complete(SpeechTranscript.Heard("on the phone"));

        Assert.Equal("on the phone", (await second).Text);
    }

    [Fact]
    public async Task A_failed_transcription_is_no_fallback_and_the_next_turn_tries_the_cloud_again()
    {
        _settings.Engine = DictationEngine.Cloud;
        _service.Answer = (_, _) => Task.FromResult(
            ScriptedTranscriptionService.Problem(HttpStatusCode.BadGateway, SyncErrorCodes.TranscriptionFailed));
        await using var speech = Selecting();

        var first = speech.ListenAsync(TestContext.Current.CancellationToken);
        await speech.StopAsync();
        Assert.Equal(SpeechTranscript.Failed("Transcription failed, try again."), await first);

        _service.Answer = (_, _) => Task.FromResult(ScriptedTranscriptionService.Text("second time lucky"));
        var second = speech.ListenAsync(TestContext.Current.CancellationToken);
        await speech.StopAsync();

        Assert.Equal("second time lucky", (await second).Text);
        Assert.Equal(2, _recorder.Turns);
    }

    [Fact]
    public async Task A_phone_that_cannot_record_dictates_on_the_device_even_with_the_cloud_chosen()
    {
        _settings.Engine = DictationEngine.Cloud;
        _recorder.Supported = false;
        await using var speech = Selecting();

        var turn = speech.ListenAsync(TestContext.Current.CancellationToken);
        _onDevice.Complete(SpeechTranscript.Heard("on the phone"));

        Assert.Equal("on the phone", (await turn).Text);
    }

    [Fact]
    public async Task Dictation_is_offered_where_the_chosen_engine_can_take_a_turn()
    {
        await using var speech = Selecting();

        _onDevice.Supported = false;
        Assert.False(await speech.IsSupportedAsync(TestContext.Current.CancellationToken));

        _settings.Engine = DictationEngine.Cloud;
        Assert.True(await speech.IsSupportedAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_fallback_holds_for_every_screen_of_the_app_run_and_not_past_it()
    {
        await using (var app = Composed())
        {
            await using (var screen = app.CreateAsyncScope())
            {
                var speech = screen.ServiceProvider.GetRequiredService<ISpeechTranscriber>();
                var turn = speech.ListenAsync(TestContext.Current.CancellationToken);
                await speech.StopAsync();
                Assert.Equal(SpeechTranscript.Failed(CloudSpeechTranscriber.UnavailableNotice), await turn);
            }

            await using (var next = app.CreateAsyncScope())
            {
                Assert.False(next.ServiceProvider.GetRequiredService<CloudDictationAvailability>().IsAvailable);
            }
        }

        await using var restarted = Composed();
        Assert.True(restarted.GetRequiredService<CloudDictationAvailability>().IsAvailable);
    }

    /// <summary>The browser's support question can take a round trip — and a permission
    /// prompt sits behind the recorder's start. A stop pressed in that window is the
    /// person's answer for this turn, not a press that reached nothing.</summary>
    [Fact]
    public async Task A_stop_before_an_engine_took_the_turn_ends_it_with_nothing_recorded()
    {
        _settings.Engine = DictationEngine.Cloud;
        _recorder.SupportGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var speech = Selecting();

        var turn = speech.ListenAsync(TestContext.Current.CancellationToken);
        await speech.StopAsync();
        _recorder.SupportGate.SetResult(true);

        Assert.Equal(SpeechTranscript.Heard(string.Empty), await turn);
        Assert.Equal(0, _recorder.Turns);
        Assert.False(_onDevice.IsListening);
        Assert.Empty(_service.Uploads);
    }

    [Fact]
    public async Task A_stop_holds_for_its_own_turn_only()
    {
        _settings.Engine = DictationEngine.Cloud;
        _recorder.SupportGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var speech = Selecting();

        var first = speech.ListenAsync(TestContext.Current.CancellationToken);
        await speech.StopAsync();
        _recorder.SupportGate.SetResult(true);
        await first;

        var second = speech.ListenAsync(TestContext.Current.CancellationToken);
        await WaitUntil(() => _recorder.IsRecording);
        await speech.StopAsync();

        Assert.Equal("hello from the cloud", (await second).Text);
    }

    [Fact]
    public async Task A_turn_cancelled_before_an_engine_took_it_starts_neither()
    {
        _settings.Engine = DictationEngine.Cloud;
        _recorder.SupportGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await using var speech = Selecting();

        var turn = speech.ListenAsync(cancel.Token);
        await cancel.CancelAsync();
        _recorder.SupportGate.SetResult(true);

        Assert.True((await turn).IsError);
        Assert.Equal(0, _recorder.Turns);
        Assert.False(_onDevice.IsListening);
    }

    /// <summary>The engine that takes the turn words its first line, so a cloud turn never
    /// reads "Listening…" on its way to "Recording…".</summary>
    [Fact]
    public async Task An_on_device_turn_says_it_is_listening()
    {
        var stages = new List<SpeechTurnStage>();
        await using var speech = Selecting();

        var turn = speech.ListenAsync(stages.Add, TestContext.Current.CancellationToken);
        _onDevice.Complete(SpeechTranscript.Heard("on the phone"));
        await turn;

        Assert.Equal([SpeechTurnStage.Listening], stages);
    }

    [Fact]
    public async Task A_cloud_turn_says_nothing_until_the_cloud_takes_it_and_then_that_it_records()
    {
        _settings.Engine = DictationEngine.Cloud;
        _recorder.SupportGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stages = new List<SpeechTurnStage>();
        await using var speech = Selecting();

        var turn = speech.ListenAsync(stages.Add, TestContext.Current.CancellationToken);
        Assert.Empty(stages);

        _recorder.SupportGate.SetResult(true);
        await WaitUntil(() => _recorder.IsRecording);
        Assert.Equal([SpeechTurnStage.Recording], stages);

        await speech.StopAsync();
        await turn;
        Assert.Equal([SpeechTurnStage.Recording, SpeechTurnStage.Transcribing], stages);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.True(condition());
    }

    private SelectingSpeechTranscriber Selecting() =>
        new(_onDevice, new CloudSpeechTranscriber(_recorder, _service.Client(), _settings, _availability), _settings, _availability, _status);

    /// <summary>The registration both heads make, over a service with no model configured.</summary>
    private ServiceProvider Composed()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISpeechSettingsStore>(new InMemorySpeechSettingsStore { Engine = DictationEngine.Cloud });
        services.AddSingleton<ISyncStatusSource>(_status);
        services.AddSingleton<IAudioRecorder>(_ => new ScriptedAudioRecorder());
        var service = new ScriptedTranscriptionService
        {
            Answer = (_, _) => Task.FromResult(
                ScriptedTranscriptionService.Problem(HttpStatusCode.ServiceUnavailable, SyncErrorCodes.TranscriptionUnavailable))
        };
        services.AddSingleton(_ => service.Client());
        services.AddDictation<ScriptedSpeechTranscriber>();
        return services.BuildServiceProvider();
    }

    /// <summary>A sync status line that reads what the test says.</summary>
    private sealed class FixedSyncStatus(SyncStatusReading reading) : ISyncStatusSource
    {
        public SyncStatusReading Current { get; set; } = reading;

        public event Action? Changed { add { } remove { } }
    }
}
