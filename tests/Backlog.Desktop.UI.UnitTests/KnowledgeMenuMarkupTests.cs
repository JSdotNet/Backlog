namespace Backlog.Desktop.UI.UnitTests;

public sealed class KnowledgeMenuMarkupTests
{
    [Fact]
    public void Knowledge_menu_shows_open_in_vscode_button_only_for_active_root_heading()
    {
        var home = NormalizeLineEndings(File.ReadAllText(FindHomeRazor()));

        Assert.Contains("@if (ActiveKnowledgeMenuRoot is { Kind: KnowledgeMenuNodeKind.Folder, Available: true } rootFolder)", home, StringComparison.Ordinal);
        Assert.Contains("@RenderOpenKnowledgeFolderButton(rootFolder)", home, StringComparison.Ordinal);
        Assert.DoesNotContain("@RenderOpenKnowledgeFolderButton(node)", home, StringComparison.Ordinal);
    }

    [Fact]
    public void Knowledge_menu_shows_folder_open_errors_above_the_menu_tree()
    {
        var home = NormalizeLineEndings(File.ReadAllText(FindHomeRazor()));

        Assert.Contains("<AppErrorMessage Message=\"@_knowledgeFolderOpenError\"", home, StringComparison.Ordinal);
        Assert.Contains("TestId=\"knowledge-menu-open-error\"", home, StringComparison.Ordinal);
        Assert.DoesNotContain("knowledge-menu__open-error", home, StringComparison.Ordinal);
    }

    [Fact]
    public void Reusable_error_component_is_shared_across_panels()
    {
        var errorComponent = NormalizeLineEndings(File.ReadAllText(FindComponent("AppErrorMessage.razor")));
        var technologyPanel = NormalizeLineEndings(File.ReadAllText(FindComponent("TechnologyKnowledgePanel.razor")));

        Assert.Contains("public string? Message", errorComponent, StringComparison.Ordinal);
        Assert.Contains("public string Role", errorComponent, StringComparison.Ordinal);
        Assert.Contains("app-error-message", errorComponent, StringComparison.Ordinal);

        Assert.Contains("<AppErrorMessage Message=\"@_technologyFolderOpenError\"", technologyPanel, StringComparison.Ordinal);
    }

    private static string NormalizeLineEndings(string text) => text.Replace("\r\n", "\n");

    private static string FindHomeRazor() => FindRelativeFile(
        "src",
        "App",
        "Backlog.Desktop.UI",
        "Components",
        "Pages",
        "Home.razor");

    private static string FindComponent(string fileName) => FindRelativeFile(
        "src",
        "App",
        "Backlog.Desktop.UI",
        "Components",
        fileName);

    private static string FindRelativeFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var pathSegments = new string[segments.Length + 1];
            pathSegments[0] = directory.FullName;
            Array.Copy(segments, 0, pathSegments, 1, segments.Length);
            var candidate = Path.Combine(pathSegments);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {Path.Combine(segments)} from the test output directory.");
    }
}
