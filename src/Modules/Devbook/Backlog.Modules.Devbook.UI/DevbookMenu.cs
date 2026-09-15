using Backlog.Infrastructure.GitHub;

using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Desktop.UI.Devbook;

public sealed class DevbookMenu(IDevbookFolderSource source)
{
    /// <summary>Re-published from the folder source so an open pane can rebuild
    /// its menu when the configured folder moves. The four knowledge stores
    /// forward it the same way; this is the one the pane subscribes to, and the
    /// only one, because a second forwarder on <see cref="DevbookScope"/> would
    /// only reload the same menu twice for one folder change — everything the
    /// scope answers is re-read on the render the reload brings with it.</summary>
    public event Action? Changed
    {
        add => source.Changed += value;
        remove => source.Changed -= value;
    }

    /// <summary>
    /// The rail, area by area.
    /// <para>
    /// Each area is <em>listed</em>, never fetched: the source is asked to make
    /// the folder listable — which for a branch is its index and nothing more —
    /// and the walk goes through the tree that source hands back rather than
    /// the disk, so a branch draws its menu before one of its chapters is here.
    /// The chapters arrive when a panel opens the area.
    /// </para>
    /// </summary>
    public async Task<DevbookMenuTree> LoadAsync(
        IReadOnlyCollection<string> visibleAreaKeys,
        string? repositoryAlias = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(visibleAreaKeys);
        cancellationToken.ThrowIfCancellationRequested();

        var folders = new List<DevbookMenuNode>();

        foreach (var folder in DevbookFolderSetting.Defaults()
                     .Where(folder => visibleAreaKeys.Contains(AreaKey(folder.Key), StringComparer.Ordinal)))
        {
            folders.Add(await ReadFolderAsync(folder, repositoryAlias, cancellationToken).ConfigureAwait(false));
        }

        return new DevbookMenuTree(folders);
    }

    private async Task<DevbookMenuNode> ReadFolderAsync(DevbookFolderSetting folder, string? repositoryAlias, CancellationToken cancellationToken)
    {
        var areaKey = AreaKey(folder.Key);
        var location = await source.PrepareListingAsync(folder.Key, repositoryAlias, cancellationToken).ConfigureAwait(false);

        if (!location.Available || location.FullPath is null)
        {
            return new DevbookMenuNode(areaKey, folder.DisplayName, folder.Key, DevbookMenuNodeKind.Folder, areaKey, [], false, location.Message);
        }

        var tree = source.FileTree(location);

        if (string.Equals(areaKey, "instructions", StringComparison.OrdinalIgnoreCase))
        {
            var roots = EnumerateInstructionRoots(tree, location.FullPath, areaKey, cancellationToken);
            return new DevbookMenuNode(areaKey, folder.DisplayName, folder.Key, DevbookMenuNodeKind.Folder, areaKey, roots, true);
        }

        // Once per area, not once per directory: the folder's authored order and
        // the generated titles are both read here and handed down the walk.
        var outline = DevbookMenuOutline.Read(location.FullPath);
        var children = EnumerateChildren(tree, location.FullPath, location.FullPath, areaKey, outline, cancellationToken);
        return new DevbookMenuNode(areaKey, folder.DisplayName, folder.Key, DevbookMenuNodeKind.Folder, areaKey, children, true);
    }

    private static IReadOnlyList<DevbookMenuNode> EnumerateInstructionRoots(
        IDevbookFileTree tree,
        string repositoryRoot,
        string areaKey,
        CancellationToken cancellationToken)
    {
        var roots = new List<DevbookMenuNode>();
        AddInstructionRoot(tree, roots, repositoryRoot, ".github", ".github", areaKey, cancellationToken);
        AddInstructionRoot(tree, roots, repositoryRoot, ".claude", ".claude", areaKey, cancellationToken);
        AddInstructionRoot(tree, roots, repositoryRoot, ".agent", ".agent", areaKey, cancellationToken, fallbackRelativePath: ".agents");
        AddRootInstructionFiles(tree, roots, repositoryRoot, areaKey);
        return roots;
    }

    /// <summary>
    /// The instruction files that live at the root, as leaves beside the folders.
    ///
    /// <para>Without these the menu offers three folders and nothing else, so
    /// <c>CLAUDE.md</c> - which discovery reads and the loading comparison lists -
    /// has no row to click. The names come from discovery rather than from a
    /// second list here, because two lists is how a menu starts disagreeing with
    /// what the panel beside it can open.</para>
    /// <para>Last, after the folders: they are the structure, and a handful of
    /// loose files reads as a footnote to it rather than as a peer.</para>
    /// </summary>
    private static void AddRootInstructionFiles(IDevbookFileTree tree, List<DevbookMenuNode> roots, string repositoryRoot, string areaKey)
    {
        foreach (var name in InstructionSourceDiscovery.RootFileNames)
        {
            var fullPath = Path.Combine(repositoryRoot, name);
            if (!tree.FileExists(fullPath)) continue;

            roots.Add(new DevbookMenuNode(
                Key(repositoryRoot, fullPath),
                FileLabel(fullPath),
                RelativePath(repositoryRoot, fullPath),
                DevbookMenuNodeKind.File,
                areaKey,
                [],
                true));
        }
    }

