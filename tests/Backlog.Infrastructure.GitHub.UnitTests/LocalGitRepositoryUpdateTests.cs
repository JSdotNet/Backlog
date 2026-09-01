using System.Diagnostics;

using Backlog.Infrastructure.GitHub;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// "Not on the latest version" is a conversation with git, and the states it has
/// to tell apart — behind, ahead, diverged, tracking nothing, not on a branch —
/// are distinguished by git's own output. A fake git would only pin our idea of
/// that output, so each test builds a throwaway origin, clones it, and runs the
/// real thing, the way <c>GitFileHistoryServiceTests</c> does.
/// </summary>
public sealed class LocalGitRepositoryUpdateTests : IDisposable
{
    private readonly List<string> _directories = [];
    private readonly LocalGitRepositoryService _service = new();
    private readonly GitHubRepositoryRef _repository = new("backlog", "JSdotNet", "Backlog");

    [Fact]
    public async Task A_clone_that_has_everything_the_remote_has_is_on_the_latest_version()
    {
        var (_, clone) = NewOriginAndClone();

        var check = await _service.CheckForUpdatesAsync(_repository, clone);

        Assert.Equal(LocalGitRepositoryCurrency.UpToDate, check.Currency);
        Assert.Equal(0, check.Behind);
        Assert.False(check.CanPull);
        Assert.Contains("On the latest version", check.Summary);
    }

    [Fact]
    public async Task A_clone_the_remote_has_moved_past_is_behind_and_can_be_pulled()
    {
        var (origin, clone) = NewOriginAndClone();
        CommitTo(origin, "docs/second.md", "a second chapter");
        CommitTo(origin, "docs/third.md", "a third chapter");

        var check = await _service.CheckForUpdatesAsync(_repository, clone);

        Assert.Equal(LocalGitRepositoryCurrency.Behind, check.Currency);
        Assert.Equal(2, check.Behind);
        Assert.Equal(0, check.Ahead);
        Assert.True(check.CanPull);
        Assert.Contains("2 commits behind", check.Summary);
    }

    [Fact]
    public async Task One_commit_behind_is_counted_in_the_singular()
    {
        var (origin, clone) = NewOriginAndClone();
        CommitTo(origin, "docs/second.md", "a second chapter");

        var check = await _service.CheckForUpdatesAsync(_repository, clone);

        Assert.Contains("1 commit behind", check.Summary);
    }

    [Fact]
    public async Task Pulling_brings_the_remote_commits_into_the_clone_and_reports_it_current()
    {
        var (origin, clone) = NewOriginAndClone();
        CommitTo(origin, "docs/second.md", "a second chapter");

        var result = await _service.PullAsync(_repository, clone);

        Assert.True(result.Success, result.Message);
        Assert.True(File.Exists(Path.Combine(clone, "docs", "second.md")));
        Assert.Equal("a second chapter", await File.ReadAllTextAsync(Path.Combine(clone, "docs", "second.md")));
        Assert.Equal(LocalGitRepositoryCurrency.UpToDate, result.State?.Currency);
    }

    [Fact]
    public async Task A_clone_with_local_changes_is_still_reported_behind_but_refuses_the_pull()
    {
        var (origin, clone) = NewOriginAndClone();
        CommitTo(origin, "docs/second.md", "a second chapter");
        await File.WriteAllTextAsync(Path.Combine(clone, "docs", "first.md"), "edited here and not committed");

        var check = await _service.CheckForUpdatesAsync(_repository, clone);

        Assert.Equal(LocalGitRepositoryCurrency.Behind, check.Currency);
        Assert.True(check.HasLocalChanges);
        Assert.False(check.CanPull);
        Assert.Contains("local changes", check.Summary);

        var result = await _service.PullAsync(_repository, clone);

        Assert.False(result.Success);
        Assert.Contains("Commit or discard them", result.Message);
    }

    [Fact]
    public async Task A_clone_ahead_of_its_remote_has_nothing_to_pull()
    {
        var (_, clone) = NewOriginAndClone();
        CommitTo(clone, "docs/local-only.md", "written here");

        var check = await _service.CheckForUpdatesAsync(_repository, clone);

        Assert.Equal(LocalGitRepositoryCurrency.Ahead, check.Currency);
        Assert.Equal(1, check.Ahead);
        Assert.False(check.CanPull);
        Assert.Contains("nothing to pull", check.Summary);
    }

    [Fact]
    public async Task A_clone_that_has_diverged_says_so_rather_than_offering_a_pull()
    {
        var (origin, clone) = NewOriginAndClone();
        CommitTo(origin, "docs/theirs.md", "written there");
        CommitTo(clone, "docs/mine.md", "written here");

        var check = await _service.CheckForUpdatesAsync(_repository, clone);

        Assert.Equal(LocalGitRepositoryCurrency.Diverged, check.Currency);
        Assert.Equal(1, check.Ahead);
        Assert.Equal(1, check.Behind);
        Assert.False(check.CanPull);
        Assert.Contains("diverged", check.Summary);
    }

