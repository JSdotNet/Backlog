namespace Backlog.UI.Components.Menus;

/// <summary>
/// What <see cref="TreeView"/> needs to know about a row. Callers keep their own
/// richer node type and map onto this, so the tree stays free of any one app's
/// vocabulary; <see cref="Id"/> is how they find the original again.
/// </summary>
public sealed record TreeNode(
    string Id,
    string Label,
    TreeNodeKind Kind,
    bool Available,
    string? Message,
    IReadOnlyList<TreeNode> Children)
{
    /// <summary>A row with nothing under it — the common case.</summary>
    public TreeNode(string id, string label, TreeNodeKind kind = TreeNodeKind.Item, bool available = true, string? message = null)
        : this(id, label, kind, available, message, [])
    {
    }

    public static TreeNode Leaf(string id, string label, bool available = true, string? message = null) =>
        new(id, label, TreeNodeKind.Item, available, message);

    public bool HasChildren => Children.Count > 0;
}

public enum TreeNodeKind
{
    Item,
    Folder,
    Message
}
