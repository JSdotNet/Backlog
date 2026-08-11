using Backlog.Infrastructure.GitHub;

namespace Backlog.Desktop.UI.Services;

public sealed class KnowledgeMenu(KnowledgeFolderSource source)
{
    public Task<KnowledgeMenuTree> LoadAsync(
        IReadOnlyCollection<string> visibleAreaKeys,
        string? repositoryAlias = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(visibleAreaKeys);
        cancellationToken.ThrowIfCancellationRequested();

        var folders = KnowledgeFolderSetting.Defaults()
            .Where(folder => visibleAreaKeys.Contains(AreaKey(folder.Key), StringComparer.Ordinal))
            .Select(folder => ReadFolder(folder, repositoryAlias, cancellationToken))
            .ToList();

        return Task.FromResult(new KnowledgeMenuTree(folders));
    }

    private KnowledgeMenuNode ReadFolder(KnowledgeFolderSetting folder, string? repositoryAlias, CancellationToken cancellationToken)
    {
        var areaKey = AreaKey(folder.Key);
        if (string.Equals(folder.Key, "instructions", StringComparison.OrdinalIgnoreCase))
        {
            return new KnowledgeMenuNode(areaKey, folder.DisplayName, folder.Key, KnowledgeMenuNodeKind.Folder, areaKey, [], true);
        }

        var location = source.Resolve(folder.Key, repositoryAlias);
        if (!location.Available || location.FullPath is null)
        {
            return new KnowledgeMenuNode(areaKey, folder.DisplayName, folder.Key, KnowledgeMenuNodeKind.Folder, areaKey, [], false, location.Message);
        }

        var children = EnumerateChildren(location.FullPath, location.FullPath, areaKey, cancellationToken);
        return new KnowledgeMenuNode(areaKey, folder.DisplayName, folder.Key, KnowledgeMenuNodeKind.Folder, areaKey, children, true);
    }

    private static IReadOnlyList<KnowledgeMenuNode> EnumerateChildren(
        string root,
        string directory,
        string areaKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var directories = Directory.EnumerateDirectories(directory)
                .Where(path => !Path.GetFileName(path).StartsWith('_'))
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .Select(path => new KnowledgeMenuNode(
                    Key(root, path),
                    Humanize(Path.GetFileName(path)),
                    RelativePath(root, path),
                    KnowledgeMenuNodeKind.Folder,
                    areaKey,
                    EnumerateChildren(root, path, areaKey, cancellationToken),
                    true));

            var files = Directory.EnumerateFiles(directory, "*.md", SearchOption.TopDirectoryOnly)
                .Where(path => !Path.GetFileName(path).StartsWith('_'))
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .Select(path => new KnowledgeMenuNode(
                    Key(root, path),
                    FileLabel(path),
                    RelativePath(root, path),
                    KnowledgeMenuNodeKind.File,
                    areaKey,
                    [],
                    true));

            return [.. directories.Concat(files)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            return
            [
                new KnowledgeMenuNode(
                    Key(root, directory) + ":unavailable",
                    "Unable to read folder",
                    RelativePath(root, directory),
                    KnowledgeMenuNodeKind.Message,
                    areaKey,
                    [],
                    false,
                    ex.Message)
            ];
        }
    }

    internal static string AreaKey(string folderKey) => folderKey.ToLowerInvariant() switch
    {
        ".backlog" => "backlog",
        ".domain" => "domain",
        ".arc42" => "arc42",
        ".tech" => "tech",
        ".design" => "design",
        "instructions" => "instructions",
        _ => folderKey.TrimStart('.').ToLowerInvariant()
    };

    private static string Key(string root, string path) => RelativePath(root, path).Replace('\\', '/').ToLowerInvariant();

    private static string RelativePath(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
        return relative == "." ? Path.GetFileName(root) : relative;
    }

    private static string FileLabel(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return name.Equals("README", StringComparison.OrdinalIgnoreCase) ? "README" : Humanize(name);
    }

    private static string Humanize(string text) => string.Join(' ', text
        .Replace('_', '-')
        .Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(word => word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..]));
}

public sealed record KnowledgeMenuTree(IReadOnlyList<KnowledgeMenuNode> Roots)
{
    public static KnowledgeMenuTree Empty { get; } = new([]);
}

public sealed record KnowledgeMenuNode(
    string Key,
    string Label,
    string Path,
    KnowledgeMenuNodeKind Kind,
    string AreaKey,
    IReadOnlyList<KnowledgeMenuNode> Children,
    bool Available,
    string? Message = null)
{
    public bool HasChildren => Children.Count > 0;
}

public enum KnowledgeMenuNodeKind
{
    Folder,
    File,
    Message
}
