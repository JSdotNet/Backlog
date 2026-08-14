using Backlog.Infrastructure.GitHub;

namespace Backlog.Desktop.UI.Services;

/// <summary>
/// Resolves configured knowledge folders to local directories. Repository-scoped
/// knowledge uses repository settings; the global view uses storage settings.
/// </summary>
public sealed class KnowledgeFolderSource(GitHubSettingsStore settings, BacklogStore store, bool useRepositoryFallbackWhenNoAlias = false)
{
    public KnowledgeFolderSource(GitHubSettingsStore settings)
        : this(settings, new BacklogStore(Path.Combine(Path.GetTempPath(), "backlog-knowledge-source")), useRepositoryFallbackWhenNoAlias: true)
    {
    }
    public event Action? Changed
    {
        add
        {
            settings.Changed += value;
            store.RootChanged += value;
        }
        remove
        {
            settings.Changed -= value;
            store.RootChanged -= value;
        }
    }

    public KnowledgeFolderLocation Resolve(string key, string? repositoryAlias = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (!string.IsNullOrWhiteSpace(repositoryAlias)) return ResolveRepository(key, repositoryAlias);

        if (!useRepositoryFallbackWhenNoAlias) return ResolveStorage(key);

        return settings.Current.Repositories.Count > 0
            ? ResolveRepository(key, settings.Current.Repositories[0].Alias)
            : KnowledgeFolderLocation.Unavailable(key, "Configure a repository before opening repository knowledge.");
    }

    private KnowledgeFolderLocation ResolveStorage(string key)
    {
        var folder = store.KnowledgeFolders.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));
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
                rootPath: store.RootDirectory);
        }

        return ResolvePath(key, folder, store.RootDirectory, null, "storage", store.RootDirectory);
    }

    private KnowledgeFolderLocation ResolveRepository(string key, string repositoryAlias)
    {
        var repository = settings.Current.Find(repositoryAlias);
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
                repository);
        }

        if (!folder.Enabled)
        {
            return KnowledgeFolderLocation.Unavailable(
                key,
                $"{folder.DisplayName} knowledge is turned off for {repository.FullName}.",
                repository,
                folder,
                rootPath: repository.CloneDirectory);
        }

        if (string.IsNullOrWhiteSpace(repository.CloneDirectory))
        {
            return KnowledgeFolderLocation.Unavailable(
                key,
                $"Add a local clone directory for {repository.FullName} in Settings to read {folder.DisplayName} knowledge.",
                repository,
                folder,
                rootPath: repository.CloneDirectory);
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
                repository,
                folder,
                fullPath,
                rootPath);
        }

        if (!Directory.Exists(fullPath))
        {
            return KnowledgeFolderLocation.Unavailable(
                key,
                $"{folder.DisplayName} knowledge folder was not found at {fullPath}.",
                repository,
                folder,
                fullPath,
                rootPath);
        }

        return new KnowledgeFolderLocation(
            key,
            true,
            null,
            repository,
            folder,
            fullPath,
            rootPath,
            scopeLabel);
    }
}

public sealed record KnowledgeFolderLocation(
    string Key,
    bool Available,
    string? Message,
    GitHubRepositoryRef? Repository,
    KnowledgeFolderSetting? Folder,
    string? FullPath,
    string? RootPath = null,
    string? ScopeLabel = null)
{
    public static KnowledgeFolderLocation Unavailable(
        string key,
        string message,
        GitHubRepositoryRef? repository = null,
        KnowledgeFolderSetting? folder = null,
        string? fullPath = null,
        string? rootPath = null,
        string? scopeLabel = null) => new(key, false, message, repository, folder, fullPath, rootPath, scopeLabel);
}