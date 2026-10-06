using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Features.DeleteItem;

/// <summary>Deletes an item for good — the triage outcome that, unlike
/// archiving, keeps nothing. The app confirms before it gets here.</summary>
public sealed record DeleteItemCommand(Guid Id);

/// <summary>
/// When the item came from the replica and the phone may still be offering it,
/// the store keeps its acknowledgement for the outbox, so the phone drops the
/// capture exactly as it does for an archived one. Any other item leaves its
/// id behind, so a feed or an import offering it again does not bring it back.
/// <para>
/// The item's file folder goes with it, unless the item was routed: routing
/// handed that folder to the task as its attachment, and the task still points
/// there. The folder is removed after the row, so a run that dies half way
/// leaves stray files rather than an item whose files are gone.
/// </para>
/// </summary>
public sealed class DeleteItemCommandHandler(
    IInboxItemRepository items,
    TimeProvider clock,
    IInboxAttachmentFiles? attachmentFiles = null)
    : ICommandHandler<DeleteItemCommand, Result>
{
    public async Task<Result> Handle(DeleteItemCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var item = await items.GetAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (item is null) return Result.Failure(InboxErrors.ItemNotFound);

        item.Delete(clock.GetUtcNow());

        await items.DeleteAsync(item, cancellationToken).ConfigureAwait(false);

        if (attachmentFiles is not null && !item.IsRouted && item.Attachments.Count > 0)
        {
            await attachmentFiles.RemoveFolderAsync(item.Id, cancellationToken).ConfigureAwait(false);
        }

        return Result.Success();
    }
}
