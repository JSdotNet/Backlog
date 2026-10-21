using Backlog.Infrastructure.Sync;
using Backlog.Modules.Sync.Abstractions;

namespace Backlog.Mobile.UI.Services;

/// <summary>
/// What a failed pull means, read once for the four pages that pull — Today,
/// Inbox, Notes and Stand-up — so they cannot tell a person four different
/// things about the same failure.
/// </summary>
public static class SyncPullFailure
{
    /// <summary>The opening sentence of the page's notice.</summary>
    public static string Reason(Exception failure) => failure switch
    {
        // Nothing was sent: the service is newer than this app.
        SyncUnavailableException { Code: SyncErrorCodes.ClientOutdated } => SyncCompatibility.UpdateRequiredMessage,
        SyncUnavailableException { Code: SyncErrorCodes.ReplicaUnavailable } => "Cloud sync is still starting up.",
        _ => "Cloud sync can't be reached."
    };

    /// <summary>
    /// Whether the status line should say the phone is offline. Not for a
    /// service newer than the app: nothing tried to reach it, and the shell's
    /// sync-paused callout already says why sync stopped — "offline" beside it
    /// would be a second, wrong reason for the same state.
    /// </summary>
    public static bool IsUnreachable(Exception failure) =>
        failure is not SyncUnavailableException { Code: SyncErrorCodes.ClientOutdated };
}
