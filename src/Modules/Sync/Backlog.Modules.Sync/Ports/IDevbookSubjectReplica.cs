using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;

namespace Backlog.Modules.Sync.Ports;

/// <summary>
/// Where an owner's devbook subject states — a change proposal's, a decision
/// record's, a debt record's — are kept while they travel between that person's
/// devices. Declared here and implemented outside, as
/// <see cref="IAnnotationReplica"/> is, with the same two operations and nothing
/// beside them: the desktop's own subject state store is the system of record,
/// and this is a relay.
/// <para>
/// Every method takes the owner, and the owner comes from the caller's validated
/// token. Nothing underneath re-checks it (.devbook/arc42/adr/0005 §Identity).
/// </para>
/// </summary>
public interface IDevbookSubjectReplica
{
    /// <summary>Writes a batch of changes and answers how many were taken. A
    /// change that is not a later version than the document already held — by
    /// <see cref="DevbookSubjectChangePrecedence"/> — is dropped whole, neither
    /// written nor counted. The scope's device id is stamped on each one written
    /// so a client can recognise its own echo.</summary>
    Task<int> Upsert(OwnerScope scope, IReadOnlyList<DevbookSubjectChange> changes, CancellationToken cancellationToken = default);

    /// <summary>One page of this owner's change feed, from
    /// <paramref name="cursor"/> or from the beginning when it is null.</summary>
    Task<DevbookSubjectReplicaPage> ReadChanges(
        OwnerId owner,
        DevbookSubjectReplicaCursor? cursor,
        int maxItems,
        CancellationToken cancellationToken = default);
}
