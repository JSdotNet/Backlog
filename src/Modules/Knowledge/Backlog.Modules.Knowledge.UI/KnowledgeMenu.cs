using Backlog.Infrastructure.GitHub;

using Backlog.Modules.Knowledge.Abstractions;

namespace Backlog.Desktop.UI.Knowledge;

public sealed class KnowledgeMenu(IKnowledgeFolderSource source)
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
        var location = source.Resolve(folder.Key, repositoryAlias);

        if (string.Equals(areaKey, "instructions", StringComparison.OrdinalIgnoreCase))
        {
            if (!location.Available || location.FullPath is null)
            {
                return new KnowledgeMenuNode(areaKey, folder.DisplayName, folder.Key, KnowledgeMenuNodeKind.Folder, areaKey, [], false, location.Message);
            }

            var roots = EnumerateInstructionRoots(location.FullPath, areaKey, cancellationToken);
            return new KnowledgeMenuNode(areaKey, folder.DisplayName, folder.Key, KnowledgeMenuNodeKind.Folder, areaKey, roots, true);
        }

        if (!location.Available || location.FullPath is null)
        {
            return new KnowledgeMenuNode(areaKey, folder.DisplayName, folder.Key, KnowledgeMenuNodeKind.Folder, areaKey, [], false, location.Message);
        }

        var children = EnumerateChildren(location.FullPath, location.FullPath, areaKey, cancellationToken);
        return new KnowledgeMenuNode(areaKey, folder.DisplayName, folder.Key, KnowledgeMenuNodeKind.Folder, areaKey, children, true);
    }

    private static IReadOnlyList<KnowledgeMenuNode> EnumerateInstructionRoots(
        string repositoryRoot,
        string areaKey,
        CancellationToken cancellationToken)
    {
        var roots = new List<KnowledgeMenuNode>();
        AddInstructionRoot(roots, repositoryRoot, ".github", ".github", areaKey, cancellationToken);
        AddInstructionRoot(roots, repositoryRoot, ".claude", ".claude", areaKey, cancellationToken);
        AddInstructionRoot(roots, repositoryRoot, ".agent", ".agent", areaKey, cancellationToken, fallbackRelativePath: ".agents");
        return roots;
    }

    private static void AddInstructionRoot(
        List<KnowledgeMenuNode> roots,
        string repositoryRoot,
        string displayPath,
        string relativePath,
        string areaKey,
        CancellationToken cancellationToken,
        string? fallbackRelativePath = null)
    {
        var fullPath = Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var nodePath = relativePath;
        if (!Directory.Exists(fullPath) && fallbackRelativePath is not null)
        {
            fullPath = Path.Combine(repositoryRoot, fallbackRelativePath.Replace('/', Path.DirectorySeparatorChar));
            nodePath = Directory.Exists(fullPath) ? fallbackRelativePath : relativePath;
        }

        var available = Directory.Exists(fullPath);
        var children = available
            ? EnumerateInstructionChildren(repositoryRoot, fullPath, displayPath, nodePath, areaKey, cancellationToken)
            : [];
        roots.Add(new KnowledgeMenuNode(areaKey, displayPath, displayPath, KnowledgeMenuNodeKind.Folder, areaKey, children, available));
    }


    private static IReadOnlyList<KnowledgeMenuNode> EnumerateInstructionChildren(
        string repositoryRoot,
        string directory,
        string displayRoot,
        string sourceRoot,
        string areaKey,
        CancellationToken cancellationToken)
    {
        var nodes = EnumerateChildren(repositoryRoot, directory, areaKey, cancellationToken, includeAllFiles: true);
        return nodes.Select(node => RewriteInstructionPath(node, displayRoot, sourceRoot)).ToList();
    }

    private static KnowledgeMenuNode RewriteInstructionPath(KnowledgeMenuNode node, string displayRoot, string sourceRoot)
    {
        var displayPath = RewriteInstructionPath(node.Path, displayRoot, sourceRoot);
        return node with
        {
            Key = RewriteInstructionPath(node.Key, displayRoot, sourceRoot).ToLowerInvariant(),
            Path = displayPath,
            Children = node.Children.Select(child => RewriteInstructionPath(child, displayRoot, sourceRoot)).ToList()
        };
    }

    private static string RewriteInstructionPath(string path, string displayRoot, string sourceRoot) =>
        path.StartsWith(sourceRoot, StringComparison.OrdinalIgnoreCase)
            ? displayRoot + path[sourceRoot.Length..]
            : path;
    private static IReadOnlyList<KnowledgeMenuNode> EnumerateChildren(
        string root,
        string directory,
        string areaKey,
        CancellationToken cancellationToken,
        bool includeAllFiles = false)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var directories = Directory.EnumerateDirectories(directory)
                .Where(path => !Path.GetFileName(path).StartsWith('_'))
                .Select(path => new KnowledgeMenuNode(
                    Key(root, path),
                    Humanize(Path.GetFileName(path)),
                    DirectoryNodePath(root, path),
                    KnowledgeMenuNodeKind.Folder,
                    areaKey,
                    EnumerateChildren(root, path, areaKey, cancellationToken, includeAllFiles),
                    true));

            var files = Directory.EnumerateFiles(directory, includeAllFiles ? "*" : "*.md", SearchOption.TopDirectoryOnly)
                .Where(path => !Path.GetFileName(path).StartsWith('_'))
                .Where(path => !IsIndexMarkdown(path) || string.Equals(root, directory, StringComparison.OrdinalIgnoreCase))
                .Select(path => new KnowledgeMenuNode(
                    Key(root, path),
                    FileLabel(path),
                    RelativePath(root, path),
                    KnowledgeMenuNodeKind.File,
                    areaKey,
                    [],
                    true));

            return
            [
                .. directories.Concat(files)
                    .OrderBy(node => SortKey(areaKey, root, directory, node), StringComparer.OrdinalIgnoreCase)
                    .ThenBy(node => node.Label, StringComparer.OrdinalIgnoreCase)
            ];
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

    private static string SortKey(string areaKey, string root, string directory, KnowledgeMenuNode node)
    {
        if (string.Equals(root, directory, StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(areaKey, "domain", StringComparison.OrdinalIgnoreCase)
                && string.Equals(node.Path, "context-map.md", StringComparison.OrdinalIgnoreCase))
            {
                return "00-context-map.md";
            }

            if (string.Equals(areaKey, "arc42", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(node.Path, "adr", StringComparison.OrdinalIgnoreCase)) return "09.5-adr";
                if (string.Equals(node.Path, "tdr", StringComparison.OrdinalIgnoreCase)) return "11.5-tdr";
            }
        }

        return node.Path;
    }

    private static string DirectoryNodePath(string root, string directory)
    {
        var indexPath = IndexMarkdownPath(directory);
        return indexPath is null ? RelativePath(root, directory) : RelativePath(root, indexPath);
    }

    private static string? IndexMarkdownPath(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory, "*.md", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(IsIndexMarkdown);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    private static bool IsIndexMarkdown(string path) => string.Equals(Path.GetFileName(path), "index.md", StringComparison.OrdinalIgnoreCase);

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
