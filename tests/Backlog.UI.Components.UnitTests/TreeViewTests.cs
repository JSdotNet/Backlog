namespace Backlog.UI.Components.UnitTests;

public sealed class TreeViewTests
{
    private static readonly TreeNode Child = TreeNode.Leaf("intake/domain.md", "Domain");
    private static readonly TreeNode Unavailable = TreeNode.Leaf("intake/draft.md", "Draft", available: false, message: "Not written yet");
    private static readonly TreeNode Folder = new("intake", "Intake", TreeNodeKind.Folder, true, null, [Child, Unavailable]);
    private static readonly TreeNode Root = new("domain", "Domain", TreeNodeKind.Folder, true, null, [Folder]);

    [Fact]
    public void Nested_nodes_render_as_a_tree_of_treeitems()
    {
        using var context = new BunitContext();

        var tree = Render(context, expanded: _ => true);

        Assert.Equal("Knowledge chapters", tree.Find("[role='tree']").GetAttribute("aria-label"));
        Assert.Equal("group", tree.Find("[role='tree'] ul").GetAttribute("role"));

        var labels = tree.FindAll("[role='treeitem']").Select(item => item.TextContent.Trim()).ToArray();
        Assert.Contains(labels, label => label.Contains("Intake", StringComparison.Ordinal));
        Assert.Contains(labels, label => label.Contains("Domain", StringComparison.Ordinal));
    }

    [Fact]
    public void Children_only_appear_while_their_parent_is_expanded()
    {
        using var context = new BunitContext();

        var collapsed = Render(context, expanded: _ => false);
        var expanded = Render(context, expanded: _ => true);

        Assert.Equal("false", collapsed.Find("[role='treeitem']").GetAttribute("aria-expanded"));
        Assert.DoesNotContain("Domain", collapsed.Find("[role='tree']").TextContent, StringComparison.Ordinal);
        Assert.Equal("true", expanded.Find("[role='treeitem']").GetAttribute("aria-expanded"));
        Assert.Contains("Domain", expanded.Find("[role='tree']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_selected_node_is_the_one_the_host_says_is_selected()
    {
        using var context = new BunitContext();

        var tree = Render(context, expanded: _ => true, selected: node => node.Id == Child.Id);
        var selected = tree.FindAll("[role='treeitem']")
            .Where(item => item.GetAttribute("aria-selected") == "true")
            .ToArray();

        Assert.Single(selected);
        Assert.Contains("Domain", selected[0].TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unavailable_node_is_disabled_and_says_why()
    {
        using var context = new BunitContext();

        var tree = Render(context, expanded: _ => true);
        var draft = tree.FindAll("[role='treeitem']").Single(item => item.TextContent.Contains("Draft", StringComparison.Ordinal));

        Assert.True(draft.HasAttribute("disabled"));
        Assert.Equal("Not written yet", draft.GetAttribute("title"));
    }

    [Fact]
    public void A_message_node_is_text_rather_than_a_control()
    {
        using var context = new BunitContext();

        var tree = context.Render<TreeView>(parameters => parameters
            .Add(v => v.Nodes, new[] { new TreeNode("empty", string.Empty, TreeNodeKind.Message, true, "Nothing here yet") }));

        var message = tree.Find("[role='treeitem']");

        Assert.Equal("p", message.NodeName.ToLowerInvariant());
        Assert.Equal("Nothing here yet", message.TextContent);
    }

    [Fact]
    public void Selecting_a_node_hands_back_the_node_the_host_passed_in()
    {
        // The host keeps its own richer node type and finds it again by identity,
        // so the tree has to return the same instance, not a copy.
        using var context = new BunitContext();
        TreeNode? selected = null;

        var tree = Render(context, expanded: _ => true, onSelected: node => selected = node);
        tree.FindAll("[role='treeitem']").Single(item => item.TextContent.Contains("Domain", StringComparison.Ordinal)).Click();

        Assert.Same(Child, selected);
    }

    [Fact]
    public void The_class_prefix_renames_every_row_class()
    {
        using var context = new BunitContext();

        var tree = context.Render<TreeView>(parameters => parameters
            .Add(v => v.ClassPrefix, "chapter-menu")
            .Add(v => v.Nodes, new[] { Child }));

        Assert.NotNull(tree.Find("nav.chapter-menu .chapter-menu__heading"));
        Assert.Equal("chapter-menu", tree.Find("[role='tree']").GetAttribute("class"));
        Assert.Equal("chapter-menu__item", tree.Find("[role='treeitem']").GetAttribute("class"));
    }

    [Fact]
    public void Only_an_available_root_folder_gets_the_open_action()
    {
        using var context = new BunitContext();
        TreeNode? opened = null;

        var tree = context.Render<TreeView>(parameters => parameters
            .Add(v => v.RootFolder, Root)
            .Add(v => v.Nodes, Root.Children)
            .Add(v => v.OpenButtonTestId, "open-folder")
            .Add(v => v.OnOpenFolder, (TreeNode node) => opened = node));

        tree.Find("[data-testid='open-folder']").Click();

        Assert.Same(Root, opened);
        Assert.Empty(tree.FindAll(".knowledge-menu__row [data-testid='open-folder']"));
    }

    private static IRenderedComponent<TreeView> Render(
        BunitContext context,
        Func<TreeNode, bool> expanded,
        Func<TreeNode, bool>? selected = null,
        Action<TreeNode>? onSelected = null) =>
        context.Render<TreeView>(parameters => parameters
            .Add(v => v.Nodes, new[] { Folder })
            .Add(v => v.IsNodeExpanded, expanded)
            .Add(v => v.IsNodeSelected, selected ?? (_ => false))
            .Add(v => v.OnNodeSelected, (TreeNode node) => onSelected?.Invoke(node)));
}
