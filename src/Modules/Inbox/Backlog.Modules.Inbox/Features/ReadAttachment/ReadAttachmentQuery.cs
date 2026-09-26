using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Inbox.DomainModels;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Features.ReadAttachment;

/// <summary>The bytes of one downloaded file on an item — what the pane draws a
/// thumbnail from.</summary>
public sealed record ReadAttachmentQuery(Guid Id, Guid AttachmentId);

/// <summary>
/// Reads a file the item says is on this machine. A file that is not — still
/// waiting, failed, or deleted from the folder by hand since — answers
/// <c>inbox.attachment.not_downloaded</c>, and the pane draws the row without a
/// picture rather than a broken one.
/// </summary>
public sealed class ReadAttachmentQueryHandler(IInboxItemRepository items, IInboxAttachmentFiles? attachmentFiles = null)
    : IQueryHandler<ReadAttachmentQuery, Result<byte[]>>
{
    public async Task<Result<byte[]>> Handle(ReadAttachmentQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var found = await DownloadedAttachment.FindAsync(items, query.Id, query.AttachmentId, cancellationToken).ConfigureAwait(false);
        if (found.IsFailure) return Result.Failure<byte[]>(found.Error);
        if (attachmentFiles is null) return Result.Failure<byte[]>(InboxErrors.AttachmentsUnavailable);

        var bytes = await attachmentFiles.ReadAsync(found.Value.LocalPath!, cancellationToken).ConfigureAwait(false);

        return bytes is null
            ? Result.Failure<byte[]>(InboxErrors.AttachmentNotDownloaded)
            : Result.Success(bytes);
    }
}

/// <summary>The look-up the read and the open share: the item, the file on it,
/// and that the file is on this machine.</summary>
internal static class DownloadedAttachment
{
    public static async Task<Result<InboxAttachment>> FindAsync(
        IInboxItemRepository items,
        Guid itemId,
        Guid attachmentId,
        CancellationToken cancellationToken)
    {
        var item = await items.GetAsync(itemId, cancellationToken).ConfigureAwait(false);
        if (item is null) return Result.Failure<InboxAttachment>(InboxErrors.ItemNotFound);

        var attachment = item.Attachments.FirstOrDefault(candidate => candidate.Id == attachmentId);
        if (attachment is null) return Result.Failure<InboxAttachment>(InboxErrors.AttachmentNotFound);
        if (!attachment.IsDownloaded) return Result.Failure<InboxAttachment>(InboxErrors.AttachmentNotDownloaded);

        return Result.Success(attachment);
    }
}
