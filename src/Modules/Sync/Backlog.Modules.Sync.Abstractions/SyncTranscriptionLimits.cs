namespace Backlog.Modules.Sync.Abstractions;

/// <summary>
/// The defaults local ADR 0045 §2 puts on a dictation upload: the cap, the audio
/// allowlist, and the language hints the route takes. Written down here, beside the
/// routes, because both ends read them — the service enforces them (as the defaults of
/// its <c>SyncTranscriptionOptions</c> setting) and the phone records only a listed type
/// and sends only a listed hint.
/// </summary>
public static class SyncTranscriptionLimits
{
    /// <summary>The most one recording may weigh by default: 25 MB, Foundry's own file
    /// limit for the transcription model.</summary>
    public const long DefaultMaxBytes = 25L * 1024 * 1024;

    /// <summary>The audio types a recording may declare when the setting names none. Each
    /// is one Foundry's transcription model takes.</summary>
    public static readonly IReadOnlyList<string> DefaultContentTypes =
    [
        "audio/mp4",
        "audio/x-m4a",
        "audio/webm",
        "audio/wav",
        "audio/mpeg",
    ];

    /// <summary>The language hints the route passes on: Dutch and English. Anything else is
    /// refused; no hint at all lets the model detect the language.</summary>
    public static readonly IReadOnlyList<string> Languages = ["nl", "en"];
}
