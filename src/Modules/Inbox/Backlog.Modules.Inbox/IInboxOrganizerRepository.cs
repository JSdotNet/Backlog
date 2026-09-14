using Backlog.Modules.Inbox.DomainModels;

namespace Backlog.Modules.Inbox;

/// <summary>
/// Persistence for the organiser — lists and groups — behind one port because
/// they are edited together: ungrouping writes lists and deletes a group in one
/// use case, and two ports would be two adapters to keep in the same file.
/// Deletes here really delete; see <see cref="InboxList"/> for why there is no
/// tombstone.
/// </summary>
public interface IInboxOrganizerRepository
{
    /// <summary>Every list, in <see cref="InboxList.Order"/> then name order.</summary>
    Task<IReadOnlyList<InboxList>> ListListsAsync(CancellationToken cancellationToken = default);

    /// <summary>Every group, in <see cref="InboxGroup.Order"/> then name order.</summary>
    Task<IReadOnlyList<InboxGroup>> ListGroupsAsync(CancellationToken cancellationToken = default);

    Task SaveListAsync(InboxList list, CancellationToken cancellationToken = default);

    Task DeleteListAsync(Guid id, CancellationToken cancellationToken = default);

    Task SaveGroupAsync(InboxGroup group, CancellationToken cancellationToken = default);

    Task DeleteGroupAsync(Guid id, CancellationToken cancellationToken = default);
}
