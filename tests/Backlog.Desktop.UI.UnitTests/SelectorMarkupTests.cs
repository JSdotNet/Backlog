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

    /// <summary>
    /// Entries and steps are both rows in the shared task list, and the pane writes
    /// neither of their titles itself.
    /// <para>
    /// This replaces a pin on two collapse buttons integrated into the two titles.
    /// Both titles were <c>AppButton</c>s the pane composed, each folding its own
    /// content; both are now <c>TaskItem</c> rows inside a <c>TaskListView</c>, whose
    /// title is the row's own button and whose fold is <c>FoldControl</c>'s. The claim
    /// worth keeping is the one underneath that: the pane renders the library's rows
    /// rather than a title control of its own, for the entries and for the steps
    /// alike.
    /// </para>
    /// </summary>
    [Fact]
    public void Entries_and_steps_are_both_rows_in_the_shared_task_list()
    {
        var pane = NormalizeLineEndings(File.ReadAllText(FindBacklogPane()));

        // Two lists: the entries on the left, the selected entry's steps on the
        // right. Plus the selected entry itself, which wears the same row.
        Assert.Contains("TestId=\"entry-list\"", pane, StringComparison.Ordinal);
        Assert.Contains("TestId=\"subitem-list\"", pane, StringComparison.Ordinal);
        Assert.Contains("<TaskItem", pane, StringComparison.Ordinal);

        Assert.DoesNotContain("entry-title-button", pane, StringComparison.Ordinal);
        Assert.DoesNotContain("subitem-title-button", pane, StringComparison.Ordinal);
        Assert.DoesNotContain("subitem-card__title", pane, StringComparison.Ordinal);
    }

    /// <summary>
    /// The hand-offs are the entry's and there are none on a step.
    /// <para>
    /// This assertion used to say the opposite: that a step's GitHub push and Copilot
    /// hand-off arrived through <c>TaskListView.RowActions</c>. The slot is still the
    /// right answer to "where does a host put its own controls on a row" — that rule
    /// has not changed — but the question was wrong. A step is not a thing this
    /// product files as an issue: <c>.domain/backlog/domain.md</c> gives
    /// <c>ProjectionRef</c> to the entry, and a Sub-Item projects to checkboxes
    /// <em>inside</em> that entry's issue.
    /// </para>
    /// <para>
    /// Asserted on the markup rather than on a render because the failure this catches
    /// is somebody adding the buttons back: a rendered test can only fail on the
    /// entries it happens to build, and this one fails on the source.
    /// </para>
    /// </summary>
    [Fact]
    public void No_step_carries_a_hand_off_of_its_own()
    {
        var pane = NormalizeLineEndings(File.ReadAllText(FindBacklogPane()));

        Assert.DoesNotContain("subitem-github-push-button", pane, StringComparison.Ordinal);
        Assert.DoesNotContain("subitem-copilot-cli-button", pane, StringComparison.Ordinal);

        // And the entry-level pair is still there, named for the whole task.
        Assert.Contains("TestId=\"github-push-button\"", pane, StringComparison.Ordinal);
        Assert.Contains("TestId=\"copilot-cli-button\"", pane, StringComparison.Ordinal);
        Assert.Contains("Hand over the whole entry", pane, StringComparison.Ordinal);
    }

    private static string NormalizeLineEndings(string text) => text.Replace("\r\n", "\n");
}
