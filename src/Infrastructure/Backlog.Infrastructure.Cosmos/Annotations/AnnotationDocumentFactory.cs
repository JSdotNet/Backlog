using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;

namespace Backlog.Infrastructure.Cosmos.Annotations;

/// <summary>
/// Between the wire contract and the stored document, in both directions.
/// Separate from <see cref="CosmosAnnotationReplica"/> so the two things that
/// are easy to get silently wrong — the partition key spelling and the TTL rule
/// — can be pinned without an emulator.
/// </summary>
internal static class AnnotationDocumentFactory
{
    /// <summary>
    /// The document to write for one change. The TTL is the whole rule: a live
    /// annotation carries no <c>ttl</c>, a tombstone carries the configured
    /// one, and the container's <c>defaultTtl</c> of -1 enables expiry without
    /// expiring anything by itself. Re-pushing a tombstone restarts its clock,
    /// which is correct for the reason <c>TaskDocumentFactory</c> gives — and,
    /// as there, <strong>the emulator does not honour TTL</strong>, so expiry is
    /// deployed-only behaviour no test here covers.
    /// </summary>
    public static AnnotationDocument From(OwnerScope scope, AnnotationChange change, CosmosOptions options) =>
        new()
        {
            Id = ReplicaDocumentSerialization.Key(change.Id),
            OwnerId = ReplicaDocumentSerialization.Key(scope.OwnerId.Value),
            DeviceId = ReplicaDocumentSerialization.Key(scope.DeviceId.Value),
            UpdatedAt = change.UpdatedAt,
            DeletedAt = change.DeletedAt,
            Ttl = change.DeletedAt is null ? null : options.AnnotationTombstoneTtlSeconds,
            Annotation = change.Annotation,
        };

    /// <summary>
    /// The record to hand back for one stored document, or null for a document
    /// this service did not write — a missing payload, or an id or device id
    /// that is not a GUID. Null rather than a throw, so one unreadable document
    /// cannot stop an owner from ever syncing again.
    /// </summary>
    public static AnnotationChangeRecord? ToRecord(AnnotationDocument document)
    {
        if (document.Annotation is null
            || !Guid.TryParse(document.Id, out var id)
            || !Guid.TryParse(document.DeviceId, out var deviceId))
        {
            return null;
        }

        return new AnnotationChangeRecord(
            new AnnotationChange(id, document.UpdatedAt, document.DeletedAt, document.Annotation),
            deviceId,
            document.Timestamp);
    }
}
