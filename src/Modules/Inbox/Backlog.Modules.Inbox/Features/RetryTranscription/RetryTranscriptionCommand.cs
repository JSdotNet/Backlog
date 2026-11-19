using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Inbox.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Features.RetryTranscription;

/// <summary>Sends a recording to transcription again — Retry on a recording row whose
/// transcription failed three times (local ADR 0049 §6).</summary>
public sealed record RetryTranscriptionCommand(Guid NoteId, Guid AttachmentId);

/// <summary>
/// Forgets the recording's failed attempts and asks for its note to be taken up again, so
/// the background step sends the file once more with a fresh count of three. It sends
/// nothing itself: the answer comes when the transcript is written or the row shows the next
/// failure. A file whose failures are already gone is asked for all the same, which changes
/// nothing a second press could undo.
/// </summary>
public sealed class RetryTranscriptionCommandHandler(
    IInboxItemRepository items,
    IInboxTranscriptionRecords? records = null,
    IInboxRecordingQueue? recordings = null)
    : ICommandHandler<RetryTranscriptionCommand, Result>
{
    public async Task<Result> Handle(RetryTranscriptionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var item = await items.GetAsync(command.NoteId, cancellationToken).ConfigureAwait(false);
        if (item is null) return Result.Failure(InboxErrors.ItemNotFound);

        var recording = item.Attachments.FirstOrDefault(attachment => attachment.Id == command.AttachmentId);
        if (recording is null) return Result.Failure(InboxErrors.AttachmentNotFound);
        if (records is null || recordings is null) return Result.Failure(InboxErrors.TranscriptionUnavailable);

        await records.ClearFailureAsync(recording.Sha256, cancellationToken).ConfigureAwait(false);
        recordings.Request(item.Id);
        return Result.Success();
    }
}
