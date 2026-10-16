using Backlog.Desktop.UI.Devbook;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The branch overview the Devbook pane loads once when it opens on a repository:
/// every branch, what each changes in the devbook against the default branch, how
/// far ahead and behind it is, and whether the local clone is current.
/// </summary>
public sealed class DevbookBranchOverviewTests : IDisposable
{
    private const string Alias = "backlog";

    private static readonly GitHubBranchHead DefaultHead = new("main", "sha-main");

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "devbook-branch-overview-tests-" + Guid.NewGuid().ToString("N"));

    public DevbookBranchOverviewTests() => Directory.CreateDirectory(_root);

    // --- Loading once ----------------------------------------------------------

    /// <summary>One listing, one default-branch lookup, one comparison per branch
    /// other than the default, and one look at the clone.</summary>
    [Fact]
    public async Task The_first_load_lists_compares_every_branch_and_checks_the_clone()
    {
        var harness = Harness(withClone: true, branches: ["main", "feature/a", "feature/b"]);

        var list = await harness.Overview.LoadAsync(Alias, TestContext.Current.CancellationToken);

        Assert.Null(list.Error);
        Assert.Equal("main", list.DefaultBranch);
        Assert.Equal(["feature/a", "feature/b"], list.Branches.Select(row => row.Branch));
        Assert.Equal(1, harness.Catalog.HeadListings);
        Assert.Equal(["sha-feature/a", "sha-feature/b"], harness.Comparisons.Compared.Select(pair => pair.Head).Order());
        Assert.All(harness.Comparisons.Compared, pair => Assert.Equal("sha-main", pair.Base));
        Assert.Equal(1, harness.Git.ChecksRequested);
        Assert.NotNull(list.Local);
    }

    [Fact]
    public async Task A_second_load_answers_from_the_first_and_asks_nothing()
    {
        var harness = Harness(withClone: true, branches: ["main", "feature/a"]);
        var first = await harness.Overview.LoadAsync(Alias, TestContext.Current.CancellationToken);

        // Another caller, on a token of its own.
        using var another = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var second = await harness.Overview.LoadAsync(Alias, another.Token);

        Assert.Same(first, second);
        Assert.Equal(1, harness.Catalog.HeadListings);
        Assert.Equal(1, harness.Catalog.Resolves);
        Assert.Single(harness.Comparisons.Compared);
        Assert.Equal(1, harness.Git.ChecksRequested);
    }

    /// <summary>Two windows opening the pane at once share one load.</summary>
    [Fact]
    public async Task Loads_asked_for_at_once_share_one()
    {
        var harness = Harness(branches: ["main", "feature/a"]);
        harness.Comparisons.Gate = new TaskCompletionSource();

        var first = harness.Overview.LoadAsync(Alias, TestContext.Current.CancellationToken);
        var second = harness.Overview.LoadAsync(Alias, TestContext.Current.CancellationToken);
        Assert.True(harness.Overview.IsLoading(Alias));
        harness.Comparisons.Gate.SetResult();

        Assert.Same(await first, await second);
        Assert.Equal(1, harness.Catalog.HeadListings);
        Assert.Single(harness.Comparisons.Compared);
        Assert.False(harness.Overview.IsLoading(Alias));
    }

    /// <summary>A pane that closes stops its own wait; the window still waiting
    /// gets the whole answer.</summary>
    [Fact]
    public async Task One_caller_giving_up_does_not_cancel_the_shared_load()
    {
        var harness = Harness(branches: ["main", "feature/a"]);
        harness.Comparisons.Gate = new TaskCompletionSource();
        harness.Comparisons.Changes["sha-feature/a"] = [Changed(".devbook/arc42/adr/0001.md")];

        using var leaving = new CancellationTokenSource();
        var abandoned = harness.Overview.LoadAsync(Alias, leaving.Token);
        var staying = harness.Overview.LoadAsync(Alias, TestContext.Current.CancellationToken);

        await leaving.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned);
        harness.Comparisons.Gate.SetResult();

        var list = await staying;
        Assert.Equal(["arc42"], Assert.Single(list.DevbookBranches).Change!.Areas);
    }

    [Fact]
    public async Task What_is_loaded_is_there_to_read_without_waiting()
    {
        var harness = Harness(branches: ["main", "feature/a"]);
        Assert.Null(harness.Overview.Current(Alias));

        var list = await harness.Overview.LoadAsync(Alias, TestContext.Current.CancellationToken);

        Assert.Same(list, harness.Overview.Current(Alias));
    }

    /// <summary>The one way to ask again within a session.</summary>
    [Fact]
    public async Task A_refresh_loads_again()
    {
        var harness = Harness(branches: ["main", "feature/a"]);
        await harness.Overview.LoadAsync(Alias, TestContext.Current.CancellationToken);

        await harness.Overview.RefreshAsync(Alias, TestContext.Current.CancellationToken);

        Assert.Equal(2, harness.Catalog.HeadListings);
    }

    [Fact]
    public async Task A_repository_with_no_clone_is_not_checked_for_one()
    {
        var harness = Harness(withClone: false, branches: ["main"]);

        var list = await harness.Overview.LoadAsync(Alias, TestContext.Current.CancellationToken);

        Assert.Equal(0, harness.Git.ChecksRequested);
        Assert.Null(list.Local);
    }

    [Fact]
    public async Task No_repository_loads_nothing()
    {
        var harness = Harness(branches: ["main"]);

        var list = await harness.Overview.LoadAsync("nobody", TestContext.Current.CancellationToken);

        Assert.Empty(list.Branches);
        Assert.Equal(0, harness.Catalog.HeadListings);
    }

    // --- What a branch changes ----------------------------------------------

    [Fact]
    public async Task Ahead_and_behind_come_from_the_comparison()
    {
        var harness = Harness(branches: ["main", "feature/a"]);
        harness.Comparisons.Changes["sha-feature/a"] = [Changed(".devbook/domain/tasks/domain.md")];
        harness.Comparisons.Counts["sha-feature/a"] = (2, 5);

        var list = await harness.Overview.LoadAsync(Alias, TestContext.Current.CancellationToken);

        var change = Assert.Single(list.Branches).Change!;
        Assert.Equal(2, change.AheadBy);
        Assert.Equal(5, change.BehindBy);
    }

    /// <summary>Each section once, in the order the tabs show them, whichever
    /// order GitHub listed the files in.</summary>
    [Fact]
    public async Task A_branch_changes_the_sections_its_files_are_under_in_tab_order()
    {
        var harness = Harness(branches: ["main", "feature/a"]);
        harness.Comparisons.Changes["sha-feature/a"] =
        [
            Changed(".devbook/design/tokens.md"),
            Changed("src/Program.cs"),
            Changed(".devbook/arc42/README.md"),
            Changed(".devbook/design/images/strip.png")
        ];

        var list = await harness.Overview.LoadAsync(Alias, TestContext.Current.CancellationToken);

        Assert.Equal(["arc42", "design"], Assert.Single(list.Branches).Change!.Areas);
    }

    /// <summary>A repository still on the root layout keeps its sections.</summary>
    [Fact]
    public async Task A_legacy_root_folder_counts_as_its_section()
    {
        var harness = Harness(branches: ["main", "feature/a"]);
        harness.Comparisons.Changes["sha-feature/a"] = [Changed(".tech/dotnet.md")];

        var list = await harness.Overview.LoadAsync(Alias, TestContext.Current.CancellationToken);

        Assert.Equal(["tech"], Assert.Single(list.Branches).Change!.Areas);
    }

    [Fact]
    public async Task A_folder_pointed_elsewhere_counts_as_its_section()
    {
        var folders = DevbookFolderSetting.Defaults()
            .Select(folder => folder.Key == ".design" ? folder with { Path = "docs/design" } : folder)
            .ToList();
        var harness = Harness(branches: ["main", "feature/a"], folders: folders);
        harness.Comparisons.Changes["sha-feature/a"] = [Changed("docs/design/colour.md")];

        var list = await harness.Overview.LoadAsync(Alias, TestContext.Current.CancellationToken);

        Assert.Equal(["design"], Assert.Single(list.Branches).Change!.Areas);
    }

    /// <summary>The instructions tab reads the agent instruction files, and a
    /// branch changing only those changes no devbook section.</summary>
    [Fact]
    public async Task Code_and_instruction_files_change_no_section()
    {
        var harness = Harness(branches: ["main", "feature/a"]);
        harness.Comparisons.Changes["sha-feature/a"] =
        [
            Changed("src/Program.cs"),
            Changed("AGENTS.md"),
            Changed(".agents/rules/naming.md"),
            Changed(".github/instructions/csharp.instructions.md")
        ];

        var list = await harness.Overview.LoadAsync(Alias, TestContext.Current.CancellationToken);

        var row = Assert.Single(list.Branches);
        Assert.Empty(row.Change!.Areas);
        Assert.False(row.ChangesDevbook);
        Assert.Empty(list.DevbookBranches);
    }

    /// <summary>A rename out of the devbook still changed it.</summary>
    [Fact]
    public async Task A_file_renamed_out_of_a_section_changed_it()
    {
        var harness = Harness(branches: ["main", "feature/a"]);
        harness.Comparisons.Changes["sha-feature/a"] =
        [
            new GitHubChangedFile("docs/old-adr.md", ".devbook/arc42/adr/0002.md", GitHubFileStatus.Renamed, "blob")
        ];

        var list = await harness.Overview.LoadAsync(Alias, TestContext.Current.CancellationToken);

        Assert.Equal(["arc42"], Assert.Single(list.Branches).Change!.Areas);
    }

    /// <summary>GitHub stops listing files at three hundred, so a full list with
    /// no devbook file in it may still hide one.</summary>
    [Fact]
    public async Task A_cut_short_file_list_with_no_devbook_file_may_change_it()
    {
        var harness = Harness(branches: ["main", "feature/a"]);
        harness.Comparisons.Changes["sha-feature/a"] =
            [.. Enumerable.Range(0, GitHubBranchComparison.FileListLimit).Select(i => Changed($"src/File{i}.cs"))];

        var list = await harness.Overview.LoadAsync(Alias, TestContext.Current.CancellationToken);

        var row = Assert.Single(list.Branches);
        Assert.True(row.Change!.MayChange);
        Assert.Empty(row.Change.Areas);
        Assert.True(row.ChangesDevbook);
    }

    // --- Failures --------------------------------------------------------------

    [Fact]
    public async Task A_comparison_that_fails_leaves_that_branch_out_and_nothing_else()
    {
        var harness = Harness(branches: ["main", "feature/a", "feature/b"]);
        harness.Comparisons.Failing.Add("sha-feature/a");
        harness.Comparisons.Changes["sha-feature/b"] = [Changed(".devbook/ai/adoption.md")];

        var list = await harness.Overview.LoadAsync(Alias, TestContext.Current.CancellationToken);

        Assert.Null(list.Error);
        Assert.Equal(["feature/b"], list.DevbookBranches.Select(row => row.Branch));
        Assert.Null(list.Find("feature/a")!.Change);
    }

    [Fact]
    public async Task A_listing_that_fails_comes_back_as_a_message()
    {
        var harness = Harness(withClone: true, branches: ["main"], failure: "API rate limit exceeded.");

        var list = await harness.Overview.LoadAsync(Alias, TestContext.Current.CancellationToken);

        Assert.Equal("API rate limit exceeded.", list.Error);
        Assert.Empty(list.Branches);
        Assert.NotNull(list.Local);
    }

    /// <summary>Not retried on its own: the next session, or a refresh, asks again.</summary>
    [Fact]
    public async Task A_failed_load_is_not_retried_by_loading_again()
    {
        var harness = Harness(branches: ["main"], failure: "No network.");
        await harness.Overview.LoadAsync(Alias, TestContext.Current.CancellationToken);

        await harness.Overview.LoadAsync(Alias, TestContext.Current.CancellationToken);

        Assert.Equal(1, harness.Catalog.HeadListings);
    }

    [Fact]
    public async Task A_clone_check_that_fails_leaves_the_clone_unknown()
    {
        var harness = Harness(withClone: true, branches: ["main", "feature/a"]);
        harness.Git.CheckFailure = new InvalidOperationException("git is not installed.");
        harness.Comparisons.Changes["sha-feature/a"] = [Changed(".devbook/arc42/x.md")];

        var list = await harness.Overview.LoadAsync(Alias, TestContext.Current.CancellationToken);

        Assert.Null(list.Local);
        Assert.Single(list.DevbookBranches);
    }

    /// <summary>Asking again offline must not take away what the last good load
    /// listed: the branches stay, and the refusal is said beside them.</summary>
    [Fact]
    public async Task A_refused_refresh_keeps_the_branches_the_last_load_listed()
    {
        var harness = Harness(branches: ["main", "docs/adr"]);
        harness.Comparisons.Changes["sha-docs/adr"] = [Changed(".devbook/arc42/x.md")];
        await harness.Overview.LoadAsync(Alias, TestContext.Current.CancellationToken);
        harness.Catalog.Failure = "No network.";

        var list = await harness.Overview.RefreshAsync(Alias, TestContext.Current.CancellationToken);

        Assert.Equal("No network.", list.Error);
        Assert.Equal("main", list.DefaultBranch);
        Assert.Equal(["docs/adr"], list.DevbookBranches.Select(row => row.Branch));
        Assert.Same(list, harness.Overview.Current(Alias));
    }

    /// <summary>The load's own deadline stops the comparisons still out, so a
    /// load that ran out of time stops spending the rate limit.</summary>
    [Fact]
    public async Task A_load_that_runs_out_of_time_stops_the_comparisons_it_started()
    {
        var harness = Harness(branches: ["main", "feature/a"], loadTimeout: TimeSpan.FromMilliseconds(100));
        harness.Comparisons.Gate = new TaskCompletionSource();

        var list = await harness.Overview.LoadAsync(Alias, TestContext.Current.CancellationToken);

        Assert.Null(Assert.Single(list.Branches).Change);

        // The load stops waiting on the same deadline the read is stopped by, so
        // the read's own end may land a moment after the load's answer.
        await WaitUntil(() => harness.Comparisons.CancelledReads == 1);
    }

    // --- The clone after a pull ----------------------------------------------

    /// <summary>A pull moves the clone and nothing else, so the clone's answer is
    /// replaced and the branches are not asked about again.</summary>
    [Fact]
    public async Task Recording_the_clone_replaces_its_answer_and_keeps_the_branches()
    {
        var harness = Harness(withClone: true, branches: ["main", "feature/a"]);
        await harness.Overview.LoadAsync(Alias, TestContext.Current.CancellationToken);
        var pulled = new LocalGitRepositoryUpdateCheck(LocalGitRepositoryCurrency.UpToDate, 0, 0, false, "origin/main", "Pulled.");

        harness.Overview.RecordLocal(Alias, pulled);

        var list = harness.Overview.Current(Alias)!;
        Assert.Same(pulled, list.Local);
        Assert.Single(list.Branches);
        Assert.Equal(1, harness.Catalog.HeadListings);
    }

    /// <summary>A pull pressed while a refresh is still out is newer than the
    /// refresh's look at the clone, which began before it; the pull's answer stands.</summary>
    [Fact]
    public async Task A_clone_recorded_during_a_load_is_not_overwritten_by_that_loads_older_look()
    {
        var harness = Harness(withClone: true, branches: ["main", "feature/a"]);
        await harness.Overview.LoadAsync(Alias, TestContext.Current.CancellationToken);
        harness.Comparisons.Gate = new TaskCompletionSource();
        harness.Git.NextCheck = new LocalGitRepositoryUpdateCheck(LocalGitRepositoryCurrency.Behind, 0, 3, false, "origin/main", "3 behind.");

        var refresh = harness.Overview.RefreshAsync(Alias, TestContext.Current.CancellationToken);
        await WaitUntil(() => harness.Git.ChecksRequested == 2);
        var pulled = new LocalGitRepositoryUpdateCheck(LocalGitRepositoryCurrency.UpToDate, 0, 0, false, "origin/main", "Pulled.");
        harness.Overview.RecordLocal(Alias, pulled);
        harness.Comparisons.Gate.SetResult();
        await refresh;

        Assert.Same(pulled, harness.Overview.Current(Alias)!.Local);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(condition());
    }

    // --- Fixture ---------------------------------------------------------------

    private static GitHubChangedFile Changed(string path) => new(path, null, GitHubFileStatus.Modified, "blob-" + path);

    private OverviewHarness Harness(
        IReadOnlyList<string> branches,
        bool withClone = false,
        string? failure = null,
        List<DevbookFolderSetting>? folders = null,
        TimeSpan? loadTimeout = null)
    {
        var store = new GitHubSettingsStore(Path.Combine(_root, "github.json"), () => Path.Combine(_root, "workspace"));
        var repository = new GitHubRepositoryRef(Alias, "JSdotNet", "Backlog") { DevbookFolders = folders ?? DevbookFolderSetting.Defaults() };
        Assert.Null(store.SetRepositories([repository]));

        if (withClone)
        {
            var clone = Path.Combine(_root, "clone");
            Directory.CreateDirectory(clone);
            Assert.Null(store.SetCloneDirectory(Alias, clone));
        }

        var catalog = new StubBranchCatalog
        {
            Failure = failure,
            Heads = { [string.Empty] = DefaultHead }
        };
        catalog.Branches.AddRange(branches);

        var comparisons = new ScriptedBranchComparisons();
        var git = new ScriptedGitRepositoryService();

        return new OverviewHarness(
            new DevbookBranchOverview(store, catalog, new DevbookBranchComparisons(comparisons), git, loadTimeout),
            catalog,
            comparisons,
            git);
    }

    private sealed record OverviewHarness(
        DevbookBranchOverview Overview,
        StubBranchCatalog Catalog,
        ScriptedBranchComparisons Comparisons,
        ScriptedGitRepositoryService Git);

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

