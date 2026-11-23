using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Inbox.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Features.RetryTranscription;

/// <summary>Sends a recording to transcription again — Retry on a recording row whose
/// transcription failed three times (local ADR 0049 §6).</summary>
public sealed record RetryTranscriptionCommand(Guid Id, Guid AttachmentId);

/// <summary>
/// Clears the file's failure record and queues its note, so the worker's next take-up of the
/// note sends the file again with a fresh three attempts. Nothing is sent here: the worker
/// keeps one recording at a time, in the note's order. The record store and the queue are
/// optional ports, as on <see cref="TranscribeRecordings.TranscribeRecordingsCommandHandler"/>:
/// a host without the store transcribes nothing, and Retry says so.
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

        var item = await items.GetAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (item is null) return Result.Failure(InboxErrors.ItemNotFound);

        var recording = item.Attachments.FirstOrDefault(attachment => attachment.Id == command.AttachmentId);
        if (recording is null) return Result.Failure(InboxErrors.AttachmentNotFound);
        if (records is null) return Result.Failure(InboxErrors.TranscriptionUnavailable);

        try
        {
            await records.ClearFailureAsync(recording.Sha256, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // The pane is waiting on an answer, not an exception; the record still stands, so
            // queuing the note would only skip the file again.
            return Result.Failure(InboxErrors.TranscriptionRetryFailed);
        }

        recordings?.Request(item.Id);

        return Result.Success();
    }
}
