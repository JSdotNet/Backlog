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
        Assert.Equal("context-map.md", domain.Children.First().Path);

        var intake = Assert.Single(domain.Children, node => node.Kind == KnowledgeMenuNodeKind.Folder && node.Path == "intake");
        Assert.Equal("Intake", intake.Label);
        Assert.Contains(intake.Children, node => node.Kind == KnowledgeMenuNodeKind.File && node.Path == "intake/domain.md");
        Assert.DoesNotContain(domain.Children, node => node.Path.StartsWith("_meta", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Uses_folder_index_as_group_target_without_showing_index_child()
    {
        var repo = TempDir();
        Directory.CreateDirectory(Path.Combine(repo, ".domain", "localization"));
        File.WriteAllText(Path.Combine(repo, ".domain", "context-map.md"), "# Context map");
        File.WriteAllText(Path.Combine(repo, ".domain", "localization", "index.md"), "# Localization");
        File.WriteAllText(Path.Combine(repo, ".domain", "localization", "config.md"), "# Config");

        var settings = NewSettingsStore();
        ConfigureRepository(settings, repo);

        var tree = await new KnowledgeMenu(new KnowledgeFolderSource(settings)).LoadAsync(["domain"]);

        var domain = Assert.Single(tree.Roots);
        var localization = Assert.Single(domain.Children, node => node.Kind == KnowledgeMenuNodeKind.Folder && node.Label == "Localization");
        Assert.Equal("localization/index.md", localization.Path);
        Assert.DoesNotContain(localization.Children, node => string.Equals(node.Label, "Index", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(localization.Children, node => node.Kind == KnowledgeMenuNodeKind.File && node.Path == "localization/config.md");
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
        Directory.CreateDirectory(repo);
        var settings = NewSettingsStore();
        ConfigureRepository(settings, repo);

        var tree = await new KnowledgeMenu(new KnowledgeFolderSource(settings)).LoadAsync(["instructions"]);

        var instructions = Assert.Single(tree.Roots);
        Assert.Equal("instructions", instructions.AreaKey);
        Assert.Equal("Instructions", instructions.Label);
        Assert.True(instructions.Available);
    }


    [Fact]
    public async Task Orders_arc42_adr_after_chapter_09_and_tdr_after_chapter_11()
    {
        var repo = TempDir();
        Directory.CreateDirectory(Path.Combine(repo, ".arc42", "adr"));
        Directory.CreateDirectory(Path.Combine(repo, ".arc42", "tdr"));
        File.WriteAllText(Path.Combine(repo, ".arc42", "09-architecture-decisions.md"), "# Decisions");
        File.WriteAllText(Path.Combine(repo, ".arc42", "10-quality-requirements.md"), "# Quality");
        File.WriteAllText(Path.Combine(repo, ".arc42", "11-risks-and-technical-debt.md"), "# Risks");
        File.WriteAllText(Path.Combine(repo, ".arc42", "12-glossary.md"), "# Glossary");

        var settings = NewSettingsStore();
        ConfigureRepository(settings, repo);

        var tree = await new KnowledgeMenu(new KnowledgeFolderSource(settings)).LoadAsync(["arc42"]);

        var arc42 = Assert.Single(tree.Roots);
        Assert.Equal(
            ["09-architecture-decisions.md", "adr", "10-quality-requirements.md", "11-risks-and-technical-debt.md", "tdr", "12-glossary.md"],
            arc42.Children.Select(node => node.Path));
    }

    [Fact]
    public async Task Builds_instruction_roots_from_agent_folders_and_all_files()
    {
        var repo = TempDir();
        Directory.CreateDirectory(Path.Combine(repo, ".github", "workflows"));
        Directory.CreateDirectory(Path.Combine(repo, ".claude", "rules"));
        Directory.CreateDirectory(Path.Combine(repo, ".agents", "guides"));
        File.WriteAllText(Path.Combine(repo, ".github", "copilot-instructions.md"), "# Copilot");
        File.WriteAllText(Path.Combine(repo, ".github", "workflows", "ci.yml"), "name: CI");
        File.WriteAllText(Path.Combine(repo, ".claude", "rules", "style.md"), "# Style");
        File.WriteAllText(Path.Combine(repo, ".agents", "guides", "coding.txt"), "Code well");

        var settings = NewSettingsStore();
        ConfigureRepository(settings, repo);

        var tree = await new KnowledgeMenu(new KnowledgeFolderSource(settings)).LoadAsync(["instructions"]);

        var instructions = Assert.Single(tree.Roots);
        Assert.Equal([".github", ".claude", ".agent"], instructions.Children.Select(node => node.Label));
        var github = instructions.Children.Single(node => node.Path == ".github");
        var agent = instructions.Children.Single(node => node.Path == ".agent");
        Assert.Contains(github.Children.Single(node => node.Path == ".github/workflows").Children, node => node.Path == ".github/workflows/ci.yml");
        Assert.Contains(agent.Children.Single(node => node.Path == ".agent/guides").Children, node => node.Path == ".agent/guides/coding.txt");
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
