namespace Backlog.Desktop.UI.Shell;

/// <summary>What closing the main window does (local ADR 0043 §5).</summary>
public enum MainWindowCloseAction
{
    /// <summary>Cancel the close and hide the window to the notification area: the
    /// app, its MCP server and every popped-out window keep running.</summary>
    Hide,

    /// <summary>Close every window and end the app.</summary>
    Quit
}

/// <summary>
/// Decides whether closing the main window hides it or quits the app (local ADR 0043
/// §5). Hide while the person keeps the app running in the tray, nothing asked to
/// quit, and the tray icon is there to bring the window back; Quit when the setting is
/// off, the tray's Quit, sign-out or shutdown asked, or Windows refused the icon — a
/// hidden window with no icon would leave no way to quit.
/// </summary>
public static class MainWindowClosePolicy
{
    public static MainWindowCloseAction For(bool keepRunningInTray, bool quitRequested, bool trayIconShown) =>
        keepRunningInTray && !quitRequested && trayIconShown ? MainWindowCloseAction.Hide : MainWindowCloseAction.Quit;
}
