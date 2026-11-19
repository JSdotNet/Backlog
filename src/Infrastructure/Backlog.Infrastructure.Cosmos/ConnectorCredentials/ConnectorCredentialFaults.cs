using System.Net;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Ports;
using Microsoft.Azure.Cosmos;

namespace Backlog.Infrastructure.Cosmos.ConnectorCredentials;

/// <summary>
/// What a Cosmos failure in either connector credential store becomes on the way out: a
/// coded problem rather than an unclassified 500 (inherited ADR 0017). Both stores share
/// one container, so they say the same thing about it.
/// </summary>
internal static class ConnectorCredentialFaults
{
    /// <summary>What a caller is told when the container cannot be reached.</summary>
    public const string UnavailableMessage = "The connector store is not available yet. Try again shortly.";

    /// <summary>A 429 is the store busy; anything else — a timeout included — is the store
    /// unavailable.</summary>
    public static SyncReplicaException From(Exception failure) =>
        failure is CosmosException { StatusCode: HttpStatusCode.TooManyRequests }
            ? new SyncReplicaException(SyncErrorCodes.ReplicaBusy, "The connector store is busy. Try again shortly.", failure)
            : new SyncReplicaException(SyncErrorCodes.ReplicaUnavailable, UnavailableMessage, failure);

    /// <summary>Two writes of the client raced and this one lost. Retryable: the caller reads
    /// the client again and computes the next generation from what is stored now.</summary>
    public static SyncReplicaException Conflict(CosmosException? failure) => new(
        SyncErrorCodes.ReplicaBusy,
        "The connector secret was changed at the same moment. Try again.",
        failure);
}
