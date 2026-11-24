using Backlog.Modules.Sync.Features.Enrolment;

namespace Backlog.Modules.Sync.Api.Security;

/// <summary>
/// Says at startup that a deployed service has no pinned owner (local ADR 0058 §1).
/// Registered outside Development only. Existing devices keep syncing either way; what
/// the warning is for is the enrolment and recovery the service will refuse while
/// another owner's devices are on record, so the operator learns it before the owner does.
/// It reads no store, so a Cosmos that is still starting does not hold the start.
/// </summary>
internal sealed class UnpinnedOwnerWarning(OwnerEnrolment owner, ILogger<UnpinnedOwnerWarning> log) : IHostedService
{
    // LoggerMessage.Define rather than [LoggerMessage], for the reason GitHubOwnerSignIn
    // gives: this project sees two copies of the logging source generator.
    private static readonly Action<ILogger, Guid?, Exception?> Unpinned = LoggerMessage.Define<Guid?>(
        LogLevel.Warning,
        new EventId(1, nameof(Unpinned)),
        "Owner__OwnerId is unset, so signed-in enrolment and a connector sign-in both bind to the derived owner {DerivedOwnerId}. While devices of any other owner are on record, enrolment and recovery are refused; pin the existing owner with Owner__OwnerId (azd: SYNC_OWNER_ID).");

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (owner.MustCheckForOtherOwners)
        {
            Unpinned(log, owner.Owner?.Value, null);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
