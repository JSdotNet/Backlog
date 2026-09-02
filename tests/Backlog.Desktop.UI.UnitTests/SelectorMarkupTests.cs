namespace Backlog.Desktop.UI.UnitTests;

public sealed class SelectorMarkupTests
{
    // The backlog context left Backlog.Desktop.UI for its own project under
    // src/Modules; the namespace came with it, so only the folder moved.
    private static string BacklogUi =>
        RepositoryRoot.Directory("src", "Modules", "Tasks", "Backlog.Modules.Tasks.UI");

    private static string FindTasksPane() =>
        RepositoryRoot.File("src", "Modules", "Tasks", "Backlog.Modules.Tasks.UI", "TasksPane.razor");

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

    /// <summary>
    /// The pane reaches the library for the selectors it draws.
    /// <para>
    /// <c>PrioritySelector</c> is not among them any more, and that is a move
    /// rather than a hand-roll: priority became a row in the detail pane's Ranking
    /// group, where the picker behind it is the same shared <c>SelectField</c> the
    /// repeat row opens. A ranking read as one more label on a line of labels, and
    /// the pane's job is to keep "how much this matters" away from "when it is
    /// due". The component itself is still the library's and still rendered — the
    /// storybook and the Roadmap editor both use it.
    /// </para>
    /// </summary>
    [Fact]
    public void The_backlog_pane_uses_the_shared_selector_components()
    {
        var pane = NormalizeLineEndings(File.ReadAllText(FindTasksPane()));

        Assert.Contains("<RepositorySelector", pane, StringComparison.Ordinal);
        Assert.Contains("<StatusSelector", pane, StringComparison.Ordinal);

        // The ranking's picker, which is the same shared field every other row in
        // the pane opens.
        Assert.Contains("TestId=\"entry-priority-select\"", pane, StringComparison.Ordinal);
    }

