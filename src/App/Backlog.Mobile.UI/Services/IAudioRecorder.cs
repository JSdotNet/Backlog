namespace Backlog.Mobile.UI.Services;

/// <summary>
/// What one recording turn captured: the audio and the type it is in, or a reason there is
/// none.
/// </summary>
/// <remarks>
/// A failure is a result for the reason <see cref="SpeechTranscript"/> gives: a denied
/// microphone or a missing one is an ordinary answer the screen has to put into words.
/// </remarks>
/// <param name="Content">The encoded audio. Empty when <paramref name="Error"/> is set.</param>
/// <param name="ContentType">The audio's media type as the recorder produced it, parameters
/// included — <c>audio/webm;codecs=opus</c>, <c>audio/mp4</c>.</param>
/// <param name="Error">A message fit to show a person, or <c>null</c> when it recorded.</param>
public sealed record AudioRecording(byte[] Content, string ContentType, string? Error)
{
    /// <summary>A turn that recorded.</summary>
    public static AudioRecording Recorded(byte[] content, string contentType) =>
        new(content ?? [], contentType, null);

    /// <summary>A turn that recorded nothing, and why.</summary>
    public static AudioRecording Failed(string error) => new([], string.Empty, error);

    /// <summary>Whether this turn ended in a reason rather than audio.</summary>
    public bool IsError => Error is not null;
}

/// <summary>
/// The phone's microphone, as the cloud dictation engine sees it (local ADR 0045 §6).
/// </summary>
/// <remarks>
/// Two implementations, one per host, for the reason <see cref="ISpeechTranscriber"/> has
/// two: the browser harness records webm through <c>MediaRecorder</c>, and the Android head
/// records AAC in MPEG-4 through Android's own <c>MediaRecorder</c>. Each records in a type
/// the sync route's allowlist takes.
/// </remarks>
public interface IAudioRecorder : IAsyncDisposable
{
    /// <summary>
    /// Whether this device can record at all. <c>false</c> rather than a throw where it
    /// cannot, and while a component is prerendering.
    /// </summary>
    ValueTask<bool> IsSupportedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts recording and completes when the recording ends — because
    /// <see cref="StopAsync"/> was called, or because <paramref name="maxDuration"/> passed,
    /// which ends it the same way — with what it captured.
    /// </summary>
    /// <remarks>One turn at a time: starting a second abandons the first.</remarks>
    Task<AudioRecording> RecordAsync(TimeSpan maxDuration, CancellationToken cancellationToken = default);

    /// <summary>Finishes the recording; its audio arrives on <see cref="RecordAsync"/>.
    /// Safe to call when nothing is recording.</summary>
    ValueTask StopAsync();
}
