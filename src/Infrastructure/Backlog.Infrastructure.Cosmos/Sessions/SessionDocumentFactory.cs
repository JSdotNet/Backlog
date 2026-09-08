using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;

namespace Backlog.Infrastructure.Cosmos.Sessions;

/// <summary>
/// Between the wire contract and the stored document, in both directions.
/// <para>
/// Separate from <see cref="CosmosSessionReplica"/> so the mapping can be tested
/// without a store. The two things that are easy to get silently wrong here need
/// no emulator to pin: the property names the container's indexing policy
/// depends on, and the composition of the document id that makes one machine
/// unable to address another's record.
/// </para>
/// </summary>
internal static class SessionDocumentFactory
{
    /// <summary>
    /// The document to write for one record.
    /// <para>
    /// <strong>The machine id comes from the scope and never from the record</strong>,
    /// which is not a defensive choice so much as the only one available: a
    /// <see cref="SessionRecord"/> has no machine id on it. That is
    /// .arc42/adr/0005 §Session records' "a caller may only write records stamped
    /// with its own machine id" held by construction, and the id then leads the
    /// document key, so a machine can only ever address documents beginning with
    /// its own device id.
    /// </para>
    /// <para>
    /// <strong>No <c>ttl</c> is written, and none should be.</strong> Unlike the
    /// tasks container — where a live task must carry no expiry and a tombstone
    /// must carry one, so the number has to reach this code — the sessions
    /// container expires whole records at twelve months through
    /// <c>defaultTtl</c> in <c>infra/sync/main.bicep</c>. A per-document
    /// <c>ttl</c> written here would override that container setting for every
    /// record this service wrote, quietly, and the emulator does not honour TTL
    /// at all so nothing local would ever show it.
    /// </para>
    /// </summary>
    public static SessionDocument From(OwnerScope scope, SessionRecord record) =>
        new()
        {
            Id = SessionRecordKey.For(scope.DeviceId.Value, record),
            OwnerId = ReplicaDocumentSerialization.Key(scope.OwnerId.Value),
            MachineId = ReplicaDocumentSerialization.Key(scope.DeviceId.Value),
            SessionId = record.SessionId,
            AgentKind = record.AgentKind,
            MachineName = record.MachineName,
            RepositoryAlias = record.RepositoryAlias,
            Branch = record.Branch,
            StartedAt = record.StartedAt,
            LastActivityAt = record.LastActivityAt,
            TurnCount = record.TurnCount,
            DurationSeconds = record.DurationSeconds,
        };

    /// <summary>
    /// The entry to hand back for one stored document.
    /// <para>
    /// A document whose machine id is not a GUID, or which is missing the two
    /// halves of a session's identity, is not a document this service wrote. It
    /// comes back as null and the caller drops it, rather than throwing: one
    /// unreadable document must not be able to stop an owner from ever syncing
    /// session records again.
    /// </para>
    /// </summary>
    public static SessionRecordEntry? ToEntry(SessionDocument document)
    {
        if (!Guid.TryParse(document.MachineId, out var machineId)
            || string.IsNullOrEmpty(document.SessionId)
            || string.IsNullOrEmpty(document.AgentKind))
        {
            return null;
        }

        return new SessionRecordEntry(
            new SessionRecord(
                document.SessionId,
                document.AgentKind,
                document.MachineName,
                document.RepositoryAlias,
                document.Branch,
                document.StartedAt,
                document.LastActivityAt,
                document.TurnCount,
                document.DurationSeconds),
            machineId,
            document.Timestamp);
    }
}
