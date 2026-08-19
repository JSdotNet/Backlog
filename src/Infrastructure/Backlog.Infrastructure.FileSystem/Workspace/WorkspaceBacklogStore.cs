using Backlog.Modules.Backlog.Abstractions.Services;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// Backlog Management's view of the workspace: where its tasks live.
/// <para>
/// A thin pass-through over the workspace settings, and deliberately still its
/// own type. The module declares the port; which adapter answers it, and over
/// what, is the host's business — and the settings store answers two other
/// audiences (the GitHub repository behind the folder, and the knowledge folder
/// list) that the module may not see.
/// </para>
/// </summary>
public sealed class WorkspaceBacklogStore(WorkspaceSettingsStore settings) : IBacklogStore
{
    public event Action? RootChanged
    {
        add => settings.RootChanged += value;
        remove => settings.RootChanged -= value;
    }

    public string DefaultRootDirectory => settings.DefaultRootDirectory;

    public string RootDirectory => settings.RootDirectory;

    public bool IsDefaultRoot => settings.IsDefaultRoot;

    public string DatabasePath => settings.DatabasePath;

    public string InboxDirectory => settings.InboxDirectory;

    public string? TryUseRoot(string? path) => settings.TryUseRoot(path);

    public string? ResetToDefault() => settings.ResetToDefault();
}
