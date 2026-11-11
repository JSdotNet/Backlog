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

/// <summary>The local record of every recording transcribed here, keyed by the file's
/// sha256: a file whose digest is here is never sent again (local ADR 0049 §1).</summary>
public interface IInboxTranscriptionRecords
{
    Task<InboxTranscriptionRecordDto?> FindAsync(string sha256, CancellationToken cancellationToken = default);

    Task SaveAsync(InboxTranscriptionRecordDto record, CancellationToken cancellationToken = default);
}