    [Fact]
    public async Task A_branch_that_tracks_nothing_has_no_latest_version_to_compare_against()
    {
        var (_, clone) = NewOriginAndClone();
        Git(clone, "checkout", "--quiet", "-b", "local-only");

        var check = await _service.CheckForUpdatesAsync(_repository, clone);

        Assert.Equal(LocalGitRepositoryCurrency.NoUpstream, check.Currency);
        Assert.False(check.CanPull);
        Assert.Contains("local-only", check.Summary);
        Assert.Contains("tracks no remote branch", check.Summary);
    }

    [Fact]
    public async Task A_clone_that_is_not_on_a_branch_has_no_latest_version_either()
    {
        var (_, clone) = NewOriginAndClone();
        Git(clone, "checkout", "--quiet", "--detach", "HEAD");

        var check = await _service.CheckForUpdatesAsync(_repository, clone);

        Assert.Equal(LocalGitRepositoryCurrency.Detached, check.Currency);
        Assert.False(check.CanPull);
        Assert.Contains("not on a branch", check.Summary);
    }

    [Fact]
    public async Task A_folder_that_is_not_a_clone_cannot_be_checked_or_pulled()
    {
        var directory = NewDirectory();

        var check = await _service.CheckForUpdatesAsync(_repository, directory);
        var pull = await _service.PullAsync(_repository, directory);

        Assert.Equal(LocalGitRepositoryCurrency.Unknown, check.Currency);
        Assert.Contains("no git clone", check.Summary);
        Assert.False(pull.Success);
        Assert.Contains("no git clone", pull.Message);
    }

    [Fact]
    public async Task No_configured_clone_directory_is_reported_rather_than_thrown()
    {
        var check = await _service.CheckForUpdatesAsync(_repository, null);
        var pull = await _service.PullAsync(_repository, "   ");

        Assert.Equal(LocalGitRepositoryCurrency.Unknown, check.Currency);
        Assert.Contains("No local clone directory", check.Summary);
        Assert.False(pull.Success);
        Assert.Contains("No local clone directory", pull.Message);
    }

    /// <summary>
    /// An origin with one commit on it, and a clone of it. The origin is a normal
    /// working clone rather than a bare one so a test can commit to it the same
    /// way it commits to the clone; pushing into it is never needed.
    /// </summary>
    private (string Origin, string Clone) NewOriginAndClone()
    {
        var origin = NewRepository();
        CommitTo(origin, "docs/first.md", "a first chapter");

        var clone = Path.Combine(NewDirectory(), "clone");
        Git(Path.GetDirectoryName(clone)!, "clone", "--quiet", origin, clone);
        _directories.Add(clone);
        PinConfig(clone);

        return (origin, clone);
    }

    private string NewDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "backlog-local-git-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        _directories.Add(directory);
        return directory;
    }

    private string NewRepository()
    {
        var directory = NewDirectory();
        Git(directory, "init", "--quiet", "--initial-branch", "main");
        PinConfig(directory);
        return directory;
    }

    /// <summary>
    /// Everything the machine's global config could otherwise decide is pinned
    /// locally, so these tests measure git's behaviour rather than the developer's
    /// settings — an identity so committing works on a bare CI account, no
    /// signing, and no rebase-on-pull, which would turn the fast-forward these
    /// tests are about into something else.
    /// </summary>
    private static void PinConfig(string directory)
    {
        Git(directory, "config", "user.name", "Backlog Tests");
        Git(directory, "config", "user.email", "tests@backlog.invalid");
        Git(directory, "config", "commit.gpgsign", "false");
        Git(directory, "config", "pull.rebase", "false");
        Git(directory, "config", "pull.ff", "only");
    }

    private static void CommitTo(string repository, string relativePath, string content)
    {
        var path = Path.Combine(repository, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);

        Git(repository, "add", "--all");
        Git(repository, "commit", "--quiet", "--no-verify", "-m", $"add {relativePath}");
    }

    private static void Git(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(
            process.ExitCode == 0,
            $"git {string.Join(' ', arguments)} failed with exit code {process.ExitCode}: {standardError}{standardOutput}");
    }

    public void Dispose()
    {
        // Deepest first, because a clone is created inside a directory that is
        // also tracked and deleting the parent first would leave the child's entry
        // pointing at nothing.
        foreach (var directory in _directories.OrderByDescending(path => path.Length).Where(Directory.Exists))
        {
            // git writes loose objects read-only and Directory.Delete refuses
            // those, so the attribute comes off before the recursive delete.
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                try { File.SetAttributes(file, FileAttributes.Normal); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }

            try { Directory.Delete(directory, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
