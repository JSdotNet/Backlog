using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.DomainModels;
using Backlog.Modules.Inbox.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Features.CreateGroup;

/// <summary>A new, empty group, named uniquely among groups without regard to
/// case and placed after them.</summary>
public sealed record CreateGroupCommand(string Name);

public sealed class CreateGroupCommandHandler(IInboxOrganizerRepository organizer, TimeProvider clock)
    : ICommandHandler<CreateGroupCommand, Result<InboxGroupDto>>
{
    public async Task<Result<InboxGroupDto>> Handle(CreateGroupCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var name = (command.Name ?? string.Empty).Trim();
        if (name.Length == 0) return InboxErrors.GroupNeedsName;

        var groups = await organizer.ListGroupsAsync(cancellationToken).ConfigureAwait(false);
        if (groups.Any(group => string.Equals(group.Name, name, StringComparison.OrdinalIgnoreCase)))
            return InboxErrors.GroupDuplicateName;

        var created = InboxGroup.Create(
            name,
            groups.Count == 0 ? 0 : groups.Max(group => group.Order) + 1,
            clock.GetUtcNow());

        await organizer.SaveGroupAsync(created, cancellationToken).ConfigureAwait(false);

        return created.ToDto();
    }
}
