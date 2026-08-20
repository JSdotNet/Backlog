using Backlog.Infrastructure.GitHub;

namespace Backlog.Desktop.UI.UnitTests;

public sealed class KnowledgeFolderOpenServiceTests : IDisposable
{
    private readonly List<string> _tempDirs = [];

    [Fact]
    public async Task Opens_nested_knowledge_folder_in_configured_area()
    {
        var repo = TempDir();
        var target = Path.Combine(repo, ".domain", "intake");
        Directory.CreateDirectory(target);
        var launcher = new RecordingFolderEditorLauncher();
        var service = new KnowledgeFolderOpenService(new KnowledgeFolderSource(NewSettingsStore(repo)), launcher);

        await service.OpenAsync("domain", "intake");

        Assert.Equal(Path.GetFullPath(target), launcher.OpenedFolder);
    }

    [Fact]
    public async Task Opens_area_root_folder()
    {
        var repo = TempDir();
        var target = Path.Combine(repo, ".domain");
        Directory.CreateDirectory(target);
        var launcher = new RecordingFolderEditorLauncher();
        var service = new KnowledgeFolderOpenService(new KnowledgeFolderSource(NewSettingsStore(repo)), launcher);

        await service.OpenAsync("domain", ".domain");

        Assert.Equal(Path.GetFullPath(target), launcher.OpenedFolder);
    }

    [Fact]
    public async Task Opens_agents_folder_when_instructions_menu_displays_agent()
    {
        var repo = TempDir();
        var target = Path.Combine(repo, ".agents", "guides");
        Directory.CreateDirectory(target);
        var launcher = new RecordingFolderEditorLauncher();
        var service = new KnowledgeFolderOpenService(new KnowledgeFolderSource(NewSettingsStore(repo)), launcher);

        await service.OpenAsync("instructions", ".agent/guides");

        Assert.Equal(Path.GetFullPath(target), launcher.OpenedFolder);
    }

    [Fact]
    public async Task Rejects_paths_outside_knowledge_root()
    {
        var repo = TempDir();
        Directory.CreateDirectory(Path.Combine(repo, ".domain"));
        var service = new KnowledgeFolderOpenService(new KnowledgeFolderSource(NewSettingsStore(repo)), new RecordingFolderEditorLauncher());

        await Assert.ThrowsAsync<KnowledgeFolderOpenException>(() => service.OpenAsync("domain", "../outside"));
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs.Where(Directory.Exists))
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    private GitHubSettingsStore NewSettingsStore(string repo)
    {
        var path = Path.Combine(TempDir(), "github.json");
        var settings = new GitHubSettingsStore(path);
        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);
        settings.SetRepositories(repositories);
        settings.SetCloneDirectory("backlog", repo);
        return settings;
    }

    private string TempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "knowledge-folder-open-tests", Guid.NewGuid().ToString("n"));
        _tempDirs.Add(path);
        return path;
    }

    private sealed class RecordingFolderEditorLauncher : IFolderEditorLauncher
    {
        public string? OpenedFolder { get; private set; }

        public Task OpenFolderAsync(string folderPath, CancellationToken cancellationToken = default)
        {
            OpenedFolder = folderPath;
            return Task.CompletedTask;
        }
    }
}
