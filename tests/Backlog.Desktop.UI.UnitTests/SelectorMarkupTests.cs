namespace Backlog.Desktop.UI.UnitTests;

public sealed class SelectorMarkupTests
{
    [Fact]
    public void Shared_selector_component_files_exist()
    {
        Assert.True(File.Exists(FindRepoFile("src", "App", "Backlog.Desktop.UI", "Components", "StatusSelector.razor")));
        Assert.True(File.Exists(FindRepoFile("src", "App", "Backlog.Desktop.UI", "Components", "PrioritySelector.razor")));
        Assert.True(File.Exists(FindRepoFile("src", "App", "Backlog.Desktop.UI", "Components", "RepositorySelector.razor")));
    }

    [Fact]
    public void Home_and_knowledge_panels_use_the_shared_selector_components()
    {
        var home = NormalizeLineEndings(File.ReadAllText(FindRepoFile("src", "App", "Backlog.Desktop.UI", "Components", "Pages", "Home.razor")));
        var arc42 = NormalizeLineEndings(File.ReadAllText(FindRepoFile("src", "App", "Backlog.Desktop.UI", "Components", "Arc42KnowledgePanel.razor")));
        var domain = NormalizeLineEndings(File.ReadAllText(FindRepoFile("src", "App", "Backlog.Desktop.UI", "Components", "DomainKnowledgePanel.razor")));
        var technology = NormalizeLineEndings(File.ReadAllText(FindRepoFile("src", "App", "Backlog.Desktop.UI", "Components", "TechnologyKnowledgePanel.razor")));

        Assert.Contains("<PrioritySelector", home, StringComparison.Ordinal);
        Assert.Contains("<RepositorySelector", home, StringComparison.Ordinal);
        Assert.Contains("<StatusSelector", home, StringComparison.Ordinal);
        Assert.Contains("<StatusSelector", arc42, StringComparison.Ordinal);
        Assert.Contains("<StatusSelector", domain, StringComparison.Ordinal);
        Assert.Contains("<StatusSelector", technology, StringComparison.Ordinal);
    }

    [Fact]
    public void Sub_item_repository_metadata_uses_an_interactive_repository_selector()
    {
        var home = NormalizeLineEndings(File.ReadAllText(FindRepoFile("src", "App", "Backlog.Desktop.UI", "Components", "Pages", "Home.razor")));

        Assert.True(CountOccurrences(home, "<RepositorySelector") >= 2);
        Assert.Contains("OnSubItemRepositoryChangedAsync", home, StringComparison.Ordinal);
    }

    [Fact]
    public void Home_markup_no_longer_branches_on_is_read_only()
    {
        var home = NormalizeLineEndings(File.ReadAllText(FindRepoFile("src", "App", "Backlog.Desktop.UI", "Components", "Pages", "Home.razor")));
        Assert.DoesNotContain("row.IsReadOnly", home, StringComparison.Ordinal);
    }

    [Fact]
    public void Entry_and_sub_item_titles_use_integrated_collapse_buttons()
    {
        var home = NormalizeLineEndings(File.ReadAllText(FindRepoFile("src", "App", "Backlog.Desktop.UI", "Components", "Pages", "Home.razor")));
        Assert.Contains("data-testid=\"entry-title-button\"", home, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"subitem-title-button\"", home, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string NormalizeLineEndings(string text) => text.Replace("\r\n", "\n");

    private static string FindRepoFile(params string[] relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativePath).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return Path.Combine(new[] { AppContext.BaseDirectory }.Concat(relativePath).ToArray());
    }
}
