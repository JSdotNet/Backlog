using Backlog.Modules.Inbox.DomainModels;

namespace Backlog.Modules.Inbox;

/// <summary>
/// Local-first persistence for <see cref="InboxItem"/> aggregates, whole.
/// <para>
/// A deleted item is removed, not tombstoned — nothing replicates an item, so
/// there is no other machine to tell about the row. What does leave this store
/// for the replica is an acknowledgement: <see cref="ListPendingReplicaAckAsync"/>
/// serves the items that owe one, and <see cref="ListDeletedCapturesAsync"/> the
/// deleted ones that still did when they went.
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

    /// <summary>Removes an item <see cref="InboxItem.Delete"/> has run on, with
    /// its file rows. When it still owes the replica an acknowledgement, an
    /// <see cref="InboxDeletedCapture"/> is kept in the same write; when it has
    /// no replica behind it, its id is kept for <see cref="WasDismissedAsync"/>.</summary>
    Task DeleteAsync(InboxItem item, CancellationToken cancellationToken = default);

    /// <summary>Whether an item with no replica behind it was deleted here
    /// under this id. A feed offers every entry on every run, so this is what
    /// keeps a deleted one from being captured again. Never read by the outbox:
    /// nothing is owed to anyone for it.</summary>
    Task<bool> WasDismissedAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The other half of the outbox read: deleted captures the replica
    /// has not yet heard about, oldest deletion first.</summary>
    Task<IReadOnlyList<InboxDeletedCapture>> ListDeletedCapturesAsync(CancellationToken cancellationToken = default);

    /// <summary>The deleted capture with that id, or null when there is none
    /// waiting — so a replay of it is not taken for a new capture.</summary>
    Task<InboxDeletedCapture?> GetDeletedCaptureAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Drops a deleted capture once the replica has it, or once the
    /// replica withdrew it itself. Nothing happens when there is none.</summary>
    Task ForgetDeletedCaptureAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Keeps (or re-stamps) what is left of a deleted item while the
    /// replica is owed its tombstone. The note intake calls it when the phone edits
    /// a note this desktop already deleted, so the tombstone goes out again under
    /// a stamp later than the edit.</summary>
    Task RememberDeletedCaptureAsync(InboxDeletedCapture capture, CancellationToken cancellationToken = default);
}
