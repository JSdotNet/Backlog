using System.IO.Compression;
using System.Text.Json;
using Backlog.Infrastructure.GitHub;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// The disk half of <see cref="IKnowledgeSnapshotCache"/>: where the copy of a
/// branch goes, how it is replaced without ever being half-replaced, and how a
/// caller asks whether it is still current.
/// <para>
/// It lives here rather than beside the port because the network half stays in
/// <c>Backlog.Infrastructure.GitHub</c>, behind
/// <see cref="IGitHubArchiveClient"/> and <see cref="IGitHubBranchCatalog"/>,
/// and that adapter may not reach for this one — the architecture tests enforce
/// the direction. The cache root arrives as a delegate for the same reason.
/// </para>
/// </summary>
public sealed class KnowledgeSnapshotCache(
    Func<string> cacheRoot,
    IGitHubArchiveClient archives,
    IGitHubBranchCatalog branches) : IKnowledgeSnapshotCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>The folder name standing in for "whatever this repository calls
    /// its default branch". The cache is keyed on what was configured rather
    /// than on what that resolved to, because resolving needs the network and
    /// <see cref="SnapshotPath"/> may not.</summary>
    private const string DefaultBranchKey = "_default";

    private readonly Func<string> _cacheRoot = cacheRoot ?? throw new ArgumentNullException(nameof(cacheRoot));
    private readonly IGitHubArchiveClient _archives = archives ?? throw new ArgumentNullException(nameof(archives));
    private readonly IGitHubBranchCatalog _branches = branches ?? throw new ArgumentNullException(nameof(branches));

    public string SnapshotPath(GitHubRepositoryRef repository, string? branch)
    {
        ArgumentNullException.ThrowIfNull(repository);

        return Path.Combine(RepositoryRoot(repository), BranchKey(branch), "tree");
    }

    public KnowledgeSnapshot? TryRead(GitHubRepositoryRef repository, string? branch)
    {
        ArgumentNullException.ThrowIfNull(repository);

        try
        {
            var statePath = StatePath(repository, branch);
            if (!File.Exists(statePath)) return null;

            var stored = JsonSerializer.Deserialize<SnapshotState>(File.ReadAllText(statePath), JsonOptions);
            if (stored?.Sha is null or "" || stored.Branch is null or "") return null;

            // A state file whose tree is gone describes a snapshot that is not
            // there. Reporting it would make the folder resolve to an empty
            // directory and every panel read as "this repository has no
            // knowledge" rather than as "nothing has been fetched yet".
            return Directory.Exists(SnapshotPath(repository, branch))
                ? new KnowledgeSnapshot(stored.Branch, stored.Sha, stored.FetchedUtc)
                : null;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public async Task<KnowledgeSnapshotResult> CheckAsync(
        GitHubRepositoryRef repository,
        string? branch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var current = TryRead(repository, branch);

        GitHubBranchHead? head;
        try
        {
            head = await _branches.ResolveHeadAsync(repository, branch, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is GitHubException or GitHubNotConfiguredException)
        {
            return KnowledgeSnapshotResult.Failed(current, exception.Message);
        }

        if (head is null)
        {
            return KnowledgeSnapshotResult.Failed(
                current,
                $"{repository.FullName} has no branch called {BranchLabel(branch)}.");
        }

        if (current is not null && string.Equals(current.Sha, head.Sha, StringComparison.OrdinalIgnoreCase))
        {
            return new KnowledgeSnapshotResult(current, false, false, $"Up to date with {head.Branch}.");
        }

        return new KnowledgeSnapshotResult(
            current,
            false,
            true,
            current is null
                ? $"{head.Branch} has not been fetched yet."
                : $"{head.Branch} has moved on since this copy was taken.");
    }

    public async Task<KnowledgeSnapshotResult> FetchAsync(
        GitHubRepositoryRef repository,
        string? branch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var check = await CheckAsync(repository, branch, cancellationToken).ConfigureAwait(false);
        if (!check.Behind) return check;

        GitHubBranchHead? head;
        try
        {
            head = await _branches.ResolveHeadAsync(repository, branch, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is GitHubException or GitHubNotConfiguredException)
        {
            return KnowledgeSnapshotResult.Failed(check.Snapshot, exception.Message);
        }

        if (head is null)
        {
            return KnowledgeSnapshotResult.Failed(
                check.Snapshot,
                $"{repository.FullName} has no branch called {BranchLabel(branch)}.");
        }

        try
        {
            await DownloadAsync(repository, branch, head, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is GitHubException or GitHubNotConfiguredException or IOException
                                              or UnauthorizedAccessException or InvalidDataException)
        {
            // The snapshot that was already there is untouched — the staging
            // folder is what failed — so the caller keeps reading the copy it
            // had rather than losing it to a failed refresh.
            return KnowledgeSnapshotResult.Failed(check.Snapshot, exception.Message);
        }

        var snapshot = new KnowledgeSnapshot(head.Branch, head.Sha, DateTimeOffset.UtcNow);

        return new KnowledgeSnapshotResult(snapshot, true, false, $"Updated to the latest {head.Branch}.");
    }

    private async Task DownloadAsync(
        GitHubRepositoryRef repository,
        string? branch,
        GitHubBranchHead head,
        CancellationToken cancellationToken)
    {
        var branchRoot = Path.Combine(RepositoryRoot(repository), BranchKey(branch));
        var tree = Path.Combine(branchRoot, "tree");
        var staging = Path.Combine(branchRoot, "staging");
        var retired = Path.Combine(branchRoot, "retired");

        Directory.CreateDirectory(branchRoot);
        DeleteDirectory(staging);
        DeleteDirectory(retired);

        await using (var zip = await _archives.DownloadBranchZipAsync(repository, head.Branch, cancellationToken).ConfigureAwait(false))
        {
            Extract(zip, staging, cancellationToken);
        }

        // Swap rather than overwrite: a torn extraction must never be the thing
        // a panel reads. The old tree is moved aside first so that the move into
        // place is a rename onto nothing, and only then deleted — which means a
        // failure between the two leaves the new tree readable rather than
        // leaving no tree at all.
        WriteState(repository, branch, null);

        if (Directory.Exists(tree)) Directory.Move(tree, retired);

        Directory.Move(staging, tree);
        DeleteDirectory(retired);

        WriteState(repository, branch, new SnapshotState
        {
            Branch = head.Branch,
            Sha = head.Sha,
            FetchedUtc = DateTimeOffset.UtcNow
        });
    }

    /// <summary>
    /// Unpacks the archive, dropping GitHub's single wrapper folder.
    /// <para>
    /// A zipball's entries all sit under one <c>owner-name-sha</c> directory,
    /// which is an artifact of the download rather than part of the repository.
    /// Keeping it would put every knowledge folder one level below where the
    /// repository says it is.
    /// </para>
    /// </summary>
    private static void Extract(Stream zip, string destination, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destination);

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));

        using var archive = new ZipArchive(zip, ZipArchiveMode.Read);

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relative = WithoutWrapper(entry.FullName);
            if (relative is null) continue;

            var target = Path.GetFullPath(Path.Combine(destination, relative));

            // An entry naming a path outside the destination is the archive
            // trying to write somewhere it was not invited. Refused rather than
            // sanitized: an archive doing this is not one to take the rest of.
            if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"The downloaded archive contains an entry that escapes the snapshot folder: {entry.FullName}");
            }

            // A directory entry — trailing separator, no content.
            if (entry.Name.Length == 0)
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    /// <summary>The entry's path with GitHub's wrapper folder removed, or null
    /// when the entry <em>is</em> the wrapper folder.</summary>
    private static string? WithoutWrapper(string entryPath)
    {
        var normalized = entryPath.Replace('\\', '/');
        var separator = normalized.IndexOf('/');

        if (separator < 0) return null;

        var withinRepository = normalized[(separator + 1)..];

        return withinRepository.Length == 0 ? null : withinRepository.Replace('/', Path.DirectorySeparatorChar);
    }

    private string RepositoryRoot(GitHubRepositoryRef repository) =>
        Path.Combine(_cacheRoot(), CachePaths.Safe($"{repository.Owner}-{repository.Name}"));

    private string StatePath(GitHubRepositoryRef repository, string? branch) =>
        Path.Combine(RepositoryRoot(repository), BranchKey(branch), "snapshot.json");

    private void WriteState(GitHubRepositoryRef repository, string? branch, SnapshotState? state)
    {
        var path = StatePath(repository, branch);

        try
        {
            if (state is null)
            {
                if (File.Exists(path)) File.Delete(path);
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(state, JsonOptions));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A snapshot whose state could not be written is a snapshot that
            // will be fetched again next time somebody asks. Wasteful, not
            // wrong, and far better than failing a download that succeeded.
        }
    }

    private static string BranchKey(string? branch) =>
        string.IsNullOrWhiteSpace(branch) ? DefaultBranchKey : CachePaths.Safe(branch.Trim());

    private static string BranchLabel(string? branch) =>
        string.IsNullOrWhiteSpace(branch) ? "its default branch" : branch.Trim();

    private static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path)) return;

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Left behind rather than fatal. The name is reused on the next
            // attempt, and a stale staging folder costs disk rather than
            // correctness.
        }
    }

    private sealed record SnapshotState
    {
        public string? Branch { get; init; }

        public string? Sha { get; init; }

        public DateTimeOffset FetchedUtc { get; init; }
    }
}
