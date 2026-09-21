using Backlog.UI.Components.Menus;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// The repository as a pickable tree, for asking the comparison what an
/// assistant would load while working on a given file.
///
/// <para>Source only. The same exclusions <see cref="InstructionSourceDiscovery"/>
/// keeps apply here for the same reason: a tree that offered
/// <c>bin/Debug/net10.0/…</c> would bury the files somebody actually edits under
/// thousands they never will, and no instruction file scopes itself to build
/// output.</para>
///
/// <para>Read in one pass rather than lazily. The repository this serves holds
/// on the order of a thousand source files, which is a single enumeration and a
/// tree small enough to hand to the view whole — and the alternative, expanding a
/// folder on click, would put a disk read behind a keystroke.</para>
/// </summary>
public static class InstructionPathTree
{
    // Version control, editor state, build output, dependencies, and Claude
    // Code's worktrees — the same exclusions discovery walks by, asked of
    // InstructionFileWalk rather than kept as a second list here. The worktrees
    // were the gap: this tree pruned bin and obj and then walked thirty sibling
    // checkouts underneath .claude, ten thousand directories to the clone's four
    // hundred, to offer files nobody edits from this pane.

    /// <summary>
    /// The tree under <paramref name="root"/>, folders before files and each
    /// sorted by name. A node's id is its repository-relative path in forward
    /// slashes, which is both what identifies it here and what the globs are
    /// written against, so nothing has to be translated on the way to the
    /// matcher.
    /// </summary>
    public static IReadOnlyList<TreeNode> Read(string? root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return [];

        try
        {
            return Children(root, root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            // A folder that moved or locked underneath the read. The picker is an
            // aid to the table beside it, so it goes quiet rather than taking the
            // panel down with it.
            return [];
        }
    }

    private static IReadOnlyList<TreeNode> Children(string root, string directory)
    {
        var nodes = new List<TreeNode>();

        foreach (var child in Directory.EnumerateDirectories(directory).OrderBy(Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!InstructionFileWalk.Descends(root, child)) continue;

            nodes.Add(new TreeNode(
                Relative(root, child),
                Name(child),
                TreeNodeKind.Folder,
                true,
                null,
                Children(root, child)));
        }

        foreach (var file in Directory.EnumerateFiles(directory).OrderBy(Name, StringComparer.OrdinalIgnoreCase))
        {
            // Excluded by name, not by being a directory. In a worktree `.git` is
            // a small pointer file rather than a folder, so a filter that only
            // looked at directories let it through here and nowhere else — which
            // is exactly the kind of difference that survives review, because the
            // clone everybody tests in does not have it.
            if (InstructionFileWalk.IsExcludedName(Name(file))) continue;

            nodes.Add(TreeNode.Leaf(Relative(root, file), Name(file)));
        }

        return nodes;
    }

    private static string Name(string path) => Path.GetFileName(path);

    private static string Relative(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');
}
