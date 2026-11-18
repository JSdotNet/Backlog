using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;

namespace Backlog.Modules.Sync.Ports;

/// <summary>
/// Where an owner's knowledge events and event series are kept while they travel between
/// that person's devices (local ADR 0050 §2). Declared here and implemented outside, as
/// <see cref="IDevbookSubjectReplica"/> is, with the same two operations and nothing beside
/// them: each desktop's own event store is its system of record, and this is a relay.
/// <para>
/// Every method takes the owner, and the owner comes from the caller's validated token.
/// Nothing underneath re-checks it (.devbook/arc42/adr/0005 §Identity).
/// </para>
/// </summary>
public interface IKnowledgeEventReplica
{
    /// <summary>Writes a batch of changes and answers how many were taken. A change that is
    /// not a later version than the document already held — by
    /// <see cref="KnowledgeEventChangePrecedence"/> — is dropped whole, neither written nor
    /// counted. The scope's device id is stamped on each one written so a client can
    /// recognise its own echo.</summary>
    Task<int> Upsert(OwnerScope scope, IReadOnlyList<KnowledgeEventChange> changes, CancellationToken cancellationToken = default);

    /// <summary>One page of this owner's change feed, from <paramref name="cursor"/> or from
    /// the beginning when it is null.</summary>
    Task<KnowledgeEventReplicaPage> ReadChanges(
        OwnerId owner,
        KnowledgeEventReplicaCursor? cursor,
        int maxItems,
        CancellationToken cancellationToken = default);
}
