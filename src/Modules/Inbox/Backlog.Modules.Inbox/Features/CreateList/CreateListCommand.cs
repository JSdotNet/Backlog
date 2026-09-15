using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.DomainModels;
using Backlog.Modules.Inbox.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Features.CreateList;

/// <summary>A new list, at the top level or inside a group. Named uniquely
/// among its siblings without regard to case, and placed after them.</summary>
public sealed record CreateListCommand(string Name, Guid? GroupId = null);

public sealed class CreateListCommandHandler(IInboxOrganizerRepository organizer, TimeProvider clock)
    : ICommandHandler<CreateListCommand, Result<InboxListDto>>
{
    public async Task<Result<InboxListDto>> Handle(CreateListCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var name = (command.Name ?? string.Empty).Trim();
        if (name.Length == 0) return InboxErrors.ListNeedsName;

        if (command.GroupId is { } groupId)
        {
            var groups = await organizer.ListGroupsAsync(cancellationToken).ConfigureAwait(false);
            if (groups.All(group => group.Id != groupId)) return InboxErrors.GroupNotFound;
        }

        var lists = await organizer.ListListsAsync(cancellationToken).ConfigureAwait(false);
        var siblings = lists.Where(list => list.GroupId == command.GroupId).ToList();

        if (siblings.Any(list => string.Equals(list.Name, name, StringComparison.OrdinalIgnoreCase)))
            return InboxErrors.ListDuplicateName;

        var created = InboxList.Create(
            name,
            command.GroupId,
            siblings.Count == 0 ? 0 : siblings.Max(list => list.Order) + 1,
            clock.GetUtcNow());

        await organizer.SaveListAsync(created, cancellationToken).ConfigureAwait(false);

        return created.ToDto();
    }
}
