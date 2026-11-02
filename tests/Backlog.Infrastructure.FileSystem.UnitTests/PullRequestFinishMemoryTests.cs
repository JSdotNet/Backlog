using Backlog.Infrastructure.FileSystem;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// Which entries a merged pull request's finish is settled for (local ADR 0042 §3):
/// what comes back, what a file the outcome memory before it wrote amounts to, and what
/// a file this version cannot read amounts to.
/// <para>
/// Real temp directories, as <see cref="PullRequestDetailCacheTests"/> uses: the
/// behaviour under test is the disk.
/// </para>
/// </summary>
public sealed class PullRequestFinishMemoryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "pull-request-finish-memory-tests-" + Guid.NewGuid().ToString("N"));

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

    private PullRequestFinishMemory Memory() => new(() => _root);

    /// <summary>The folder and its files are the ones the outcome memory wrote, so an
    /// entry settled before the move to Repository Management stays settled: its outcome,
    /// read time and branches are passed over, and its settled entries come back.</summary>
    [Fact]
    public void A_file_the_outcome_memory_wrote_still_reads_its_settled_entries()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        WriteOldFile("jsdotnet-backlog", 1482, $$"""
            {
              "version": 2,
              "repository": "JSdotNet/Backlog",
              "number": 1482,
              "outcome": "Merged",
              "readAt": "2026-10-26T09:00:00+00:00",
              "baseRefName": "plan/lobes-atlas",
              "defaultBranch": "main",
              "settledEntries": [ "{{first}}", "{{second}}" ]
            }
            """);

        var read = Assert.Single(Memory().ReadAll());

        Assert.Equal(("JSdotNet/Backlog", 1482), (read.Repository, read.Number));
        Assert.Equal([first, second], read.SettledEntries);
    }

    [Fact]
    public void A_settled_entry_comes_back_after_a_restart()
    {
        var entry = Guid.NewGuid();

        Memory().Settle("JSdotNet/Backlog", 1482, entry);

        var read = Assert.Single(Memory().ReadAll());
        Assert.Equal(("JSdotNet/Backlog", 1482), (read.Repository, read.Number));
        Assert.Equal([entry], read.SettledEntries);
    }

    /// <summary>One pull request can land the work of several entries: each settles on
    /// its own, beside the others, whatever the repository's casing, and once.</summary>
    [Fact]
    public void Settling_another_entry_appends_and_settling_one_again_changes_nothing()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var memory = Memory();

        memory.Settle("JSdotNet/Backlog", 1482, first);
        memory.Settle("jsdotnet/backlog", 1482, second);
        memory.Settle("JSdotNet/Backlog", 1482, first);

        Assert.Equal([first, second], Assert.Single(memory.ReadAll()).SettledEntries);
    }

    /// <summary>Settling appends to a file the outcome memory wrote and leaves its other
    /// fields as they were, so a build from before the move reading the same shared
    /// folder still reads the merge and its branches.</summary>
    [Fact]
    public void Settling_keeps_what_the_outcome_memory_wrote_beside_the_entries()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        // Where this memory keeps 1482, written over as the outcome memory wrote it.
        Memory().Settle("JSdotNet/Backlog", 1482, first);
        var file = Assert.Single(Directory.GetFiles(_root, "1482.json", SearchOption.AllDirectories));
        File.WriteAllText(file, $$"""
            {
              "version": 2,
              "repository": "JSdotNet/Backlog",
              "number": 1482,
              "outcome": "Merged",
              "readAt": "2026-10-26T09:00:00+00:00",
              "baseRefName": "plan/lobes-atlas",
              "defaultBranch": "main",
              "settledEntries": [ "{{first}}" ]
            }
            """);

        Memory().Settle("JSdotNet/Backlog", 1482, second);

        var text = File.ReadAllText(file);
        Assert.Contains("\"outcome\": \"Merged\"", text, StringComparison.Ordinal);
        Assert.Contains("\"baseRefName\": \"plan/lobes-atlas\"", text, StringComparison.Ordinal);
        Assert.Contains("\"defaultBranch\": \"main\"", text, StringComparison.Ordinal);
        Assert.Equal([first, second], Assert.Single(Memory().ReadAll()).SettledEntries);
    }

    /// <summary>
    /// Settling is only ever done for a merge, so the file says merged whatever it said
    /// before. A file the outcome memory wrote as closed, left so, would have a build from
    /// before the move — sharing the folder — read the pull request as closed, read it
    /// merged, rewrite the file without its settled entries, and move a reopened entry to
    /// Done again.
    /// </summary>
    [Fact]
    public void Settling_a_pull_request_the_outcome_memory_kept_as_closed_says_it_merged()
    {
        var entry = Guid.NewGuid();
        Memory().Settle("JSdotNet/Backlog", 1482, Guid.NewGuid());
        var file = Assert.Single(Directory.GetFiles(_root, "1482.json", SearchOption.AllDirectories));
        File.WriteAllText(file, """
            {
              "version": 2,
              "repository": "JSdotNet/Backlog",
              "number": 1482,
              "outcome": "Closed",
              "readAt": "2026-10-26T09:00:00+00:00",
              "settledEntries": []
            }
            """);

        Memory().Settle("JSdotNet/Backlog", 1482, entry);

        Assert.Contains("\"outcome\": \"Merged\"", File.ReadAllText(file), StringComparison.Ordinal);
        Assert.Equal([entry], Assert.Single(Memory().ReadAll()).SettledEntries);
    }

    [Fact]
    public void Nothing_settled_and_no_folder_reads_as_empty()
    {
        Assert.Empty(new PullRequestFinishMemory(() => Path.Combine(_root, "never-made")).ReadAll());
    }

    /// <summary>A half-written file, or one in a shape this version does not know — the
    /// folder is shared by every checkout on the machine — is a miss for that one pull
    /// request, never a throw and never a guess.</summary>
    [Fact]
    public void A_corrupt_or_other_version_file_is_a_miss_and_the_rest_still_read()
    {
        var memory = Memory();
        memory.Settle("JSdotNet/Backlog", 1482, Guid.NewGuid());
        memory.Settle("JSdotNet/Backlog", 1483, Guid.NewGuid());
        memory.Settle("JSdotNet/Backlog", 1489, Guid.NewGuid());

        var files = Directory.GetFiles(_root, "*.json", SearchOption.AllDirectories);
        Assert.Equal(3, files.Length);
        File.WriteAllText(files.Single(f => f.EndsWith("1483.json", StringComparison.Ordinal)), "{ \"version\": ");
        var other = files.Single(f => f.EndsWith("1489.json", StringComparison.Ordinal));
        File.WriteAllText(other, File.ReadAllText(other).Replace("\"version\": 2", "\"version\": 1", StringComparison.Ordinal));

        Assert.Equal(1482, Assert.Single(memory.ReadAll()).Number);
    }

    /// <summary>Every pull request its own file, so two processes writing at once —
    /// two worktrees' harnesses share this folder — never lose each other's entry,
    /// and a write leaves no temporary file behind.</summary>
    [Fact]
    public void Each_pull_request_is_written_on_its_own_and_leaves_nothing_else_behind()
    {
        Memory().Settle("JSdotNet/Backlog", 1482, Guid.NewGuid());
        Memory().Settle("JSdotNet/Backlog", 1483, Guid.NewGuid());

        Assert.Equal([1482, 1483], Memory().ReadAll().Select(pr => pr.Number).Order());
        Assert.All(Directory.GetFiles(_root, "*", SearchOption.AllDirectories), f => Assert.EndsWith(".json", f, StringComparison.Ordinal));
    }

    /// <summary>The folder name is persisted: it is where every earlier build kept these
    /// files, so it never changes with the class's name.</summary>
    [Fact]
    public void The_folder_keeps_the_name_the_outcome_memory_gave_it()
    {
        Assert.Equal("pull-request-states", PullRequestFinishMemory.FolderName);
    }

    private string WriteOldFile(string folder, int number, string json)
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root, folder)).FullName;
        var path = Path.Combine(directory, $"{number}.json");
        File.WriteAllText(path, json);
        return path;
    }
}
