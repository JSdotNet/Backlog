using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Inbox.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Modules.Inbox.Features.RetryAttachment;

/// <summary>Fetches one of an item's files again — Retry on a row whose
/// download failed.</summary>
public sealed record RetryAttachmentCommand(Guid Id, Guid AttachmentId);

/// <summary>
/// The intake's fetch, asked for by a person. The same step, so the same rules:
/// a file already on this machine with the capture's digest is not fetched, and
/// a failure is recorded on the file. What differs is the answer — the pane is
/// waiting for one, so the recorded reason also comes back as a failed
/// <see cref="Result"/> for the toast, and the item is saved either way so the
/// row shows it after the reload. Allowed in every state: a file belongs to the
/// thought however it was decided, though once the item is routed or archived
/// the service has usually released the blob and the answer says so.
/// </summary>
public sealed class RetryAttachmentCommandHandler(
    IInboxItemRepository items,
    TimeProvider clock,
    IInboxAttachmentSource? attachmentSource = null,
    IInboxAttachmentFiles? attachmentFiles = null,
    ILogger<RetryAttachmentCommandHandler>? logger = null)
    : ICommandHandler<RetryAttachmentCommand, Result>
{
    private readonly ILogger _logger = logger ?? NullLogger<RetryAttachmentCommandHandler>.Instance;

    public async Task<Result> Handle(RetryAttachmentCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var item = await items.GetAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (item is null) return Result.Failure(InboxErrors.ItemNotFound);
        if (item.Attachments.All(attachment => attachment.Id != command.AttachmentId)) return Result.Failure(InboxErrors.AttachmentNotFound);
        if (attachmentSource is null || attachmentFiles is null) return Result.Failure(InboxErrors.AttachmentsUnavailable);

        var error = await InboxAttachmentDownloads
            .DownloadAsync(item, command.AttachmentId, attachmentSource, attachmentFiles, clock, _logger, cancellationToken)
            .ConfigureAwait(false);

        await items.SaveAsync(item, cancellationToken).ConfigureAwait(false);

        return error is null
            ? Result.Success()
            : Result.Failure(new Error("inbox.attachment.download_failed", error));
    }
}
