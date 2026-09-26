using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.DomainModels;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Features.DeferItem;

/// <summary>Puts an item aside until <paramref name="Until"/>, or with no date
/// until a person brings it back. Sent again on a deferred item, it changes the
/// review date.</summary>
public sealed record DeferItemCommand(Guid Id, DateOnly? Until);

public sealed class DeferItemCommandHandler(IInboxItemRepository items, TimeProvider clock)
    : ICommandHandler<DeferItemCommand, Result>
{
    public async Task<Result> Handle(DeferItemCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var item = await items.GetAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (item is null) return Result.Failure(InboxErrors.ItemNotFound);

        try
        {
            item.Defer(command.Until, clock.GetUtcNow());
        }
        catch (InvalidInboxTransitionException refused)
        {
            return Result.Failure(InboxErrors.InvalidTransition(refused.Message));
        }

        await items.SaveAsync(item, cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
