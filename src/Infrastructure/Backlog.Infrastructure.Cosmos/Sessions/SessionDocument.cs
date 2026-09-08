using System.Text.Json.Serialization;

namespace Backlog.Infrastructure.Cosmos.Sessions;

/// <summary>
/// One session record as it sits in the <c>sessions</c> container: the ten
/// whitelisted fields, the owner, and the two Cosmos maintains.
/// <para>
/// <strong>The whitelist is flat, and it is flat because the index is.</strong>
/// The <c>sessions</c> container in <c>infra/sync/main.bicep</c> includes exactly
/// <c>/ownerId/?</c>, <c>/machineId/?</c>, <c>/repositoryAlias/?</c>,
/// <c>/startedAt/?</c> and <c>/lastActivityAt/?</c> and excludes <c>/*</c> —
/// every one of those a root path. Nesting the record under a <c>record</c>
/// property the way <c>TaskDocument</c> nests its payload would leave three of
/// the five indexing nothing, silently: Cosmos does not object to an included
/// path no document has, and the only symptom would be a query nobody has
/// written yet running slower than it should. The camelCase policy in
/// <see cref="ReplicaDocumentSerialization"/> is what turns these property names
/// into those paths, and <c>SessionDocumentTests</c> is what pins that it still
/// does.
/// </para>
/// <para>
/// Flat is also honest here in a way it would not be for a task. A task payload
/// is an open shape the service stores whole and never reads; a session record
/// is a closed list of ten fields that .arc42/adr/0005 §Session records
/// enumerates, so writing them out is writing down the whitelist rather than
/// duplicating a contract that will grow behind this file's back. A field
/// appearing here that is not in that table is a defect, not a feature.
/// </para>
/// <para>
/// <strong>There is no <c>ttl</c> property, and there is not going to be one.</strong>
/// The <c>tasks</c> container has <c>defaultTtl: -1</c> — expiry enabled,
/// nothing expiring unless its own document asks — because only tombstones
/// expire there. The <c>sessions</c> container has
/// <c>defaultTtl: sessionRetentionSeconds</c>, twelve months, because the whole
/// record expires. So here retention genuinely is the container setting
/// §Retention is Cosmos TTL, not code argues for, and a per-document <c>ttl</c>
/// written by this service would silently override it for every record it wrote.
/// </para>
/// <para>
/// Nothing here is ever updated in place and nothing is ever tombstoned. A
/// session that moves gets a later record, written by the machine that ran it,
/// under the same id.
/// </para>
/// </summary>
internal sealed class SessionDocument
{
    /// <summary>
    /// <c>{machineId}:{agentKind}:{sessionId}</c>, built by
    /// <c>SessionRecordKey</c>. Cosmos requires the property to be called exactly
    /// <c>id</c>.
    /// <para>
    /// The composition is argued where the key is built, and both halves of the
    /// argument matter: the agent because
    /// .domain/sessions/naming.md#session-identity puts a session's identity at
    /// the agent plus the id that agent issued, and the machine id first because
    /// that makes the single-writer rule structural rather than checked.
    /// </para>
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Whose session log this belongs to, and the partition key. <c>"D"</c>
    /// format: a partition key is a string here rather than a GUID, because Cosmos
    /// partition keys are strings and a client that formatted the same GUID
    /// differently would write into a second partition for one person.</summary>
    public string OwnerId { get; set; } = string.Empty;

    /// <summary>Which of the owner's machines ran the session — stamped from the
    /// caller's validated token, never from the request. Indexed, because it is
    /// what a reading device groups by.</summary>
    public string MachineId { get; set; } = string.Empty;

    /// <summary>The identifier the agent gave the session. Unique only within its
    /// own agent, which is why <see cref="AgentKind"/> is part of the id
    /// above.</summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>Which assistant ran it, as the opaque token the device wrote.
    /// Never parsed here: .arc42/adr/0005 §Storage says no domain logic runs
    /// against the replica, so a third assistant needs no redeployment.</summary>
    public string AgentKind { get; set; } = string.Empty;

    /// <summary>What the operating system calls the machine. A display label, and
    /// deliberately not indexed — a section is keyed on
    /// <see cref="MachineId"/>, because a name can be shared by two machines and
    /// changed on one.</summary>
    public string MachineName { get; set; } = string.Empty;

    /// <summary>The repository's alias, never its path. Absent when the session
    /// ran outside a repository.</summary>
    public string? RepositoryAlias { get; set; }

    /// <summary>The branch the session worked on. Absent on a detached head, or
    /// when there was no repository to have one.</summary>
    public string? Branch { get; set; }

    /// <summary>When the session began, if the agent recorded it.</summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>When it was last seen alive. Indexed, because recency is the one
    /// ordering a session log cares about.</summary>
    public DateTimeOffset LastActivityAt { get; set; }

    /// <summary>How many turns it has taken, or absent where the agent recorded
    /// nothing to count from. Absent rather than <c>0</c>, and the nulls-dropped
    /// policy in <see cref="ReplicaDocumentSerialization"/> is what makes it
    /// absent: <c>0</c> is a count, not a gap, and a session log may not fill a
    /// gap its agent left.</summary>
    public int? TurnCount { get; set; }

    /// <summary>How long it has been running, in seconds.</summary>
    public long DurationSeconds { get; set; }

    /// <summary>Cosmos's own write stamp, in unix seconds. Read-only and set by
    /// the store, which is what makes it usable as an ordering two machines with
    /// skewed clocks still agree on.</summary>
    [JsonPropertyName("_ts")]
    public long Timestamp { get; set; }

    /// <summary>Cosmos's version tag. Not used for concurrency — a session record
    /// has exactly one writer, so there is no race for an etag to lose. Mapped so
    /// it round-trips rather than being dropped and rewritten as null, and
    /// excluded from the index by the bicep for the same reason it is on the
    /// tasks container.</summary>
    [JsonPropertyName("_etag")]
    public string? ETag { get; set; }
}
