namespace Backlog.Desktop.UI.UnitTests;

public sealed class GlobalPaneMarkupTests
{
    [Fact]
    public void Home_shell_exposes_global_pane_multiselect_and_sections()
    {
        var home = NormalizeLineEndings(File.ReadAllText(FindHomeRazor()));

        Assert.Contains("data-testid=\"global-pane-multiselect\"", home, StringComparison.Ordinal);

        // The three options are the shared ToggleButton, so their test ids reach
        // the DOM through its TestId parameter rather than literal attributes.
        Assert.Contains("TestId=\"inbox-pane-option\"", home, StringComparison.Ordinal);
        Assert.Contains("TestId=\"backlog-pane-option\"", home, StringComparison.Ordinal);
        Assert.Contains("TestId=\"knowledge-pane-option\"", home, StringComparison.Ordinal);

        // Each pane carries its own landmark id from its own folder; the shell
        // only points the multiselect's aria-controls at them.
        Assert.Contains("id=\"inbox-pane\"", NormalizeLineEndings(File.ReadAllText(FindInboxPane())), StringComparison.Ordinal);
        Assert.Contains("id=\"backlog-pane\"", NormalizeLineEndings(File.ReadAllText(FindBacklogPane())), StringComparison.Ordinal);
        Assert.Contains("id=\"repository-knowledge-pane\"", NormalizeLineEndings(File.ReadAllText(FindKnowledgePane())), StringComparison.Ordinal);
    }

    [Fact]
    public void Inbox_option_and_pane_are_guarded_by_feature_flag()
    {
        var home = NormalizeLineEndings(File.ReadAllText(FindHomeRazor()));

        Assert.Contains("@if (InboxPaneOptionVisible)", home, StringComparison.Ordinal);
        Assert.Contains("AppFeatures.InboxPane", home, StringComparison.Ordinal);
        Assert.Contains("_globalPanes.TrySetAvailable(GlobalPane.Inbox, InboxPaneOptionVisible);", home, StringComparison.Ordinal);
    }

    [Fact]
    public void Pane_multiselect_uses_selected_state_and_capacity_aware_disabling()
    {
        var home = NormalizeLineEndings(File.ReadAllText(FindHomeRazor()));

        // ToggleButton derives aria-pressed from Pressed, so the visibility of a
        // pane is stated once and the attribute cannot drift away from it.
        Assert.Contains("Pressed=\"InboxPaneVisible\"", home, StringComparison.Ordinal);
        Assert.Contains("Pressed=\"BacklogPaneVisible\"", home, StringComparison.Ordinal);
        Assert.Contains("Pressed=\"KnowledgePaneVisible\"", home, StringComparison.Ordinal);

        Assert.Contains("if (_globalPanes.IsEnabled(pane))", home, StringComparison.Ordinal);
        Assert.Contains("return !_globalPanes.CanDisable(pane);", home, StringComparison.Ordinal);
        Assert.Contains("return !_globalPanes.CanEnable(pane);", home, StringComparison.Ordinal);

        Assert.DoesNotContain("Show inbox", home, StringComparison.Ordinal);
        Assert.DoesNotContain("Hide inbox", home, StringComparison.Ordinal);
        Assert.DoesNotContain("Show backlog", home, StringComparison.Ordinal);
        Assert.DoesNotContain("Hide backlog", home, StringComparison.Ordinal);
        Assert.DoesNotContain("Show knowledge", home, StringComparison.Ordinal);
        Assert.DoesNotContain("Hide knowledge", home, StringComparison.Ordinal);
    }

    [Fact]
    public void Home_receives_viewport_pane_capacity_from_javascript()
    {
        var home = NormalizeLineEndings(File.ReadAllText(FindHomeRazor()));
        // The resizer moved into the shared component library; Home still owns
        // the callback it reports into.
        var componentsJs = NormalizeLineEndings(File.ReadAllText(FindComponentsJs()));

        Assert.Contains("public Task SetGlobalPaneCapacityAsync(int capacity)", home, StringComparison.Ordinal);
        Assert.Contains("backlogPaneOwner.invokeMethodAsync('SetGlobalPaneCapacityAsync', backlogPaneCapacity());", componentsJs, StringComparison.Ordinal);
        Assert.Contains("const BACKLOG_SINGLE_PANE_MAX_REM = 72;", componentsJs, StringComparison.Ordinal);
        Assert.Contains("const BACKLOG_THREE_PANE_MIN_REM = 96;", componentsJs, StringComparison.Ordinal);
    }

    [Fact]
    public void Side_layout_opens_split_only_when_backlog_and_side_panes_are_both_visible()
    {
        var home = NormalizeLineEndings(File.ReadAllText(FindHomeRazor()));

        Assert.Contains("(BacklogPaneVisible && RightSidePaneVisible) ? \"knowledge-layout--side-open\"", home, StringComparison.Ordinal);
        Assert.Contains("side-pane-stack--full", home, StringComparison.Ordinal);
        Assert.Contains("ToolsVisible ? \"side-pane-stack--right-docked\"", home, StringComparison.Ordinal);
    }

