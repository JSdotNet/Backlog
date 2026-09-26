using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Inbox.Features.ReadAttachment;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Features.OpenAttachment;

/// <summary>Opens one downloaded file on an item with the machine's own
/// application for it.</summary>
public sealed record OpenAttachmentCommand(Guid Id, Guid AttachmentId);

/// <summary>Hands the file to the operating system. Nothing about the item
/// changes; the command exists so the pane never learns where the file is.</summary>
public sealed class OpenAttachmentCommandHandler(IInboxItemRepository items, IInboxAttachmentFiles? attachmentFiles = null)
    : ICommandHandler<OpenAttachmentCommand, Result>
{
    public async Task<Result> Handle(OpenAttachmentCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var found = await DownloadedAttachment.FindAsync(items, command.Id, command.AttachmentId, cancellationToken).ConfigureAwait(false);
        if (found.IsFailure) return Result.Failure(found.Error);
        if (attachmentFiles is null) return Result.Failure(InboxErrors.AttachmentsUnavailable);

        return attachmentFiles.Open(found.Value.LocalPath!);
    }
}
