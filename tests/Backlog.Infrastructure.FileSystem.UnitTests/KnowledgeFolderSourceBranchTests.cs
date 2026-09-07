using Backlog.Infrastructure.FileSystem;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Knowledge.Abstractions;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// What a knowledge folder resolves to when its repository is read from a
/// branch, and — the point of the whole change — whether the caller may write to
/// what it was handed.
/// </summary>
public class KnowledgeFolderSourceBranchTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "knowledge-folder-source-branch-tests-" + Guid.NewGuid().ToString("N"));

    private string WorkspaceRoot => Path.Combine(_root, "workspace");

    private string AppData => Path.Combine(_root, "appdata");

    public KnowledgeFolderSourceBranchTests() => Directory.CreateDirectory(_root);

    private GitHubSettingsStore Settings()
    {
        var store = new GitHubSettingsStore(Path.Combine(_root, "github.json"), () => WorkspaceRoot);
        Assert.Null(store.SetRepositories([new GitHubRepositoryRef("backlog", "JSdotNet", "Backlog")]));
        return store;
    }

    private WorkspaceSettingsStore Workspace() => new(AppData, Path.Combine(AppData, "settings.json"));

    private KnowledgeFolderSource Source(GitHubSettingsStore settings, IKnowledgeSnapshotCache? snapshots) =>
        new(settings, Workspace(), snapshots);

    // --- Reading a branch -----------------------------------------------------

    [Fact]
    public void A_fetched_branch_resolves_read_only()
    {
        var settings = Settings();
        var snapshot = Path.Combine(_root, "snapshot");
        Directory.CreateDirectory(Path.Combine(snapshot, ".domain"));

        var location = Source(settings, new StubSnapshotCache(snapshot, fetched: true)).Resolve(".domain", "backlog");

        Assert.True(location.Available);
        Assert.Equal(KnowledgeSourceKind.Branch, location.Source);
        Assert.False(location.CanEdit);
        Assert.Equal(Path.Combine(snapshot, ".domain"), location.FullPath);
    }

    /// <summary>Resolution never fetches — it runs on every panel load, and one
    /// that reached the network would put GitHub in front of opening a tab. A
    /// branch nobody has fetched says so, and the pane's update control is what
    /// goes and gets it.</summary>
    [Fact]
    public void A_branch_nobody_has_fetched_says_so_rather_than_fetching()
    {
        var settings = Settings();
        var cache = new StubSnapshotCache(Path.Combine(_root, "snapshot"), fetched: false);

        var location = Source(settings, cache).Resolve(".domain", "backlog");

        Assert.False(location.Available);
        Assert.Equal(KnowledgeSourceKind.Branch, location.Source);
        Assert.Contains("has not been fetched", location.Message, StringComparison.Ordinal);
        Assert.Equal(0, cache.Fetches);
    }

    /// <summary>"Which of these am I reading?" is a real question the moment the
    /// same repository can be read two ways.</summary>
    [Fact]
    public void The_scope_label_names_the_branch()
    {
        var settings = Settings();
        Assert.Null(settings.SetKnowledgeSource("backlog", "release/2.0", useLocalFolder: false));

        var snapshot = Path.Combine(_root, "snapshot");
        Directory.CreateDirectory(Path.Combine(snapshot, ".domain"));

        var location = Source(settings, new StubSnapshotCache(snapshot, fetched: true)).Resolve(".domain", "backlog");

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
        Directory.CreateDirectory(snapshot);

        var location = Source(settings, new StubSnapshotCache(snapshot, fetched: true)).Resolve(".design", "backlog");

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
        Assert.Equal(KnowledgeSourceKind.LocalFolder, location.Source);
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
        Assert.Null(settings.SetKnowledgeSource("backlog", "main", useLocalFolder: false));

        var snapshot = Path.Combine(_root, "snapshot");
        Directory.CreateDirectory(Path.Combine(snapshot, ".domain"));

        var location = Source(settings, new StubSnapshotCache(snapshot, fetched: true)).Resolve(".domain", "backlog");

        Assert.Equal(Path.Combine(snapshot, ".domain"), location.FullPath);
        Assert.False(location.CanEdit);
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

    /// <summary>The storage scope is a real folder somebody owns, and stays
    /// editable however repositories are configured.</summary>
    [Fact]
    public void The_storage_scope_is_unaffected()
    {
        var workspace = Workspace();
        Directory.CreateDirectory(Path.Combine(workspace.RootDirectory, ".domain"));

        var location = new KnowledgeFolderSource(Settings(), workspace).Resolve(".domain");

        Assert.True(location.Available);
        Assert.Equal(KnowledgeSourceKind.LocalFolder, location.Source);
        Assert.True(location.CanEdit);
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

    /// <summary>Counts fetches, because "resolution never reaches the network" is
    /// one of the things under test.</summary>
    private sealed class StubSnapshotCache(string path, bool fetched) : IKnowledgeSnapshotCache
    {
        public int Fetches { get; private set; }

        public string SnapshotPath(GitHubRepositoryRef repository, string? branch) => path;

        public KnowledgeSnapshot? TryRead(GitHubRepositoryRef repository, string? branch) =>
            fetched ? new KnowledgeSnapshot(branch ?? "main", "sha-1", DateTimeOffset.UtcNow) : null;

        public Task<KnowledgeSnapshotResult> FetchAsync(
            GitHubRepositoryRef repository,
            string? branch,
            CancellationToken cancellationToken = default)
        {
            Fetches++;
            return Task.FromResult(new KnowledgeSnapshotResult(TryRead(repository, branch), true, false, null));
        }

        public Task<KnowledgeSnapshotResult> CheckAsync(
            GitHubRepositoryRef repository,
            string? branch,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new KnowledgeSnapshotResult(TryRead(repository, branch), false, false, null));
    }
}