    [Fact]
    public void Inbox_renders_before_backlog_when_both_are_visible()
    {
        var home = NormalizeLineEndings(File.ReadAllText(FindHomeRazor()));

        Assert.Contains("@if (InboxBeforeBacklogVisible)", home, StringComparison.Ordinal);
        Assert.Contains("@if (!BacklogPaneVisible && InboxPaneVisible)", home, StringComparison.Ordinal);
        Assert.Contains("knowledge-layout--inbox-before-backlog", home, StringComparison.Ordinal);

        var inboxGuardIndex = home.IndexOf("@if (InboxBeforeBacklogVisible)", StringComparison.Ordinal);
        var backlogPaneIndex = home.IndexOf("<BacklogPane />", StringComparison.Ordinal);

        Assert.True(inboxGuardIndex >= 0);
        Assert.True(backlogPaneIndex > inboxGuardIndex);
    }

    [Fact]
    public void App_version_opens_a_separate_update_window()
    {
        var home = NormalizeLineEndings(File.ReadAllText(FindHomeRazor()));

        Assert.Contains("TestId=\"app-version\"", home, StringComparison.Ordinal);
        Assert.Contains("OnClick=\"OpenUpdateWindow\"", home, StringComparison.Ordinal);
        // The dialog shell is now the shared Modal component, so the test id
        // reaches the DOM through its TestId parameter instead of a literal
        // attribute.
        Assert.Contains("TestId=\"app-update-dialog\"", home, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"check-for-updates\"", home, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"install-update\"", home, StringComparison.Ordinal);
        Assert.DoesNotContain("app-version__hint", home, StringComparison.Ordinal);
    }

    [Fact]
    public void Knowledge_folder_errors_do_not_use_empty_razor_fragment_tags()
    {
        var home = NormalizeLineEndings(File.ReadAllText(FindHomeRazor()));

        Assert.DoesNotContain("@<>", home, StringComparison.Ordinal);
        Assert.DoesNotContain("</>", home, StringComparison.Ordinal);
    }

    /// <summary>
    /// The entry list is the shared task list, and which row is open is the pane's
    /// to say.
    /// <para>
    /// This replaces a fact about a fold button on the entry title. There is no fold
    /// left to press: a row in the list is one line and the expansion is the detail
    /// pane beside it, so what used to be "the title integrates its own collapse
    /// button" is now "the title is a row in <c>TaskListView</c>, and the pane hands
    /// it <c>SelectedId</c>". The storybook says why the row cannot decide that for
    /// itself — which one is open is a fact about the pane, not about the row.
    /// </para>
    /// </summary>
    [Fact]
    public void The_entry_list_is_the_shared_task_list_and_the_pane_owns_the_selection()
    {
        var pane = NormalizeLineEndings(File.ReadAllText(FindBacklogPane()));

        Assert.Contains("<TaskListView", pane, StringComparison.Ordinal);
        Assert.Contains("SelectedId=\"@SelectedTaskId\"", pane, StringComparison.Ordinal);
        Assert.Contains("OnSelected=\"OnEntrySelectedAsync\"", pane, StringComparison.Ordinal);
        Assert.Contains("TestId=\"entry-list\"", pane, StringComparison.Ordinal);

        // No fold of the pane's own came back beside the list.
        Assert.DoesNotContain("entry-fold-button", pane, StringComparison.Ordinal);
        Assert.DoesNotContain("entry-title-button", pane, StringComparison.Ordinal);
        Assert.DoesNotContain("ToggleEntry", pane, StringComparison.Ordinal);
    }

    /// <summary>
    /// There is no click-to-edit surface for a badge to have to keep its clicks away
    /// from.
    /// <para>
    /// This replaces the pin on the metadata row's <c>stopPropagation</c> pair. Those
    /// existed because the badges sat inside a read view that opened the raw editor
    /// on click and on Enter, so a status change that reached the card swapped the
    /// entry for a textarea mid-edit. The read view is gone: the pane is opened by
    /// selecting a row, and the source is a toggle of its own. Guarding the same
    /// intent now means asserting the surface has not come back, because a
    /// propagation stop is only ever a fix for one.
    /// </para>
    /// </summary>
    [Fact]
    public void No_control_sits_inside_a_surface_that_opens_an_editor_on_click()
    {
        var pane = NormalizeLineEndings(File.ReadAllText(FindBacklogPane()));

        // Quoted, because `entry-doc__reading` under the escape hatch is a prefix of
        // it and is a different thing: a hint about what the source parses to, not a
        // surface that opens an editor.
        Assert.DoesNotContain("\"entry-doc__read\"", pane, StringComparison.Ordinal);
        Assert.DoesNotContain("entry-read-view", pane, StringComparison.Ordinal);
        Assert.DoesNotContain("State.BeginEdit", pane, StringComparison.Ordinal);

        // The source is reached deliberately instead — a toggle, and the shortcut
        // .design/content-editing.md#raw-markdown-escape-hatch asks for.
        Assert.Contains("TestId=\"entry-raw-toggle\"", pane, StringComparison.Ordinal);
        Assert.Contains("Ctrl+Shift+M", pane, StringComparison.Ordinal);
    }

