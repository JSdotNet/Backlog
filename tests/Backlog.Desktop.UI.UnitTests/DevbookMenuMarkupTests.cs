using AngleSharp.Dom;
using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

public sealed class DevbookMenuTreeViewTests
{
    [Fact]
    public void Domain_heading_has_single_open_in_vscode_action_and_nested_rows_have_none()
    {
        using var context = new BunitContext();
        var root = BuildDomainTree();

        var component = context.Render<DevbookMenuTreeView>(parameters => parameters
            .Add(view => view.HeadingLabel, "Domain")
            .Add(view => view.RootFolder, root)
            .Add(view => view.Nodes, root.Children)
            .Add(view => view.IsNodeSelected, _ => false)
            .Add(view => view.IsNodeExpanded, node => node.Kind == DevbookMenuNodeKind.Folder)
            .Add(view => view.NodeClass, _ => "devbook-menu__item")
            .Add(view => view.IsFolderOpening, _ => false));

        Assert.Single(component.FindAll("[data-testid='devbook-open-vscode-button']"));
        Assert.Single(component.FindAll(".devbook-stack__menu-heading [data-testid='devbook-open-vscode-button']"));
        Assert.Empty(component.FindAll(".devbook-menu__row [data-testid='devbook-open-vscode-button']"));

        var labels = component.FindAll(".devbook-menu__label").Select(label => label.TextContent.Trim()).ToArray();
        Assert.Contains("Intake", labels);
        Assert.Contains("Domain", labels);
        Assert.Contains("Specs", labels);
    }

    [Fact]
    public void Menu_keeps_error_between_heading_and_tree_and_preserves_callbacks()
    {
        using var context = new BunitContext();
        var root = BuildDomainTree();
        DevbookMenuNode? opened = null;
        DevbookMenuNode? selected = null;

        var component = context.Render<DevbookMenuTreeView>(parameters => parameters
            .Add(view => view.HeadingLabel, "Domain")
            .Add(view => view.RootFolder, root)
            .Add(view => view.Nodes, root.Children)
            .Add(view => view.OpenErrorMessage, "Open failed")
            .Add(view => view.IsNodeSelected, _ => false)
            .Add(view => view.IsNodeExpanded, node => node.Kind == DevbookMenuNodeKind.Folder)
            .Add(view => view.NodeClass, _ => "devbook-menu__item")
            .Add(view => view.IsFolderOpening, _ => false)
            .Add(view => view.OnOpenFolder, (DevbookMenuNode node) => opened = node)
            .Add(view => view.OnNodeSelected, (DevbookMenuNode node) => selected = node));

        var nav = component.Find("nav.devbook-stack__menu");
        var children = nav.Children.OfType<IElement>().ToArray();
        var headingIndex = Array.FindIndex(children, child => child.ClassList.Contains("devbook-stack__menu-heading"));
        var errorIndex = Array.FindIndex(children, child => string.Equals(child.GetAttribute("data-testid"), "devbook-menu-open-error", StringComparison.Ordinal));
        var treeIndex = Array.FindIndex(children, child => child.ClassList.Contains("devbook-menu"));

        Assert.True(headingIndex >= 0);
        Assert.True(errorIndex > headingIndex);
        Assert.True(treeIndex > errorIndex);

        component.Find(".devbook-stack__menu-heading [data-testid='devbook-open-vscode-button']").Click();
        Assert.Same(root, opened);

        var nestedNode = component.FindAll(".devbook-menu__row button").Single(button => button.TextContent.Contains("Domain", StringComparison.Ordinal));
        nestedNode.Click();
        Assert.Equal("intake/domain.md", selected?.Path);
    }

    private static DevbookMenuNode BuildDomainTree()
    {
        var domainDoc = new DevbookMenuNode("intake/domain.md", "Domain", "intake/domain.md", DevbookMenuNodeKind.File, "domain", [], true);
        var specsDoc = new DevbookMenuNode("intake/specs/spec.md", "Specs", "intake/specs/spec.md", DevbookMenuNodeKind.File, "domain", [], true);
        var specsFolder = new DevbookMenuNode("intake/specs", "Specs", "intake/specs", DevbookMenuNodeKind.Folder, "domain", [specsDoc], true);
        var intakeFolder = new DevbookMenuNode("intake", "Intake", "intake", DevbookMenuNodeKind.Folder, "domain", [domainDoc, specsFolder], true);
        var contextMap = new DevbookMenuNode("context-map.md", "Context Map", "context-map.md", DevbookMenuNodeKind.File, "domain", [], true);

        return new DevbookMenuNode("domain", "Domain", "domain", DevbookMenuNodeKind.Folder, "domain", [contextMap, intakeFolder], true);
    }
}
