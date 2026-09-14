using Backlog.Modules.Inbox.Abstractions;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Features.RenameList;

/// <summary>Renames a list. The name is trimmed, must not be empty, and must
/// not collide with a sibling's — renaming a list to its own name, in any
/// case, is allowed and is how a person fixes capitalisation.</summary>
public sealed record RenameListCommand(Guid ListId, string Name);

public sealed class RenameListCommandHandler(IInboxOrganizerRepository organizer)
    : ICommandHandler<RenameListCommand, Result>
{
    public async Task<Result> Handle(RenameListCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var name = (command.Name ?? string.Empty).Trim();
        if (name.Length == 0) return Result.Failure(InboxErrors.ListNeedsName);

        var lists = await organizer.ListListsAsync(cancellationToken).ConfigureAwait(false);
        var list = lists.FirstOrDefault(candidate => candidate.Id == command.ListId);
        if (list is null) return Result.Failure(InboxErrors.ListNotFound);

        var taken = lists.Any(sibling =>
            sibling.Id != list.Id
            && sibling.GroupId == list.GroupId
            && string.Equals(sibling.Name, name, StringComparison.OrdinalIgnoreCase));
        if (taken) return Result.Failure(InboxErrors.ListDuplicateName);

        list.Rename(name);

        await organizer.SaveListAsync(list, cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
