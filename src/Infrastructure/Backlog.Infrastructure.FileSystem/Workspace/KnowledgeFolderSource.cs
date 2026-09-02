using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Knowledge.Abstractions;

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
/// resolution rules exist once: Second Brain takes it as
/// <see cref="IKnowledgeFolderSource"/>, Tasks through
/// <see cref="WorkspaceTaskStore"/> — which asks only for the storage root,
/// because <c>.backlog</c> is not a knowledge folder. Tasks keeps
/// its entries in the workspace, not in a configured section of somebody's
/// repository.
/// </para>
/// </summary>
public sealed class KnowledgeFolderSource : IKnowledgeFolderSource
{
    private readonly GitHubSettingsStore _settings;
    private readonly WorkspaceSettingsStore _store;
    private readonly bool _useRepositoryFallbackWhenNoAlias;

    public KnowledgeFolderSource(GitHubSettingsStore settings, WorkspaceSettingsStore store)
        : this(settings, store, useRepositoryFallbackWhenNoAlias: false)
    {
    }

    /// <summary>The knowledge-only composition: nothing has told this source
    /// where the workspace is, so an unscoped question falls back to the first
    /// configured repository rather than to a storage folder nobody chose.</summary>
    public KnowledgeFolderSource(GitHubSettingsStore settings)
        : this(
            settings,
            new WorkspaceSettingsStore(Path.Combine(Path.GetTempPath(), "backlog-knowledge-source")),
            useRepositoryFallbackWhenNoAlias: true)
    {
    }

    public KnowledgeFolderSource(GitHubSettingsStore settings, WorkspaceSettingsStore store, bool useRepositoryFallbackWhenNoAlias)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(store);

        _settings = settings;
        _store = store;
        _useRepositoryFallbackWhenNoAlias = useRepositoryFallbackWhenNoAlias;
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

    public IReadOnlyList<KnowledgeFolderSetting> Folders(string? repositoryAlias)
    {
        if (string.IsNullOrWhiteSpace(repositoryAlias)) return _store.KnowledgeFolders;

        return _settings.Current.Find(repositoryAlias) is { } repository
            ? repository.KnowledgeFolders
            : _store.KnowledgeFolders;
    }

    public KnowledgeFolderLocation Resolve(string key, string? repositoryAlias = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (!string.IsNullOrWhiteSpace(repositoryAlias)) return ResolveRepository(key, repositoryAlias);

        if (!_useRepositoryFallbackWhenNoAlias) return ResolveStorage(key);

        return _settings.Current.Repositories.Count > 0
            ? ResolveRepository(key, _settings.Current.Repositories[0].Alias)
            : KnowledgeFolderLocation.Unavailable(key, "Configure a repository before opening repository knowledge.");
    }

    private KnowledgeFolderLocation ResolveStorage(string key)
    {
        var folder = _store.KnowledgeFolders.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));
        if (folder is null)
        {
            return KnowledgeFolderLocation.Unavailable(
                key,
                $"Storage has no {key} knowledge-folder setting.");
        }

        if (!folder.Enabled)
        {
            return KnowledgeFolderLocation.Unavailable(
                key,
                $"{folder.DisplayName} knowledge is turned off for storage.",
                folder: folder,
                rootPath: _store.RootDirectory);
        }

        return ResolvePath(key, folder, _store.RootDirectory, null, "storage", _store.RootDirectory);
    }

    private KnowledgeFolderLocation ResolveRepository(string key, string repositoryAlias)
    {
        var repository = _settings.Current.Find(repositoryAlias);
        if (repository is null)
        {
            return KnowledgeFolderLocation.Unavailable(
                key,
                "Select a configured repository before opening repository knowledge.");
        }

        var folder = repository.KnowledgeFolders.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));
        if (folder is null)
        {
            return KnowledgeFolderLocation.Unavailable(
                key,
                $"{repository.FullName} has no {key} knowledge-folder setting.",
                repository.FullName,
                repositoryAlias: repository.Alias);
        }

        if (!folder.Enabled)
        {
            return KnowledgeFolderLocation.Unavailable(
                key,
                $"{folder.DisplayName} knowledge is turned off for {repository.FullName}.",
                repository.FullName,
                folder,
                rootPath: repository.CloneDirectory,
                repositoryAlias: repository.Alias);
        }

        if (string.IsNullOrWhiteSpace(repository.CloneDirectory))
        {
            return KnowledgeFolderLocation.Unavailable(
                key,
                $"Add a local clone directory for {repository.FullName} in Settings to read {folder.DisplayName} knowledge.",
                repository.FullName,
                folder,
                rootPath: repository.CloneDirectory,
                repositoryAlias: repository.Alias);
        }

        return ResolvePath(key, folder, repository.CloneDirectory, repository, repository.FullName, repository.CloneDirectory);
    }

    private static KnowledgeFolderLocation ResolvePath(
        string key,
        KnowledgeFolderSetting folder,
        string rootDirectory,
        GitHubRepositoryRef? repository,
        string scopeLabel,
        string? rootPath)
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
            return KnowledgeFolderLocation.Unavailable(
                key,
                $"{folder.DisplayName} knowledge path is not valid: {ex.Message}",
                repository?.FullName,
                folder,
                fullPath,
                rootPath,
                repositoryAlias: repository?.Alias);
        }

        if (!Directory.Exists(fullPath))
        {
            return KnowledgeFolderLocation.Unavailable(
                key,
                $"{folder.DisplayName} knowledge folder was not found at {fullPath}.",
                repository?.FullName,
                folder,
                fullPath,
                rootPath,
                repositoryAlias: repository?.Alias);
        }

        return new KnowledgeFolderLocation(
            key,
            true,
            null,
            repository?.FullName,
            folder,
            fullPath,
            rootPath,
            scopeLabel,
            repository?.Alias);
    }
}
