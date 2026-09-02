using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// Answers <see cref="ITaskStore"/> over the workspace settings: where the tasks
/// are kept, not what is in them.
/// <para>
/// A thin pass-through over the workspace settings, and deliberately still its
/// own type. The module declares the port; which adapter answers it, and over
/// what, is the host's business — and the settings store answers two other
/// audiences (the GitHub repository behind the folder, and the knowledge folder
/// list) that the module may not see.
/// </para>
/// </summary>
public sealed class WorkspaceTaskStore(WorkspaceSettingsStore settings) : ITaskStore
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
