using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// Resolves configured knowledge folders to local directories. Repository-scoped
/// knowledge uses the GitHub repository settings; the global view uses the
/// workspace settings.
/// <para>
/// This is the join, and it lives here on purpose. Answering "where does this
/// folder live?" needs both the repository settings and the workspace root, and
/// neither context may see the other; an adapter may see both, which is what an
/// adapter is for. Two module ports are served from this one engine so the
/// resolution rules exist once: Devbook takes it as
/// <see cref="IDevbookFolderSource"/>, Tasks through
/// <see cref="WorkspaceTaskStore"/> — which asks only for the storage root,
/// because <c>.backlog</c> is not a knowledge folder. Tasks keeps
/// its entries in the workspace, not in a configured section of somebody's
/// repository.
/// </para>
/// </summary>
public sealed class DevbookFolderSource : IDevbookFolderSource
{
    private readonly GitHubSettingsStore _settings;
    private readonly WorkspaceSettingsStore _store;
    private readonly bool _useRepositoryFallbackWhenNoAlias;

    /// <summary>
    /// The branch snapshots, or null where nothing composed one.
    /// <para>
    /// Optional rather than required because a snapshot needs a network client
    /// and several compositions — tests, and anything that only ever reads a
    /// local folder — have no business constructing one. Where it is absent, a
    /// repository with no clone answers exactly what it answered before branch
    /// loading existed.
    /// </para>
    /// </summary>
    private readonly IDevbookSnapshotCache? _snapshots;

    public DevbookFolderSource(GitHubSettingsStore settings, WorkspaceSettingsStore store)
        : this(settings, store, useRepositoryFallbackWhenNoAlias: false)
    {
    }

    public DevbookFolderSource(GitHubSettingsStore settings, WorkspaceSettingsStore store, IDevbookSnapshotCache? snapshots)
        : this(settings, store, useRepositoryFallbackWhenNoAlias: false, snapshots)
    {
    }

    /// <summary>The devbook-only composition: nothing has told this source
    /// where the workspace is, so an unscoped question falls back to the first
    /// configured repository rather than to a storage folder nobody chose.</summary>
    public DevbookFolderSource(GitHubSettingsStore settings)
        : this(
            settings,
            new WorkspaceSettingsStore(Path.Combine(Path.GetTempPath(), "backlog-devbook-source")),
            useRepositoryFallbackWhenNoAlias: true)
    {
    }

    public DevbookFolderSource(
        GitHubSettingsStore settings,
        WorkspaceSettingsStore store,
        bool useRepositoryFallbackWhenNoAlias,
        IDevbookSnapshotCache? snapshots = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(store);

        _settings = settings;
        _store = store;
        _useRepositoryFallbackWhenNoAlias = useRepositoryFallbackWhenNoAlias;
        _snapshots = snapshots;
    }

    /// <summary>
    /// Three sources of the same news, so the accessor forwards to two stores and
    /// keeps a delegate of its own. The own delegate is what
    /// <see cref="NotifyContentChanged"/> raises: content being replaced under a
    /// folder is nothing either settings store can know about, because nothing was
    /// configured differently.
    /// </summary>
    private Action? _contentChanged;

    public event Action? Changed
    {
        add
        {
            _settings.Changed += value;
            _store.RootChanged += value;
            _contentChanged += value;
        }
        remove
        {
            _settings.Changed -= value;
            _store.RootChanged -= value;
            _contentChanged -= value;
        }
    }

    public void NotifyContentChanged() => _contentChanged?.Invoke();

    public string StorageDirectory => _store.RootDirectory;

    public IReadOnlyList<DevbookFolderSetting> Folders(string? repositoryAlias)
    {
        if (string.IsNullOrWhiteSpace(repositoryAlias)) return _store.DevbookFolders;

        return _settings.Current.Find(repositoryAlias) is { } repository
            ? repository.DevbookFolders
            : _store.DevbookFolders;
    }

    public DevbookFolderLocation Resolve(string key, string? repositoryAlias = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (!string.IsNullOrWhiteSpace(repositoryAlias)) return ResolveRepository(key, repositoryAlias);

        if (!_useRepositoryFallbackWhenNoAlias) return ResolveStorage(key);

        return _settings.Current.Repositories.Count > 0
            ? ResolveRepository(key, _settings.Current.Repositories[0].Alias)
            : DevbookFolderLocation.Unavailable(key, "Configure a repository before opening repository knowledge.");
    }

