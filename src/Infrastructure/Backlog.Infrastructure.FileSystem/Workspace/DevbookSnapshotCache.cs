using System.Collections.Concurrent;
using System.Text.Json;
using Backlog.Infrastructure.GitHub;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// The disk half of <see cref="IDevbookSnapshotCache"/>: where the copy of a
/// branch goes, how its index and its files are kept in step with the commit
/// they came from, and how a caller asks whether either is here.
/// <para>
/// It lives here rather than beside the port because the network half stays in
/// <c>Backlog.Infrastructure.GitHub</c>, behind <see cref="IGitHubTreeClient"/>
/// and <see cref="IGitHubBranchCatalog"/>, and that adapter may not reach for
/// this one — the architecture tests enforce the direction. The cache root
/// arrives as a delegate for the same reason.
/// </para>
/// <para>
/// One branch on disk is four things under its folder: <c>snapshot.json</c>
/// (which commit), <c>index.json</c> (every path in it), <c>fetched.json</c>
/// (which files are here, at which blob id), and <c>tree/</c> (the files).
/// The invariant every write keeps is that a file under <c>tree/</c> is the
/// indexed commit's version of that path: a refresh that moves the commit
/// deletes what changed before it says the new commit is here, so a reader
/// never finds yesterday's chapter under today's index.
/// </para>
/// </summary>
public sealed class DevbookSnapshotCache(
    Func<string> cacheRoot,
    IGitHubTreeClient trees,
    IGitHubBranchCatalog branches) : IDevbookSnapshotCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>The folder name standing in for "whatever this repository calls
    /// its default branch". The cache is keyed on what was configured rather
    /// than on what that resolved to, because resolving needs the network and
    /// <see cref="SnapshotPath"/> may not.</summary>
    private const string DefaultBranchKey = "_default";

    /// <summary>How many blobs are in flight at once when an area is filled in.
    /// Enough that a folder of a hundred chapters is seconds rather than a
    /// minute; few enough not to trip GitHub's secondary rate limit, which
    /// counts concurrent requests rather than requests per hour.</summary>
    private const int DownloadConcurrency = 4;

    private readonly Func<string> _cacheRoot = cacheRoot ?? throw new ArgumentNullException(nameof(cacheRoot));
    private readonly IGitHubTreeClient _trees = trees ?? throw new ArgumentNullException(nameof(trees));
    private readonly IGitHubBranchCatalog _branches = branches ?? throw new ArgumentNullException(nameof(branches));

    /// <summary>One writer per branch folder. Two panels asking for two areas of
    /// the same branch at once would otherwise both rewrite <c>fetched.json</c>
    /// from the copy each read, and one of them would lose the other's files.</summary>
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _writers = new(StringComparer.OrdinalIgnoreCase);

    public string SnapshotPath(GitHubRepositoryRef repository, string? branch)
    {
        ArgumentNullException.ThrowIfNull(repository);

        return Path.Combine(BranchRoot(repository, branch), "tree");
    }

    /// <summary>
    /// The state file and the index have to name the same commit. An index is
    /// written before the state that points at it, so between the two a reader
    /// finds an old state beside a new index — and answering "fetched" then
    /// would hand out a menu of one commit under the name of another. The
    /// mismatch reads as "not fetched", which the next call settles.
    /// </summary>
    public DevbookSnapshot? TryRead(GitHubRepositoryRef repository, string? branch) =>
        ReadState(repository, branch) is { } state
        && ReadJson<IndexHeader>(IndexPath(repository, branch)) is { Sha: { Length: > 0 } indexed }
        && string.Equals(indexed, state.Sha, StringComparison.OrdinalIgnoreCase)
            ? new DevbookSnapshot(state.Branch!, state.Sha!, state.FetchedUtc)
            : null;

    public DevbookSnapshotIndex? TryReadIndex(GitHubRepositoryRef repository, string? branch)
    {
        ArgumentNullException.ThrowIfNull(repository);

        if (TryRead(repository, branch) is not { } snapshot) return null;

        var index = ReadJson<IndexFile>(IndexPath(repository, branch));
        if (index?.Entries is null || !string.Equals(index.Sha, snapshot.Sha, StringComparison.OrdinalIgnoreCase)) return null;

        var fetched = ReadJson<Dictionary<string, string>>(FetchedPath(repository, branch)) ?? [];

        return new DevbookSnapshotIndex(
            snapshot,
            [
                .. index.Entries
                    .Where(entry => !string.IsNullOrWhiteSpace(entry.Path) && !string.IsNullOrWhiteSpace(entry.Sha))
                    .Select(entry => new DevbookSnapshotEntry(entry.Path!, entry.Sha!, entry.Dir))
            ],
            new HashSet<string>(fetched.Keys, StringComparer.OrdinalIgnoreCase),
            index.Truncated);
    }

    public async Task<DevbookSnapshotResult> CheckAsync(
        GitHubRepositoryRef repository,
        string? branch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var (result, _) = await CheckHeadAsync(repository, branch, cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<DevbookSnapshotResult> FetchAsync(
        GitHubRepositoryRef repository,
        string? branch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var (check, head) = await CheckHeadAsync(repository, branch, cancellationToken).ConfigureAwait(false);
        if (!check.Behind || head is null) return check;

        GitHubTreeListing listing;
        try
        {
            listing = await _trees.ListTreeAsync(repository, head.Sha, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is GitHubException or GitHubNotConfiguredException)
        {
            // The index that was already there is untouched, so the caller keeps
            // reading the copy it had rather than losing it to a failed refresh.
            return DevbookSnapshotResult.Failed(check.Snapshot, exception.Message);
        }

        var writer = Writer(repository, branch);
        await writer.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Two panels opening one branch at once both find no index, both
            // list the commit, and both arrive here. The second to take the lock
            // finds the first's index already at the head and leaves it alone —
            // replacing it again would only re-run the pruning walk and, for
            // the moment it took, hide an index that was perfectly good.
            if (TryRead(repository, branch) is { } landed && string.Equals(landed.Sha, head.Sha, StringComparison.OrdinalIgnoreCase))
            {
                return new DevbookSnapshotResult(landed, false, false, $"Up to date with {head.Branch}.");
            }

            ReplaceIndex(repository, branch, head, listing);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return DevbookSnapshotResult.Failed(check.Snapshot, exception.Message);
        }
        finally
        {
            writer.Release();
        }

        var snapshot = new DevbookSnapshot(head.Branch, head.Sha, DateTimeOffset.UtcNow);

        return new DevbookSnapshotResult(snapshot, true, false, $"Updated to the latest {head.Branch}.");
    }

    public async Task<DevbookSnapshotResult> EnsureAsync(
        GitHubRepositoryRef repository,
        string? branch,
        IReadOnlyCollection<string> selection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(selection);

        var index = TryReadIndex(repository, branch);
        if (index is null)
        {
            var fetched = await FetchAsync(repository, branch, cancellationToken).ConfigureAwait(false);
            index = TryReadIndex(repository, branch);
            if (index is null) return fetched.Snapshot is null ? fetched : DevbookSnapshotResult.Failed(null, fetched.Message ?? "The branch index could not be read.");
        }

        var wanted = DevbookSnapshotSelection.Select(index.Entries, selection);
        var onDisk = ReadJson<Dictionary<string, string>>(FetchedPath(repository, branch)) ?? [];
        var tree = SnapshotPath(repository, branch);

        var missing = wanted
            .Where(entry => !(onDisk.TryGetValue(entry.Path, out var sha)
                              && string.Equals(sha, entry.Sha, StringComparison.OrdinalIgnoreCase)
                              && File.Exists(FullPath(tree, entry.Path))))
            .ToList();

        if (missing.Count == 0) return new DevbookSnapshotResult(index.Snapshot, false, false, null);

        var writer = Writer(repository, branch);
        await writer.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await DownloadAsync(repository, branch, index.Snapshot, tree, missing, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            writer.Release();
        }
    }

    /// <summary>The check, plus the head it resolved, so a fetch that needs both
    /// makes one call for the branch rather than two.</summary>
    private async Task<(DevbookSnapshotResult Result, GitHubBranchHead? Head)> CheckHeadAsync(
        GitHubRepositoryRef repository,
        string? branch,
        CancellationToken cancellationToken)
    {
        var current = TryRead(repository, branch);

        GitHubBranchHead? head;
        try
        {
            head = await _branches.ResolveHeadAsync(repository, branch, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is GitHubException or GitHubNotConfiguredException)
        {
            return (DevbookSnapshotResult.Failed(current, exception.Message), null);
        }

        if (head is null)
        {
            return (DevbookSnapshotResult.Failed(
                current,
                $"{repository.FullName} has no branch called {BranchLabel(branch)}."), null);
        }

        if (current is not null && string.Equals(current.Sha, head.Sha, StringComparison.OrdinalIgnoreCase))
        {
            return (new DevbookSnapshotResult(current, false, false, $"Up to date with {head.Branch}."), head);
        }

        return (new DevbookSnapshotResult(
            current,
            false,
            true,
            current is null
                ? $"{head.Branch} has not been fetched yet."
                : $"{head.Branch} has moved on since this copy was taken."), head);
    }

    /// <summary>
    /// Puts a new commit's listing in place of the old one.
    /// <para>
    /// Order matters. Every fetched file the new commit changed or dropped is
    /// deleted first, so the tree holds only files the new index still vouches
    /// for; then the index is written, naming its commit; then the state, which
    /// is what makes the branch read as fetched at that commit. Nothing is
    /// deleted up front: the old state and the old index stay a consistent pair
    /// until the new index lands, and from then until the new state lands the
    /// pair disagrees and <see cref="TryRead"/> answers "not fetched" — a window
    /// of one small file write, and never a window in which a reader is handed
    /// a commit it was not asked for. A failure part-way leaves the old state
    /// beside a new index, which reads the same way, and the next call treats
    /// as "fetch the index" — the files that survived are the ones the next
    /// index will list at the same blob id, so they are not fetched twice.
    /// </para>
    /// <para>
    /// Every file under the tree that the record does not vouch for goes too,
    /// not only the ones it does. A tree left by the archive-based snapshot this
    /// replaced holds the whole repository at whatever commit it was taken, with
    /// no record at all; a tree whose record was lost holds files nobody can
    /// date. Either way a reader would find them under the new index and take
    /// them for the new commit's, which is exactly the drift the record exists
    /// to prevent — so the walk is the invariant, and the record is the shortcut
    /// through it.
    /// </para>
    /// </summary>
    private void ReplaceIndex(GitHubRepositoryRef repository, string? branch, GitHubBranchHead head, GitHubTreeListing listing)
    {
        var tree = SnapshotPath(repository, branch);

        Directory.CreateDirectory(tree);

        var incoming = listing.Entries
            .Where(entry => !entry.IsDirectory)
            .ToDictionary(entry => entry.Path, entry => entry.Sha, StringComparer.OrdinalIgnoreCase);

        var fetched = ReadJson<Dictionary<string, string>>(FetchedPath(repository, branch)) ?? [];
        var kept = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (path, sha) in fetched)
        {
            if (incoming.TryGetValue(path, out var current) && string.Equals(current, sha, StringComparison.OrdinalIgnoreCase)
                && File.Exists(FullPath(tree, path)))
            {
                kept[path] = sha;
                continue;
            }

            DeleteFile(FullPath(tree, path));
        }

        DeleteUnrecorded(tree, kept);
        WriteJson(FetchedPath(repository, branch), kept);
        WriteJson(IndexPath(repository, branch), new IndexFile
        {
            Sha = head.Sha,
            Truncated = listing.Truncated,
            Entries = [.. listing.Entries.Select(entry => new IndexEntry { Path = entry.Path, Sha = entry.Sha, Dir = entry.IsDirectory })]
        });
        WriteJson(StatePath(repository, branch), new SnapshotState
        {
            Branch = head.Branch,
            Sha = head.Sha,
            FetchedUtc = DateTimeOffset.UtcNow
        });
    }

    private async Task<DevbookSnapshotResult> DownloadAsync(
        GitHubRepositoryRef repository,
        string? branch,
        DevbookSnapshot snapshot,
        string tree,
        IReadOnlyList<DevbookSnapshotEntry> missing,
        CancellationToken cancellationToken)
    {
        var landed = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? failure = null;

        using var gate = new SemaphoreSlim(DownloadConcurrency);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var downloads = missing.Select(async entry =>
        {
            await gate.WaitAsync(stop.Token).ConfigureAwait(false);
            try
            {
                var bytes = await _trees.ReadBlobAsync(repository, entry.Sha, stop.Token).ConfigureAwait(false);
                WriteFile(tree, entry.Path, bytes);
                landed[entry.Path] = entry.Sha;
            }
            catch (Exception exception) when (exception is GitHubException or GitHubNotConfiguredException
                                                  or IOException or UnauthorizedAccessException or InvalidDataException)
            {
                // The first reason is the one reported; the rest are stopped
                // rather than tried, because a credential GitHub refused once it
                // refuses a hundred times, and every one of those is a rate-limit
                // hit against the account.
                Interlocked.CompareExchange(ref failure, exception.Message, null);
                await stop.CancelAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Stopped by a sibling's failure; that sibling reported.
            }
            finally
            {
                gate.Release();
            }
        });

        try
        {
            await Task.WhenAll(downloads).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }

        if (!landed.IsEmpty)
        {
            var fetched = ReadJson<Dictionary<string, string>>(FetchedPath(repository, branch)) ?? [];
            foreach (var (path, sha) in landed) fetched[path] = sha;

            try
            {
                WriteJson(FetchedPath(repository, branch), fetched);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Files that landed without being recorded are fetched again
                // next time. Wasteful, not wrong, and far better than failing a
                // download that succeeded.
                failure ??= exception.Message;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        return failure is null
            ? new DevbookSnapshotResult(snapshot, true, false, null)
            : new DevbookSnapshotResult(snapshot, !landed.IsEmpty, false, failure);
    }

    /// <summary>Removes every file under the tree that <paramref name="kept"/>
    /// does not name, and every directory that is empty afterwards.</summary>
    private static void DeleteUnrecorded(string tree, Dictionary<string, string> kept)
    {
        if (!Directory.Exists(tree)) return;

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(tree));

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
            if (kept.ContainsKey(relative)) continue;

            DeleteFile(file);
        }

        // Deepest first, so a directory whose only content was directories that
        // just emptied is itself found empty.
        IEnumerable<string> directories;
        try
        {
            directories = Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                .OrderByDescending(directory => directory.Length)
                .ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var directory in directories)
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // An empty folder left behind costs nothing and vouches for nothing.
            }
        }
    }

    /// <summary>
    /// Writes one blob where the index says it goes, refusing a path that would
    /// land outside the tree. An index naming <c>../</c> is GitHub's listing
    /// trying to write somewhere it was not invited — refused rather than
    /// sanitized, because a listing doing this is not one to take the rest of.
    /// </summary>
    private static void WriteFile(string tree, string relativePath, byte[] bytes)
    {
        var target = FullPath(tree, relativePath);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(tree));

        if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"The branch index names a path that escapes the snapshot folder: {relativePath}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        // Written beside and moved in, so a reader that opens the path mid-write
        // sees either the previous version or the whole new one, never a torn
        // file. A previous version exists only when the same blob is being
        // re-fetched after its record was lost.
        var staging = target + ".fetching";
        File.WriteAllBytes(staging, bytes);
        File.Move(staging, target, overwrite: true);
    }

    private static string FullPath(string tree, string relativePath) =>
        Path.GetFullPath(Path.Combine(tree, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private SemaphoreSlim Writer(GitHubRepositoryRef repository, string? branch) =>
        _writers.GetOrAdd(BranchRoot(repository, branch), _ => new SemaphoreSlim(1, 1));

    private string BranchRoot(GitHubRepositoryRef repository, string? branch) =>
        Path.Combine(_cacheRoot(), CachePaths.Safe($"{repository.Owner}-{repository.Name}"), BranchKey(branch));

    private string StatePath(GitHubRepositoryRef repository, string? branch) =>
        Path.Combine(BranchRoot(repository, branch), "snapshot.json");

    private string IndexPath(GitHubRepositoryRef repository, string? branch) =>
        Path.Combine(BranchRoot(repository, branch), "index.json");

    private string FetchedPath(GitHubRepositoryRef repository, string? branch) =>
        Path.Combine(BranchRoot(repository, branch), "fetched.json");

    private SnapshotState? ReadState(GitHubRepositoryRef repository, string? branch)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var state = ReadJson<SnapshotState>(StatePath(repository, branch));
        return state?.Sha is null or "" || state.Branch is null or "" ? null : state;
    }

    private static T? ReadJson<T>(string path) where T : class
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions) : null;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void WriteJson<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var staging = path + ".writing";
        File.WriteAllText(staging, JsonSerializer.Serialize(value, JsonOptions));
        File.Move(staging, path, overwrite: true);
    }

    private static void DeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Left behind rather than fatal: it is no longer recorded as fetched,
            // so nothing vouches for it, and the next fetch of that path
            // overwrites it.
        }
    }

    private static string BranchKey(string? branch) =>
        string.IsNullOrWhiteSpace(branch) ? DefaultBranchKey : CachePaths.Safe(branch.Trim());

    private static string BranchLabel(string? branch) =>
        string.IsNullOrWhiteSpace(branch) ? "its default branch" : branch.Trim();

    private sealed record SnapshotState
    {
        public string? Branch { get; init; }

        public string? Sha { get; init; }

        public DateTimeOffset FetchedUtc { get; init; }
    }

    /// <summary>Just the commit an index file names, so <see cref="TryRead"/>
    /// can check it without parsing the entries.</summary>
    private sealed record IndexHeader
    {
        public string? Sha { get; init; }
    }

    private sealed record IndexFile
    {
        public string? Sha { get; init; }

        public bool Truncated { get; init; }

        public List<IndexEntry>? Entries { get; init; }
    }

    private sealed record IndexEntry
    {
        public string? Path { get; init; }

        public string? Sha { get; init; }

        public bool Dir { get; init; }
    }
}
