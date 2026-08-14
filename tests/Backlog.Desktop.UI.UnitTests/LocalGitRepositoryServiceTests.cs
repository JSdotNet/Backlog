using Backlog.Infrastructure.GitHub;

namespace Backlog.Desktop.UI.UnitTests;

public sealed class LocalGitRepositoryServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-local-git-tests", Guid.NewGuid().ToString("n"));
    private readonly LocalGitRepositoryService _service = new();
    private readonly GitHubRepositoryRef _repository = new("backlog", "JSdotNet", "Backlog");

    [Fact]
    public void Missing_clone_directory_cannot_be_cloned_until_a_path_is_entered()
    {
        var status = _service.GetStatus(_repository, null);

        Assert.False(status.IsCloned);
        Assert.False(status.CanClone);
        Assert.Contains("No local clone directory", status.Summary);
    }

    [Fact]
    public void Missing_target_directory_can_be_cloned()
    {
        var target = Path.Combine(_root, "Backlog");

        var status = _service.GetStatus(_repository, target);

        Assert.False(status.IsCloned);
        Assert.True(status.CanClone);
        Assert.Equal(target, status.CloneDirectory);
    }

    [Fact]
    public void Existing_git_directory_is_already_cloned()
    {
        var target = Path.Combine(_root, "Backlog");
        CreateGitMetadata(target, "https://github.com/JSdotNet/Backlog.git");

        var status = _service.GetStatus(_repository, target);

        Assert.True(status.IsCloned);
        Assert.False(status.CanClone);
        Assert.Contains("Local clone is ready", status.Summary);
    }

    [Fact]
    public void Existing_git_directory_for_another_origin_is_not_this_clone()
    {
        var target = Path.Combine(_root, "Backlog");
        CreateGitMetadata(target, "https://github.com/SomeoneElse/Backlog.git");

        var status = _service.GetStatus(_repository, target);

        Assert.False(status.IsCloned);
        Assert.False(status.CanClone);
        Assert.Contains("origin is not JSdotNet/Backlog", status.Summary);
    }

    [Fact]
    public void Existing_non_git_directory_blocks_clone()
    {
        var target = Path.Combine(_root, "Backlog");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "notes.txt"), "not a clone");

        var status = _service.GetStatus(_repository, target);

        Assert.False(status.IsCloned);
        Assert.False(status.CanClone);
        Assert.Contains("exists but is not a git clone", status.Summary);
    }

    private static void CreateGitMetadata(string target, string origin)
    {
        var git = Path.Combine(target, ".git");
        Directory.CreateDirectory(git);
        File.WriteAllText(Path.Combine(git, "HEAD"), "ref: refs/heads/main");
        File.WriteAllText(Path.Combine(git, "config"), $"[remote \"origin\"]{Environment.NewLine}\turl = {origin}{Environment.NewLine}");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
