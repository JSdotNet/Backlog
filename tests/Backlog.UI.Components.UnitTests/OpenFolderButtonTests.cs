using Backlog.UI.Components.Menus;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The "open in VS Code" affordance, and which components offer it. One control
/// used in two headers — a second implementation is how the storybook ends up
/// showing the button nobody renders.
/// </summary>
public sealed class OpenFolderButtonTests
{
    private static readonly IReadOnlyList<FolderEntry> Tree =
    [
        FolderEntry.File("index.md", "Index", 1_204)
    ];

    private static IRenderedComponent<T> Render<T>(BunitContext context, Action<ComponentParameterCollectionBuilder<T>> parameters)
        where T : IComponent
    {
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context.Render(parameters);
    }

    [Fact]
    public void The_mark_is_drawn_and_the_name_is_words()
    {
        // A screen reader hears the words; the icon is decoration on top of them.
        using var context = new BunitContext();

        var view = Render<OpenFolderButton>(context, p => p.Add(b => b.Label, "Open .arc42 in VS Code"));
        var button = view.Find("button");

        Assert.Equal("Open .arc42 in VS Code", button.GetAttribute("aria-label"));
        Assert.Equal("Open .arc42 in VS Code", button.GetAttribute("title"));

        var glyph = view.Find("svg");
        Assert.Equal("true", glyph.GetAttribute("aria-hidden"));

        // Drawn, not fetched: a button that needs the network to show itself
        // does not work in a WebView with no egress.
        Assert.Empty(view.FindAll("img"));
        Assert.NotEmpty(view.FindAll("svg path"));
    }

    [Fact]
    public void Busy_disables_it_without_taking_it_off_the_screen()
    {
        using var context = new BunitContext();

        var view = Render<OpenFolderButton>(context, p => p.Add(b => b.Busy, true));
        var button = view.Find("button");

        Assert.True(button.HasAttribute("disabled"));
        Assert.NotNull(view.Find(".spinner, .open-folder__spinner"));
    }

    [Fact]
    public void FolderView_offers_it_only_when_something_is_listening()
    {
        using var context = new BunitContext();

        var without = Render<FolderView>(context, p => p
            .Add(v => v.Name, ".arc42")
            .Add(v => v.Entries, Tree));

        Assert.Empty(without.FindAll(".folder-view__open"));

        var with = Render<FolderView>(context, p => p
            .Add(v => v.Name, ".arc42")
            .Add(v => v.Entries, Tree)
            .Add(v => v.OnOpenFolder, () => { }));

        Assert.Single(with.FindAll(".folder-view__open"));
    }

    [Fact]
    public void FolderViews_button_names_the_folder_and_raises_the_callback()
    {
        using var context = new BunitContext();
        var opened = false;

        var view = Render<FolderView>(context, p => p
            .Add(v => v.Name, ".tech")
            .Add(v => v.Entries, Tree)
            .Add(v => v.OnOpenFolder, () => opened = true)
            .Add(v => v.TestId, "folder"));

        var button = view.Find("[data-testid='folder-open']");
        Assert.Equal("Open .tech in VS Code", button.GetAttribute("aria-label"));

        button.Click();
        Assert.True(opened);
    }

    [Fact]
    public void A_failed_open_is_said_rather_than_left_to_be_noticed()
    {
        // Opening leaves the app entirely, so it fails in ways nothing here can
        // prevent — and the reader is otherwise watching nothing happen.
        using var context = new BunitContext();

        var view = Render<FolderView>(context, p => p
            .Add(v => v.Name, ".tech")
            .Add(v => v.Entries, Tree)
            .Add(v => v.OnOpenFolder, () => { })
            .Add(v => v.OpenErrorMessage, "VS Code is not installed.")
            .Add(v => v.TestId, "folder"));

        Assert.Equal("VS Code is not installed.", view.Find("[data-testid='folder-open-error']").TextContent);
    }

    [Fact]
    public void A_tree_that_is_only_a_tree_does_not_offer_to_open_anything()
    {
        // The Menus chapter's sample: rows with nowhere on disk behind them.
        using var context = new BunitContext();

        var view = Render<TreeView>(context, p => p
            .Add(t => t.Nodes, new[] { TreeNode.Leaf("a", "A row") })
            .Add(t => t.RootFolder, new TreeNode("root", "Chapters", TreeNodeKind.Folder))
            .Add(t => t.ShowOpenButton, false));

        Assert.Empty(view.FindAll("button.knowledge-menu__open-vscode"));
        Assert.Single(view.FindAll("[role='treeitem']"));
    }

    [Fact]
    public void A_tree_that_does_stand_for_a_folder_still_offers_it()
    {
        // The desktop's knowledge menu, unchanged: the button keeps the class its
        // stylesheet already knows.
        using var context = new BunitContext();

        var view = Render<TreeView>(context, p => p
            .Add(t => t.Nodes, new[] { TreeNode.Leaf("a", "A row") })
            .Add(t => t.RootFolder, new TreeNode("root", ".arc42", TreeNodeKind.Folder))
            .Add(t => t.OpenButtonTestId, "tree-open"));

        var button = view.Find("[data-testid='tree-open']");

        Assert.Contains("knowledge-menu__open-vscode", button.ClassList);
        Assert.Equal("Open .arc42 in VS Code", button.GetAttribute("aria-label"));
    }
}
