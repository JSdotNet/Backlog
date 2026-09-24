using Backlog.UI.Components.Shell;

namespace Backlog.Mobile.UI.Services;

/// <summary>
/// Where the app bar's status line reads the device's sync state from. The line
/// asks this rather than the credential store and the sync client directly, so
/// the outbox can take over the numbers — what is waiting, whether the service
/// answered — without the shell changing.
/// </summary>
public interface ISyncStatusSource
{
    SyncStatusReading Current { get; }

    /// <summary>Raised whenever <see cref="Current"/> changes. It may be raised
    /// off the render thread, so a component marshals before it redraws.</summary>
    event Action? Changed;
}
