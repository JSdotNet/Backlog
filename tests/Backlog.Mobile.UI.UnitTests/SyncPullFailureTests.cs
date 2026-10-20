using System.Net;

using Backlog.Infrastructure.Sync;
using Backlog.Modules.Sync.Abstractions;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// What the pulling pages say about a failed pull, and whether the status line
/// calls the phone offline for it. A service newer than the app is not an
/// offline phone: nothing tried to reach it.
/// </summary>
public sealed class SyncPullFailureTests
{
    private static readonly SyncUnavailableException Outdated =
        new((HttpStatusCode)426, SyncErrorCodes.ClientOutdated, SyncCompatibility.UpdateRequiredMessage);

    [Fact]
    public void A_service_newer_than_the_app_asks_for_an_update_and_is_not_offline()
    {
        Assert.Equal(SyncCompatibility.UpdateRequiredMessage, SyncPullFailure.Reason(Outdated));
        Assert.False(SyncPullFailure.IsUnreachable(Outdated));
    }

    [Fact]
    public void A_replica_still_starting_says_so_and_is_offline()
    {
        var starting = new SyncUnavailableException(HttpStatusCode.ServiceUnavailable, SyncErrorCodes.ReplicaUnavailable, null);

        Assert.Equal("Cloud sync is still starting up.", SyncPullFailure.Reason(starting));
        Assert.True(SyncPullFailure.IsUnreachable(starting));
    }

    [Fact]
    public void Anything_else_is_unreachable()
    {
        var failure = new HttpRequestException("No route to host.");

        Assert.Equal("Cloud sync can't be reached.", SyncPullFailure.Reason(failure));
        Assert.True(SyncPullFailure.IsUnreachable(failure));
    }
}