    private static void AddInstructionRoot(
        IDevbookFileTree tree,
        List<DevbookMenuNode> roots,
        string repositoryRoot,
        string displayPath,
        string relativePath,
        string areaKey,
        CancellationToken cancellationToken,
        string? fallbackRelativePath = null)
    {
        var fullPath = Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var nodePath = relativePath;
        if (!tree.DirectoryExists(fullPath) && fallbackRelativePath is not null)
        {
            fullPath = Path.Combine(repositoryRoot, fallbackRelativePath.Replace('/', Path.DirectorySeparatorChar));
            nodePath = tree.DirectoryExists(fullPath) ? fallbackRelativePath : relativePath;
        }

        var available = tree.DirectoryExists(fullPath);
        var children = available
            ? EnumerateInstructionChildren(tree, repositoryRoot, fullPath, displayPath, nodePath, areaKey, cancellationToken)
            : [];
        roots.Add(new DevbookMenuNode(areaKey, displayPath, displayPath, DevbookMenuNodeKind.Folder, areaKey, children, available));
    }


    private static IReadOnlyList<DevbookMenuNode> EnumerateInstructionChildren(
        IDevbookFileTree tree,
        string repositoryRoot,
        string directory,
        string displayRoot,
        string sourceRoot,
        string areaKey,
        CancellationToken cancellationToken)
    {
        // No outline: the instruction area is assembled out of agent folders
        // rather than being a knowledge folder with a reading order of its own.
        var nodes = EnumerateChildren(tree, repositoryRoot, directory, areaKey, DevbookMenuOutline.None, cancellationToken, includeAllFiles: true);
        return nodes.Select(node => RewriteInstructionPath(node, displayRoot, sourceRoot)).ToList();
    }

    private static DevbookMenuNode RewriteInstructionPath(DevbookMenuNode node, string displayRoot, string sourceRoot)
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
    private static IReadOnlyList<DevbookMenuNode> EnumerateChildren(
        IDevbookFileTree tree,
        string root,
        string directory,
        string areaKey,
        DevbookMenuOutline outline,
        CancellationToken cancellationToken,
        bool includeAllFiles = false)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var directories = tree.EnumerateDirectories(directory)
                .Where(path => !Path.GetFileName(path).StartsWith('_'))
                .Select(path => (DiskPath: path, Node: new DevbookMenuNode(
                    Key(root, path),
                    Humanize(Path.GetFileName(path)),
                    DirectoryNodePath(tree, root, path),
                    DevbookMenuNodeKind.Folder,
                    areaKey,
                    EnumerateChildren(tree, root, path, areaKey, outline, cancellationToken, includeAllFiles),
                    true)));

            var files = tree.EnumerateFiles(directory, includeAllFiles ? "*" : "*.md")
                .Where(path => !Path.GetFileName(path).StartsWith('_'))
                .Where(path => !IsIndexMarkdown(path) || string.Equals(root, directory, StringComparison.OrdinalIgnoreCase))
                .Select(path => (DiskPath: path, Node: new DevbookMenuNode(
                    Key(root, path),
                    FileLabel(path),
                    RelativePath(root, path),
                    DevbookMenuNodeKind.File,
                    areaKey,
                    [],
                    true)));

            // The node's own Path is not the name to look either answer up by: a
            // directory holding an index.md reports that file as its path, so the
            // name on disk is carried alongside it.
            var relativeDirectory = RelativeDirectory(root, directory);
            var entries = directories.Concat(files).ToList();

