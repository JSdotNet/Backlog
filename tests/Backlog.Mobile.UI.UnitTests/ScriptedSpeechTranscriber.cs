namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// A recogniser the test finishes by hand. <see cref="ListenAsync"/> stays
/// pending exactly as the real ones do, which is what makes the listening state
/// observable at all. It says it has no recogniser until <see cref="Supported"/>
/// is set.
/// </summary>
internal sealed class ScriptedSpeechTranscriber : ISpeechTranscriber
{
    private TaskCompletionSource<SpeechTranscript>? _pending;

    public bool Supported { get; set; }

    public int StopCount { get; private set; }

    public bool IsListening => Volatile.Read(ref _pending) is not null;

    public ValueTask<bool> IsSupportedAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Supported);

    public Task<SpeechTranscript> ListenAsync(CancellationToken cancellationToken = default)
    {
        if (!Supported) return Task.FromResult(SpeechTranscript.Failed("No recogniser."));

        var pending = new TaskCompletionSource<SpeechTranscript>(TaskCreationOptions.RunContinuationsAsynchronously);
        Volatile.Write(ref _pending, pending);
        return pending.Task;
    }

    public ValueTask StopAsync()
    {
        StopCount++;
        return ValueTask.CompletedTask;
    }

    public void Complete(SpeechTranscript result) =>
        Interlocked.Exchange(ref _pending, null)?.TrySetResult(result);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