    /// <summary>
    /// The shell composes the three contexts; it does not render them. If a pane's
    /// own markup starts leaking back into Home.razor, the folder split has
    /// stopped meaning anything.
    /// </summary>
    [Fact]
    public void The_shell_composes_the_panes_rather_than_rendering_them()
    {
        var home = NormalizeLineEndings(File.ReadAllText(FindHomeRazor()));

        Assert.Contains("<InboxPane Items=", home, StringComparison.Ordinal);
        Assert.Contains("<BacklogPane />", home, StringComparison.Ordinal);
        Assert.Contains("<KnowledgePane RepositoryAlias=", home, StringComparison.Ordinal);

        Assert.DoesNotContain("entry-doc__meta", home, StringComparison.Ordinal);
        Assert.DoesNotContain("inbox-pane__list", home, StringComparison.Ordinal);
        Assert.DoesNotContain("knowledge-stack__nav", home, StringComparison.Ordinal);
    }

    /// <summary>
    /// The two halves of the split scroll on their own, and the CSS the entry card
    /// needed is gone rather than left behind.
    /// <para>
    /// This replaces a pin on how the entry title line laid its metadata out without
    /// wrapping. There is no title line: a row in the list is the shared task row and
    /// the metadata strip only ever appears in the pane beside it, at one width. What
    /// is worth pinning about the new layout is the thing that would be wrong if
    /// somebody simplified it — one scrollbar for both halves, which would mean
    /// scrolling the list to reach the bottom of the entry next to it.
    /// </para>
    /// </summary>
    [Fact]
    public void Each_half_of_the_backlog_split_scrolls_on_its_own()
    {
        var css = NormalizeLineEndings(File.ReadAllText(FindAppCss()));

        Assert.Contains(".backlog-list {", css, StringComparison.Ordinal);
        Assert.Contains(".entry-detail {", css, StringComparison.Ordinal);

        foreach (var block in new[] { ".backlog-list {", ".entry-detail {" })
        {
            var start = css.IndexOf(block, StringComparison.Ordinal);
            var rules = css[start..css.IndexOf('}', start)];

            Assert.Contains("overflow-y: auto;", rules, StringComparison.Ordinal);
            Assert.Contains("min-height: 0;", rules, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The entry card, its four grab rails, its drop zones and the sub-item cards
    /// are gone from the stylesheet as well as from the markup.
    /// <para>
    /// Dead CSS is not harmless: it is the next reader's evidence that a shape still
    /// exists. Every selector below styled something the shared components now draw,
    /// and a rule for it surviving would describe a card nobody renders.
    /// </para>
    /// </summary>
    [Fact]
    public void The_replaced_entry_card_css_was_removed_rather_than_left_behind()
    {
        var css = NormalizeLineEndings(File.ReadAllText(FindAppCss()));

        foreach (var dead in new[]
                 {
                     ".entry-list {",
                     ".entry-group {",
                     ".entry-doc {",
                     ".entry-doc--one-line",
                     ".entry-doc__grip",
                     ".entry-doc__drop",
                     ".entry-doc__read {",
                     ".entry-doc__title-line",
                     ".subitem-card",
                     ".subitem-list"
                 })
        {
            Assert.DoesNotContain(dead, css, StringComparison.Ordinal);
        }
    }

    private static string NormalizeLineEndings(string text) => text.Replace("\r\n", "\n");

    private static string FindAppCss() => RepositoryRoot.File("src", "App", "Backlog.Desktop.UI", "wwwroot", "app.css");

    private static string FindHomeRazor() => RepositoryRoot.File("src", "App", "Backlog.Desktop.UI", "Shell", "Home.razor");

    // The three bounded contexts left Backlog.Desktop.UI and became their own
    // projects under src/Modules; only the shell's own chrome stayed behind.
    private static string FindInboxPane() => RepositoryRoot.File("src", "Modules", "Inbox", "Backlog.Modules.Inbox.UI", "InboxPane.razor");

    private static string FindBacklogPane() => RepositoryRoot.File("src", "Modules", "Backlog", "Backlog.Modules.Backlog.UI", "BacklogPane.razor");

    private static string FindKnowledgePane() => RepositoryRoot.File("src", "Modules", "Knowledge", "Backlog.Modules.Knowledge.UI", "KnowledgePane.razor");

    private static string FindAppJs() => RepositoryRoot.File("src", "App", "Backlog.Desktop.UI", "wwwroot", "app.js");

    private static string FindComponentsJs() => RepositoryRoot.File("src", "Core", "Backlog.UI.Components", "wwwroot", "components.js");
}
