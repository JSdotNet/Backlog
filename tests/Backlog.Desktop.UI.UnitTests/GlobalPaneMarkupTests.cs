namespace Backlog.Desktop.UI.UnitTests;

public sealed class GlobalPaneMarkupTests
{
    [Fact]
    public void Home_shell_exposes_global_pane_toggles_and_sections()
    {
        var home = NormalizeLineEndings(File.ReadAllText(FindHomeRazor()));

        Assert.Contains("data-testid=\"inbox-toggle-button\"", home, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"backlog-toggle-button\"", home, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"knowledge-toggle-button\"", home, StringComparison.Ordinal);

        Assert.Contains("id=\"inbox-pane\"", home, StringComparison.Ordinal);
        Assert.Contains("id=\"backlog-pane\"", home, StringComparison.Ordinal);
        Assert.Contains("id=\"repository-knowledge-pane\"", home, StringComparison.Ordinal);
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
