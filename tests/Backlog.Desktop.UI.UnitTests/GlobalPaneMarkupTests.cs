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

    [Fact]
    public void Expandable_entry_title_uses_an_integrated_collapse_button()
    {
        var pane = NormalizeLineEndings(File.ReadAllText(FindBacklogPane()));

        Assert.Contains("TestId=\"entry-title-button\"", pane, StringComparison.Ordinal);
        Assert.DoesNotContain("entry-fold-button", pane, StringComparison.Ordinal);
    }

    [Fact]
    public void Entry_metadata_keeps_focus_events_out_of_read_view_editor()
    {
        var pane = NormalizeLineEndings(File.ReadAllText(FindBacklogPane()));

        // The entry's own metadata row only. A sub-item no longer has a metadata
        // row to keep focus out of: it carries a title, a status, notes and an
        // order, and none of those is edited through a badge.
        Assert.Contains("class=\"entry-doc__meta\" @onmousedown:stopPropagation=\"true\" @onclick:stopPropagation=\"true\"", pane, StringComparison.Ordinal);
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

    [Fact]
    public void Title_line_css_aligns_title_and_metadata_without_wrapping_controls()
    {
        var css = NormalizeLineEndings(File.ReadAllText(FindAppCss()));

        Assert.Contains(".entry-doc__title-line .entry-doc__title {\n    flex: 1 1 auto;\n    margin: 0;", css, StringComparison.Ordinal);
        Assert.Contains(".entry-doc:not(.entry-doc--one-line) .entry-doc__title-line .entry-doc__meta-start,\n.entry-doc:not(.entry-doc--one-line) .entry-doc__title-line .entry-doc__meta-end {\n    flex-wrap: nowrap;\n}", css, StringComparison.Ordinal);
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
