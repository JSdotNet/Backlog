using Backlog.Modules.Inbox.Abstractions;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Features.RenameGroup;

/// <summary>Renames a group, under the same rules as renaming a list.</summary>
public sealed record RenameGroupCommand(Guid GroupId, string Name);

public sealed class RenameGroupCommandHandler(IInboxOrganizerRepository organizer)
    : ICommandHandler<RenameGroupCommand, Result>
{
    public async Task<Result> Handle(RenameGroupCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var name = (command.Name ?? string.Empty).Trim();
        if (name.Length == 0) return Result.Failure(InboxErrors.GroupNeedsName);

        var groups = await organizer.ListGroupsAsync(cancellationToken).ConfigureAwait(false);
        var group = groups.FirstOrDefault(candidate => candidate.Id == command.GroupId);
        if (group is null) return Result.Failure(InboxErrors.GroupNotFound);

        var taken = groups.Any(other =>
            other.Id != group.Id && string.Equals(other.Name, name, StringComparison.OrdinalIgnoreCase));
        if (taken) return Result.Failure(InboxErrors.GroupDuplicateName);

        group.Rename(name);

        await organizer.SaveGroupAsync(group, cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
