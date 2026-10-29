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
    /// popped-out window beside it (local ADR 0041 §4), so the store flush on close
    /// and the rule that closing the main window quits the app are one class's.
    /// </summary>
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var services = activationState?.Context.Services
            ?? throw new InvalidOperationException("The app has no services to open its main window with.");

        var window = services.GetRequiredService<ShellWindows>().CreateMainWindow();

        // The backlog:// link the app was launched with opens once the main window is
        // up, beside it (local ADR 0041 §7): queued, so the main window finishes
        // opening before the next one does.
        var links = services.GetRequiredService<BacklogLinkActivation>();
        window.Created += (_, _) => MainThread.BeginInvokeOnMainThread(links.Started);

        return window;
    }
}
