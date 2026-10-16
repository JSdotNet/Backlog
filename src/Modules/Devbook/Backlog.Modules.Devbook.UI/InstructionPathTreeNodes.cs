using Backlog.UI.Components.Menus;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// The repository as the comparison's path picker draws it: the rows
/// <see cref="InstructionPathTree"/> read, as the tree control's nodes. The read is
/// the adapter's (issue #738), and the adapter references no UI project, so the
/// mapping onto <see cref="TreeNode"/> is the screen's.
/// </summary>
public static class InstructionPathTreeNodes
{
    /// <summary>The tree under <paramref name="root"/>, read by
    /// <see cref="InstructionPathTree.Read"/> and mapped.</summary>
    public static IReadOnlyList<TreeNode> Read(string? root) => From(InstructionPathTree.Read(root));

    /// <summary>Folders stay folders with their children; files become leaves. A
    /// node keeps the repository-relative path as its id, which is what the globs
    /// are matched against.</summary>
    public static IReadOnlyList<TreeNode> From(IReadOnlyList<InstructionPathNode> nodes) =>
    [
        .. nodes.Select(node => node.IsFolder
            ? new TreeNode(node.Id, node.Label, TreeNodeKind.Folder, true, null, From(node.Children))
            : TreeNode.Leaf(node.Id, node.Label))
    ];
}