    private DevbookFolderLocation ResolveStorage(string key)
    {
        var folder = _store.DevbookFolders.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));
        if (folder is null)
        {
            return DevbookFolderLocation.Unavailable(
                key,
                $"Storage has no {key} devbook-folder setting.");
        }

        if (!folder.Enabled)
        {
            return DevbookFolderLocation.Unavailable(
                key,
                $"{folder.DisplayName} knowledge is turned off for storage.",
                folder: folder,
                rootPath: _store.RootDirectory);
        }

        return ResolvePath(key, folder, _store.RootDirectory, null, "storage", _store.RootDirectory);
    }

    private DevbookFolderLocation ResolveRepository(string key, string repositoryAlias)
    {
        var repository = _settings.Current.Find(repositoryAlias);
        if (repository is null)
        {
            return DevbookFolderLocation.Unavailable(
                key,
                "Select a configured repository before opening repository knowledge.");
        }

        var folder = repository.DevbookFolders.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));
        if (folder is null)
        {
            return DevbookFolderLocation.Unavailable(
                key,
                $"{repository.FullName} has no {key} devbook-folder setting.",
                repository.FullName,
                repositoryAlias: repository.Alias);
        }

        if (!folder.Enabled)
        {
            return DevbookFolderLocation.Unavailable(
                key,
                $"{folder.DisplayName} knowledge is turned off for {repository.FullName}.",
                repository.FullName,
                folder,
                rootPath: repository.CloneDirectory,
                repositoryAlias: repository.Alias);
        }

        if (repository.DevbookSource is DevbookSourceKind.Branch) return ResolveBranch(key, folder, repository);

        return ResolvePath(key, folder, repository.CloneDirectory!, repository, repository.FullName, repository.CloneDirectory);
    }

    /// <summary>
    /// Resolves against the cached copy of the repository's branch.
    /// <para>
    /// Never fetches. This runs during every panel load, and a resolution that
    /// reached the network would put GitHub in front of opening a tab — so a
    /// branch nobody has fetched resolves to "not fetched yet" and the pane's
    /// existing update control is what goes and gets it. That is the rule ADR
    /// 0004 states for the knowledge index and it holds here for the same
    /// reason: refresh is an optimisation, never a precondition.
    /// </para>
    /// </summary>
    private DevbookFolderLocation ResolveBranch(string key, DevbookFolderSetting folder, GitHubRepositoryRef repository)
    {
        var branch = repository.DevbookBranch;

        if (_snapshots is null)
        {
            return DevbookFolderLocation.Unavailable(
                key,
                $"Add a local clone directory for {repository.FullName} in Settings to read {folder.DisplayName} knowledge.",
                repository.FullName,
                folder,
                repositoryAlias: repository.Alias);
        }

        var root = _snapshots.SnapshotPath(repository, branch);
        var label = string.IsNullOrWhiteSpace(branch) ? "its default branch" : branch.Trim();

        if (_snapshots.TryRead(repository, branch) is null)
        {
            return DevbookFolderLocation.Unavailable(
                key,
                $"{repository.FullName} has not been fetched from {label} yet.",
                repository.FullName,
                folder,
                rootPath: root,
                repositoryAlias: repository.Alias,
                source: DevbookSourceKind.Branch);
        }

        // The scope label names the branch rather than just the repository,
        // because "which of these am I reading?" is a real question the moment
        // the same repository can be read two ways.
        return ResolvePath(
            key,
            folder,
            root,
            repository,
            $"{repository.FullName} ({label})",
            root,
            DevbookSourceKind.Branch);
    }

    private static DevbookFolderLocation ResolvePath(
        string key,
        DevbookFolderSetting folder,
        string rootDirectory,
        GitHubRepositoryRef? repository,
        string scopeLabel,
        string? rootPath,
        DevbookSourceKind source = DevbookSourceKind.LocalFolder)
    {
        var path = folder.EffectivePath;
        var fullPath = Path.IsPathRooted(path)
            ? path
            : Path.Combine(rootDirectory, path);

        try
        {
            fullPath = Path.GetFullPath(fullPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return DevbookFolderLocation.Unavailable(
                key,
                $"{folder.DisplayName} knowledge path is not valid: {ex.Message}",
                repository?.FullName,
                folder,
                fullPath,
                rootPath,
                repositoryAlias: repository?.Alias,
                source: source);
        }

        if (!Directory.Exists(fullPath))
        {
            // Worth different words when the tree came from a branch: the folder
            // is not missing from somebody's disk, it is missing from the commit,
            // and telling them to check their clone would send them looking in a
            // folder that has nothing to do with it.
            return DevbookFolderLocation.Unavailable(
                key,
                source is DevbookSourceKind.Branch
                    ? $"{scopeLabel} has no {folder.DisplayName} knowledge folder at {folder.EffectivePath}."
                    : $"{folder.DisplayName} knowledge folder was not found at {fullPath}.",
                repository?.FullName,
                folder,
                fullPath,
                rootPath,
                repositoryAlias: repository?.Alias,
                source: source);
        }

        return new DevbookFolderLocation(
            key,
            true,
            null,
            repository?.FullName,
            folder,
            fullPath,
            rootPath,
            scopeLabel,
            repository?.Alias,
            source);
    }
}
