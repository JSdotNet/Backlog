using System.Diagnostics;
using System.Text;
using Backlog.Infrastructure.GitHub;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// The other tests in this project pin parsing, which can be done in memory. This
/// one cannot: what is being tested is the conversation with git — that the states
/// a compare view has to tell apart come back apart, and that the committed text
/// arrives byte for byte. A fake git would only pin our own idea of its output, so
/// each test builds a throwaway repository and runs the real thing against it.
/// </summary>
public sealed class GitFileHistoryServiceTests : IDisposable
{
    private readonly List<string> _directories = [];
    private readonly GitFileHistoryService _service = new();

    public void Dispose()
    {
        foreach (var directory in _directories.Where(Directory.Exists))
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

    [Fact]
    public async Task The_committed_text_comes_back_verbatim_while_the_working_copy_has_moved_on()
    {
        var repository = NewRepository();

        // Mixed endings and no trailing newline, because those are exactly the
        // details a diff would invent a change out of if anything rewrote them.
        const string committed = "# Title\r\nsecond line\nthird line\r\n\tindented";
        var file = Write(repository, "docs/nested/domain.md", committed);
        Commit(repository, "add domain notes");

        Write(repository, "docs/nested/domain.md", "# Title\r\nedited in the working tree");

        var result = await _service.ReadAtHeadAsync(file);

        Assert.Equal(GitFileAtRevisionState.Committed, result.State);
        Assert.Equal(committed, result.Content);
    }

    [Fact]
    public async Task A_file_that_only_exists_in_the_working_tree_is_not_tracked_yet()
    {
        var repository = NewRepository();
        Write(repository, "committed.md", "anything");
        Commit(repository, "first commit");

        var result = await _service.ReadAtHeadAsync(Write(repository, "brand-new.md", "never committed"));

        Assert.Equal(GitFileAtRevisionState.NotTracked, result.State);
        Assert.Null(result.Content);
    }

    [Fact]
    public async Task An_empty_committed_file_reads_as_empty_content_rather_than_as_untracked()
    {
        var repository = NewRepository();
        var file = Write(repository, "empty.md", string.Empty);
        Commit(repository, "add an empty file");

        Write(repository, "empty.md", "somebody typed something");

        var result = await _service.ReadAtHeadAsync(file);

        Assert.Equal(GitFileAtRevisionState.Committed, result.State);
        Assert.Equal(string.Empty, result.Content);
    }

    [Fact]
    public async Task A_repository_with_no_commits_has_nothing_to_compare_against()
    {
        var result = await _service.ReadAtHeadAsync(Write(NewRepository(), "first.md", "not committed yet"));

        Assert.Equal(GitFileAtRevisionState.NotTracked, result.State);
    }

    [Fact]
    public async Task A_path_outside_any_repository_says_so_instead_of_failing()
    {
        var directory = NewDirectory();
        var file = Path.Combine(directory, "loose.md");
        await File.WriteAllTextAsync(file, "not under version control");

        var result = await _service.ReadAtHeadAsync(file);

        Assert.Equal(GitFileAtRevisionState.NotARepository, result.State);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public async Task A_path_whose_folder_does_not_exist_is_not_a_repository_either()
    {
        var result = await _service.ReadAtHeadAsync(Path.Combine(NewDirectory(), "gone", "missing.md"));

        Assert.Equal(GitFileAtRevisionState.NotARepository, result.State);
    }

    [Fact]
    public async Task A_blank_path_is_the_caller_getting_it_wrong_rather_than_a_state()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.ReadAtHeadAsync("   "));
    }

    private string NewDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "backlog-git-history-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        _directories.Add(directory);
        return directory;
    }

    private string NewRepository()
    {
        var directory = NewDirectory();
        Git(directory, "init", "--quiet");

        // Everything the machine's global config could otherwise decide for us is
        // pinned locally: an identity so committing works on a bare CI account, no
        // signing, and no autocrlf — otherwise git would normalise the line endings
        // the verbatim test is about, and the test would be measuring the config.
        Git(directory, "config", "user.name", "Backlog Tests");
        Git(directory, "config", "user.email", "tests@backlog.invalid");
        Git(directory, "config", "commit.gpgsign", "false");
        Git(directory, "config", "core.autocrlf", "false");
        Git(directory, "config", "core.safecrlf", "false");
        return directory;
    }

    private static string Write(string repository, string relativePath, string content)
    {
        var path = Path.Combine(repository, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // WriteAllText would append nothing and translate nothing, but the bytes are
        // written directly anyway so the test's own expectation is unambiguous.
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes(content));
        return path;
    }

    private static void Commit(string repository, string message)
    {
        Git(repository, "add", "--all");
        Git(repository, "commit", "--quiet", "--no-verify", "-m", message);
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
}
