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

/// <summary>A recording whose transcription has failed here, by the file's sha256: how many
/// attempts have failed, the last one's reason, and when (local ADR 0049 §1). After
/// <see cref="MaxAttempts"/> the file waits for a person's Retry.</summary>
/// <param name="Reason">Words a person can act on, never anything Foundry said.</param>
public sealed record InboxTranscriptionFailureDto(
    string Sha256,
    Guid NoteId,
    Guid AttachmentId,
    int Attempts,
    string Reason,
    DateTimeOffset FailedAt)
{
    /// <summary>How many failed attempts a file is given before it waits for Retry.</summary>
    public const int MaxAttempts = 3;

    /// <summary>Whether the file is no longer sent on a pull or a start.</summary>
    public bool WaitsForRetry => Attempts >= MaxAttempts;
}

/// <summary>The local record of every recording transcribed here, keyed by the file's
/// sha256: a file whose digest is here is never sent again (local ADR 0049 §1). Beside the
/// done records it keeps a failure record per sha256, the count that bounds a failing file to
/// three attempts, and what Settings reads the last failure from.</summary>
public interface IInboxTranscriptionRecords
{
    Task<InboxTranscriptionRecordDto?> FindAsync(string sha256, CancellationToken cancellationToken = default);

    Task SaveAsync(InboxTranscriptionRecordDto record, CancellationToken cancellationToken = default);

    Task<InboxTranscriptionFailureDto?> FindFailureAsync(string sha256, CancellationToken cancellationToken = default);

    /// <summary>Counts one more failed attempt for <paramref name="sha256"/>, keeping
    /// <paramref name="reason"/> as the last, and answers the record as it now stands.</summary>
    Task<InboxTranscriptionFailureDto> RecordFailureAsync(
        string sha256,
        Guid noteId,
        Guid attachmentId,
        string reason,
        DateTimeOffset failedAt,
        CancellationToken cancellationToken = default);

    /// <summary>Forgets the failures of <paramref name="sha256"/>: on a success, and on Retry.</summary>
    Task ClearFailureAsync(string sha256, CancellationToken cancellationToken = default);

    /// <summary>Every failure record kept, in no particular order.</summary>
    Task<IReadOnlyList<InboxTranscriptionFailureDto>> ListFailuresAsync(CancellationToken cancellationToken = default);
}
