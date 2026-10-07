namespace Backlog.UI.Components.Capture;

/// <summary>
/// Where one of the phone's own captures stands on its way to the desktop.
/// </summary>
public enum CaptureSyncState
{
    /// <summary>Kept on the phone; the outbox is still sending it.</summary>
    Waiting,

    /// <summary>Kept on the phone; the outbox has given up retrying on its own
    /// and waits for a tap.</summary>
    Stuck,

    /// <summary>Cloud sync has it, and it waits there for the desktop to take
    /// it in.</summary>
    Sent
}
