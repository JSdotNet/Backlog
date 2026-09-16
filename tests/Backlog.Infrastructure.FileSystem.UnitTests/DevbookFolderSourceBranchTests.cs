using Backlog.Infrastructure.FileSystem;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// What a knowledge folder resolves to when its repository is read from a
/// branch, and — the point of the whole change — whether the caller may write to
/// what it was handed.
/// </summary>
public class DevbookFolderSourceBranchTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "devbook-folder-source-branch-tests-" + Guid.NewGuid().ToString("N"));

    private string WorkspaceRoot => Path.Combine(_root, "workspace");

    private string AppData => Path.Combine(_root, "appdata");

    public DevbookFolderSourceBranchTests() => Directory.CreateDirectory(_root);

    private GitHubSettingsStore Settings()
    {
        var store = new GitHubSettingsStore(Path.Combine(_root, "github.json"), () => WorkspaceRoot);
        Assert.Null(store.SetRepositories([new GitHubRepositoryRef("backlog", "JSdotNet", "Backlog")]));
        return store;
    }

    private WorkspaceSettingsStore Workspace() => new(AppData, Path.Combine(AppData, "settings.json"));

    private DevbookFolderSource Source(GitHubSettingsStore settings, IDevbookSnapshotCache? snapshots) =>
        new(settings, Workspace(), snapshots);

    /// <summary>Waits for whatever background fetch a resolve started, so the
    /// next resolve sees its outcome rather than racing it. A gated stub is
    /// released here, after the pending fetch has been taken hold of: released
    /// earlier, the fetch can finish before there is anything to wait on.</summary>
    private static async Task Settle(DevbookFolderSource source, GitHubSettingsStore settings, StubSnapshotCache cache)
    {
        var pending = source.PendingFetch(settings.Current.Find("backlog")!);
        cache.FetchGate?.TrySetResult();
        if (pending is not null) await pending;
    }

    // --- Reading a branch -----------------------------------------------------

    [Fact]
    public void A_fetched_branch_resolves_read_only()
    {
        var settings = Settings();
        var snapshot = Path.Combine(_root, "snapshot");

        var location = Source(settings, new StubSnapshotCache(snapshot, fetched: true, ".domain/context-map.md")).Resolve(".domain", "backlog");

        Assert.True(location.Available);
        Assert.Equal(DevbookSourceKind.Branch, location.Source);
        Assert.False(location.CanEdit);
        Assert.Equal(Path.Combine(snapshot, ".domain"), location.FullPath);
    }

    /// <summary>The folder's existence is a fact about the commit, read off the
    /// index — not about the disk, where nothing is until a reader asks. This is
    /// what lets the menu list an area before one of its chapters is here.</summary>
    [Fact]
    public void A_folder_the_index_names_resolves_before_any_file_is_on_disk()
    {
        var settings = Settings();
        var snapshot = Path.Combine(_root, "snapshot");

        var location = Source(settings, new StubSnapshotCache(snapshot, fetched: true, ".arc42/01-intro.md")).Resolve(".arc42", "backlog");

        Assert.True(location.Available);
        Assert.False(Directory.Exists(Path.Combine(snapshot, ".arc42")));
    }

    /// <summary>Resolution never waits on the network — it runs on every panel
    /// load, and one that blocked on GitHub would put it in front of opening a
    /// tab. But a branch nobody has fetched is fetched, in the background, from
    /// the first resolve: the answer in the meantime is "fetching", as news
    /// rather than as an error, and the second resolve does not start a second
    /// download.</summary>
    [Fact]
    public async Task A_branch_nobody_has_fetched_is_fetched_in_the_background()
    {
        var settings = Settings();
        var snapshot = Path.Combine(_root, "snapshot");
        var cache = new StubSnapshotCache(snapshot, fetched: false, ".domain/context-map.md") { FetchGate = new TaskCompletionSource() };
        var source = Source(settings, cache);

        var first = source.Resolve(".domain", "backlog");
        var second = source.Resolve(".domain", "backlog");

        Assert.False(first.Available);
        Assert.True(first.Pending);
        Assert.Equal(DevbookSourceKind.Branch, first.Source);
        Assert.Contains("Fetching the default branch of JSdotNet/Backlog", first.Message, StringComparison.Ordinal);
        Assert.True(second.Pending);

        // Let the download land, as the stub's fetch does by making the index
        // appear, and the folder is there without anybody pressing anything.
        await Settle(source, settings, cache);

        Assert.Equal(1, cache.Fetches);
        var landed = source.Resolve(".domain", "backlog");
        Assert.True(landed.Available);
        Assert.False(landed.Pending);
    }

    /// <summary>What the panels listen for. The pane's own update control
    /// announces a pull the same way, and a panel showing "fetching" has no
    /// other way to learn it can stop.</summary>
    [Fact]
    public async Task A_fetch_that_lands_announces_the_content_changed()
    {
        var settings = Settings();
        var cache = new StubSnapshotCache(Path.Combine(_root, "snapshot"), fetched: false) { FetchGate = new TaskCompletionSource() };
        var source = Source(settings, cache);
        var announced = 0;
        source.Changed += () => announced++;

        source.Resolve(".domain", "backlog");
        await Settle(source, settings, cache);

        Assert.Equal(1, announced);
    }

    /// <summary>A download that came back with nothing is the one case that is
    /// an error, and the words are the adapter's — a 404 for a private repository
    /// with no token, say — rather than a paraphrase. It is not retried on the
    /// next render: once per interval, or when the settings change.</summary>
    [Fact]
    public async Task A_failed_first_fetch_reports_the_reason_and_does_not_retry_per_resolve()
    {
        var settings = Settings();
        var cache = new StubSnapshotCache(Path.Combine(_root, "snapshot"), fetched: false) { Failure = "Not Found" };
        var source = Source(settings, cache);

        source.Resolve(".domain", "backlog");
        await Settle(source, settings, cache);

        var location = source.Resolve(".domain", "backlog");
        source.Resolve(".domain", "backlog");

        Assert.False(location.Available);
        Assert.False(location.Pending);
        Assert.Contains("Could not fetch the default branch of JSdotNet/Backlog", location.Message, StringComparison.Ordinal);
        Assert.Contains("Not Found", location.Message, StringComparison.Ordinal);
        Assert.Equal(1, cache.Fetches);
    }

    /// <summary>A settings change may be the fix for the failure being
    /// remembered — a token added — so it is forgotten, and the next resolve
    /// asks again.</summary>
    [Fact]
    public async Task Changing_the_repository_settings_retries_a_remembered_failure()
    {
        var settings = Settings();
        var cache = new StubSnapshotCache(Path.Combine(_root, "snapshot"), fetched: false) { Failure = "Not Found" };
        var source = Source(settings, cache);

        source.Resolve(".domain", "backlog");
        await Settle(source, settings, cache);
        Assert.Equal(1, cache.Fetches);

        Assert.Null(settings.SetDevbookSource("backlog", "main", useLocalFolder: false));
        source.Resolve(".domain", "backlog");
        await Settle(source, settings, cache);

        Assert.Equal(2, cache.Fetches);
    }

    /// <summary>A snapshot on disk is served at once; the head check behind it is
    /// the auto-fetch's business and never stands between the reader and the
    /// folder.</summary>
    [Fact]
    public void A_fetched_branch_is_served_while_it_is_re_checked()
    {
        var settings = Settings();
        var snapshot = Path.Combine(_root, "snapshot");
        var cache = new StubSnapshotCache(snapshot, fetched: true, ".domain/context-map.md") { FetchGate = new TaskCompletionSource() };

        var location = Source(settings, cache).Resolve(".domain", "backlog");

        Assert.True(location.Available);
        Assert.False(location.Pending);
    }

    /// <summary>"Which of these am I reading?" is a real question the moment the
    /// same repository can be read two ways.</summary>
    [Fact]
    public void The_scope_label_names_the_branch()
    {
        var settings = Settings();
        Assert.Null(settings.SetDevbookSource("backlog", "release/2.0", useLocalFolder: false));

        var snapshot = Path.Combine(_root, "snapshot");

        var location = Source(settings, new StubSnapshotCache(snapshot, fetched: true, ".domain/context-map.md")).Resolve(".domain", "backlog");

        Assert.Equal("JSdotNet/Backlog (release/2.0)", location.ScopeLabel);
    }

    /// <summary>The folder is not missing from somebody's disk, it is missing
    /// from the commit — so telling them to check their clone would send them
    /// looking somewhere that has nothing to do with it.</summary>
    [Fact]
    public void A_folder_the_branch_does_not_carry_says_the_branch_lacks_it()
    {
        var settings = Settings();
        var snapshot = Path.Combine(_root, "snapshot");

        var location = Source(settings, new StubSnapshotCache(snapshot, fetched: true, ".domain/context-map.md")).Resolve(".design", "backlog");

        Assert.False(location.Available);
        Assert.Contains("has no Design knowledge folder", location.Message, StringComparison.Ordinal);
    }

    // --- Reading a clone ------------------------------------------------------

    /// <summary>The upgrade case, at the resolution end: a repository configured
    /// before branch loading existed keeps resolving to its clone, editable.</summary>
    [Fact]
    public void A_configured_clone_still_resolves_writable()
    {
        var settings = Settings();
        var clone = Path.Combine(_root, "clone");
        Directory.CreateDirectory(Path.Combine(clone, ".domain"));
        Assert.Null(settings.SetCloneDirectory("backlog", clone));

        var location = Source(settings, new StubSnapshotCache(Path.Combine(_root, "snapshot"), fetched: true))
            .Resolve(".domain", "backlog");

        Assert.True(location.Available);
        Assert.Equal(DevbookSourceKind.LocalFolder, location.Source);
        Assert.True(location.CanEdit);
        Assert.Equal(Path.Combine(clone, ".domain"), location.FullPath);
    }

    [Fact]
    public void Choosing_the_branch_over_a_clone_reads_the_snapshot()
    {
        var settings = Settings();
        var clone = Path.Combine(_root, "clone");
        Directory.CreateDirectory(Path.Combine(clone, ".domain"));
        Assert.Null(settings.SetCloneDirectory("backlog", clone));
        Assert.Null(settings.SetDevbookSource("backlog", "main", useLocalFolder: false));

        var snapshot = Path.Combine(_root, "snapshot");

        var location = Source(settings, new StubSnapshotCache(snapshot, fetched: true, ".domain/context-map.md")).Resolve(".domain", "backlog");

        Assert.Equal(Path.Combine(snapshot, ".domain"), location.FullPath);
        Assert.False(location.CanEdit);
    }

    // --- Preparing a branch ---------------------------------------------------

    /// <summary>Listing is the index and the reading-order files, and nothing
    /// else: a menu costs one listing, never a chapter. The listing joins the
    /// download the resolve inside it started, so one menu is one fetch.</summary>
    [Fact]
    public async Task Preparing_the_listing_of_an_unfetched_branch_fetches_the_index_once_and_no_chapter()
    {
        var settings = Settings();
        var cache = new StubSnapshotCache(Path.Combine(_root, "snapshot"), fetched: false, ".arc42/01-intro.md", ".arc42/_reading-order.json");

        var location = await Source(settings, cache).PrepareListingAsync(".arc42", "backlog", TestContext.Current.CancellationToken);

        Assert.True(location.Available);
        Assert.False(location.Pending);
        Assert.Equal(1, cache.Fetches);
        Assert.Equal([["**/_reading-order.json", "**/_meta/index.json"]], cache.Selections);
    }

    /// <summary>An index on disk is served at once. The head re-check the
    /// auto-fetch starts behind it is its own business — the listing neither
    /// waits for it nor starts a second one.</summary>
    [Fact]
    public async Task Preparing_the_listing_of_a_fetched_branch_serves_the_index_while_the_head_is_rechecked()
    {
        var settings = Settings();
        var cache = new StubSnapshotCache(Path.Combine(_root, "snapshot"), fetched: true, ".arc42/01-intro.md") { FetchGate = new TaskCompletionSource() };
        var source = Source(settings, cache);

        var location = await source.PrepareListingAsync(".arc42", "backlog", TestContext.Current.CancellationToken);
        await source.PrepareListingAsync(".arc42", "backlog", TestContext.Current.CancellationToken);

        Assert.True(location.Available);
        Assert.Equal(1, cache.Fetches);

        await Settle(source, settings, cache);
    }

    /// <summary>The area, whole, and without the rendered diagram artifacts
    /// beside its chapters — they are the bulk of the folder and nothing reads
    /// them until a rendered diagram is shown.</summary>
    [Fact]
    public async Task Preparing_content_fetches_the_areas_folder_without_rendered_artifacts()
    {
        var settings = Settings();
        var cache = new StubSnapshotCache(Path.Combine(_root, "snapshot"), fetched: true, ".arc42/01-intro.md");

        await Source(settings, cache).PrepareContentAsync(".arc42", "backlog", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal([".arc42/", "!_archify"], cache.Selections.Last());
    }

    /// <summary>A caller naming what it needs names it relative to the folder;
    /// the source spells it against the repository.</summary>
    [Fact]
    public async Task Preparing_named_paths_spells_them_against_the_folder()
    {
        var settings = Settings();
        var cache = new StubSnapshotCache(Path.Combine(_root, "snapshot"), fetched: true, ".arc42/01-intro.md");

        await Source(settings, cache).PrepareContentAsync(".arc42", "backlog", ["_c4/", "_meta/graph.json", "**/AGENTS.md", "!bin"], TestContext.Current.CancellationToken);

        Assert.Equal([".arc42/_c4/", ".arc42/_meta/graph.json", "**/AGENTS.md", "!bin"], cache.Selections.Last());
    }

    /// <summary>The instructions folder is the repository root, so a folder-
    /// relative path is already a repository-relative one.</summary>
    [Fact]
    public async Task Preparing_the_instructions_area_names_paths_from_the_root()
    {
        var settings = Settings();
        var cache = new StubSnapshotCache(Path.Combine(_root, "snapshot"), fetched: true, ".github/copilot-instructions.md");

        await Source(settings, cache).PrepareContentAsync("instructions", "backlog", [".github/", "CLAUDE.md"], TestContext.Current.CancellationToken);

        Assert.Equal([".github/", "CLAUDE.md"], cache.Selections.Last());
    }

    [Fact]
    public async Task A_local_folder_prepares_without_touching_the_snapshots()
    {
        var settings = Settings();
        var clone = Path.Combine(_root, "clone");
        Directory.CreateDirectory(Path.Combine(clone, ".domain"));
        Assert.Null(settings.SetCloneDirectory("backlog", clone));
        var cache = new StubSnapshotCache(Path.Combine(_root, "snapshot"), fetched: false);

        var location = await Source(settings, cache).PrepareContentAsync(".domain", "backlog", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(location.Available);
        Assert.Equal(0, cache.Fetches);
        Assert.Empty(cache.Selections);
    }

    /// <summary>GitHub's reason, not a paraphrase, and only when there is nothing
    /// here to read instead — and the listing does not ask again on its own,
    /// because the auto-fetch is rationing exactly that.</summary>
    [Fact]
    public async Task A_branch_that_cannot_be_fetched_reports_the_reason_without_asking_again()
    {
        var settings = Settings();
        var cache = new StubSnapshotCache(Path.Combine(_root, "snapshot"), fetched: false) { Failure = "GitHub refused the listing." };
        var source = Source(settings, cache);

        var location = await source.PrepareListingAsync(".arc42", "backlog", TestContext.Current.CancellationToken);
        var again = await source.PrepareListingAsync(".arc42", "backlog", TestContext.Current.CancellationToken);

        Assert.False(location.Available);
        Assert.False(location.Pending);
        Assert.Contains("GitHub refused the listing.", location.Message, StringComparison.Ordinal);
        Assert.Equal(DevbookSourceKind.Branch, location.Source);
        Assert.False(again.Available);
        Assert.Equal(1, cache.Fetches);
    }

    [Fact]
    public async Task A_folder_with_nothing_fetched_is_unavailable_when_its_files_cannot_be_fetched()
    {
        var settings = Settings();
        var cache = new StubSnapshotCache(Path.Combine(_root, "snapshot"), fetched: true, ".arc42/01-intro.md") { Failure = "Offline." };

        var location = await Source(settings, cache).PrepareContentAsync(".arc42", "backlog", cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(location.Available);
        Assert.Equal("Offline.", location.Message);
    }

    /// <summary>What was fetched before stays readable when the network is not
    /// there — the property the whole snapshot design exists to keep.</summary>
    [Fact]
    public async Task A_folder_with_something_fetched_stays_available_when_more_cannot_be_fetched()
    {
        var settings = Settings();
        var cache = new StubSnapshotCache(Path.Combine(_root, "snapshot"), fetched: true, ".arc42/01-intro.md", ".arc42/02-goals.md")
        {
            Failure = "Offline.",
            FetchedPaths = { ".arc42/01-intro.md" }
        };

        var location = await Source(settings, cache).PrepareContentAsync(".arc42", "backlog", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(location.Available);
    }

    // --- Listing a branch -----------------------------------------------------

    /// <summary>The tree a branch hands back lists what the commit has, at the
    /// paths the files will be at, whether or not they are here yet.</summary>
    [Fact]
    public void The_file_tree_of_a_branch_lists_the_index()
    {
        var settings = Settings();
        var snapshot = Path.Combine(_root, "snapshot");
        var source = Source(settings, new StubSnapshotCache(snapshot, fetched: true, ".arc42/01-intro.md", ".arc42/adr/0001-x.md", ".arc42/_meta/index.json"));

        var location = source.Resolve(".arc42", "backlog");
        var tree = source.FileTree(location);

        Assert.IsType<DevbookSnapshotFileTree>(tree);
        Assert.Equal([Path.Combine(snapshot, ".arc42", "01-intro.md")], tree.EnumerateFiles(location.FullPath!, "*.md"));
        Assert.Equal([Path.Combine(snapshot, ".arc42", "_meta"), Path.Combine(snapshot, ".arc42", "adr")], tree.EnumerateDirectories(location.FullPath!).Order(StringComparer.Ordinal));
        Assert.True(tree.FileExists(Path.Combine(snapshot, ".arc42", "adr", "0001-x.md")));
        Assert.False(File.Exists(Path.Combine(snapshot, ".arc42", "01-intro.md")));
    }

    [Fact]
    public void The_file_tree_of_a_clone_is_the_disk()
    {
        var settings = Settings();
        var clone = Path.Combine(_root, "clone");
        Directory.CreateDirectory(Path.Combine(clone, ".domain"));
        Assert.Null(settings.SetCloneDirectory("backlog", clone));
        var source = Source(settings, new StubSnapshotCache(Path.Combine(_root, "snapshot"), fetched: true));

        var tree = source.FileTree(source.Resolve(".domain", "backlog"));

        Assert.Same(DevbookDiskFileTree.Instance, tree);
    }

    /// <summary>A composition that never built a snapshot cache answers exactly
    /// what it answered before branch loading existed.</summary>
    [Fact]
    public void With_no_snapshot_cache_a_repository_with_no_clone_asks_for_one()
    {
        var location = Source(Settings(), snapshots: null).Resolve(".domain", "backlog");

        Assert.False(location.Available);
        Assert.Contains("Add a local clone directory", location.Message, StringComparison.Ordinal);
    }

    /// <summary>The storage folder is never read as a devbook, however it
    /// happens to be laid out: an unscoped ask is told to pick a repository.</summary>
    [Fact]
    public void The_storage_folder_is_not_a_scope()
    {
        var workspace = Workspace();
        Directory.CreateDirectory(Path.Combine(workspace.RootDirectory, ".domain"));

        var source = new DevbookFolderSource(Settings(), workspace);
        var location = source.Resolve(".domain");

        Assert.False(location.Available);
        Assert.Equal(DevbookFolderSource.NoRepositoryScoped, location.Message);
        Assert.Empty(source.Folders(null));
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

        GC.SuppressFinalize(this);
    }

    /// <summary>Counts fetches, because "resolution starts one download and only
    /// one" is one of the things under test, and records every selection asked
    /// of it, because what a reader asks for is the other. A fetch makes the
    /// index appear — the <paramref name="files"/> it lists — unless it was told
    /// to fail; <see cref="FetchGate"/> holds it open so a test can look at the
    /// world mid-download.</summary>
    private sealed class StubSnapshotCache(string path, bool fetched, params string[] files) : IDevbookSnapshotCache
    {
        private readonly object _gate = new();
        private bool _fetched = fetched;

        public int Fetches { get; private set; }

        public List<string[]> Selections { get; } = [];

        public TaskCompletionSource? FetchGate { get; init; }

        /// <summary>When set, every fetch and ensure fails with this reason.</summary>
        public string? Failure { get; init; }

        public HashSet<string> FetchedPaths { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string SnapshotPath(GitHubRepositoryRef repository, string? branch) => path;

        public DevbookSnapshot? TryRead(GitHubRepositoryRef repository, string? branch)
        {
            lock (_gate)
            {
                return _fetched ? new DevbookSnapshot(branch ?? "main", "sha-1", DateTimeOffset.UtcNow) : null;
            }
        }

        public DevbookSnapshotIndex? TryReadIndex(GitHubRepositoryRef repository, string? branch)
        {
            if (TryRead(repository, branch) is not { } snapshot) return null;

            var entries = new List<DevbookSnapshotEntry>();
            var directories = new HashSet<string>(StringComparer.Ordinal);

            foreach (var file in files)
            {
                var parts = file.Split('/');
                for (var depth = 1; depth < parts.Length; depth++)
                {
                    var directory = string.Join('/', parts[..depth]);
                    if (directories.Add(directory)) entries.Add(new DevbookSnapshotEntry(directory, "tree", true));
                }

                entries.Add(new DevbookSnapshotEntry(file, "blob", false));
            }

            return new DevbookSnapshotIndex(snapshot, entries, FetchedPaths, false);
        }

        public async Task<DevbookSnapshotResult> FetchAsync(
            GitHubRepositoryRef repository,
            string? branch,
            CancellationToken cancellationToken = default)
        {
            lock (_gate) Fetches++;

            if (FetchGate is not null) await FetchGate.Task;

            if (Failure is not null) return DevbookSnapshotResult.Failed(TryRead(repository, branch), Failure);

            lock (_gate) _fetched = true;

            return new DevbookSnapshotResult(TryRead(repository, branch), true, false, null);
        }

        public Task<DevbookSnapshotResult> CheckAsync(
            GitHubRepositoryRef repository,
            string? branch,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new DevbookSnapshotResult(TryRead(repository, branch), false, false, null));

        public Task<DevbookSnapshotResult> EnsureAsync(
            GitHubRepositoryRef repository,
            string? branch,
            IReadOnlyCollection<string> selection,
            CancellationToken cancellationToken = default)
        {
            Selections.Add([.. selection]);

            return Task.FromResult(Failure is not null
                ? DevbookSnapshotResult.Failed(TryRead(repository, branch), Failure)
                : new DevbookSnapshotResult(TryRead(repository, branch), false, false, null));
        }
    }
}
