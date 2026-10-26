using Backlog.Infrastructure.FileSystem;
using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The remembered outcomes of the backlog's pull requests: what comes back, what is
/// replaced and forgotten, and what a file this version cannot read amounts to.
/// <para>
/// Real temp directories, as <see cref="PullRequestDetailCacheTests"/> uses: the
/// behaviour under test is the disk.
/// </para>
/// </summary>
public sealed class PullRequestStateMemoryTests : IDisposable
{
    private static readonly DateTimeOffset Morning = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "pull-request-state-memory-tests-" + Guid.NewGuid().ToString("N"));

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

    private PullRequestStateMemory Memory() => new(() => _root);

    [Fact]
    public void What_was_remembered_comes_back_after_a_restart()
    {
        Memory().Remember(new RememberedPullRequest("JSdotNet/Backlog", 1482, PullRequestOutcome.Merged, Morning));
        Memory().Remember(new RememberedPullRequest("JSdotNet/Backlog", 1490, PullRequestOutcome.Closed, Morning));

        var read = Memory().ReadAll().OrderBy(pr => pr.Number).Select(pr => (pr.Repository, pr.Number, pr.Outcome, pr.ReadAt)).ToList();

        Assert.Equal(
            [
                ("JSdotNet/Backlog", 1482, PullRequestOutcome.Merged, Morning),
                ("JSdotNet/Backlog", 1490, PullRequestOutcome.Closed, Morning)
            ],
            read);
    }

    /// <summary>A merge comes back with where it merged and which entries its finish
    /// is settled for — what a restart needs to owe the move to any other entry.</summary>
    [Fact]
    public void A_merge_comes_back_with_its_branches_and_its_settled_entries()
    {
        var settled = Guid.NewGuid();
        Memory().Remember(new RememberedPullRequest("JSdotNet/Backlog", 1482, PullRequestOutcome.Merged, Morning)
        {
            BaseRefName = "plan/lobes-atlas",
            DefaultBranch = "main",
            SettledEntries = [settled]
        });

        var read = Assert.Single(Memory().ReadAll());

        Assert.Equal("plan/lobes-atlas", read.BaseRefName);
        Assert.Equal("main", read.DefaultBranch);
        Assert.Equal([settled], read.SettledEntries);
    }

    [Fact]
    public void A_later_outcome_replaces_the_earlier_one_whatever_the_repository_casing()
    {
        var memory = Memory();
        memory.Remember(new RememberedPullRequest("JSdotNet/Backlog", 1482, PullRequestOutcome.Closed, Morning));
        memory.Remember(new RememberedPullRequest("jsdotnet/backlog", 1482, PullRequestOutcome.Merged, Morning.AddHours(1)));

        var read = Assert.Single(memory.ReadAll());

        Assert.Equal(PullRequestOutcome.Merged, read.Outcome);
        Assert.Equal(Morning.AddHours(1), read.ReadAt);
    }

    [Fact]
    public void A_forgotten_pull_request_is_gone_and_the_others_stay()
    {
        var memory = Memory();
        memory.Remember(new RememberedPullRequest("JSdotNet/Backlog", 1482, PullRequestOutcome.Merged, Morning));
        memory.Remember(new RememberedPullRequest("JSdotNet/Backlog", 1490, PullRequestOutcome.Closed, Morning));

        memory.Forget("JSDOTNET/BACKLOG", 1490);

        Assert.Equal(1482, Assert.Single(memory.ReadAll()).Number);
    }

    [Fact]
    public void Nothing_remembered_and_no_folder_reads_as_empty()
    {
        Assert.Empty(new PullRequestStateMemory(() => Path.Combine(_root, "never-made")).ReadAll());
    }

    /// <summary>A half-written file, or one an older or newer build wrote in another
    /// shape — the folder is shared by every checkout on the machine — is a miss for
    /// that one pull request, never a throw and never a guess.</summary>
    [Fact]
    public void A_corrupt_or_other_version_file_is_a_miss_and_the_rest_still_read()
    {
        var memory = Memory();
        memory.Remember(new RememberedPullRequest("JSdotNet/Backlog", 1482, PullRequestOutcome.Merged, Morning));
        memory.Remember(new RememberedPullRequest("JSdotNet/Backlog", 1483, PullRequestOutcome.Merged, Morning));
        memory.Remember(new RememberedPullRequest("JSdotNet/Backlog", 1489, PullRequestOutcome.Merged, Morning));

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
        var first = Memory();
        var second = Memory();

        first.Remember(new RememberedPullRequest("JSdotNet/Backlog", 1482, PullRequestOutcome.Merged, Morning));
        second.Remember(new RememberedPullRequest("JSdotNet/Backlog", 1483, PullRequestOutcome.Merged, Morning));

        Assert.Equal([1482, 1483], first.ReadAll().Select(pr => pr.Number).Order());
        Assert.All(Directory.GetFiles(_root, "*", SearchOption.AllDirectories), f => Assert.EndsWith(".json", f, StringComparison.Ordinal));
    }
}
