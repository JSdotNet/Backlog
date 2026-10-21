namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The "C4 views" list on the architecture surface's left navigation: every view of
/// the workspace once, the zoom chain first and indented by level, the counts being
/// what each level opens onto, then the views beside the chain.
/// </summary>
public sealed class C4ViewIndexTests
{
    private static C4Workspace Workspace => C4ZoomNavigationTests.Workspace;

    [Fact]
    public void The_chain_comes_first_indented_by_level_then_dynamic_and_deployment()
    {
        var entries = C4ViewIndex.For(Workspace);

        Assert.Equal(
            ["landscape", "context-backlog", "containers-backlog", "components-desktop", "capture-to-issue", "deployment-local"],
            entries.Select(entry => entry.ViewKey));
        Assert.Equal([0, 0, 1, 2, 0, 0], entries.Select(entry => entry.Depth));
        Assert.Equal(["System landscape", "System context", "Containers", "Desktop App", "Capture to issue", "Local machine"], entries.Select(entry => entry.Label));
    }

    [Fact]
    public void A_level_counts_what_it_opens_onto_and_a_view_beside_the_chain_says_its_kind()
    {
        var entries = C4ViewIndex.For(Workspace).ToDictionary(entry => entry.ViewKey);

        Assert.Equal("2", entries["containers-backlog"].Detail);
        Assert.Equal("2", entries["components-desktop"].Detail);
        Assert.Null(entries["context-backlog"].Detail);
        Assert.Equal("dynamic", entries["capture-to-issue"].Detail);
        Assert.Equal("deploy", entries["deployment-local"].Detail);
    }

    [Fact]
    public void A_component_with_code_gets_a_code_line_under_its_components_view()
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

    /// <summary>Two context views of one system both lead to its containers: the
    /// containers view is listed once, so every line has its own view.</summary>
    [Fact]
    public void A_view_two_contexts_lead_to_is_listed_once_and_two_container_views_are_told_apart()
    {
        var context = Workspace.View("context-backlog")!;
        var containers = Workspace.View("containers-backlog")!;
        var workspace = Workspace with
        {
            Views = [.. Workspace.Views, context with { Key = "context-again", Title = "Context again" }, containers with { Key = "containers-again", Title = "Containers again" }]
        };

        var entries = C4ViewIndex.For(workspace);

        Assert.Equal(entries.Count, entries.Select(entry => (entry.ViewKey, entry.CodeComponentId)).Distinct().Count());
        Assert.Contains(entries, entry => entry.Label == "Containers again");
        Assert.Contains(entries, entry => entry.ViewKey == "containers-backlog" && entry.Label == "Containers");
    }
}
