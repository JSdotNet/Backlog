namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The browser harness's recorder ends a turn's microphone however the turn ends: a cancelled
/// turn and a torn-down page abandon the recording in <c>recorder.js</c> rather than leaving it
/// running to the cap, and a stop that came before the microphone opened is a recording with
/// nothing in it.
/// </summary>
public sealed class WebAudioRecorderTests
{
    private const string ModulePath = "./_content/Backlog.Mobile.UI/recorder.js";

    [Fact]
    public async Task A_cancelled_recording_turns_the_microphone_off()
    {
        using var context = new BunitContext();
        var module = Recorder(context);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await using var recorder = new WebAudioRecorder(context.JSInterop.JSRuntime);

        var turn = recorder.RecordAsync(TimeSpan.FromMinutes(5), cancel.Token);
        await cancel.CancelAsync();

        Assert.True((await turn).IsError);
        module.VerifyInvoke("abort");
    }

    [Fact]
    public async Task Leaving_mid_turn_abandons_the_recording_rather_than_finishing_it()
    {
        using var context = new BunitContext();
        var module = Recorder(context);
        var recorder = new WebAudioRecorder(context.JSInterop.JSRuntime);

        var turn = recorder.RecordAsync(TimeSpan.FromMinutes(5), TestContext.Current.CancellationToken);
        await recorder.DisposeAsync();

        Assert.True((await turn).IsError);
        module.VerifyInvoke("abort");
        module.VerifyNotInvoke("stop");
    }

    [Fact]
    public async Task A_stop_before_the_microphone_opened_is_an_empty_recording()
    {
        using var context = new BunitContext();
        Recorder(context);
        await using var recorder = new WebAudioRecorder(context.JSInterop.JSRuntime);

        var turn = recorder.RecordAsync(TimeSpan.FromMinutes(5), TestContext.Current.CancellationToken);
        await recorder.OnStoppedBeforeRecordingAsync("audio/webm;codecs=opus");

        var recording = await turn;
        Assert.False(recording.IsError);
        Assert.Empty(recording.Content);
    }

    private static BunitJSModuleInterop Recorder(BunitContext context)
    {
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var module = context.JSInterop.SetupModule(ModulePath);
        module.Mode = JSRuntimeMode.Loose;
        return module;
    }
}
