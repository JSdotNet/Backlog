using Backlog.Desktop.Services;
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
    /// popped-out window beside it (local ADR 0040 §4), so the store flush on close
    /// and the rule that closing the main window quits the app are one class's.
    /// </summary>
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var services = activationState?.Context.Services
            ?? throw new InvalidOperationException("The app has no services to open its main window with.");

        return services.GetRequiredService<ShellWindows>().CreateMainWindow();
    }
}
