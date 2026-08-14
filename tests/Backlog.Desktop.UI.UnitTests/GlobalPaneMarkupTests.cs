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

        Assert.Contains("id=\"inbox-pane\"", home, StringComparison.Ordinal);
        Assert.Contains("id=\"backlog-pane\"", home, StringComparison.Ordinal);
        Assert.Contains("id=\"repository-knowledge-pane\"", home, StringComparison.Ordinal);
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
        var appJs = NormalizeLineEndings(File.ReadAllText(FindAppJs()));

        Assert.Contains("public Task SetGlobalPaneCapacityAsync(int capacity)", home, StringComparison.Ordinal);
        Assert.Contains("backlogPaneOwner.invokeMethodAsync('SetGlobalPaneCapacityAsync', backlogPaneCapacity());", appJs, StringComparison.Ordinal);
        Assert.Contains("const BACKLOG_SINGLE_PANE_MAX_REM = 72;", appJs, StringComparison.Ordinal);
        Assert.Contains("const BACKLOG_THREE_PANE_MIN_REM = 96;", appJs, StringComparison.Ordinal);
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
        var backlogPaneIndex = home.IndexOf("<section @key=@(\"backlog-pane\") class=\"backlog-workspace\"", StringComparison.Ordinal);

        Assert.True(inboxGuardIndex >= 0);
        Assert.True(backlogPaneIndex > inboxGuardIndex);
    }

    [Fact]
    public void App_version_opens_a_separate_update_window()
    {
        var home = NormalizeLineEndings(File.ReadAllText(FindHomeRazor()));

        Assert.Contains("data-testid=\"app-version\"", home, StringComparison.Ordinal);
        Assert.Contains("@onclick=\"OpenUpdateWindow\"", home, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"app-update-dialog\"", home, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"check-for-updates\"", home, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"install-update\"", home, StringComparison.Ordinal);
        Assert.DoesNotContain("app-version__hint", home, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderKnowledgeFolderOpenError_uses_explicit_builder_fragment()
    {
        var home = NormalizeLineEndings(File.ReadAllText(FindHomeRazor()));

        var methodIndex = home.IndexOf(
            "private RenderFragment RenderKnowledgeFolderOpenError(KnowledgeMenuNode node) => builder =>",
            StringComparison.Ordinal);
        Assert.True(methodIndex >= 0, "Could not locate RenderKnowledgeFolderOpenError in Home.razor.");

        var terminatorIndex = home.IndexOf("    };", methodIndex, StringComparison.Ordinal);
        Assert.True(terminatorIndex > methodIndex, "The RenderKnowledgeFolderOpenError method should be complete.");

        var methodBody = home.Substring(methodIndex, terminatorIndex - methodIndex);

        Assert.Contains("builder.OpenElement(0, \"p\");", methodBody, StringComparison.Ordinal);
        Assert.Contains("builder.AddAttribute(1, \"class\", \"knowledge-menu__open-error\");", methodBody, StringComparison.Ordinal);
        Assert.Contains("builder.AddAttribute(2, \"role\", \"status\");", methodBody, StringComparison.Ordinal);
        Assert.Contains("builder.AddContent(3, error);", methodBody, StringComparison.Ordinal);
        Assert.DoesNotContain("@<>", methodBody, StringComparison.Ordinal);
        Assert.DoesNotContain("</>", methodBody, StringComparison.Ordinal);
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
        var home = NormalizeLineEndings(File.ReadAllText(FindHomeRazor()));

        Assert.Contains("data-testid=\"entry-title-button\"", home, StringComparison.Ordinal);
        Assert.DoesNotContain("data-testid=\"entry-fold-button\"", home, StringComparison.Ordinal);
    }

    [Fact]
    public void Entry_metadata_keeps_focus_events_out_of_read_view_editor()
    {
        var home = NormalizeLineEndings(File.ReadAllText(FindHomeRazor()));

        Assert.Contains("class=\"entry-doc__meta\" @onmousedown:stopPropagation=\"true\" @onclick:stopPropagation=\"true\"", home, StringComparison.Ordinal);
        Assert.Contains("class=\"entry-doc__meta subitem-card__meta\" aria-label=\"Sub-item metadata and actions\" @onmousedown:stopPropagation=\"true\" @onclick:stopPropagation=\"true\"", home, StringComparison.Ordinal);
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

    private static string FindHomeRazor() => FindProjectFile(Path.Combine("src", "App", "Backlog.Desktop.UI", "Components", "Pages", "Home.razor"));

    private static string FindAppJs() => FindProjectFile(Path.Combine("src", "App", "Backlog.Desktop.UI", "wwwroot", "app.js"));

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
