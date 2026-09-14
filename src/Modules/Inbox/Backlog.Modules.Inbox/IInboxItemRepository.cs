using Backlog.Modules.Inbox.DomainModels;

namespace Backlog.Modules.Inbox;

/// <summary>
/// Local-first persistence for <see cref="InboxItem"/> aggregates, whole.
/// <para>
/// No delete member, and no tombstone either — the two reasons differ from
/// Tasks'. Nothing in this scope deletes an item: archived is its terminal
/// state, and an archived row is what the archive view shows. And nothing
/// replicates an item, so there is no other machine to tell about a deletion;
/// the only thing that leaves this store for the replica is an
/// acknowledgement, which <see cref="ListPendingReplicaAckAsync"/> serves.
/// </para>
/// </summary>
public interface IInboxItemRepository
{
    /// <summary>Creates or updates an item.</summary>
    Task SaveAsync(InboxItem item, CancellationToken cancellationToken = default);

    /// <summary>The full aggregate, or null when there is no item with that id.</summary>
    Task<InboxItem?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Every item in every status, newest capture first. Archived
    /// included on purpose: which slice to show is the screen's decision, and a
    /// read that hid the archive would need a second read to show it.</summary>
    Task<IReadOnlyList<InboxItem>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>The outbox read: items whose replica has not yet heard what
    /// this desktop decided about them.</summary>
    Task<IReadOnlyList<InboxItem>> ListPendingReplicaAckAsync(CancellationToken cancellationToken = default);
}
