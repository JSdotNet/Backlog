using Backlog.UI.Components.Menus;

using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Devbook menu re-renders whenever the pane does, and the pane re-renders on
/// every keystroke typed into a task beside it. Mapping the whole tree into the
/// library's nodes on each of those renders is work for a menu that has not
/// changed, so the adapter maps once per tree it is handed and again only when it
/// is handed another.
/// </summary>
public sealed class DevbookMenuTreeViewRerenderTests
{
    [Fact]
    public void An_unchanged_tree_is_not_mapped_again_on_a_re_render()
    {
        using var context = new BunitContext();
        var root = BuildTree();

        var component = context.Render<DevbookMenuTreeView>(parameters => parameters
            .Add(view => view.RootFolder, root)
            .Add(view => view.Nodes, root.Children));
        var tree = component.FindComponent<TreeView>().Instance;
        var nodes = tree.Nodes;
        var rootFolder = tree.RootFolder;

        component.Render(parameters => parameters
            .Add(view => view.RootFolder, root)
            .Add(view => view.Nodes, root.Children));

        Assert.Same(nodes, tree.Nodes);
        Assert.Same(rootFolder, tree.RootFolder);
    }

    [Fact]
    public void A_new_list_of_rows_is_mapped_again()
    {
        using var context = new BunitContext();
        var root = BuildTree();

        var component = context.Render<DevbookMenuTreeView>(parameters => parameters
            .Add(view => view.RootFolder, root)
            .Add(view => view.Nodes, root.Children));
        var tree = component.FindComponent<TreeView>().Instance;
        var nodes = tree.Nodes;

        var added = new DevbookMenuNode("glossary.md", "Glossary", "glossary.md", DevbookMenuNodeKind.File, "domain", [], true);
        component.Render(parameters => parameters
            .Add(view => view.RootFolder, root)
            .Add(view => view.Nodes, [.. root.Children, added]));

        Assert.NotSame(nodes, tree.Nodes);
        Assert.Contains("Glossary", component.FindAll(".devbook-menu__label").Select(label => label.TextContent.Trim()));
    }

    /// <summary>The row a reader presses is still the node the host handed in,
    /// after a re-render that mapped nothing: the ids behind the rows are the
    /// ones the last mapping numbered.</summary>
    [Fact]
    public void A_row_pressed_after_an_unchanged_re_render_hands_back_the_hosts_node()
    {
        using var context = new BunitContext();
        var root = BuildTree();
        DevbookMenuNode? selected = null;

        var component = context.Render<DevbookMenuTreeView>(parameters => parameters
            .Add(view => view.RootFolder, root)
            .Add(view => view.Nodes, root.Children)
            .Add(view => view.OnNodeSelected, (DevbookMenuNode node) => selected = node));
        component.Render(parameters => parameters
            .Add(view => view.RootFolder, root)
            .Add(view => view.Nodes, root.Children)
            .Add(view => view.OnNodeSelected, (DevbookMenuNode node) => selected = node));

        component.FindAll(".devbook-menu__row button")
            .Single(button => button.TextContent.Contains("Context Map", StringComparison.Ordinal))
            .Click();

        Assert.Same(root.Children[0], selected);
    }

    private static DevbookMenuNode BuildTree()
    {
        var contextMap = new DevbookMenuNode("context-map.md", "Context Map", "context-map.md", DevbookMenuNodeKind.File, "domain", [], true);
        var domainDoc = new DevbookMenuNode("intake/domain.md", "Domain", "intake/domain.md", DevbookMenuNodeKind.File, "domain", [], true);
        var intakeFolder = new DevbookMenuNode("intake", "Intake", "intake", DevbookMenuNodeKind.Folder, "domain", [domainDoc], true);

        return new DevbookMenuNode("domain", "Domain", "domain", DevbookMenuNodeKind.Folder, "domain", [contextMap, intakeFolder], true);
    }
}
