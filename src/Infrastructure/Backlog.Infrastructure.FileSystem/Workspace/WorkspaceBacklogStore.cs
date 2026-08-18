using Backlog.Modules.Backlog.Abstractions.Services;
using Backlog.Modules.Knowledge.Abstractions;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// Backlog Management's view of the workspace: where its entries live, and where
/// a scoped repository keeps the ones it authored.
/// <para>
/// Two collaborators rather than one, because the port asks two different
/// questions. The root comes from the workspace settings file; the
/// <c>.backlog</c> folder comes from the same resolver Second Brain's panels
/// use, because <c>.backlog</c> is one of the configured knowledge folders and
/// resolving it twice, separately, is how two answers drift apart. Composing
/// them here is the whole point of the port: Backlog Management asks its own
/// store and never learns that Second Brain's settings were consulted.
/// </para>
/// </summary>
public sealed class WorkspaceBacklogStore(WorkspaceSettingsStore settings, IKnowledgeFolderSource folders) : IBacklogStore
{
    private const string BacklogFolderKey = ".backlog";

    public event Action? RootChanged
    {
        add => settings.RootChanged += value;
        remove => settings.RootChanged -= value;
    }

    public string DefaultRootDirectory => settings.DefaultRootDirectory;

    public string RootDirectory => settings.RootDirectory;

    public bool IsDefaultRoot => settings.IsDefaultRoot;

    public string EntriesDirectory => settings.EntriesDirectory;

    public string InboxDirectory => settings.InboxDirectory;

    public string? TryUseRoot(string? path) => settings.TryUseRoot(path);

    public string? ResetToDefault() => settings.ResetToDefault();

    public BacklogFolderLocation ResolveBacklogFolder(string? repositoryAlias)
    {
        var location = folders.Resolve(BacklogFolderKey, repositoryAlias);
        if (!location.Available || location.FullPath is null) return BacklogFolderLocation.None;

        return new BacklogFolderLocation(
            true,
            location.FullPath,
            // Only a repository scope has a clone directory to make paths
            // relative to; a storage-scoped entry is named relative to the
            // folder it was found in, which is what a reader of the list expects.
            location.RepositoryFullName is null ? null : location.RootPath,
            location.RepositoryFullName,
            location.RepositoryAlias);
    }
}
