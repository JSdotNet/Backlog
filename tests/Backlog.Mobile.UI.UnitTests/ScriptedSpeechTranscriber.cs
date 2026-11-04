namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// A recogniser the test finishes by hand. <see cref="ListenAsync"/> stays
/// pending exactly as the real ones do, which is what makes the listening state
/// observable at all. It says it has no recogniser until <see cref="Supported"/>
/// is set. Like the real ones, it reads the Dictation setting at the start of
/// every turn, when it has been given one, and keeps what each turn asked for.
/// </summary>
internal sealed class ScriptedSpeechTranscriber : ISpeechTranscriber
{
    private TaskCompletionSource<SpeechTranscript>? _pending;

    public bool Supported { get; set; }

    public int StopCount { get; private set; }

    /// <summary>The Dictation setting each turn reads, as the real transcribers do.</summary>
    public ISpeechSettingsStore? Settings { get; set; }

    /// <summary>The Dictation language every turn started with, in order.</summary>
    public List<DictationLanguage> TurnLanguages { get; } = [];

    public bool IsListening => Volatile.Read(ref _pending) is not null;

    public ValueTask<bool> IsSupportedAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Supported);

    public Task<SpeechTranscript> ListenAsync(CancellationToken cancellationToken = default)
    {
        if (!Supported) return Task.FromResult(SpeechTranscript.Failed("No recogniser."));

        if (Settings is not null) TurnLanguages.Add(Settings.Language);

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