    /// <summary>
    /// Second Brain shows a status too, with its own vocabulary. It reaches the
    /// same shared selector the backlog does — through the library, not through
    /// the backlog folder.
    /// <para>
    /// Two of the three panels draw a selector of their own directly: Technology on
    /// a node, Domain beside a section while its body is being edited. The third,
    /// arc42, no longer draws one at all — it hands the whole status to the shared
    /// <c>FileView</c> through <c>RenderKnowledgeMetadata</c>, which is the same
    /// shared selector reached one control further in. That is the stronger form of
    /// the rule this test exists for, not an exception to it: a panel that draws no
    /// selector of its own cannot draw a hand-rolled one. So arc42 is pinned to the
    /// shared file view instead, and all three are pinned against reaching into the
    /// backlog folder for a selector.
    /// </para>
    /// </summary>
    [Fact]
    public void Knowledge_panels_use_the_same_shared_status_selector()
    {
        foreach (var panel in new[] { "Arc42KnowledgePanel.razor", "DomainKnowledgePanel.razor", "TechnologyKnowledgePanel.razor" })
        {
            var markup = NormalizeLineEndings(File.ReadAllText(
                RepositoryRoot.File("src", "Modules", "Knowledge", "Backlog.Modules.Knowledge.UI", panel)));

            // The status is a shared control either way: drawn directly by the two
            // panels that still keep a selector of their own, and reached through
            // the shared file view's knowledge metadata by arc42, which keeps none.
            Assert.True(
                markup.Contains("<StatusSelector", StringComparison.Ordinal)
                    || markup.Contains("RenderKnowledgeMetadata=\"true\"", StringComparison.Ordinal),
                $"{panel} draws its status through neither the shared selector nor the shared file view.");

            Assert.DoesNotContain("Backlog.Desktop.UI.Tasks", markup, StringComparison.Ordinal);
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
        var pane = NormalizeLineEndings(File.ReadAllText(FindTasksPane()));
        Assert.DoesNotContain("row.IsReadOnly", pane, StringComparison.Ordinal);
    }

    /// <summary>
    /// Entries and steps are both rows in the shared task list, the open entry is the
    /// shared panel, and the pane writes none of their titles itself.
    /// <para>
    /// This replaces a pin on two collapse buttons integrated into the two titles.
    /// Both titles were <c>AppButton</c>s the pane composed, each folding its own
    /// content; both became <c>TaskItem</c> rows inside a <c>TaskListView</c>, whose
    /// title is the row's own button and whose fold is <c>FoldControl</c>'s. The claim
    /// worth keeping is the one underneath that: the pane renders the library's
    /// controls rather than title controls of its own.
    /// </para>
    /// <para>
    /// The open entry no longer wears a row at all. It is <c>TaskPanel</c>, whose
    /// heading is an <c>h2</c> and whose parts are slots — which is why the
    /// <c>ul</c> of one row and the <c>TaskItem</c> inside it are both gone from
    /// this file. A panel is not a list of one, and calling it one put an <c>li</c>
    /// where the pane's only heading should have been.
    /// </para>
    /// </summary>
    [Fact]
    public void Entries_and_steps_are_rows_and_the_open_entry_is_the_shared_panel()
    {
        var pane = NormalizeLineEndings(File.ReadAllText(FindTasksPane()));

        // Two lists: the entries on the left, the selected entry's steps on the
        // right. The entry that is open is the panel they are beside.
        Assert.Contains("TestId=\"entry-list\"", pane, StringComparison.Ordinal);
        Assert.Contains("TestId=\"subitem-list\"", pane, StringComparison.Ordinal);
        Assert.Contains("<TaskPanel", pane, StringComparison.Ordinal);

        // No row for the open entry, and so no list to put one in.
        Assert.DoesNotContain("<TaskItem", pane, StringComparison.Ordinal);
        Assert.DoesNotContain("entry-detail__row", pane, StringComparison.Ordinal);

        Assert.DoesNotContain("entry-title-button", pane, StringComparison.Ordinal);
        Assert.DoesNotContain("subitem-title-button", pane, StringComparison.Ordinal);
        Assert.DoesNotContain("subitem-card__title", pane, StringComparison.Ordinal);
    }

    /// <summary>
    /// The scheduling rows are laid out by <c>TaskActionPane</c>, not stacked by the
    /// pane itself.
    /// <para>
    /// They were a <c>div</c> with a <c>role="group"</c> and five <c>TaskAction</c>s
    /// down it, which is the shape the library grew <c>TaskActionPane</c> and
    /// <c>TaskActionGroup</c> to replace: one lead row for the decision somebody
    /// makes again every morning, and the facts about the entry captioned and
    /// balanced across columns.
    /// </para>
    /// </summary>
    [Fact]
    public void The_scheduling_rows_are_laid_out_by_the_shared_action_pane()
    {
        var pane = NormalizeLineEndings(File.ReadAllText(FindTasksPane()));

        Assert.Contains("<TaskActionPane", pane, StringComparison.Ordinal);
        Assert.Contains("<TaskActionGroup", pane, StringComparison.Ordinal);

        // The hand-rolled group the pane used to stack them in.
        Assert.DoesNotContain("aria-label=\"Scheduling and dependencies\"", pane, StringComparison.Ordinal);
    }

    /// <summary>
    /// The hand-offs are the entry's and there are none on a step.
    /// <para>
    /// This assertion used to say the opposite: that a step's GitHub push and Copilot
    /// hand-off arrived through <c>TaskListView.RowActions</c>. The slot is still the
    /// right answer to "where does a host put its own controls on a row" — that rule
    /// has not changed — but the question was wrong. A step is not a thing this
    /// product files as an issue: <c>.domain/tasks/domain.md</c> gives
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
        var pane = NormalizeLineEndings(File.ReadAllText(FindTasksPane()));

        Assert.DoesNotContain("subitem-github-push-button", pane, StringComparison.Ordinal);
        Assert.DoesNotContain("subitem-copilot-cli-button", pane, StringComparison.Ordinal);

        // And the entry-level pair is still there, named for the whole task.
        Assert.Contains("TestId=\"github-push-button\"", pane, StringComparison.Ordinal);
        Assert.Contains("TestId=\"copilot-cli-button\"", pane, StringComparison.Ordinal);
        Assert.Contains("Hand over the whole entry", pane, StringComparison.Ordinal);
    }

    private static string NormalizeLineEndings(string text) => text.Replace("\r\n", "\n");
}
