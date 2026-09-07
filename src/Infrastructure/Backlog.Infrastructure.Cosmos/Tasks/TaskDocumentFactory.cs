using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;

namespace Backlog.Infrastructure.Cosmos.Tasks;

/// <summary>
/// Between the wire contract and the stored document, in both directions.
/// <para>
/// Separate from <see cref="CosmosTaskReplica"/> so the mapping can be tested
/// without a store: the two things that are easy to get silently wrong here are
/// the partition key spelling and the TTL rule, and neither needs an emulator to
/// pin.
/// </para>
/// </summary>
internal static class TaskDocumentFactory
{
    /// <summary>
    /// The document to write for one change.
    /// <para>
    /// The TTL is the whole rule: a live task carries no <c>ttl</c> and lives
    /// forever, a tombstone carries the configured one. The container's
    /// <c>defaultTtl</c> is -1, which enables expiry without expiring anything by
    /// itself, so the absence of the property is what keeps a task.
    /// </para>
    /// <para>
    /// Two consequences worth knowing. Re-pushing a tombstone restarts its
    /// clock — correct, because a device still pushing that deletion is a device
    /// that has not caught up yet and still needs the tombstone to exist. And
    /// <strong>the emulator does not honour TTL at all</strong>: expiry is
    /// deployed-only behaviour, so no test in this repository covers it and none
    /// should pretend to.
    /// </para>
    /// </summary>
    public static TaskDocument From(OwnerScope scope, TaskChange change, CosmosOptions options) =>
        new()
        {
            Id = TaskDocumentSerialization.Key(change.Id),
            OwnerId = TaskDocumentSerialization.Key(scope.OwnerId.Value),
            DeviceId = TaskDocumentSerialization.Key(scope.DeviceId.Value),
            UpdatedAt = change.UpdatedAt,
            DeletedAt = change.DeletedAt,
            Ttl = change.DeletedAt is null ? null : options.TaskTombstoneTtlSeconds,
            Task = change.Task,
        };

    /// <summary>
    /// The record to hand back for one stored document.
    /// <para>
    /// A document whose id or device id is not a GUID, or whose payload is
    /// missing, is not a document this service wrote. It comes back as null and
    /// the caller drops it, rather than throwing: one unreadable document must
    /// not be able to stop an owner from ever syncing again.
    /// </para>
    /// </summary>
    public static TaskChangeRecord? ToRecord(TaskDocument document)
    {
        if (document.Task is null
            || !Guid.TryParse(document.Id, out var id)
            || !Guid.TryParse(document.DeviceId, out var deviceId))
        {
            return null;
        }

        return new TaskChangeRecord(
            new TaskChange(id, document.UpdatedAt, document.DeletedAt, document.Task),
            deviceId,
            document.Timestamp);
    }
}