/// <summary>
/// A comparison per head commit, each answering with the files a test gave it —
/// none by default — and counting what it was asked.
/// </summary>
internal sealed class ScriptedBranchComparisons : IGitHubBranchComparison
{
    private readonly Lock _gate = new();

    public Dictionary<string, List<GitHubChangedFile>> Changes { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, (int Ahead, int Behind)> Counts { get; } = new(StringComparer.Ordinal);

    public HashSet<string> Failing { get; } = new(StringComparer.Ordinal);

    /// <summary>When set, every comparison waits for it.</summary>
    public TaskCompletionSource? Gate { get; set; }

    public List<(string Base, string Head)> Compared { get; } = [];

    private int _cancelledReads;

    /// <summary>How many comparisons were stopped by the token they were handed.</summary>
    public int CancelledReads => Volatile.Read(ref _cancelledReads);

    public async Task<GitHubComparison> CompareAsync(
        GitHubRepositoryRef repository,
        string baseRef,
        string headRef,
        CancellationToken cancellationToken = default)
    {
        lock (_gate) Compared.Add((baseRef, headRef));

        if (Gate is { } gate)
        {
            try
            {
                await gate.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Interlocked.Increment(ref _cancelledReads);
                throw;
            }
        }

        if (Failing.Contains(headRef)) throw new GitHubException($"GitHub refused {headRef}.");

        var (ahead, behind) = Counts.TryGetValue(headRef, out var counts) ? counts : (1, 0);
        var files = Changes.TryGetValue(headRef, out var changed) ? changed : [];

        return new GitHubComparison("merge-base", ahead, behind, ahead, [], files);
    }

    public Task<GitHubCommitChange> ReadCommitAsync(
        GitHubRepositoryRef repository,
        string commitSha,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The overview reads no commit.");
}