            return Label(
                Order(entries, outline.Order(relativeDirectory), areaKey, root, directory),
                outline,
                relativeDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            return
            [
                new DevbookMenuNode(
                    Key(root, directory) + ":unavailable",
                    "Unable to read folder",
                    RelativePath(root, directory),
                    DevbookMenuNodeKind.Message,
                    areaKey,
                    [],
                    false,
                    ex.Message)
            ];
        }
    }

    internal static string AreaKey(string folderKey) => folderKey.ToLowerInvariant() switch
    {
        ".domain" => "domain",
        ".arc42" => "arc42",
        ".tech" => "tech",
        ".design" => "design",
        "instructions" => "instructions",
        _ => folderKey.TrimStart('.').ToLowerInvariant()
    };

    /// <summary>
    /// One level of the rail in reading order: the entries the folder's
    /// <c>_reading-order.json</c> names, in the order it names them, then
    /// everything it does not, in the order the rail has always sorted them.
    ///
    /// <para>A directory that declares nothing keeps that sort alone — which is
    /// <c>.arc42</c>, whose numbered chapters sequence themselves and whose two
    /// record folders belong at 09.5 and 11.5, and every folder in a checkout
    /// that carries no such file at all. That is rung five of ADR 0004's ladder:
    /// no declaration has to read exactly as it did before there was one.</para>
    /// </summary>
    private static List<(string DiskPath, DevbookMenuNode Node)> Order(
        List<(string DiskPath, DevbookMenuNode Node)> entries,
        IReadOnlyList<string> declared,
        string areaKey,
        string root,
        string directory)
    {
        var alphabetical = entries
            .OrderBy(entry => SortKey(areaKey, root, directory, entry.Node), StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Node.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (declared.Count == 0) return alphabetical;

        var ordinals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < declared.Count; index++) ordinals.TryAdd(declared[index], index);

        // A declared name with nothing on disk behind it never reached the list
        // above, so it simply draws no row.
        return
        [
            .. alphabetical
                .Where(entry => ordinals.ContainsKey(Path.GetFileName(entry.DiskPath)))
                .OrderBy(entry => ordinals[Path.GetFileName(entry.DiskPath)]),
            .. alphabetical.Where(entry => !ordinals.ContainsKey(Path.GetFileName(entry.DiskPath)))
        ];
    }

    /// <summary>
    /// The rail's row labels, taking a chapter's real title from the generated
    /// outline only where it still tells one row from another.
    ///
    /// <para>The outline knows that <c>dev-pc-management</c> is "Dev PC
    /// Management" and <c>monitoring</c> is "Monitoring &amp; Dashboard", neither
    /// of which a title-cased filename can say. It also knows that every document
    /// inside a bounded context carries the context's own H1, so a rail that took
    /// every title would replace six distinguishable rows with six called
    /// "Inbox". A title is therefore adopted only when no sibling's title and no
    /// sibling's filename label already claims it — whatever a level adopts, its
    /// rows still tell each other apart.</para>
    /// </summary>
    private static IReadOnlyList<DevbookMenuNode> Label(
        List<(string DiskPath, DevbookMenuNode Node)> ordered,
        DevbookMenuOutline outline,
        string relativeDirectory)
    {
        var titles = ordered
            .Select(entry => outline.TryTitle(relativeDirectory, Path.GetFileName(entry.DiskPath), out var title) ? title : null)
            .ToList();

        var nodes = new List<DevbookMenuNode>(ordered.Count);

        for (var index = 0; index < ordered.Count; index++)
        {
            var title = titles[index];
            var position = index;
            var distinguishes = title is not null
                && !ordered.Where((_, other) => other != position)
                    .Any(entry => string.Equals(entry.Node.Label, title, StringComparison.OrdinalIgnoreCase))
                && !titles.Where((_, other) => other != position)
                    .Any(other => string.Equals(other, title, StringComparison.OrdinalIgnoreCase));

            nodes.Add(distinguishes ? ordered[index].Node with { Label = title! } : ordered[index].Node);
        }

        return nodes;
    }

    /// <summary>Where a directory sits inside the knowledge folder, as the
    /// authored reading order and the generated outline both key it: empty at the
    /// folder's own root, <c>/</c>-separated below it.</summary>
    private static string RelativeDirectory(string root, string directory)
    {
        var relative = Path.GetRelativePath(root, directory).Replace(Path.DirectorySeparatorChar, '/');
        return relative == "." ? string.Empty : relative;
    }

    private static string SortKey(string areaKey, string root, string directory, DevbookMenuNode node)
    {
        if (string.Equals(root, directory, StringComparison.OrdinalIgnoreCase))
        {
            // A folder's README is its opening chapter, not the entry between
            // "interaction-guidelines" and "typography" that its name sorts it
            // to. Only at the root, because a README further down describes the
            // folder it sits in rather than the area.
            if (string.Equals(node.Path, "README.md", StringComparison.OrdinalIgnoreCase))
            {
                return "00-README.md";
            }

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

    private static string DirectoryNodePath(IDevbookFileTree tree, string root, string directory)
    {
        var indexPath = IndexMarkdownPath(tree, directory);
        return indexPath is null ? RelativePath(root, directory) : RelativePath(root, indexPath);
    }

    private static string? IndexMarkdownPath(IDevbookFileTree tree, string directory)
    {
        try
        {
            return tree.EnumerateFiles(directory, "*.md")
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

    /// <summary>Name segments that are acronyms rather than words, so a folder
    /// called "adr" reads as ADR instead of Adr. Deliberately only the segments
    /// the knowledge folders actually use — anything else keeps title case
    /// rather than being shouted on a guess.</summary>
    private static readonly HashSet<string> Acronyms = new(StringComparer.OrdinalIgnoreCase) { "adr", "tdr" };

    private static string Humanize(string text) => string.Join(' ', text
        .Replace('_', '-')
        .Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(Titlecase));

    private static string Titlecase(string word) => word.Length == 0
        ? word
        : Acronyms.Contains(word)
            ? word.ToUpperInvariant()
            : char.ToUpperInvariant(word[0]) + word[1..];
}

public sealed record DevbookMenuTree(IReadOnlyList<DevbookMenuNode> Roots)
{
    public static DevbookMenuTree Empty { get; } = new([]);
}

public sealed record DevbookMenuNode(
    string Key,
    string Label,
    string Path,
    DevbookMenuNodeKind Kind,
    string AreaKey,
    IReadOnlyList<DevbookMenuNode> Children,
    bool Available,
    string? Message = null)
{
    public bool HasChildren => Children.Count > 0;
}

public enum DevbookMenuNodeKind
{
    Folder,
    File,
    Message
}
