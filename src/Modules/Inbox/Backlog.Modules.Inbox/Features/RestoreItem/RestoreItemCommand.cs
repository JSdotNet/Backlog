using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.DomainModels;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Features.RestoreItem;

/// <summary>Takes an archive back: the item is unprocessed again and no longer
/// anything's duplicate. The way back from Archive, "Archive as duplicate of…"
/// and "Merge into a task" — the session undo history's, and only that: an
/// archived item has no Restore of its own on screen.</summary>
public sealed record RestoreItemCommand(Guid Id);

public sealed class RestoreItemCommandHandler(IInboxItemRepository items, TimeProvider clock)
    : ICommandHandler<RestoreItemCommand, Result>
{
    public async Task<Result> Handle(RestoreItemCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var item = await items.GetAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (item is null) return Result.Failure(InboxErrors.ItemNotFound);

        try
        {
            item.Restore(clock.GetUtcNow());
        }
        catch (InvalidInboxTransitionException refused)
        {
            return Result.Failure(InboxErrors.InvalidTransition(refused.Message));
        }

        await items.SaveAsync(item, cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
