using System.Text.Json.Serialization;

using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Abstractions.Services;

/// <summary>One meeting recording to transcribe, as it lies on this machine.</summary>
/// <param name="Audio">The file's bytes: AAC in ADTS, or an M4A file.</param>
/// <param name="ContentType">The media type the note names it with.</param>
public sealed record InboxRecordingDto(ReadOnlyMemory<byte> Audio, string ContentType);

/// <summary>What a recording became: the transcript, each part headed by its offset, and the
/// summary with its action items as <c>- [ ]</c> lines. Markdown, with no heading of its
/// own; the Inbox writes the headings.</summary>
public sealed record InboxRecordingTranscriptDto(string TranscriptMd, string SummaryMd);

/// <summary>
/// Whatever turns a note's meeting recording into text — today an adapter over Azure
/// Foundry's transcription and chat deployments (local ADR 0049). A host without one
/// registers nothing, and recordings stay on their notes.
/// </summary>
public interface IInboxRecordingTranscriber
{
    /// <summary>Whether a recording would be sent at all: false while Foundry is not
    /// configured, so nothing leaves the machine and nothing is marked done.</summary>
    bool IsAvailable { get; }

    /// <summary>Transcribes and summarizes the recording. Every failure — a part refused,
    /// the summary failed, Foundry unreachable — comes back as
    /// <c>inbox.transcription.failed</c> rather than thrown, with nothing Foundry said.</summary>
    Task<Result<InboxRecordingTranscriptDto>> TranscribeAsync(InboxRecordingDto recording, CancellationToken cancellationToken = default);
}

/// <summary>A recording this desktop transcribed: the sections it wrote to its note, kept
/// after the audio is gone so a phone edit that drops them can be repaired (local ADR 0049
/// §6).</summary>
public sealed record InboxTranscriptionRecordDto(
    string Sha256,
    Guid NoteId,
    Guid AttachmentId,
    string SectionsMd,
    DateTimeOffset WrittenAt);

/// <summary>A recording this desktop tried and failed to transcribe: how many attempts
/// failed, the last one's reason, and when (local ADR 0049 §1). Removed on a success or a
/// person's Retry.</summary>
public sealed record InboxTranscriptionFailureDto(
    string Sha256,
    int Attempts,
    string LastReason,
    DateTimeOffset At)
{
    /// <summary>The failed attempts after which a file is not sent again until a person
    /// presses Retry.</summary>
    public const int MaxAttempts = 3;

    /// <summary>Whether the file has failed often enough to wait for a person's Retry.</summary>
    [JsonIgnore]
    public bool WaitsForRetry => Attempts >= MaxAttempts;
}

/// <summary>The local record of every recording transcribed here, keyed by the file's
/// sha256: a file whose digest is here is never sent again (local ADR 0049 §1). Beside it,
/// a failure record per sha256 that bounds the attempts at a file Foundry keeps refusing.</summary>
public interface IInboxTranscriptionRecords
{
    Task<InboxTranscriptionRecordDto?> FindAsync(string sha256, CancellationToken cancellationToken = default);

    Task SaveAsync(InboxTranscriptionRecordDto record, CancellationToken cancellationToken = default);

    /// <summary>The failure record for a file, or null when no attempt at it has failed
    /// since its last success or Retry.</summary>
    Task<InboxTranscriptionFailureDto?> FindFailureAsync(string sha256, CancellationToken cancellationToken = default);

    /// <summary>Adds one failed attempt to the file's record, keeping
    /// <paramref name="reason"/> and <paramref name="at"/> as the last, and answers the
    /// record as it now stands.</summary>
    Task<InboxTranscriptionFailureDto> CountFailureAsync(string sha256, string reason, DateTimeOffset at, CancellationToken cancellationToken = default);

    /// <summary>Removes the file's failure record — on a success, or a person's Retry.
    /// Nothing happens when there is none.</summary>
    Task ClearFailureAsync(string sha256, CancellationToken cancellationToken = default);

    /// <summary>The most recent failure of any file — what Settings shows under the
    /// transcription deployment (local ADR 0049 §2). Null when none is kept, or when a
    /// recording was transcribed after it, because then the last transcription did not
    /// fail.</summary>
    Task<InboxTranscriptionFailureDto?> LatestFailureAsync(CancellationToken cancellationToken = default);
}
