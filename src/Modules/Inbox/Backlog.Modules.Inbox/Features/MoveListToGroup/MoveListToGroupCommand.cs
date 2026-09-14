using Backlog.Modules.Inbox.Abstractions;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Features.MoveListToGroup;

/// <summary>Moves a list into a group, or out to the top level with null. It
/// takes the last position among its new siblings, and may not take a name one
/// of them already has.</summary>
public sealed record MoveListToGroupCommand(Guid ListId, Guid? GroupId);

public sealed class MoveListToGroupCommandHandler(IInboxOrganizerRepository organizer)
    : ICommandHandler<MoveListToGroupCommand, Result>
{
    public async Task<Result> Handle(MoveListToGroupCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var lists = await organizer.ListListsAsync(cancellationToken).ConfigureAwait(false);
        var list = lists.FirstOrDefault(candidate => candidate.Id == command.ListId);
        if (list is null) return Result.Failure(InboxErrors.ListNotFound);

        if (command.GroupId is { } groupId)
        {
            var groups = await organizer.ListGroupsAsync(cancellationToken).ConfigureAwait(false);
            if (groups.All(group => group.Id != groupId)) return Result.Failure(InboxErrors.GroupNotFound);
        }

        if (list.GroupId == command.GroupId) return Result.Success();

        var siblings = lists.Where(sibling => sibling.GroupId == command.GroupId).ToList();
        if (siblings.Any(sibling => string.Equals(sibling.Name, list.Name, StringComparison.OrdinalIgnoreCase)))
            return Result.Failure(InboxErrors.ListDuplicateName);

        list.MoveToGroup(command.GroupId);
        list.SetOrder(siblings.Count == 0 ? 0 : siblings.Max(sibling => sibling.Order) + 1);

        await organizer.SaveListAsync(list, cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
