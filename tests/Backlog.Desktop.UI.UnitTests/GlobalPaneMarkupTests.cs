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
    public void Pane_multiselect_uses_selected_state_and_never_zero_selection_affordance()
    {
        var home = NormalizeLineEndings(File.ReadAllText(FindHomeRazor()));

        Assert.Contains("aria-pressed=\"@(InboxPaneVisible ? \"true\" : \"false\")\"", home, StringComparison.Ordinal);
        Assert.Contains("aria-pressed=\"@(BacklogPaneVisible ? \"true\" : \"false\")\"", home, StringComparison.Ordinal);
        Assert.Contains("aria-pressed=\"@(KnowledgePaneVisible ? \"true\" : \"false\")\"", home, StringComparison.Ordinal);

        Assert.Contains("disabled=\"@PaneToggleDisabled(GlobalPane.Inbox)\"", home, StringComparison.Ordinal);
        Assert.Contains("disabled=\"@PaneToggleDisabled(GlobalPane.Backlog)\"", home, StringComparison.Ordinal);
        Assert.Contains("disabled=\"@PaneToggleDisabled(GlobalPane.Knowledge)\"", home, StringComparison.Ordinal);

        Assert.DoesNotContain("Show inbox", home, StringComparison.Ordinal);
        Assert.DoesNotContain("Hide inbox", home, StringComparison.Ordinal);
        Assert.DoesNotContain("Show backlog", home, StringComparison.Ordinal);
        Assert.DoesNotContain("Hide backlog", home, StringComparison.Ordinal);
        Assert.DoesNotContain("Show knowledge", home, StringComparison.Ordinal);
        Assert.DoesNotContain("Hide knowledge", home, StringComparison.Ordinal);
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
        var backlogPaneIndex = home.IndexOf("<section class=\"backlog-workspace\"", StringComparison.Ordinal);

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

    private static string NormalizeLineEndings(string text) => text.Replace("\r\n", "\n");

    private static string FindHomeRazor()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "src",
                "App",
                "Backlog.Desktop.UI",
                "Components",
                "Pages",
                "Home.razor");

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate src\\App\\Backlog.Desktop.UI\\Components\\Pages\\Home.razor from the test output directory.");
    }
}

