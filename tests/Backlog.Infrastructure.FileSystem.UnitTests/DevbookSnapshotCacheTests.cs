using System.Text;
using Backlog.Infrastructure.FileSystem;
using Backlog.Infrastructure.GitHub;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The local copy of a repository branch: its index, the files fetched into it
/// on demand, when either is refetched, and what happens to what is already
/// there when a fetch fails.
/// <para>
/// Real temp directories rather than a filesystem abstraction, which is what
/// every other test in this project does — the behaviour under test <em>is</em>
/// the disk, and a fake would be asserting the fake.
/// </para>
/// </summary>
public class DevbookSnapshotCacheTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "devbook-snapshot-cache-tests-" + Guid.NewGuid().ToString("N"));

    private static readonly GitHubRepositoryRef Repository = new("backlog", "JSdotNet", "Backlog");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public DevbookSnapshotCacheTests() => Directory.CreateDirectory(_root);

    private DevbookSnapshotCache Cache(StubTreeClient trees, StubBranchCatalog branches) =>
        new(() => _root, trees, branches);

    private static string TreePath(DevbookSnapshotCache cache, string relative) =>
        Path.Combine(cache.SnapshotPath(Repository, "main"), relative.Replace('/', Path.DirectorySeparatorChar));

    // --- The index ---------------------------------------------------------------

    /// <summary>The whole point: a fetch is one listing, not one archive. Nothing
    /// a chapter reader would open is on disk until it asks.</summary>
    [Fact]
    public async Task A_first_fetch_takes_the_index_and_not_one_chapter()
    {
        var trees = new StubTreeClient()
            .WithFile(".domain/context-map.md", "# Contexts")
            .WithFile(".arc42/01-introduction.md", "# Intro");
        var cache = Cache(trees, new StubBranchCatalog("main", "sha-1"));

        var result = await cache.FetchAsync(Repository, "main", Token);

        Assert.True(result.Updated);
        Assert.Equal(1, trees.Listings);
        Assert.Equal(0, trees.Blobs);

        var index = cache.TryReadIndex(Repository, "main");
        Assert.NotNull(index);
        Assert.Contains(index.Entries, entry => entry.Path == ".domain/context-map.md" && !entry.IsDirectory);
        Assert.Contains(index.Entries, entry => entry.Path == ".arc42" && entry.IsDirectory);
        Assert.Empty(index.Fetched);
        Assert.False(File.Exists(Path.Combine(cache.SnapshotPath(Repository, "main"), ".domain", "context-map.md")));
    }

    [Fact]
    public async Task An_index_already_at_the_branch_head_is_not_listed_again()
    {
        var trees = new StubTreeClient().WithFile(".domain/context-map.md", "x");
        var cache = Cache(trees, new StubBranchCatalog("main", "sha-1"));

        await cache.FetchAsync(Repository, "main", Token);
        var second = await cache.FetchAsync(Repository, "main", Token);

        Assert.Equal(1, trees.Listings);
        Assert.False(second.Updated);
        Assert.False(second.Behind);
    }

    [Fact]
    public async Task A_fetched_index_reads_back_its_branch_and_commit()
    {
        var cache = Cache(new StubTreeClient().WithFile("readme.md", "x"), new StubBranchCatalog("main", "sha-1"));
        await cache.FetchAsync(Repository, "main", Token);

        var snapshot = cache.TryRead(Repository, "main");

        Assert.NotNull(snapshot);
        Assert.Equal("main", snapshot.Branch);
        Assert.Equal("sha-1", snapshot.Sha);
    }

    [Fact]
    public void Nothing_fetched_reads_as_no_snapshot()
    {
        var cache = Cache(new StubTreeClient(), new StubBranchCatalog("main", "sha-1"));

        Assert.Null(cache.TryRead(Repository, "main"));
        Assert.Null(cache.TryReadIndex(Repository, "main"));
    }

    /// <summary>A state file whose index is gone describes a snapshot that is
    /// not there. Reporting it would make every folder resolve to "the branch
    /// has no such folder" rather than "nothing has been fetched yet".</summary>
    [Fact]
    public async Task A_snapshot_whose_index_was_deleted_reads_as_no_snapshot()
    {
        var cache = Cache(new StubTreeClient().WithFile("readme.md", "x"), new StubBranchCatalog("main", "sha-1"));
        await cache.FetchAsync(Repository, "main", Token);

        File.Delete(Path.Combine(Path.GetDirectoryName(cache.SnapshotPath(Repository, "main"))!, "index.json"));

        Assert.Null(cache.TryRead(Repository, "main"));
    }

    // --- Files on demand ---------------------------------------------------------

    [Fact]
    public async Task Ensuring_a_file_fetches_that_blob_and_lays_it_where_the_index_says()
    {
        var trees = new StubTreeClient()
            .WithFile(".domain/context-map.md", "# Contexts")
            .WithFile(".domain/inbox/features.md", "# Inbox");
        var cache = Cache(trees, new StubBranchCatalog("main", "sha-1"));

        var result = await cache.EnsureAsync(Repository, "main", [".domain/context-map.md"], Token);

        Assert.True(result.Updated);
        Assert.Equal(1, trees.Blobs);
        Assert.Equal("# Contexts", File.ReadAllText(TreePath(cache, ".domain/context-map.md")));
        Assert.False(File.Exists(TreePath(cache, ".domain/inbox/features.md")));
        Assert.Equal([".domain/context-map.md"], cache.TryReadIndex(Repository, "main")!.Fetched);
    }

    /// <summary>The first ask for a branch nobody has indexed is what fetches the
    /// index; nobody has to remember to do that first.</summary>
    [Fact]
    public async Task Ensuring_before_any_fetch_takes_the_index_on_the_way()
    {
        var trees = new StubTreeClient().WithFile("readme.md", "hello");
        var cache = Cache(trees, new StubBranchCatalog("main", "sha-1"));

        var result = await cache.EnsureAsync(Repository, "main", ["readme.md"], Token);

        Assert.True(result.Updated);
        Assert.Equal(1, trees.Listings);
        Assert.NotNull(cache.TryRead(Repository, "main"));
        Assert.Equal("hello", File.ReadAllText(TreePath(cache, "readme.md")));
    }

    [Fact]
    public async Task Ensuring_a_subtree_fetches_everything_under_it_and_nothing_beside_it()
    {
        var trees = new StubTreeClient()
            .WithFile(".arc42/01-intro.md", "a")
            .WithFile(".arc42/adr/0001-x.md", "b")
            .WithFile(".domain/context-map.md", "c")
            .WithFile("src/Program.cs", "d");
        var cache = Cache(trees, new StubBranchCatalog("main", "sha-1"));

        await cache.EnsureAsync(Repository, "main", [".arc42/"], Token);

        Assert.Equal(2, trees.Blobs);
        Assert.True(File.Exists(TreePath(cache, ".arc42/01-intro.md")));
        Assert.True(File.Exists(TreePath(cache, ".arc42/adr/0001-x.md")));
        Assert.False(File.Exists(TreePath(cache, ".domain/context-map.md")));
        Assert.False(File.Exists(TreePath(cache, "src/Program.cs")));
    }

    [Fact]
    public async Task An_excluded_directory_is_left_out_of_a_subtree()
    {
        var trees = new StubTreeClient()
            .WithFile(".arc42/01-intro.md", "a")
            .WithFile(".arc42/_archify/01-intro.1.workflow.html", "<html>");
        var cache = Cache(trees, new StubBranchCatalog("main", "sha-1"));

        await cache.EnsureAsync(Repository, "main", [".arc42/", "!_archify"], Token);

        Assert.Equal(1, trees.Blobs);
        Assert.True(File.Exists(TreePath(cache, ".arc42/01-intro.md")));
        Assert.False(File.Exists(TreePath(cache, ".arc42/_archify/01-intro.1.workflow.html")));
    }

    [Fact]
    public async Task A_file_at_any_depth_is_found_by_its_suffix()
    {
        var trees = new StubTreeClient()
            .WithFile(".arc42/_reading-order.json", "{}")
            .WithFile(".domain/inbox/_reading-order.json", "{}")
            .WithFile(".domain/inbox/features.md", "x");
        var cache = Cache(trees, new StubBranchCatalog("main", "sha-1"));

        await cache.EnsureAsync(Repository, "main", ["**/_reading-order.json"], Token);

        Assert.Equal(2, trees.Blobs);
        Assert.True(File.Exists(TreePath(cache, ".arc42/_reading-order.json")));
        Assert.True(File.Exists(TreePath(cache, ".domain/inbox/_reading-order.json")));
        Assert.False(File.Exists(TreePath(cache, ".domain/inbox/features.md")));
    }

    /// <summary>What makes "prepare before every read" affordable: a second ask
    /// for what is already here touches nothing and reaches nowhere.</summary>
    [Fact]
    public async Task A_file_already_here_is_not_fetched_again()
    {
        var trees = new StubTreeClient().WithFile("readme.md", "x");
        var cache = Cache(trees, new StubBranchCatalog("main", "sha-1"));

        await cache.EnsureAsync(Repository, "main", ["readme.md"], Token);
        var second = await cache.EnsureAsync(Repository, "main", ["readme.md"], Token);

        Assert.False(second.Updated);
        Assert.Null(second.Message);
        Assert.Equal(1, trees.Blobs);
    }

    [Fact]
    public async Task A_file_the_index_does_not_name_is_simply_not_fetched()
    {
        var trees = new StubTreeClient().WithFile("readme.md", "x");
        var cache = Cache(trees, new StubBranchCatalog("main", "sha-1"));

        var result = await cache.EnsureAsync(Repository, "main", ["missing.md"], Token);

        Assert.False(result.Updated);
        Assert.Null(result.Message);
        Assert.Equal(0, trees.Blobs);
    }

    // --- The branch moving -------------------------------------------------------

    /// <summary>A refresh keeps every fetched file the new commit still has at
    /// the same content, and drops the ones it changed — so the next ask fetches
    /// exactly what moved and a reader never sees the old version under the new
    /// index.</summary>
    [Fact]
    public async Task A_branch_that_moved_on_keeps_unchanged_files_and_drops_changed_ones()
    {
        var trees = new StubTreeClient()
            .WithFile("keep.md", "same")
            .WithFile("change.md", "first")
            .WithFile("drop.md", "gone");
        var branches = new StubBranchCatalog("main", "sha-1");
        var cache = Cache(trees, branches);

        await cache.EnsureAsync(Repository, "main", ["/"], Token);

        trees.Reset().WithFile("keep.md", "same").WithFile("change.md", "second");
        branches.Sha = "sha-2";

        var result = await cache.FetchAsync(Repository, "main", Token);

        Assert.True(result.Updated);
        Assert.True(File.Exists(TreePath(cache, "keep.md")));
        Assert.False(File.Exists(TreePath(cache, "change.md")));
        Assert.False(File.Exists(TreePath(cache, "drop.md")));
        Assert.Equal(["keep.md"], cache.TryReadIndex(Repository, "main")!.Fetched);

        await cache.EnsureAsync(Repository, "main", ["/"], Token);

        Assert.Equal("second", File.ReadAllText(TreePath(cache, "change.md")));
        Assert.Equal(4, trees.Blobs);
    }

    /// <summary>The archive-based snapshot this replaced left a whole repository
    /// under the tree with no record of it. Nothing vouches for those files, and
    /// a reader would take them for the new commit's, so the first index write
    /// clears them — and the same rule clears anything else the record does not
    /// name.</summary>
    [Fact]
    public async Task Files_the_record_does_not_vouch_for_are_cleared_when_the_index_is_written()
    {
        var trees = new StubTreeClient().WithFile("readme.md", "x");
        var cache = Cache(trees, new StubBranchCatalog("main", "sha-1"));

        var legacy = Path.Combine(cache.SnapshotPath(Repository, "main"), "src", "Program.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
        File.WriteAllText(legacy, "class Program {}");
        File.WriteAllText(Path.Combine(cache.SnapshotPath(Repository, "main"), "readme.md"), "stale");

        await cache.FetchAsync(Repository, "main", Token);

        Assert.False(File.Exists(legacy));
        Assert.False(Directory.Exists(Path.GetDirectoryName(legacy)));
        Assert.False(File.Exists(TreePath(cache, "readme.md")));
        Assert.Empty(cache.TryReadIndex(Repository, "main")!.Fetched);
    }

    /// <summary>Two panels opening one branch at once both find no index and
    /// both go for it. Whichever lands second must not undo the first, and
    /// neither may come back saying the branch is not fetched.</summary>
    [Fact]
    public async Task Two_concurrent_first_fetches_both_end_with_the_index_in_place()
    {
        var trees = new StubTreeClient().WithFile("readme.md", "x");
        var cache = Cache(trees, new StubBranchCatalog("main", "sha-1"));

        var results = await Task.WhenAll(
            cache.FetchAsync(Repository, "main", Token),
            cache.FetchAsync(Repository, "main", Token));

        Assert.All(results, result => Assert.NotNull(result.Snapshot));
        Assert.Equal(1, results.Count(result => result.Updated));
        Assert.NotNull(cache.TryReadIndex(Repository, "main"));
    }

    /// <summary>An index of one commit under a state naming another is the
    /// moment between the two writes of a refresh, or a refresh that died
    /// between them. Either way it reads as "not fetched", never as the wrong
    /// commit's menu.</summary>
    [Fact]
    public async Task An_index_and_a_state_that_disagree_read_as_no_snapshot()
    {
        var cache = Cache(new StubTreeClient().WithFile("readme.md", "x"), new StubBranchCatalog("main", "sha-1"));
        await cache.FetchAsync(Repository, "main", Token);

        var state = Path.Combine(Path.GetDirectoryName(cache.SnapshotPath(Repository, "main"))!, "snapshot.json");
        File.WriteAllText(state, File.ReadAllText(state).Replace("sha-1", "sha-0"));

        Assert.Null(cache.TryRead(Repository, "main"));
    }

    // --- Naming ------------------------------------------------------------------

    /// <summary>The cache is keyed on what was configured, not on what that
    /// resolved to, because resolving needs the network and SnapshotPath may
    /// not.</summary>
    [Fact]
    public void The_default_branch_and_a_named_one_are_different_snapshots()
    {
        var cache = Cache(new StubTreeClient(), new StubBranchCatalog("main", "sha-1"));

        Assert.NotEqual(cache.SnapshotPath(Repository, null), cache.SnapshotPath(Repository, "main"));
    }

    /// <summary>Branch names carry slashes and colons routinely. The digest is
    /// what keeps two names that fold to the same readable text apart.</summary>
    [Fact]
    public void Branch_names_that_fold_to_the_same_text_get_different_folders()
    {
        var cache = Cache(new StubTreeClient(), new StubBranchCatalog("main", "sha-1"));

        Assert.NotEqual(cache.SnapshotPath(Repository, "fix/a"), cache.SnapshotPath(Repository, "fix-a"));
    }

    [Fact]
    public async Task A_branch_name_with_a_slash_lands_in_one_folder()
    {
        var cache = Cache(new StubTreeClient().WithFile("readme.md", "x"), new StubBranchCatalog("release/2.0", "sha-1"));

        var result = await cache.EnsureAsync(Repository, "release/2.0", ["readme.md"], Token);

        Assert.True(result.Updated);
        Assert.True(File.Exists(Path.Combine(cache.SnapshotPath(Repository, "release/2.0"), "readme.md")));
    }

    // --- Checking ----------------------------------------------------------------

    [Fact]
    public async Task Checking_reports_behind_without_listing()
    {
        var trees = new StubTreeClient().WithFile("readme.md", "x");
        var branches = new StubBranchCatalog("main", "sha-1");
        var cache = Cache(trees, branches);

        await cache.FetchAsync(Repository, "main", Token);
        branches.Sha = "sha-2";

        var check = await cache.CheckAsync(Repository, "main", Token);

        Assert.True(check.Behind);
        Assert.Equal(1, trees.Listings);
    }

    [Fact]
    public async Task A_branch_that_is_gone_is_reported_rather_than_thrown()
    {
        var cache = Cache(new StubTreeClient(), new StubBranchCatalog("main", "sha-1") { Missing = true });

        var check = await cache.CheckAsync(Repository, "deleted", Token);

        Assert.False(check.Behind);
        Assert.Contains("no branch called deleted", check.Message, StringComparison.Ordinal);
    }

    // --- Failure -----------------------------------------------------------------

    /// <summary>The index already on disk is what somebody is reading. A refresh
    /// that fails must not take it with it.</summary>
    [Fact]
    public async Task A_failed_listing_leaves_the_previous_index_and_files_readable()
    {
        var trees = new StubTreeClient().WithFile("readme.md", "first");
        var branches = new StubBranchCatalog("main", "sha-1");
        var cache = Cache(trees, branches);

        await cache.EnsureAsync(Repository, "main", ["readme.md"], Token);

        trees.ListingFails = true;
        branches.Sha = "sha-2";

        var result = await cache.FetchAsync(Repository, "main", Token);

        Assert.False(result.Updated);
        Assert.NotNull(result.Message);
        Assert.Equal("first", File.ReadAllText(TreePath(cache, "readme.md")));
        Assert.Equal("sha-1", cache.TryRead(Repository, "main")!.Sha);
    }

    /// <summary>A reader is left with more than it had, never less: what landed
    /// before the failure stays and is recorded, and the reason is reported.</summary>
    [Fact]
    public async Task A_blob_that_fails_reports_the_reason_and_keeps_what_landed()
    {
        var trees = new StubTreeClient().WithFile("ok.md", "fine").WithFile("bad.md", "never");
        trees.FailingBlobs.Add("bad.md");
        var cache = Cache(trees, new StubBranchCatalog("main", "sha-1"));

        var result = await cache.EnsureAsync(Repository, "main", ["/"], Token);

        Assert.NotNull(result.Message);
        Assert.False(File.Exists(TreePath(cache, "bad.md")));

        var fetched = cache.TryReadIndex(Repository, "main")!.Fetched;
        Assert.DoesNotContain("bad.md", fetched);
        Assert.Equal(fetched.Contains("ok.md"), File.Exists(TreePath(cache, "ok.md")));
    }

    /// <summary>An entry naming a path outside the tree is the listing trying to
    /// write somewhere it was not invited.</summary>
    [Fact]
    public async Task An_index_entry_escaping_the_snapshot_folder_is_refused()
    {
        var trees = new StubTreeClient().WithFile("../escaped.md", "no");
        var cache = Cache(trees, new StubBranchCatalog("main", "sha-1"));

        var result = await cache.EnsureAsync(Repository, "main", ["/"], Token);

        Assert.False(result.Updated);
        Assert.NotNull(result.Message);
        Assert.False(File.Exists(Path.Combine(_root, "escaped.md")));
        Assert.Empty(Directory.GetFiles(_root, "escaped.md", SearchOption.AllDirectories));
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

    private static string Digest(string name) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(name)))[..8].ToLowerInvariant();

    /// <summary>A commit, the way the tree endpoint lists one: every directory
    /// on the way to a file is its own entry, and a file's id is a digest of its
    /// content, so two commits with the same text share a blob id the way git's
    /// do.</summary>
    private sealed class StubTreeClient : IGitHubTreeClient
    {
        private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);

        public int Listings { get; private set; }

        public int Blobs { get; private set; }

        public bool ListingFails { get; set; }

        public HashSet<string> FailingBlobs { get; } = new(StringComparer.Ordinal);

        public StubTreeClient WithFile(string path, string content)
        {
            _files[path] = content;
            return this;
        }

        public StubTreeClient Reset()
        {
            _files.Clear();
            return this;
        }

        public Task<GitHubTreeListing> ListTreeAsync(GitHubRepositoryRef repository, string commitSha, CancellationToken cancellationToken = default)
        {
            Listings++;

            if (ListingFails) throw new GitHubException("GitHub refused the listing.");

            var entries = new List<GitHubTreeEntry>();
            var directories = new HashSet<string>(StringComparer.Ordinal);

            foreach (var (path, content) in _files)
            {
                var parts = path.Split('/');
                for (var depth = 1; depth < parts.Length; depth++)
                {
                    var directory = string.Join('/', parts[..depth]);
                    if (directories.Add(directory)) entries.Add(new GitHubTreeEntry(directory, "tree-" + Digest(directory), true, null));
                }

                entries.Add(new GitHubTreeEntry(path, BlobSha(path, content), false, content.Length));
            }

            return Task.FromResult(new GitHubTreeListing(entries, false));
        }

        public Task<byte[]> ReadBlobAsync(GitHubRepositoryRef repository, string blobSha, CancellationToken cancellationToken = default)
        {
            Blobs++;

            foreach (var (path, content) in _files)
            {
                if (BlobSha(path, content) != blobSha) continue;

                if (FailingBlobs.Contains(path)) throw new GitHubException($"GitHub refused {path}.");

                return Task.FromResult(Encoding.UTF8.GetBytes(content));
            }

            throw new GitHubException($"No blob {blobSha}.");
        }

        private static string BlobSha(string path, string content) => "blob-" + Digest(path + "\0" + content);
    }

    private sealed class StubBranchCatalog(string branch, string sha) : IGitHubBranchCatalog
    {
        public string Sha { get; set; } = sha;

        public bool Missing { get; init; }

        public Task<IReadOnlyList<string>> ListBranchesAsync(
            GitHubRepositoryRef repository,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([branch]);

        public Task<GitHubBranchHead?> ResolveHeadAsync(
            GitHubRepositoryRef repository,
            string? requested,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Missing ? null : new GitHubBranchHead(branch, Sha));
    }
}
