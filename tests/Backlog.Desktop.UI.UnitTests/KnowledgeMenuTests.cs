using Backlog.Desktop.UI.Services;
using Backlog.Infrastructure.GitHub;

namespace Backlog.Desktop.UI.UnitTests;

public sealed class KnowledgeMenuTests : IDisposable
{
    private readonly List<string> _tempDirs = [];

    [Fact]
    public async Task Builds_hierarchical_tree_from_enabled_knowledge_folders()
    {
        var repo = TempDir();
        Directory.CreateDirectory(Path.Combine(repo, ".domain", "intake"));
        File.WriteAllText(Path.Combine(repo, ".domain", "context-map.md"), "# Context map");
        File.WriteAllText(Path.Combine(repo, ".domain", "intake", "domain.md"), "# Domain: Intake");
        Directory.CreateDirectory(Path.Combine(repo, ".domain", "_meta"));
        File.WriteAllText(Path.Combine(repo, ".domain", "_meta", "index.json"), "{}");
        Directory.CreateDirectory(Path.Combine(repo, ".backlog"));
        File.WriteAllText(Path.Combine(repo, ".backlog", "epics.md"), "# Epics");

        var settings = NewSettingsStore();
        ConfigureRepository(settings, repo);

        var tree = await new KnowledgeMenu(new KnowledgeFolderSource(settings)).LoadAsync(["backlog", "domain"]);

        Assert.Equal(["Backlog", "Domain"], tree.Roots.Select(node => node.Label));
        var domain = tree.Roots.Single(node => node.AreaKey == "domain");
        Assert.True(domain.Available);
        Assert.Contains(domain.Children, node => node.Kind == KnowledgeMenuNodeKind.File && node.Path == "context-map.md");

        var intake = Assert.Single(domain.Children, node => node.Kind == KnowledgeMenuNodeKind.Folder && node.Path == "intake");
        Assert.Equal("Intake", intake.Label);
        Assert.Contains(intake.Children, node => node.Kind == KnowledgeMenuNodeKind.File && node.Path == "intake/domain.md");
        Assert.DoesNotContain(domain.Children, node => node.Path.StartsWith("_meta", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Marks_missing_configured_folders_unavailable_without_throwing()
    {
        var repo = TempDir();
        var settings = NewSettingsStore();
        ConfigureRepository(settings, repo);

        var tree = await new KnowledgeMenu(new KnowledgeFolderSource(settings)).LoadAsync(["design"]);

        var design = Assert.Single(tree.Roots);
        Assert.Equal("Design", design.Label);
        Assert.False(design.Available);
        Assert.Contains("was not found", design.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Includes_virtual_instructions_folder_for_instruction_section()
    {
        var repo = TempDir();
        var settings = NewSettingsStore();
        ConfigureRepository(settings, repo);

        var tree = await new KnowledgeMenu(new KnowledgeFolderSource(settings)).LoadAsync(["instructions"]);

        var instructions = Assert.Single(tree.Roots);
        Assert.Equal("instructions", instructions.AreaKey);
        Assert.Equal("Instructions", instructions.Label);
        Assert.True(instructions.Available);
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs.Where(Directory.Exists))
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    private GitHubSettingsStore NewSettingsStore()
    {
        var path = Path.Combine(TempDir(), "github.json");
        return new GitHubSettingsStore(path);
    }

    private string TempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "knowledge-menu-tests", Guid.NewGuid().ToString("n"));
        _tempDirs.Add(path);
        return path;
    }

    private static void ConfigureRepository(GitHubSettingsStore settings, string repo)
    {
        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);
        settings.SetRepositories(repositories);
        settings.SetCloneDirectory("backlog", repo);
    }
}
