using System.Diagnostics;

using Backlog.Infrastructure.GitHub;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// Settings asks for a clone's status up to three times per render, on the UI
/// thread, so the answer is read from the repository's own config rather than
/// from a git process. Every git launch goes through the runner seam, which is
/// how these tests count them; where git does have to answer, the real git does,
/// so the fallback is held to git's own answer rather than to ours.
/// </summary>
public sealed class LocalGitCloneStatusTests : IDisposable
{
    private readonly List<string> _directories = [];
    private readonly List<IReadOnlyList<string>> _launches = [];
    private readonly List<IReadOnlyList<string>> _probes = [];
    private readonly GitHubRepositoryRef _repository = new("backlog", "JSdotNet", "Backlog");

    [Fact]
    public void A_normal_clone_is_read_without_starting_git()
    {
        var clone = NewDirectory();
        WriteConfig(Path.Combine(clone, ".git"), OriginSection("https://github.com/JSdotNet/Backlog.git"));

        var status = CountingService().GetStatus(_repository, clone);

        Assert.True(status.IsCloned);
        Assert.Equal($"Local clone is ready: {clone}", status.Summary);
        Assert.Empty(_launches);
    }

    [Fact]
    public void A_linked_worktree_is_read_from_the_shared_config_without_starting_git()
    {
        var main = NewRepository();
        Git(main, "remote", "add", "origin", "https://github.com/JSdotNet/Backlog.git");
        Git(main, "commit", "--quiet", "--allow-empty", "--no-verify", "-m", "first");
        var worktree = Path.Combine(NewDirectory(), "linked");
        Git(main, "worktree", "add", "--quiet", "-b", "linked", worktree);

        var status = CountingService().GetStatus(_repository, worktree);

        Assert.True(status.IsCloned, status.Summary);
        Assert.Empty(_launches);
    }

    [Fact]
    public void A_submodule_is_read_from_its_own_git_folder_without_starting_git()
    {
        var parent = NewDirectory();
        WriteConfig(Path.Combine(parent, ".git", "modules", "backlog"), OriginSection("git@github.com:JSdotNet/Backlog.git"));
        var submodule = Path.Combine(parent, "backlog");
        Directory.CreateDirectory(submodule);
        File.WriteAllText(Path.Combine(submodule, ".git"), "gitdir: ../.git/modules/backlog\n");

        var status = CountingService().GetStatus(_repository, submodule);

        Assert.True(status.IsCloned, status.Summary);
        Assert.Empty(_launches);
    }

    [Fact]
    public void A_url_rewrite_is_left_to_git_which_starts_once()
    {
        var clone = NewRepository();
        Git(clone, "config", "url.https://github.com/.insteadOf", "gh:");
        Git(clone, "remote", "add", "origin", "gh:JSdotNet/Backlog");

        var status = CountingService().GetStatus(_repository, clone);

        Assert.True(status.IsCloned, status.Summary);
        Assert.Single(_launches);
    }

    [Fact]
    public void An_included_config_is_left_to_git_which_starts_once()
    {
        // The included url comes first, so it is the one git answers, and the
        // url written in the file itself is not.
        var clone = NewRepository();
        File.WriteAllText(Path.Combine(clone, ".git", "origin.inc"), OriginSection("https://github.com/JSdotNet/Backlog.git"));
        File.AppendAllText(
            Path.Combine(clone, ".git", "config"),
            $"[include]{Environment.NewLine}\tpath = origin.inc{Environment.NewLine}" + OriginSection("https://github.com/SomeoneElse/Backlog.git"));

        var status = CountingService().GetStatus(_repository, clone);

        Assert.True(status.IsCloned, status.Summary);
        Assert.Single(_launches);
    }

    [Fact]
    public void A_quoted_url_with_a_trailing_comment_reads_as_git_reads_it()
    {
        var clone = NewDirectory();
        WriteConfig(
            Path.Combine(clone, ".git"),
            $"[remote \"origin\"] # the upstream{Environment.NewLine}\turl = \"https://github.com/JSdotNet/Backlog.git\" ; pushed by hand{Environment.NewLine}");

        var status = CountingService().GetStatus(_repository, clone);

        Assert.True(status.IsCloned, status.Summary);
        Assert.Empty(_launches);
    }

    [Fact]
    public void A_config_without_an_origin_asks_git_once()
    {
        var clone = NewRepository();

        var status = CountingService().GetStatus(_repository, clone);

        Assert.False(status.IsCloned);
        Assert.Equal($"Not cloned: {clone} is a git clone, but its origin is not JSdotNet/Backlog.", status.Summary);
        Assert.Single(_launches);
    }

