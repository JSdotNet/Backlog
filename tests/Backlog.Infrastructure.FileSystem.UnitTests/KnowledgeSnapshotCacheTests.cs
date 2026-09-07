using System.IO.Compression;
using System.Text;
using Backlog.Infrastructure.FileSystem;
using Backlog.Infrastructure.GitHub;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The local copy of a repository branch: where it goes, when it is refetched,
/// and what happens to the copy already there when a fetch fails.
/// <para>
/// Real temp directories rather than a filesystem abstraction, which is what
/// every other test in this project does — the behaviour under test <em>is</em>
/// the disk, and a fake would be asserting the fake.
/// </para>
/// </summary>
public class KnowledgeSnapshotCacheTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "knowledge-snapshot-cache-tests-" + Guid.NewGuid().ToString("N"));

    private static readonly GitHubRepositoryRef Repository = new("backlog", "JSdotNet", "Backlog");

    public KnowledgeSnapshotCacheTests() => Directory.CreateDirectory(_root);

    private KnowledgeSnapshotCache Cache(StubArchiveClient archives, StubBranchCatalog branches) =>
        new(() => _root, archives, branches);

    // --- Fetching -------------------------------------------------------------

    [Fact]
    public async Task A_first_fetch_lays_the_branch_out_as_the_repository_has_it()
    {
        var archives = new StubArchiveClient()
            .WithEntry(".domain/context-map.md", "# Contexts")
            .WithEntry(".arc42/01-introduction.md", "# Intro");
        var branches = new StubBranchCatalog("main", "sha-1");
        var cache = Cache(archives, branches);

        var result = await cache.FetchAsync(Repository, "main", TestContext.Current.CancellationToken);

        Assert.True(result.Updated);

        var tree = cache.SnapshotPath(Repository, "main");
        Assert.Equal("# Contexts", File.ReadAllText(Path.Combine(tree, ".domain", "context-map.md")));
        Assert.Equal("# Intro", File.ReadAllText(Path.Combine(tree, ".arc42", "01-introduction.md")));
    }

    /// <summary>A zipball's entries all sit under one <c>owner-name-sha</c>
    /// directory, which is an artifact of the download rather than part of the
    /// repository. Keeping it would put every knowledge folder one level below
    /// where the repository says it is.</summary>
    [Fact]
    public async Task The_archives_wrapper_folder_is_not_part_of_the_snapshot()
    {
        var archives = new StubArchiveClient().WithEntry(".domain/context-map.md", "x");
        var cache = Cache(archives, new StubBranchCatalog("main", "sha-1"));

        await cache.FetchAsync(Repository, "main", TestContext.Current.CancellationToken);

        var tree = cache.SnapshotPath(Repository, "main");
        Assert.True(Directory.Exists(Path.Combine(tree, ".domain")));
        Assert.Empty(Directory.GetDirectories(tree, "JSdotNet-Backlog-*"));
    }

    [Fact]
    public async Task A_snapshot_already_at_the_branch_head_is_not_downloaded_again()
    {
        var archives = new StubArchiveClient().WithEntry(".domain/context-map.md", "x");
        var branches = new StubBranchCatalog("main", "sha-1");
        var cache = Cache(archives, branches);

        await cache.FetchAsync(Repository, "main", TestContext.Current.CancellationToken);
        var second = await cache.FetchAsync(Repository, "main", TestContext.Current.CancellationToken);

        Assert.Equal(1, archives.Downloads);
        Assert.False(second.Updated);
        Assert.False(second.Behind);
    }

    [Fact]
    public async Task A_branch_that_moved_on_is_downloaded_again_and_replaces_the_copy()
    {
        var archives = new StubArchiveClient().WithEntry("readme.md", "first");
        var branches = new StubBranchCatalog("main", "sha-1");
        var cache = Cache(archives, branches);

        await cache.FetchAsync(Repository, "main", TestContext.Current.CancellationToken);

        archives.Reset().WithEntry("readme.md", "second");
        branches.Sha = "sha-2";

        var result = await cache.FetchAsync(Repository, "main", TestContext.Current.CancellationToken);

        Assert.True(result.Updated);
        Assert.Equal(2, archives.Downloads);
        Assert.Equal("second", File.ReadAllText(Path.Combine(cache.SnapshotPath(Repository, "main"), "readme.md")));
    }

    /// <summary>A file the branch no longer has must not survive in the snapshot.
    /// This is what the swap buys over extracting over the top.</summary>
    [Fact]
    public async Task A_file_deleted_on_the_branch_is_gone_from_the_new_snapshot()
    {
        var archives = new StubArchiveClient().WithEntry("keep.md", "a").WithEntry("drop.md", "b");
        var branches = new StubBranchCatalog("main", "sha-1");
        var cache = Cache(archives, branches);

        await cache.FetchAsync(Repository, "main", TestContext.Current.CancellationToken);

        archives.Reset().WithEntry("keep.md", "a");
        branches.Sha = "sha-2";
        await cache.FetchAsync(Repository, "main", TestContext.Current.CancellationToken);

        var tree = cache.SnapshotPath(Repository, "main");
        Assert.True(File.Exists(Path.Combine(tree, "keep.md")));
        Assert.False(File.Exists(Path.Combine(tree, "drop.md")));
    }

    // --- Reading back ---------------------------------------------------------

    [Fact]
    public void Nothing_fetched_reads_as_no_snapshot()
    {
        var cache = Cache(new StubArchiveClient(), new StubBranchCatalog("main", "sha-1"));

        Assert.Null(cache.TryRead(Repository, "main"));
    }

    [Fact]
    public async Task A_fetched_snapshot_reads_back_its_branch_and_commit()
    {
        var cache = Cache(new StubArchiveClient().WithEntry("readme.md", "x"), new StubBranchCatalog("main", "sha-1"));
        await cache.FetchAsync(Repository, "main", TestContext.Current.CancellationToken);

        var snapshot = cache.TryRead(Repository, "main");

        Assert.NotNull(snapshot);
        Assert.Equal("main", snapshot.Branch);
        Assert.Equal("sha-1", snapshot.Sha);
    }

    /// <summary>A state file whose tree is gone describes a snapshot that is not
    /// there. Reporting it would make the folder resolve to an empty directory,
    /// and every panel would read "this repository has no knowledge" rather than
    /// "nothing has been fetched yet".</summary>
    [Fact]
    public async Task A_snapshot_whose_tree_was_deleted_reads_as_no_snapshot()
    {
        var cache = Cache(new StubArchiveClient().WithEntry("readme.md", "x"), new StubBranchCatalog("main", "sha-1"));
        await cache.FetchAsync(Repository, "main", TestContext.Current.CancellationToken);

        Directory.Delete(cache.SnapshotPath(Repository, "main"), recursive: true);

        Assert.Null(cache.TryRead(Repository, "main"));
    }

    /// <summary>The cache is keyed on what was configured, not on what that
    /// resolved to, because resolving needs the network and SnapshotPath may
    /// not.</summary>
    [Fact]
    public void The_default_branch_and_a_named_one_are_different_snapshots()
    {
        var cache = Cache(new StubArchiveClient(), new StubBranchCatalog("main", "sha-1"));

        Assert.NotEqual(cache.SnapshotPath(Repository, null), cache.SnapshotPath(Repository, "main"));
    }

    /// <summary>Branch names carry slashes and colons routinely. The digest is
    /// what keeps two names that fold to the same readable text apart.</summary>
    [Fact]
    public void Branch_names_that_fold_to_the_same_text_get_different_folders()
    {
        var cache = Cache(new StubArchiveClient(), new StubBranchCatalog("main", "sha-1"));

        Assert.NotEqual(cache.SnapshotPath(Repository, "fix/a"), cache.SnapshotPath(Repository, "fix-a"));
    }

    [Fact]
    public async Task A_branch_name_with_a_slash_lands_in_one_folder()
    {
        var cache = Cache(new StubArchiveClient().WithEntry("readme.md", "x"), new StubBranchCatalog("release/2.0", "sha-1"));

        var result = await cache.FetchAsync(Repository, "release/2.0", TestContext.Current.CancellationToken);

        Assert.True(result.Updated);
        Assert.True(File.Exists(Path.Combine(cache.SnapshotPath(Repository, "release/2.0"), "readme.md")));
    }

    // --- Checking -------------------------------------------------------------

    [Fact]
    public async Task Checking_reports_behind_without_downloading()
    {
        var archives = new StubArchiveClient().WithEntry("readme.md", "x");
        var branches = new StubBranchCatalog("main", "sha-1");
        var cache = Cache(archives, branches);

        await cache.FetchAsync(Repository, "main", TestContext.Current.CancellationToken);
        branches.Sha = "sha-2";

        var check = await cache.CheckAsync(Repository, "main", TestContext.Current.CancellationToken);

        Assert.True(check.Behind);
        Assert.Equal(1, archives.Downloads);
    }

    [Fact]
    public async Task A_branch_that_is_gone_is_reported_rather_than_thrown()
    {
        var cache = Cache(new StubArchiveClient(), new StubBranchCatalog("main", "sha-1") { Missing = true });

        var check = await cache.CheckAsync(Repository, "deleted", TestContext.Current.CancellationToken);

        Assert.False(check.Behind);
        Assert.Contains("no branch called deleted", check.Message, StringComparison.Ordinal);
    }

    // --- Failure --------------------------------------------------------------

    /// <summary>The copy already on disk is what somebody is reading. A refresh
    /// that fails must not take it with it.</summary>
    [Fact]
    public async Task A_failed_refetch_leaves_the_previous_snapshot_readable()
    {
        var archives = new StubArchiveClient().WithEntry("readme.md", "first");
        var branches = new StubBranchCatalog("main", "sha-1");
        var cache = Cache(archives, branches);

        await cache.FetchAsync(Repository, "main", TestContext.Current.CancellationToken);

        archives.Fails = true;
        branches.Sha = "sha-2";

        var result = await cache.FetchAsync(Repository, "main", TestContext.Current.CancellationToken);

        Assert.False(result.Updated);
        Assert.NotNull(result.Message);
        Assert.Equal("first", File.ReadAllText(Path.Combine(cache.SnapshotPath(Repository, "main"), "readme.md")));
        Assert.Equal("sha-1", cache.TryRead(Repository, "main")!.Sha);
    }

    /// <summary>An entry naming a path outside the destination is the archive
    /// trying to write somewhere it was not invited.</summary>
    [Fact]
    public async Task An_archive_entry_escaping_the_snapshot_folder_is_refused()
    {
        var archives = new StubArchiveClient().WithRawEntry("wrapper/../../escaped.md", "no");
        var cache = Cache(archives, new StubBranchCatalog("main", "sha-1"));

        var result = await cache.FetchAsync(Repository, "main", TestContext.Current.CancellationToken);

        Assert.False(result.Updated);
        Assert.NotNull(result.Message);
        Assert.False(File.Exists(Path.Combine(_root, "escaped.md")));
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

    /// <summary>Builds a zipball the way GitHub does: every entry under one
    /// <c>owner-name-sha</c> wrapper folder.</summary>
    private sealed class StubArchiveClient : IGitHubArchiveClient
    {
        private readonly List<(string Path, string Content)> _entries = [];

        public int Downloads { get; private set; }

        public bool Fails { get; set; }

        public StubArchiveClient WithEntry(string path, string content)
        {
            _entries.Add(($"JSdotNet-Backlog-abc1234/{path}", content));
            return this;
        }

        /// <summary>An entry written exactly as given, wrapper included, for the
        /// cases where the path itself is the subject.</summary>
        public StubArchiveClient WithRawEntry(string path, string content)
        {
            _entries.Add((path, content));
            return this;
        }

        public StubArchiveClient Reset()
        {
            _entries.Clear();
            return this;
        }

        public Task<Stream> DownloadBranchZipAsync(
            GitHubRepositoryRef repository,
            string branch,
            CancellationToken cancellationToken = default)
        {
            Downloads++;

            if (Fails) throw new GitHubException("GitHub refused the archive.");

            var buffer = new MemoryStream();

            using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var (path, content) in _entries)
                {
                    using var writer = new StreamWriter(archive.CreateEntry(path).Open(), Encoding.UTF8);
                    writer.Write(content);
                }
            }

            buffer.Position = 0;
            return Task.FromResult<Stream>(buffer);
        }
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
