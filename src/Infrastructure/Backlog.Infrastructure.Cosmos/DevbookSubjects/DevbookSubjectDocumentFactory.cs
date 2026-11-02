using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;

namespace Backlog.Infrastructure.Cosmos.DevbookSubjects;

/// <summary>
/// Between the wire contract and the stored document, in both directions.
/// Separate from <see cref="CosmosDevbookSubjectReplica"/> so the partition key
/// spelling can be pinned without an emulator.
/// </summary>
internal static class DevbookSubjectDocumentFactory
{
    /// <summary>The document to write for one change. No TTL: the container
    /// keeps a subject's state for as long as the owner has one.</summary>
    public static DevbookSubjectDocument From(OwnerScope scope, DevbookSubjectChange change) =>
        new()
        {
            Id = ReplicaDocumentSerialization.Key(change.Id),
            OwnerId = ReplicaDocumentSerialization.Key(scope.OwnerId.Value),
            DeviceId = ReplicaDocumentSerialization.Key(scope.DeviceId.Value),
            UpdatedAt = change.UpdatedAt,
            Subject = change.Subject,
        };

    /// <summary>
    /// The record to hand back for one stored document, or null for a document
    /// this service did not write — a missing payload, or an id or device id
    /// that is not a GUID. Null rather than a throw, so one unreadable document
    /// cannot stop an owner from ever syncing again.
    /// </summary>
    public static DevbookSubjectChangeRecord? ToRecord(DevbookSubjectDocument document)
    {
        if (document.Subject is null
            || !Guid.TryParse(document.Id, out var id)
            || !Guid.TryParse(document.DeviceId, out var deviceId))
        {
            return null;
        }

        return new DevbookSubjectChangeRecord(
            new DevbookSubjectChange(id, document.UpdatedAt, document.Subject),
            deviceId,
            document.Timestamp);
    }
}
