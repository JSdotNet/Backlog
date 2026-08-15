using AngleSharp.Dom;
using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

public sealed class KnowledgeMenuTreeViewTests
{
    [Fact]
    public void Domain_heading_has_single_open_in_vscode_action_and_nested_rows_have_none()
    {
        using var context = new BunitContext();
        var root = BuildDomainTree();

        var component = context.Render<KnowledgeMenuTreeView>(parameters => parameters
            .Add(view => view.HeadingLabel, "Domain")
            .Add(view => view.RootFolder, root)
            .Add(view => view.Nodes, root.Children)
            .Add(view => view.IsNodeSelected, _ => false)
            .Add(view => view.IsNodeExpanded, node => node.Kind == KnowledgeMenuNodeKind.Folder)
            .Add(view => view.NodeClass, _ => "knowledge-menu__item")
            .Add(view => view.IsFolderOpening, _ => false));

        Assert.Single(component.FindAll("[data-testid='knowledge-open-vscode-button']"));
        Assert.Single(component.FindAll(".knowledge-stack__menu-heading [data-testid='knowledge-open-vscode-button']"));
        Assert.Empty(component.FindAll(".knowledge-menu__row [data-testid='knowledge-open-vscode-button']"));

        var labels = component.FindAll(".knowledge-menu__label").Select(label => label.TextContent.Trim()).ToArray();
        Assert.Contains("Intake", labels);
        Assert.Contains("Domain", labels);
        Assert.Contains("Specs", labels);
    }

    [Fact]
    public void Menu_keeps_error_between_heading_and_tree_and_preserves_callbacks()
    {
        using var context = new BunitContext();
        var root = BuildDomainTree();
        KnowledgeMenuNode? opened = null;
        KnowledgeMenuNode? selected = null;

        var component = context.Render<KnowledgeMenuTreeView>(parameters => parameters
            .Add(view => view.HeadingLabel, "Domain")
            .Add(view => view.RootFolder, root)
            .Add(view => view.Nodes, root.Children)
            .Add(view => view.OpenErrorMessage, "Open failed")
            .Add(view => view.IsNodeSelected, _ => false)
            .Add(view => view.IsNodeExpanded, node => node.Kind == KnowledgeMenuNodeKind.Folder)
            .Add(view => view.NodeClass, _ => "knowledge-menu__item")
            .Add(view => view.IsFolderOpening, _ => false)
            .Add(view => view.OnOpenFolder, (KnowledgeMenuNode node) => opened = node)
            .Add(view => view.OnNodeSelected, (KnowledgeMenuNode node) => selected = node));

        var nav = component.Find("nav.knowledge-stack__menu");
        var children = nav.Children.OfType<IElement>().ToArray();
        var headingIndex = Array.FindIndex(children, child => child.ClassList.Contains("knowledge-stack__menu-heading"));
        var errorIndex = Array.FindIndex(children, child => string.Equals(child.GetAttribute("data-testid"), "knowledge-menu-open-error", StringComparison.Ordinal));
        var treeIndex = Array.FindIndex(children, child => child.ClassList.Contains("knowledge-menu"));

        Assert.True(headingIndex >= 0);
        Assert.True(errorIndex > headingIndex);
        Assert.True(treeIndex > errorIndex);

        component.Find(".knowledge-stack__menu-heading [data-testid='knowledge-open-vscode-button']").Click();
        Assert.Same(root, opened);

        var nestedNode = component.FindAll(".knowledge-menu__row button").Single(button => button.TextContent.Contains("Domain", StringComparison.Ordinal));
        nestedNode.Click();
        Assert.Equal("intake/domain.md", selected?.Path);
    }

    private static KnowledgeMenuNode BuildDomainTree()
    {
        var domainDoc = new KnowledgeMenuNode("intake/domain.md", "Domain", "intake/domain.md", KnowledgeMenuNodeKind.File, "domain", [], true);
        var specsDoc = new KnowledgeMenuNode("intake/specs/spec.md", "Specs", "intake/specs/spec.md", KnowledgeMenuNodeKind.File, "domain", [], true);
        var specsFolder = new KnowledgeMenuNode("intake/specs", "Specs", "intake/specs", KnowledgeMenuNodeKind.Folder, "domain", [specsDoc], true);
        var intakeFolder = new KnowledgeMenuNode("intake", "Intake", "intake", KnowledgeMenuNodeKind.Folder, "domain", [domainDoc, specsFolder], true);
        var contextMap = new KnowledgeMenuNode("context-map.md", "Context Map", "context-map.md", KnowledgeMenuNodeKind.File, "domain", [], true);

        return new KnowledgeMenuNode("domain", "Domain", "domain", KnowledgeMenuNodeKind.Folder, "domain", [contextMap, intakeFolder], true);
    }
}
