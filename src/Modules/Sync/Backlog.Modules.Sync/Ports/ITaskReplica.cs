using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;

namespace Backlog.Modules.Sync.Ports;

/// <summary>
/// Where an owner's task documents are kept while they travel between that
/// person's devices. Declared here and implemented outside, so the module says
/// what it needs of storage without naming Cosmos.
/// <para>
/// The surface is deliberately this small. It never filters, never searches,
/// never sorts by a key the caller supplied, and never projects a subset of a
/// document. .arc42/adr/0005 asks that no query serve a UI out of the replica:
/// the replica is a relay, the desktop's SQLite database is the system of
/// record, and the moment a screen could be answered from here the two would
/// start to disagree about which one is true. The one read that looks like a
/// query — <see cref="ListCaptures"/> — is the inbox, which has no local
/// counterpart to disagree with.
/// </para>
/// <para>
/// Every method takes the owner, and the owner comes from the caller's
/// validated token. Nothing underneath re-checks it: the service reaches its
/// store under one identity that can see every partition, so these parameters
/// are the boundary (.arc42/adr/0005 §Identity).
/// </para>
/// </summary>
public interface ITaskReplica
{
    /// <summary>
    /// Writes a batch of changes and answers how many were taken. Whole-document
    /// last-write-wins, so there is nothing to reject and nothing to merge — the
    /// scope's device id is stamped on each one so a client can recognise its own
    /// echo on the way back.
    /// </summary>
    Task<int> Upsert(OwnerScope scope, IReadOnlyList<TaskChange> changes, CancellationToken cancellationToken = default);

    /// <summary>
    /// One page of this owner's change feed, from <paramref name="cursor"/> or
    /// from the beginning when it is null. Beginning rather than now: a newly
    /// paired device has to receive the tasks the owner already has, and there is
    /// no separate bootstrap path in the model.
    /// </summary>
    Task<TaskReplicaPage> ReadChanges(
        OwnerId owner,
        TaskReplicaCursor? cursor,
        int maxItems,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The owner's live captures — documents that are not tombstoned and still
    /// carry a source inbox id — newest first by the task's own creation time.
    /// The inbox is the one view the service answers out of the replica, and it
    /// reads exactly two fields of the payload to do it.
    /// </summary>
    Task<IReadOnlyList<TaskChangeRecord>> ListCaptures(OwnerId owner, CancellationToken cancellationToken = default);

    /// <summary>
    /// One document by id, or null. Scoped to the owner, so an id belonging to
    /// somebody else is indistinguishable from an id that never existed — the
    /// lookup starts from the owner and never sees it.
    /// </summary>
    Task<TaskChangeRecord?> Find(OwnerId owner, Guid id, CancellationToken cancellationToken = default);
}