    [Fact]
    public void A_git_file_that_leads_nowhere_asks_git_once()
    {
        var clone = NewDirectory();
        File.WriteAllText(Path.Combine(clone, ".git"), "gitdir: ../nowhere/.git/worktrees/gone\n");
        var service = new LocalGitRepositoryService(arguments =>
        {
            _launches.Add(arguments);
            return new LocalGitRepositoryService.GitCommandResult(0, "https://github.com/JSdotNet/Backlog.git\n", string.Empty);
        });

        var status = service.GetStatus(_repository, clone);

        Assert.True(status.IsCloned, status.Summary);
        Assert.Equal(["-C", clone, "remote", "get-url", "origin"], Assert.Single(_launches));
    }

    [Fact]
    public void When_git_cannot_answer_either_the_url_in_the_config_still_counts()
    {
        var clone = NewDirectory();
        WriteConfig(
            Path.Combine(clone, ".git"),
            $"[url \"git@github.com:\"]{Environment.NewLine}\tinsteadOf = gh:{Environment.NewLine}"
                + OriginSection("https://github.com/JSdotNet/Backlog.git"));
        var service = new LocalGitRepositoryService(arguments =>
        {
            _launches.Add(arguments);
            return new LocalGitRepositoryService.GitCommandResult(1, string.Empty, "git could not be started: not found");
        });

        var status = service.GetStatus(_repository, clone);

        Assert.True(status.IsCloned, status.Summary);
        Assert.Single(_launches);
    }

    [Fact]
    public void Every_status_without_a_clone_to_read_is_answered_without_starting_git()
    {
        var service = CountingService();
        var missing = Path.Combine(NewDirectory(), "Backlog");
        var empty = NewDirectory();
        var notAClone = NewDirectory();
        File.WriteAllText(Path.Combine(notAClone, "notes.txt"), "not a clone");
        var otherOrigin = NewDirectory();
        WriteConfig(Path.Combine(otherOrigin, ".git"), OriginSection("https://github.com/SomeoneElse/Backlog.git"));

        Assert.Equal(
            new LocalGitRepositoryStatus(null, false, false, "No local clone directory configured yet."),
            service.GetStatus(_repository, "   "));
        Assert.Equal(
            new LocalGitRepositoryStatus(missing, false, true, $"Not cloned yet: {missing}"),
            service.GetStatus(_repository, missing));
        Assert.Equal(
            new LocalGitRepositoryStatus(empty, false, true, $"Not cloned yet: {empty}"),
            service.GetStatus(_repository, empty));
        Assert.Equal(
            new LocalGitRepositoryStatus(notAClone, false, false, $"Not cloned: {notAClone} exists but is not a git clone. Pick an empty folder or an existing clone."),
            service.GetStatus(_repository, notAClone));
        Assert.Equal(
            new LocalGitRepositoryStatus(otherOrigin, false, false, $"Not cloned: {otherOrigin} is a git clone, but its origin is not JSdotNet/Backlog."),
            service.GetStatus(_repository, otherOrigin));
        Assert.Empty(_launches);
    }

    [Fact]
    public void The_global_config_is_asked_once_for_many_statuses()
    {
        var service = CountingService();
        var first = NewDirectory();
        var second = NewDirectory();
        WriteConfig(Path.Combine(first, ".git"), OriginSection("https://github.com/JSdotNet/Backlog.git"));
        WriteConfig(Path.Combine(second, ".git"), OriginSection("https://github.com/SomeoneElse/Backlog.git"));

        for (var render = 0; render < 3; render++)
        {
            service.GetStatus(_repository, first);
            service.GetStatus(_repository, second);
        }

        Assert.Single(_probes);
        Assert.Empty(_launches);
    }

    [Fact]
    public void A_url_rewrite_in_the_global_config_is_left_to_git()
    {
        var clone = NewDirectory();
        WriteConfig(Path.Combine(clone, ".git"), OriginSection("gh:JSdotNet/Backlog"));
        var service = CountingService(
            globalRewrites: new LocalGitRepositoryService.GitCommandResult(0, "global\turl.git@github.com:.insteadof gh:\n", string.Empty),
            originAnswer: new LocalGitRepositoryService.GitCommandResult(0, "git@github.com:JSdotNet/Backlog\n", string.Empty));

        var status = service.GetStatus(_repository, clone);

        Assert.True(status.IsCloned, status.Summary);
        Assert.Single(_launches);
    }

    [Fact]
    public void A_rewrite_only_in_the_repository_itself_does_not_count_as_global()
    {
        var clone = NewDirectory();
        WriteConfig(Path.Combine(clone, ".git"), OriginSection("https://github.com/JSdotNet/Backlog.git"));
        var service = CountingService(
            globalRewrites: new LocalGitRepositoryService.GitCommandResult(0, "local\turl.git@github.com:.insteadof gh:\n", string.Empty));

        var status = service.GetStatus(_repository, clone);

        Assert.True(status.IsCloned, status.Summary);
        Assert.Empty(_launches);
    }

