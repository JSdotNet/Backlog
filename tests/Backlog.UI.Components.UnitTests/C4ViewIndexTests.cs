namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The views list on the architecture surface's left navigation: the landscape and the
/// zoom chain one line per level, named by level and indented one step per level, then
/// the flows, the deployments, and any second view of a level apart under Other views.
/// </summary>
public sealed class C4ViewIndexTests
{
    private static C4Workspace Workspace => C4ZoomNavigationTests.Workspace;

    [Fact]
    public void The_chain_reads_one_line_per_level_named_by_level_and_indented_per_level()
    {
        var entries = C4ViewIndex.For(Workspace);

        Assert.Equal(
            ["landscape", "context-backlog", "containers-backlog", "components-desktop", "capture-to-issue", "deployment-local"],
            entries.Select(entry => entry.ViewKey));
        Assert.Equal(["System landscape", "System context", "Containers", "Desktop App", "Capture to issue", "Local machine"], entries.Select(entry => entry.Label));
        Assert.Equal([0, 0, 1, 2, 0, 0], entries.Select(entry => entry.Depth));
        Assert.Equal(
            [C4ViewIndexGroup.Model, C4ViewIndexGroup.Model, C4ViewIndexGroup.Model, C4ViewIndexGroup.Model, C4ViewIndexGroup.Flows, C4ViewIndexGroup.Deployment],
            entries.Select(entry => entry.Group));
    }

    [Fact]
    public void A_line_keeps_the_views_full_title_for_its_tooltip()
    {
        var entries = C4ViewIndex.For(Workspace).ToDictionary(entry => entry.ViewKey);

        Assert.Equal("Containers", entries["containers-backlog"].Title);
        Assert.Equal("Desktop App components", entries["components-desktop"].Title);
        Assert.Equal("Everything", entries["landscape"].Title);
    }

    [Fact]
    public void A_level_counts_what_it_opens_onto()
    {
        var entries = C4ViewIndex.For(Workspace).ToDictionary(entry => entry.ViewKey);

        Assert.Equal("2", entries["containers-backlog"].Detail);
        Assert.Equal("2", entries["components-desktop"].Detail);
        Assert.Null(entries["context-backlog"].Detail);
        Assert.Null(entries["capture-to-issue"].Detail);
        Assert.Null(entries["deployment-local"].Detail);
    }

    [Fact]
    public void A_component_with_code_gets_a_code_line_one_step_under_its_container()
    {
        var entries = C4ViewIndex.For(Workspace, id => id == "backlog.desktop.devbook");

        var code = Assert.Single(entries, entry => entry.CodeComponentId is not null);
        Assert.Equal("Devbook", code.Label);
        Assert.Equal("code", code.Detail);
        Assert.Equal(3, code.Depth);
        Assert.Equal("components-desktop", code.ViewKey);
        Assert.Equal(entries.ToList().FindIndex(entry => entry.ViewKey == "components-desktop" && entry.CodeComponentId is null) + 1, entries.ToList().IndexOf(code));
    }

    [Fact]
    public void Without_a_code_source_no_code_line_is_listed()
    {
        Assert.DoesNotContain(C4ViewIndex.For(Workspace), entry => entry.CodeComponentId is not null);
    }

    [Fact]
    public void A_line_is_open_on_its_view_and_a_code_line_only_on_its_code_level()
    {
        var entries = C4ViewIndex.For(Workspace, id => id == "backlog.desktop.devbook");
        var components = entries.Single(entry => entry.ViewKey == "components-desktop" && entry.CodeComponentId is null);
        var code = entries.Single(entry => entry.CodeComponentId is not null);

        Assert.True(components.IsOpen("components-desktop", null));
        Assert.False(code.IsOpen("components-desktop", null));
        Assert.False(components.IsOpen("components-desktop", "backlog.desktop.devbook"));
        Assert.True(code.IsOpen("components-desktop", "backlog.desktop.devbook"));
    }

    /// <summary>
    /// A second context and a second containers view of the same system — what
    /// devbook 1.28.0's migration left beside the authored ones in this repository's
    /// own workspace: the chain keeps one line per level, the view the zoom bar opens,
    /// and the others are listed once each under Other views by their own title.
    /// </summary>
    [Fact]
    public void A_second_view_of_a_level_is_listed_once_apart_from_the_chain()
    {
        var context = Workspace.View("context-backlog")!;
        var containers = Workspace.View("containers-backlog")!;
        var workspace = Workspace with
        {
            Views = [.. Workspace.Views, context with { Key = "context-zalt", Title = "Context again" }, containers with { Key = "containers-zalt", Title = "Containers again" }]
        };

        var entries = C4ViewIndex.For(workspace);

        Assert.Equal(entries.Count, entries.Select(entry => (entry.ViewKey, entry.CodeComponentId)).Distinct().Count());
        Assert.Single(entries, entry => entry.Label == "System context");
        Assert.Single(entries, entry => entry.Label == "Containers");

        var others = entries.Where(entry => entry.Group == C4ViewIndexGroup.Other).ToList();
        Assert.Equal(["Containers again", "Context again"], others.Select(entry => entry.Label).Order());
        Assert.All(others, entry => Assert.Equal(0, entry.Depth));
    }
}
