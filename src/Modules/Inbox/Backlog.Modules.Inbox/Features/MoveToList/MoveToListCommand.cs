using Backlog.Modules.Inbox.Abstractions;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Features.MoveToList;

/// <summary>Files an item in a list, or back in the unfiled inbox with null.</summary>
public sealed record MoveToListCommand(Guid Id, Guid? ListId);

public sealed class MoveToListCommandHandler(IInboxItemRepository items, IInboxOrganizerRepository organizer)
    : ICommandHandler<MoveToListCommand, Result>
{
    public async Task<Result> Handle(MoveToListCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var item = await items.GetAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (item is null) return Result.Failure(InboxErrors.ItemNotFound);

        // Checked here rather than trusted, because no foreign key does it: the
        // store keeps list_id as a bare column on purpose, so an id of a list
        // that has gone would otherwise file the item somewhere nothing draws.
        if (command.ListId is { } listId)
        {
            var lists = await organizer.ListListsAsync(cancellationToken).ConfigureAwait(false);
            if (lists.All(list => list.Id != listId)) return Result.Failure(InboxErrors.ListNotFound);
        }

        item.MoveToList(command.ListId);

        await items.SaveAsync(item, cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