    /// <summary>
    /// The real git, with a global config of the test's own handed to the child
    /// processes only, so no other test running beside it sees it.
    /// </summary>
    [Fact]
    public void A_clone_made_through_a_global_rewrite_reads_as_git_reads_it()
    {
        var clone = NewRepository();
        Git(clone, "remote", "add", "origin", "gh:JSdotNet/Backlog");
        var global = Path.Combine(NewDirectory(), "gitconfig");
        File.WriteAllText(global, $"[url \"git@github.com:\"]{Environment.NewLine}\tinsteadOf = gh:{Environment.NewLine}");
        var service = new LocalGitRepositoryService(
            arguments =>
            {
                if (!IsGlobalProbe(arguments)) _launches.Add(arguments);
                return RunGitWithGlobalConfig(global, arguments);
            },
            () => [global]);

        var status = service.GetStatus(_repository, clone);

        Assert.True(status.IsCloned, status.Summary);
        Assert.Single(_launches);
    }

    [Fact]
    public void A_changed_global_config_is_asked_again()
    {
        var global = Path.Combine(NewDirectory(), "gitconfig");
        File.WriteAllText(global, string.Empty);
        var clone = NewDirectory();
        WriteConfig(Path.Combine(clone, ".git"), OriginSection("https://github.com/JSdotNet/Backlog.git"));
        var service = CountingService(globalConfigFiles: [global]);

        service.GetStatus(_repository, clone);
        File.SetLastWriteTimeUtc(global, File.GetLastWriteTimeUtc(global).AddMinutes(1));
        service.GetStatus(_repository, clone);

        Assert.Equal(2, _probes.Count);
    }

    [Fact]
    public void Git_is_asked_again_only_once_the_config_changes()
    {
        var clone = NewRepository();
        var service = CountingService();

        for (var render = 0; render < 3; render++)
        {
            Assert.False(service.GetStatus(_repository, clone).IsCloned);
        }

        Git(clone, "remote", "add", "origin", "gh:JSdotNet/Backlog");
        Git(clone, "config", "url.https://github.com/.insteadOf", "gh:");
        var config = Path.Combine(clone, ".git", "config");
        File.SetLastWriteTimeUtc(config, File.GetLastWriteTimeUtc(config).AddMinutes(1));

        Assert.True(service.GetStatus(_repository, clone).IsCloned);
        Assert.Equal(2, _launches.Count);
    }

    /// <summary>
    /// A service whose git launches are recorded and then run for real, except
    /// the global-config probe, which is answered (by default: no rewrites) so a
    /// developer's own global config cannot decide a test.
    /// </summary>
    private LocalGitRepositoryService CountingService(
        LocalGitRepositoryService.GitCommandResult? globalRewrites = null,
        LocalGitRepositoryService.GitCommandResult? originAnswer = null,
        IReadOnlyList<string>? globalConfigFiles = null) =>
        new(
            arguments =>
            {
                if (IsGlobalProbe(arguments))
                {
                    _probes.Add(arguments);
                    return globalRewrites ?? new LocalGitRepositoryService.GitCommandResult(1, string.Empty, string.Empty);
                }

                _launches.Add(arguments);
                return originAnswer ?? LocalGitRepositoryService.RunGit(arguments);
            },
            () => globalConfigFiles ?? []);

    private static bool IsGlobalProbe(IReadOnlyList<string> arguments) =>
        arguments.Count > 0 && arguments[0] == "config" && arguments.Contains("--get-regexp");

    private static LocalGitRepositoryService.GitCommandResult RunGitWithGlobalConfig(string global, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.Environment["GIT_CONFIG_GLOBAL"] = global;
        startInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new LocalGitRepositoryService.GitCommandResult(process.ExitCode, standardOutput.Result, standardError);
    }

    private static string OriginSection(string url) =>
        $"[remote \"origin\"]{Environment.NewLine}\turl = {url}{Environment.NewLine}";

    private static void WriteConfig(string gitDirectory, string config)
    {
        Directory.CreateDirectory(gitDirectory);
        File.WriteAllText(Path.Combine(gitDirectory, "HEAD"), "ref: refs/heads/main");
        File.WriteAllText(Path.Combine(gitDirectory, "config"), config);
    }

    private string NewDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "backlog-clone-status-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        _directories.Add(directory);
        return directory;
    }

    private string NewRepository()
    {
        var directory = NewDirectory();
        Git(directory, "init", "--quiet", "--initial-branch", "main");
        Git(directory, "config", "user.name", "Backlog Tests");
        Git(directory, "config", "user.email", "tests@backlog.invalid");
        Git(directory, "config", "commit.gpgsign", "false");
        return directory;
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
