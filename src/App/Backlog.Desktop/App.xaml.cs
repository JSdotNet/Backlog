using Backlog.Aspire.ServiceDefaults;

namespace Backlog.Desktop;

public partial class App : Application
{
    private const string WindowTitle = "Backlog.Desktop";

    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new MainPage()) { Title = ResolveTitle() };
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
