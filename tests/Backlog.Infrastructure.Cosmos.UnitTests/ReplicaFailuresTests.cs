using System.Net;

using Backlog.Modules.Sync.Abstractions;

using Microsoft.Azure.Cosmos;

namespace Backlog.Infrastructure.Cosmos.UnitTests;

/// <summary>
/// The two failures every replica in this project raises the same way: a store
/// that is not there yet, and a continuation Cosmos will not resume from. The
/// code is what the sync service turns into 503 or 400, so it is pinned here
/// once for the task, session and annotation replicas alike.
/// </summary>
public sealed class ReplicaFailuresTests
{
    private static CosmosException Failure(HttpStatusCode status) =>
        new("boom", status, subStatusCode: 0, activityId: "activity", requestCharge: 0);

    [Fact]
    public void Unavailable_carries_the_replica_s_own_message_and_the_cosmos_failure()
    {
        var failure = Failure(HttpStatusCode.ServiceUnavailable);

        var exception = ReplicaFailures.Unavailable("The task replica is not available yet. Try again shortly.", failure);

        Assert.Equal(SyncErrorCodes.ReplicaUnavailable, exception.Code);
        Assert.Equal("The task replica is not available yet. Try again shortly.", exception.Message);
        Assert.Same(failure, exception.InnerException);
    }

    [Fact]
    public void Expired_says_to_pull_again_without_a_cursor()
    {
        var failure = Failure(HttpStatusCode.BadRequest);

        var exception = ReplicaFailures.Expired(failure);

        Assert.Equal(SyncErrorCodes.SyncCursorExpired, exception.Code);
        Assert.Equal("That cursor is too old to resume from. Pull again without one.", exception.Message);
        Assert.Same(failure, exception.InnerException);
    }

    [Fact]
    public void Expired_needs_no_cosmos_failure_to_be_raised()
    {
        var exception = ReplicaFailures.Expired(null);

        Assert.Equal(SyncErrorCodes.SyncCursorExpired, exception.Code);
        Assert.Null(exception.InnerException);
    }
}
