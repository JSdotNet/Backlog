using Backlog.Infrastructure.GitHub;

namespace Backlog.Desktop.UI.Services;

/// <summary>
/// Resolves configured repository knowledge folders to local directories. The
/// settings model is repository-wide, so every knowledge view can share this
/// instead of each page inventing its own clone/path rules.
/// </summary>
public sealed class KnowledgeFolderSource(GitHubSettingsStore settings)
{
    public event Action? Changed
    {
        add => settings.Changed += value;
        remove => settings.Changed -= value;
    }

    public KnowledgeFolderLocation Resolve(string key, string? repositoryAlias = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var repository = settings.Current.Find(repositoryAlias);
        if (repository is null)
        {
            return KnowledgeFolderLocation.Unavailable(
                key,
                "Configure a repository in Settings before opening repository knowledge.");
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
                repository);
        }

        if (string.IsNullOrWhiteSpace(repository.CloneDirectory))
        {
            return KnowledgeFolderLocation.Unavailable(
                key,
                $"Add a local clone directory for {repository.FullName} in Settings to read {folder.DisplayName} knowledge.",
                repository,
                folder);
        }

        var path = folder.EffectivePath;
        var fullPath = Path.IsPathRooted(path)
            ? path
            : Path.Combine(repository.CloneDirectory, path);

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
                fullPath);
        }

        if (!Directory.Exists(fullPath))
        {
            return KnowledgeFolderLocation.Unavailable(
                key,
                $"{folder.DisplayName} knowledge folder was not found at {fullPath}.",
                repository,
                folder,
                fullPath);
        }

        return new KnowledgeFolderLocation(
            key,
            true,
            null,
            repository,
            folder,
            fullPath);
    }
}

public sealed record KnowledgeFolderLocation(
    string Key,
    bool Available,
    string? Message,
    GitHubRepositoryRef? Repository,
    KnowledgeFolderSetting? Folder,
    string? FullPath)
{
    public static KnowledgeFolderLocation Unavailable(
        string key,
        string message,
        GitHubRepositoryRef? repository = null,
        KnowledgeFolderSetting? folder = null,
        string? fullPath = null) => new(key, false, message, repository, folder, fullPath);
}
