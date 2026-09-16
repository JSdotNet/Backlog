using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;

namespace Backlog.Modules.Sync.Ports;

/// <summary>
/// Where an owner's Devbook annotations are kept while they travel between that
/// person's devices. Declared here and implemented outside, so the module says
/// what it needs of storage without naming Cosmos.
/// <para>
/// Two operations, the same two <see cref="ITaskReplica"/> relays with and
/// nothing beside them: no <c>Find</c>, no listing, no query a screen could be
/// answered from. The desktop's own annotation store is the system of record
/// and this is a relay between one person's devices (.arc42/adr/0005 §Storage).
/// Unlike session records, an annotation is edited and deleted, so the model is
/// the task one's — whole-document last-write-wins with tombstones — rather than
/// the append-only session one.
/// </para>
/// <para>
/// Every method takes the owner, and the owner comes from the caller's
/// validated token. Nothing underneath re-checks it (.arc42/adr/0005 §Identity).
/// </para>
/// </summary>
public interface IAnnotationReplica
{
    /// <summary>Writes a batch of changes and answers how many were taken.
    /// Whole-document last-write-wins, so there is nothing to reject and nothing
    /// to merge; the scope's device id is stamped on each so a client can
    /// recognise its own echo on the way back.</summary>
    Task<int> Upsert(OwnerScope scope, IReadOnlyList<AnnotationChange> changes, CancellationToken cancellationToken = default);

    /// <summary>One page of this owner's change feed, from <paramref name="cursor"/>
    /// or from the beginning when it is null — which is what a freshly paired
    /// device asks for, there being no separate bootstrap path.</summary>
    Task<AnnotationReplicaPage> ReadChanges(
        OwnerId owner,
        AnnotationReplicaCursor? cursor,
        int maxItems,
        CancellationToken cancellationToken = default);
}
