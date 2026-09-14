using Backlog.Modules.Inbox.DomainModels;
using Backlog.SharedKernel.Handlers;

namespace Backlog.Modules.Inbox.Features.EnsureDefaultOrganizer;

/// <summary>Seeds the starter organiser into a workspace that has none.</summary>
public sealed record EnsureDefaultOrganizerCommand;

/// <summary>
/// Only when <em>both</em> tables are empty. A person who has made even one
/// list or group has an organiser of their own, and seeding beside it would
/// hand them seven things they did not ask for. The known edge is accepted and
/// named: someone who deletes all seven starters gets them back on the next
/// start, because an empty organiser is indistinguishable from a fresh one and
/// a once-flag would be a second thing to store for a case nobody has hit.
/// </summary>
public sealed class EnsureDefaultOrganizerCommandHandler(IInboxOrganizerRepository organizer, TimeProvider clock)
    : ICommandHandler<EnsureDefaultOrganizerCommand>
{
    /// <summary>The starter groups, in the order they appear.</summary>
    internal static readonly string[] DefaultGroups = ["Areas", "Projects", "Archive"];

    /// <summary>The starter top-level lists, in the order they appear.</summary>
    internal static readonly string[] DefaultLists = ["Resources", "Someday/Maybe", "Updates", "Wishlist"];

    public async Task Handle(EnsureDefaultOrganizerCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var lists = await organizer.ListListsAsync(cancellationToken).ConfigureAwait(false);
        if (lists.Count > 0) return;

        var groups = await organizer.ListGroupsAsync(cancellationToken).ConfigureAwait(false);
        if (groups.Count > 0) return;

        var now = clock.GetUtcNow();

        for (var order = 0; order < DefaultGroups.Length; order++)
        {
            await organizer.SaveGroupAsync(InboxGroup.Create(DefaultGroups[order], order, now), cancellationToken)
                .ConfigureAwait(false);
        }

        for (var order = 0; order < DefaultLists.Length; order++)
        {
            await organizer.SaveListAsync(InboxList.Create(DefaultLists[order], groupId: null, order, now), cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
