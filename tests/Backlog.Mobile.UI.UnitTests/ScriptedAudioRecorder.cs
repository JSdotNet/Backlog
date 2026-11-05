namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// A microphone the test finishes by hand. <see cref="RecordAsync"/> stays pending until
/// <see cref="StopAsync"/> — the person pressing stop — and then hands back
/// <see cref="Recording"/>, as the real recorders hand back what they captured. A cancelled
/// turn ends as the real ones end it, with nothing recorded. It keeps the longest recording
/// each turn was allowed.
/// </summary>
internal sealed class ScriptedAudioRecorder : IAudioRecorder
{
    private TaskCompletionSource<AudioRecording>? _pending;

    public bool Supported { get; set; } = true;

    /// <summary>When set, <see cref="IsSupportedAsync"/> answers only once the test completes
    /// it — the browser's round trip, held open.</summary>
    public TaskCompletionSource<bool>? SupportGate { get; set; }

    /// <summary>What a stopped turn hands back.</summary>
    public AudioRecording Recording { get; set; } =
        AudioRecording.Recorded([1, 2, 3, 4, 5], "audio/webm;codecs=opus");

    /// <summary>The longest recording every turn was allowed, in order.</summary>
    public List<TimeSpan> MaxDurations { get; } = [];

    public int Turns => MaxDurations.Count;

    public bool IsRecording => Volatile.Read(ref _pending) is not null;

    public async ValueTask<bool> IsSupportedAsync(CancellationToken cancellationToken = default) =>
        SupportGate is { } gate ? await gate.Task && Supported : Supported;

    public Task<AudioRecording> RecordAsync(TimeSpan maxDuration, CancellationToken cancellationToken = default)
    {
        MaxDurations.Add(maxDuration);

        var pending = new TaskCompletionSource<AudioRecording>(TaskCreationOptions.RunContinuationsAsynchronously);
        Volatile.Write(ref _pending, pending);
        cancellationToken.Register(() =>
        {
            Interlocked.CompareExchange(ref _pending, null, pending);
            pending.TrySetResult(AudioRecording.Failed("Recording was cancelled."));
        });
        return pending.Task;
    }

    public ValueTask StopAsync()
    {
        Interlocked.Exchange(ref _pending, null)?.TrySetResult(Recording);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
