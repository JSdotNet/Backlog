using Backlog.Modules.Inbox.Abstractions;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Features.UngroupLists;

/// <summary>Dissolves a group: its lists move to the top level, after the
/// lists already there, and the group is deleted. Nothing is lost — a group
/// holds nothing but lists.</summary>
public sealed record UngroupListsCommand(Guid GroupId);

public sealed class UngroupListsCommandHandler(IInboxOrganizerRepository organizer)
    : ICommandHandler<UngroupListsCommand, Result>
{
    public async Task<Result> Handle(UngroupListsCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var groups = await organizer.ListGroupsAsync(cancellationToken).ConfigureAwait(false);
        if (groups.All(group => group.Id != command.GroupId)) return Result.Failure(InboxErrors.GroupNotFound);

        var lists = await organizer.ListListsAsync(cancellationToken).ConfigureAwait(false);
        var topLevel = lists.Where(list => list.GroupId is null).ToList();
        var nextOrder = topLevel.Count == 0 ? 0 : topLevel.Max(list => list.Order) + 1;

        // Moved before the group goes, for the reason DeleteList unfiles its
        // items first: a run that dies half way leaves a smaller group, never a
        // list pointing at a group that no longer exists.
        foreach (var list in lists.Where(list => list.GroupId == command.GroupId).OrderBy(list => list.Order))
        {
            list.MoveToGroup(null);
            list.SetOrder(nextOrder++);
            await organizer.SaveListAsync(list, cancellationToken).ConfigureAwait(false);
        }

        await organizer.DeleteGroupAsync(command.GroupId, cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
