namespace Backlog.UI.Components.AppUpdate;

/// <summary>
/// Asks the host's update window to open from a screen that does not own it.
/// <para>
/// The desktop's update window belongs to the footer, and a page that says
/// "update to keep syncing" — the Devices settings page — has no way into it
/// otherwise. The footer listens; a page raises. One per window, so a page in
/// one window never opens the update window of another. A host without the
/// window registers none, and a page that resolves none offers no button.
/// </para>
/// </summary>
public sealed class AppUpdateWindowRequests
{
    /// <summary>Raised on the caller's thread when a screen asks.</summary>
    public event Action? OpenRequested;

    /// <summary>Asks the update window to open.</summary>
    public void RequestOpen() => OpenRequested?.Invoke();
}
