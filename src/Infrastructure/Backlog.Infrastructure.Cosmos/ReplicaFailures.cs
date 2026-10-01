using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Ports;

using Microsoft.Azure.Cosmos;

namespace Backlog.Infrastructure.Cosmos;

/// <summary>
/// The two failures the task, session and annotation replicas raise the same
/// way. Each replica still says which store is missing; the codes, which the
/// sync service turns into 503 and 400, are the part that has to be identical.
/// </summary>
internal static class ReplicaFailures
{
    /// <summary>A store that is not there yet, in the replica's own words.</summary>
    internal static SyncReplicaException Unavailable(string message, CosmosException failure) => new(
        SyncErrorCodes.ReplicaUnavailable,
        message,
        failure);

    /// <summary>A continuation Cosmos will not resume from. It is the client's to
    /// recover from — drop the cursor and pull from the beginning — so it is a
    /// 400 with its own code rather than an error the service can retry.</summary>
    internal static SyncReplicaException Expired(CosmosException? failure) => new(
        SyncErrorCodes.SyncCursorExpired,
        "That cursor is too old to resume from. Pull again without one.",
        failure);
}
