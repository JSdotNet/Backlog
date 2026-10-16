using Backlog.Aspire.ServiceDefaults;
using Backlog.Infrastructure.FileSystem;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop;

public partial class App : Application
{
    private const string WindowTitle = "Backlog.Desktop";

    private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(2);

    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new MainPage()) { Title = ResolveTitle() };

        // The shell writes what was on screen off the click path, so the last
        // choice made before the window closed may still be on its way to disk.
        // The window waits for it rather than leaving it to the process exit, but
        // only briefly: a write stalled on a synced folder must not hold the
        // closing window.
        var services = activationState?.Context.Services;
        window.Destroying += (_, _) =>
        {
            if (services?.GetService<ShellNavigationStore>() is { } store)
                Task.Run(store.Flush).Wait(FlushTimeout);
        };

        return window;
    }

    // Debug builds are the ones started out of a worktree, often several at
    // once, so they say which worktree they came from. An installed app has no
    // checkout above it and nothing to disambiguate, and keeps the plain name.
    private static string ResolveTitle() =>
#if DEBUG
        DevelopmentWorkspace.DecorateTitle(WindowTitle);
#else
        WindowTitle;
#endif
}
