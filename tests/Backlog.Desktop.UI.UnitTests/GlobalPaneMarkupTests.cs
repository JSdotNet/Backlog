namespace Backlog.Desktop.UI.UnitTests;

public sealed class GlobalPaneMarkupTests
{
    [Fact]
    public void Home_shell_exposes_global_pane_multiselect_and_sections()
    {
        var home = NormalizeLineEndings(File.ReadAllText(FindHomeRazor()));

        Assert.Contains("data-testid=\"global-pane-multiselect\"", home, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"inbox-pane-option\"", home, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"backlog-pane-option\"", home, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"knowledge-pane-option\"", home, StringComparison.Ordinal);

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
        Assert.Contains("AppFeatureSettingsStore.InboxPane", home, StringComparison.Ordinal);
        Assert.Contains("_globalPanes.TrySetAvailable(GlobalPane.Inbox, InboxPaneOptionVisible);", home, StringComparison.Ordinal);
    }

    [Fact]
    public void Pane_multiselect_uses_selected_state_and_capacity_aware_disabling()
    {
        var home = NormalizeLineEndings(File.ReadAllText(FindHomeRazor()));

        Assert.Contains("aria-pressed=\"@(InboxPaneVisible ? \"true\" : \"false\")\"", home, StringComparison.Ordinal);
        Assert.Contains("aria-pressed=\"@(BacklogPaneVisible ? \"true\" : \"false\")\"", home, StringComparison.Ordinal);
        Assert.Contains("aria-pressed=\"@(KnowledgePaneVisible ? \"true\" : \"false\")\"", home, StringComparison.Ordinal);

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

        Assert.Contains("data-testid=\"app-version\"", home, StringComparison.Ordinal);
        Assert.Contains("@onclick=\"OpenUpdateWindow\"", home, StringComparison.Ordinal);
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

        Assert.Contains("data-testid=\"entry-title-button\"", pane, StringComparison.Ordinal);
        Assert.DoesNotContain("data-testid=\"entry-fold-button\"", pane, StringComparison.Ordinal);
    }

    [Fact]
    public void Entry_metadata_keeps_focus_events_out_of_read_view_editor()
    {
        var pane = NormalizeLineEndings(File.ReadAllText(FindBacklogPane()));

        Assert.Contains("class=\"entry-doc__meta\" @onmousedown:stopPropagation=\"true\" @onclick:stopPropagation=\"true\"", pane, StringComparison.Ordinal);
        Assert.Contains("class=\"entry-doc__meta subitem-card__meta\" aria-label=\"Sub-item metadata and actions\" @onmousedown:stopPropagation=\"true\" @onclick:stopPropagation=\"true\"", pane, StringComparison.Ordinal);
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

    private static string FindAppCss() => FindProjectFile(Path.Combine("src", "App", "Backlog.Desktop.UI", "wwwroot", "app.css"));

    private static string FindHomeRazor() => FindProjectFile(Path.Combine("src", "App", "Backlog.Desktop.UI", "Shell", "Home.razor"));

    private static string FindInboxPane() => FindProjectFile(Path.Combine("src", "App", "Backlog.Desktop.UI", "Inbox", "InboxPane.razor"));

    private static string FindBacklogPane() => FindProjectFile(Path.Combine("src", "App", "Backlog.Desktop.UI", "BacklogManagement", "BacklogPane.razor"));

    private static string FindKnowledgePane() => FindProjectFile(Path.Combine("src", "App", "Backlog.Desktop.UI", "Knowledge", "KnowledgePane.razor"));

    private static string FindAppJs() => FindProjectFile(Path.Combine("src", "App", "Backlog.Desktop.UI", "wwwroot", "app.js"));

    private static string FindComponentsJs() => FindProjectFile(Path.Combine("src", "UI", "Backlog.UI.Components", "wwwroot", "components.js"));

    private static string FindProjectFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {relativePath} from the test output directory.");
    }
}
