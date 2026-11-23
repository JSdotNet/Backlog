using System.Text.RegularExpressions;
using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Inbox.DomainModels;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Modules.Inbox.Features.TranscribeRecordings;

/// <summary>Transcribes the meeting recordings on one note, and repairs the ones already
/// transcribed (local ADR 0049).</summary>
public sealed record TranscribeRecordingsCommand(Guid NoteId);

/// <summary>
/// A note's recordings, from download to removal (local ADR 0049): each recording whose
/// sha256 this desktop has not transcribed is sent, once downloaded, to the transcriber;
/// the note is read again after it answers, <c>## Transcript</c> and <c>## Summary</c> are
/// appended to that body, the recording is taken off the note in the same save, and the
/// sha256 is recorded with the sections written.
/// <para>
/// <b>Never on failure.</b> A refused part, a failed summary, a file that cannot be read,
/// a failed save, or Foundry not configured leaves the body and the file as they were and
/// marks nothing transcribed, so the next request — the next pull, the next start — tries
/// again and the recording still plays.
/// </para>
/// <para>
/// <b>At most three attempts.</b> Each of those failures but the last — Foundry not
/// configured, or the file not downloaded yet — counts one attempt in the file's failure
/// record. After the third the file is not sent until a person's Retry
/// (<see cref="RetryTranscription.RetryTranscriptionCommand"/>), and a success removes
/// the record. This is a budget per file across take-ups, not a retry loop: the adapter
/// keeps guideline ADR 0015's transient retry per call, and answers one
/// <c>inbox.transcription.failed</c> for every cause, so a machine offline for three
/// take-ups parks the file as a wrong deployment does — the cost ADR 0049 accepts.
/// </para>
/// <para>
/// <b>A recording already transcribed is repaired, not sent.</b> A phone edit made on the
/// body before the transcript can arrive later and name the file again or drop the
/// sections (ADR 0025's whole-note merge). For a file whose sha256 is recorded, the kept
/// sections are appended again when the body lacks them, and the file is removed again.
/// </para>
/// <para>
/// One recording at a time, in the note's order, so a rolled-over recording's parts append
/// in order: the second file to be written gets <c>## Transcript (part 2)</c>, and a file
/// that cannot be written yet holds back the files after it. Answers how
/// many recordings it wrote or repaired; it never fails, because the worker that calls it
/// has nobody to tell.
/// </para>
/// </summary>
public sealed partial class TranscribeRecordingsCommandHandler(
    IInboxItemRepository items,
    TimeProvider clock,
    IInboxTranscriptionRecords? records = null,
    IInboxRecordingTranscriber? transcriber = null,
    IInboxAttachmentFiles? files = null,
    ILogger<TranscribeRecordingsCommandHandler>? logger = null)
    : ICommandHandler<TranscribeRecordingsCommand, Result<int>>
{
    private readonly ILogger _logger = logger ?? NullLogger<TranscribeRecordingsCommandHandler>.Instance;

    public async Task<Result<int>> Handle(TranscribeRecordingsCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Without a place to record what was sent, nothing may be sent: it would be sent again.
        if (records is null) return 0;

        var item = await items.GetAsync(command.NoteId, cancellationToken).ConfigureAwait(false);
        if (item is null || !item.IsNote || item.Status is InboxStatus.Archived) return 0;

        var written = 0;
        foreach (var recording in item.Attachments.Where(attachment => attachment.IsRecording).ToList())
        {
            var record = await records.FindAsync(recording.Sha256, cancellationToken).ConfigureAwait(false);
            if (record is not null)
            {
                if (await WriteAsync(command.NoteId, recording.Id, record.SectionsMd, cancellationToken).ConfigureAwait(false)) written++;
                continue;
            }

            // Stop at the first new recording that is not written: a later file of a rolled-over
            // recording written before it would take its part number, and its audio would be
            // gone before the order could be put right. A file that failed three times is not
            // sent until a person's Retry, and holds back the files after it all the same.
            var failure = await records.FindFailureAsync(recording.Sha256, cancellationToken).ConfigureAwait(false);
            if (failure is { WaitsForRetry: true }) break;

            if (!await TranscribeAsync(command.NoteId, recording, records, cancellationToken).ConfigureAwait(false)) break;
            written++;
        }

        return written;
    }

    private async Task<bool> TranscribeAsync(Guid noteId, InboxAttachment recording, IInboxTranscriptionRecords records, CancellationToken cancellationToken)
    {
        if (transcriber is not { IsAvailable: true } || files is null || !recording.IsDownloaded) return false;

        var audio = await files.ReadAsync(recording.LocalPath!, cancellationToken).ConfigureAwait(false);
        if (audio is null)
        {
            LogUnreadable(_logger, recording.Id, noteId);
            await CountFailureAsync(records, recording, UnreadableReason, cancellationToken).ConfigureAwait(false);
            return false;
        }

        var answer = await transcriber.TranscribeAsync(new InboxRecordingDto(audio, recording.ContentType), cancellationToken).ConfigureAwait(false);
        if (answer.IsFailure)
        {
            LogFailed(_logger, recording.Id, noteId, answer.Error.Code);
            await CountFailureAsync(records, recording, answer.Error.Message, cancellationToken).ConfigureAwait(false);
            return false;
        }

        // Read again: the person may have edited the note while the recording was out, and
        // the transcript is appended to that body, not to the one read before sending.
        var current = await items.GetAsync(noteId, cancellationToken).ConfigureAwait(false);
        if (current is null || current.Attachments.All(attachment => attachment.Id != recording.Id)) return false;

        var sections = Sections(current.BodyMd, answer.Value);
        try
        {
            if (!await WriteAsync(current, recording.Id, sections, cancellationToken).ConfigureAwait(false)) return false;
        }
        catch (Exception failure) when (IsFileFailure(failure))
        {
            // Counted, then thrown on as before: the worker logs it, and the sha256 is not
            // marked transcribed, so the next take-up sends the file again.
            await CountFailureAsync(records, recording, SaveFailedReason, cancellationToken).ConfigureAwait(false);
            throw;
        }

        await records.SaveAsync(
            new InboxTranscriptionRecordDto(recording.Sha256, noteId, recording.Id, sections, clock.GetUtcNow()),
            cancellationToken).ConfigureAwait(false);

        // Best effort: the recording is written and recorded, which is what keeps it from
        // being sent again; a failure record left behind is hidden from Settings by the
        // success after it, and a later one replaces it.
        try
        {
            await records.ClearFailureAsync(recording.Sha256, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception failure) when (IsFileFailure(failure))
        {
            LogFailureRecordUnwritable(_logger, failure, recording.Id);
        }

        LogWritten(_logger, recording.Id, noteId);
        return true;
    }

    internal const string UnreadableReason = "The recording could not be read on this machine.";

    internal const string SaveFailedReason = "The note could not be saved with its transcript.";

    private static bool IsFileFailure(Exception failure) => failure is IOException or UnauthorizedAccessException;

    /// <summary>Counts one attempt, never throwing: a failure store that cannot be written
    /// must not turn a failed attempt into a different outcome, or replace the exception the
    /// save threw.</summary>
    private async Task CountFailureAsync(IInboxTranscriptionRecords records, InboxAttachment recording, string reason, CancellationToken cancellationToken)
    {
        try
        {
            await records.CountFailureAsync(recording.Sha256, reason, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception failure) when (IsFileFailure(failure))
        {
            LogFailureRecordUnwritable(_logger, failure, recording.Id);
        }
    }

    private async Task<bool> WriteAsync(Guid noteId, Guid recordingId, string sections, CancellationToken cancellationToken)
    {
        var current = await items.GetAsync(noteId, cancellationToken).ConfigureAwait(false);
        return current is not null && await WriteAsync(current, recordingId, sections, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> WriteAsync(InboxItem note, Guid recordingId, string sections, CancellationToken cancellationToken)
    {
        if (!note.AppendTranscript(recordingId, sections, clock.GetUtcNow())) return false;

        await items.SaveAsync(note, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>The two sections for one recording, numbered after the transcripts the body
    /// already holds, so a rolled-over recording's second file reads as part 2.</summary>
    internal static string Sections(string body, InboxRecordingTranscriptDto transcript)
    {
        var part = TranscriptHeading().Matches(body).Count + 1;
        var suffix = part == 1 ? string.Empty : $" (part {part})";

        return $"## Transcript{suffix}\n\n{transcript.TranscriptMd.Trim()}\n\n## Summary{suffix}\n\n{transcript.SummaryMd.Trim()}";
    }

    [GeneratedRegex(@"^## Transcript( \(part \d+\))?[ \t]*\r?$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex TranscriptHeading();

    [LoggerMessage(Level = LogLevel.Information, Message = "Wrote the transcript of recording {AttachmentId} onto note {NoteId} and removed the recording.")]
    private static partial void LogWritten(ILogger logger, Guid attachmentId, Guid noteId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not transcribe recording {AttachmentId} of note {NoteId} ({Code}); it stays on the note and is tried again.")]
    private static partial void LogFailed(ILogger logger, Guid attachmentId, Guid noteId, string code);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The failed attempts at recording {AttachmentId} could not be written; its attempt count is not current.")]
    private static partial void LogFailureRecordUnwritable(ILogger logger, Exception exception, Guid attachmentId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recording {AttachmentId} of note {NoteId} is marked downloaded but could not be read.")]
    private static partial void LogUnreadable(ILogger logger, Guid attachmentId, Guid noteId);
}
