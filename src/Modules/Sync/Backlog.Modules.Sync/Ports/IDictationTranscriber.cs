namespace Backlog.Modules.Sync.Ports;

/// <summary>
/// Turns one recorded dictation into text (local ADR 0045). Declared here, beside
/// <see cref="IAttachmentStore"/>, and implemented outside — Foundry's transcription
/// deployment, deployed and under Aspire — so the module says what it needs without naming
/// the service.
/// <para>
/// Nothing behind this port keeps the audio or the text: no store, no log, no telemetry
/// payload. An implementation reports a failure by its outcome, never by quoting what the
/// upstream said, because that may quote the input.
/// </para>
/// </summary>
public interface IDictationTranscriber
{
    /// <summary>Transcribes <paramref name="audio"/>. Answers
    /// <see cref="DictationTranscription.Unavailable"/> when this host has no transcription
    /// service, and <see cref="DictationTranscription.Failed"/> when the service did not
    /// answer with text in time; throws only when <paramref name="cancellationToken"/> is
    /// cancelled.</summary>
    Task<DictationTranscription> TranscribeAsync(DictationAudio audio, CancellationToken cancellationToken = default);
}

/// <summary>One recording, held in memory so a retry can send it again.</summary>
/// <param name="Content">The audio bytes, bounded by the route's cap.</param>
/// <param name="ContentType">The declared media type without its parameters, already on the
/// allowlist.</param>
/// <param name="Language">The language hint — <c>nl</c> or <c>en</c> — or null for the model
/// to detect.</param>
public sealed record DictationAudio(ReadOnlyMemory<byte> Content, string ContentType, string? Language);

/// <summary>How a transcription ended.</summary>
public enum DictationTranscriptionStatus
{
    /// <summary>The text came back.</summary>
    Transcribed,

    /// <summary>This host has no transcription service; asking again will not help.</summary>
    Unavailable,

    /// <summary>The service failed or ran out of time; the person may try again.</summary>
    Failed,
}

/// <summary>What a transcriber answered: a status, and the text when there is one.</summary>
public sealed record DictationTranscription(DictationTranscriptionStatus Status, string? Text)
{
    /// <summary>No transcription service on this host.</summary>
    public static DictationTranscription Unavailable { get; } = new(DictationTranscriptionStatus.Unavailable, null);

    /// <summary>The service did not answer with text.</summary>
    public static DictationTranscription Failed { get; } = new(DictationTranscriptionStatus.Failed, null);

    /// <summary>The text of the recording.</summary>
    public static DictationTranscription Transcribed(string text) => new(DictationTranscriptionStatus.Transcribed, text);
}
