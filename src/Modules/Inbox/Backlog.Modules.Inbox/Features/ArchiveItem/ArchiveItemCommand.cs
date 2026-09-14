using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.DomainModels;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Features.ArchiveItem;

/// <summary>Dismisses an item. The one triage outcome that creates nothing in
/// another context; when the item came from the replica, the aggregate flags
/// it for the outbox so the phone stops offering it.</summary>
public sealed record ArchiveItemCommand(Guid Id);

public sealed class ArchiveItemCommandHandler(IInboxItemRepository items, TimeProvider clock)
    : ICommandHandler<ArchiveItemCommand, Result>
{
    public async Task<Result> Handle(ArchiveItemCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var item = await items.GetAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (item is null) return Result.Failure(InboxErrors.ItemNotFound);

        try
        {
            item.Archive(clock.GetUtcNow());
        }
        catch (InvalidInboxTransitionException refused)
        {
            return Result.Failure(InboxErrors.InvalidTransition(refused.Message));
        }

        await items.SaveAsync(item, cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
