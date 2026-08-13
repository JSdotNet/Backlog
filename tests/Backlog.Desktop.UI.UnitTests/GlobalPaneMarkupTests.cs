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

        Assert.Contains("(BacklogPaneVisible && SidePaneVisible) ? \"knowledge-layout--side-open\"", home, StringComparison.Ordinal);
        Assert.Contains("side-pane-stack--full", home, StringComparison.Ordinal);
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
