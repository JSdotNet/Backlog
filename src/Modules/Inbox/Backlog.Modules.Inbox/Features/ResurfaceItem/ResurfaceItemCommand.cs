using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.DomainModels;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Features.ResurfaceItem;

/// <summary>Returns a deferred item to the queue now, whatever its review date —
/// the only way back for a deferral that has none.</summary>
public sealed record ResurfaceItemCommand(Guid Id);

public sealed class ResurfaceItemCommandHandler(IInboxItemRepository items, TimeProvider clock)
    : ICommandHandler<ResurfaceItemCommand, Result>
{
    public async Task<Result> Handle(ResurfaceItemCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var item = await items.GetAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (item is null) return Result.Failure(InboxErrors.ItemNotFound);

        try
        {
            item.Resurface(clock.GetUtcNow());
        }
        catch (InvalidInboxTransitionException refused)
        {
            return Result.Failure(InboxErrors.InvalidTransition(refused.Message));
        }

        await items.SaveAsync(item, cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
