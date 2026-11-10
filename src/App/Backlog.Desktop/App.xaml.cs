using Backlog.Desktop.Services;
using Backlog.Desktop.UI.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    /// <summary>
    /// The main window. <see cref="ShellWindows"/> makes it, as it makes every
    /// popped-out window beside it (local ADR 0043 §4), so the store flush on close
    /// and what closing the main window does — hide it to the tray, or quit — are one
    /// class's. A start by the sign-in startup task creates it hidden (local ADR 0043 §5).
    /// </summary>
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var services = activationState?.Context.Services
            ?? throw new InvalidOperationException("The app has no services to open its main window with.");

        var windows = services.GetRequiredService<ShellWindows>();
        var hidden = BacklogLinkInstance.StartedAtSignIn;
        var window = windows.CreateMainWindow(hidden);

        // The windows the person remembered reopen once the main window is up, beside it
        // (local ADR 0043 §5): queued like the launch link, so the main window finishes
        // opening first, and a failure there never holds the main window up. A start to
        // the tray opens them the first time the main window is shown instead.
        window.Created += (_, _) => MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                windows.ReopenRememberedWhenShown(hidden);
            }
            catch (Exception)
            {
                // Nothing to undo: the main window is open, and the next start tries
                // the remembered windows again.
            }
        });

        // The backlog:// link the app was launched with opens once the main window is
        // up, beside it (local ADR 0043 §7): queued, so the main window finishes
        // opening before the next one does.
        var links = services.GetRequiredService<BacklogLinkActivation>();
        window.Created += (_, _) => MainThread.BeginInvokeOnMainThread(links.Started);

        return window;
    }
}
