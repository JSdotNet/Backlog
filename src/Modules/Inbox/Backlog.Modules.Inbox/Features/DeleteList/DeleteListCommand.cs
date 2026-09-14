using Backlog.Modules.Inbox.Abstractions;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Features.DeleteList;

/// <summary>Deletes a list; the items in it return to the unfiled inbox.
/// The app confirms before it gets here.</summary>
public sealed record DeleteListCommand(Guid ListId);

/// <summary>
/// The items are unfiled by this handler, one save each, rather than by a
/// cascade in the store: the column carries no foreign key on purpose (SQLite
/// leaves enforcement off by default, and a cascade that only sometimes runs is
/// worse than none), and "back to the inbox" is a decision about the items that
/// belongs with a use case rather than a schema.
/// </summary>
public sealed class DeleteListCommandHandler(IInboxOrganizerRepository organizer, IInboxItemRepository items)
    : ICommandHandler<DeleteListCommand, Result>
{
    public async Task<Result> Handle(DeleteListCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var lists = await organizer.ListListsAsync(cancellationToken).ConfigureAwait(false);
        if (lists.All(list => list.Id != command.ListId)) return Result.Failure(InboxErrors.ListNotFound);

        foreach (var item in (await items.ListAsync(cancellationToken).ConfigureAwait(false))
                     .Where(item => item.ListId == command.ListId))
        {
            item.MoveToList(null);
            await items.SaveAsync(item, cancellationToken).ConfigureAwait(false);
        }

        // After the items, so a run that dies half way leaves a list that still
        // exists with fewer items in it, rather than items filed under a list
        // that is gone.
        await organizer.DeleteListAsync(command.ListId, cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
