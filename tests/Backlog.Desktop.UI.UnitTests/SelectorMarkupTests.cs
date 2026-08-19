namespace Backlog.Desktop.UI.UnitTests;

public sealed class SelectorMarkupTests
{
    // The backlog context left Backlog.Desktop.UI for its own project under
    // src/Modules; the namespace came with it, so only the folder moved.
    private static string BacklogUi =>
        RepositoryRoot.Directory("src", "Modules", "Backlog", "Backlog.Modules.Backlog.UI");

    private static string FindBacklogPane() =>
        RepositoryRoot.File("src", "Modules", "Backlog", "Backlog.Modules.Backlog.UI", "BacklogPane.razor");

    /// <summary>
    /// A status badge looks the same whether it is on a backlog entry or an
    /// arc42 chapter; only the words differ, and those arrive as options. So the
    /// selectors belong to the shared library, not to whichever context happened
    /// to need one first.
    /// </summary>
    [Fact]
    public void The_selectors_live_in_the_shared_component_library()
    {
        Assert.True(File.Exists(RepositoryRoot.Combine("src", "Core", "Backlog.UI.Components", "Selects", "StatusSelector.razor")));
        Assert.True(File.Exists(RepositoryRoot.Combine("src", "Core", "Backlog.UI.Components", "Selects", "PrioritySelector.razor")));
        Assert.True(File.Exists(RepositoryRoot.Combine("src", "Core", "Backlog.UI.Components", "Selects", "RepositorySelector.razor")));

        Assert.False(Directory.EnumerateFiles(BacklogUi, "*Selector.razor").Any());
    }

    [Fact]
    public void The_backlog_pane_uses_the_shared_selector_components()
    {
        var pane = NormalizeLineEndings(File.ReadAllText(FindBacklogPane()));

        Assert.Contains("<PrioritySelector", pane, StringComparison.Ordinal);
        Assert.Contains("<RepositorySelector", pane, StringComparison.Ordinal);
        Assert.Contains("<StatusSelector", pane, StringComparison.Ordinal);
    }

    /// <summary>
    /// Second Brain shows a status too, with its own vocabulary. It reaches the
    /// same shared selector the backlog does — through the library, not through
    /// the backlog folder.
    /// </summary>
    [Fact]
    public void Knowledge_panels_use_the_same_shared_status_selector()
    {
        foreach (var panel in new[] { "Arc42KnowledgePanel.razor", "DomainKnowledgePanel.razor", "TechnologyKnowledgePanel.razor" })
        {
            var markup = NormalizeLineEndings(File.ReadAllText(
                RepositoryRoot.File("src", "Modules", "Knowledge", "Backlog.Modules.Knowledge.UI", panel)));

            Assert.Contains("<StatusSelector", markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Backlog.Desktop.UI.BacklogManagement", markup, StringComparison.Ordinal);
        }
    }

    // There was a fact here requiring a second RepositorySelector on the sub-item
    // metadata row. That row is gone: it edited a sub-item's repository by
    // changing the *parent* entry's area, which is not what it looked like it
    // did, and a sub-item has no area of its own to change. The entry-level
    // selector is asserted above and is the only one there should be.

    [Fact]
    public void Backlog_markup_no_longer_branches_on_is_read_only()
    {
        var pane = NormalizeLineEndings(File.ReadAllText(FindBacklogPane()));
        Assert.DoesNotContain("row.IsReadOnly", pane, StringComparison.Ordinal);
    }

    [Fact]
    public void Entry_and_sub_item_titles_use_integrated_collapse_buttons()
    {
        var pane = NormalizeLineEndings(File.ReadAllText(FindBacklogPane()));

        // Both titles are the shared AppButton now, so the test id reaches the
        // DOM through its TestId parameter rather than a literal attribute.
        Assert.Contains("TestId=\"entry-title-button\"", pane, StringComparison.Ordinal);
        Assert.Contains("TestId=\"subitem-title-button\"", pane, StringComparison.Ordinal);
    }

    private static string NormalizeLineEndings(string text) => text.Replace("\r\n", "\n");
}
