namespace Backlog.Infrastructure.GitHub;

/// <summary>
/// Keeps a local, partial copy of a repository branch, so knowledge can be read
/// out of a repository nobody has cloned.
/// <para>
/// The port is declared here and implemented in
/// <c>Backlog.Infrastructure.FileSystem</c>, which is the split
/// <see cref="ILocalGitRepositoryService"/> already lives on either side of: a
/// contract phrased in terms of a <see cref="GitHubRepositoryRef"/> belongs
/// where that type is, while the half that decides where bytes land on disk
/// belongs with the workspace. Declaring it here is also what keeps the
/// dependency out of the Devbook panels' project, which already sees this
/// adapter and would otherwise need a second one.
/// </para>
/// <para>
/// A snapshot is two things. The <em>index</em> is every path the branch's
/// commit contains — one call, small, and enough to draw a menu. The
/// <em>files</em> arrive afterwards, one at a time, as somebody opens the area
/// or chapter that needs them, and stay until the branch moves and changes
/// them. So "fetched" is a property of the branch and of each file separately:
/// <see cref="TryRead"/> answers the first, <see cref="EnsureAsync"/> settles
/// the second. What was a whole archive per refresh is now a listing per
/// refresh and a download per file somebody actually read.
/// </para>
/// </summary>
public interface IDevbookSnapshotCache
{
    /// <summary>
    /// Where a repository's branch snapshot would be, whether or not one has
    /// been fetched.
    /// <para>
    /// Synchronous and offline on purpose. It is called during folder
    /// resolution, which every panel does on every load, and resolution that
    /// reached the network would make opening a panel wait on GitHub.
    /// </para>
    /// </summary>
    string SnapshotPath(GitHubRepositoryRef repository, string? branch);

    /// <summary>Which commit the index on disk describes, or null when no index
    /// has been fetched yet.</summary>
    DevbookSnapshot? TryRead(GitHubRepositoryRef repository, string? branch);

    /// <summary>The index on disk — every path the commit contains and which of
    /// its files are here — or null when none has been fetched. Offline.</summary>
    DevbookSnapshotIndex? TryReadIndex(GitHubRepositoryRef repository, string? branch);

    /// <summary>
    /// Brings the index up to the branch's current commit, listing the commit
    /// only when the one on disk is not the one the branch points at. Files
    /// already fetched stay where the new commit has them unchanged and are
    /// dropped where it does not, so the next <see cref="EnsureAsync"/> fetches
    /// exactly what moved.
    /// </summary>
    Task<DevbookSnapshotResult> FetchAsync(
        GitHubRepositoryRef repository,
        string? branch,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether the branch has moved past the index on disk. Contacts the
    /// network — one cheap call for the branch head, no download — so it runs
    /// only when somebody asks.
    /// </summary>
    Task<DevbookSnapshotResult> CheckAsync(
        GitHubRepositoryRef repository,
        string? branch,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts the selected files on disk at the indexed commit's version, fetching
    /// the index first when there is none.
    /// <para>
    /// The selection is a list of repository-relative, <c>/</c>-separated
    /// paths: a file names itself, a trailing <c>/</c> names a subtree, a
    /// <c>**/</c> prefix names a path suffix at any depth, and a leading
    /// <c>!</c> excludes a directory name wherever it occurs —
    /// <see cref="DevbookSnapshotSelection"/> is the reference. Files already
    /// here at the right version cost nothing, which is what lets a reader call
    /// this before every read rather than remember whether it already did.
    /// </para>
    /// <para>
    /// <see cref="DevbookSnapshotResult.Updated"/> says whether anything landed.
    /// A failure part-way keeps every file that did land and reports the first
    /// reason, so a reader is left with more than it had, never less.
    /// </para>
    /// </summary>
    Task<DevbookSnapshotResult> EnsureAsync(
        GitHubRepositoryRef repository,
        string? branch,
        IReadOnlyCollection<string> selection,
        CancellationToken cancellationToken = default);
}

/// <summary>One snapshot on disk: which commit of which branch, and when its
/// index was taken.</summary>
public sealed record DevbookSnapshot(string Branch, string Sha, DateTimeOffset FetchedUtc);

/// <summary>One path in the indexed commit.</summary>
/// <param name="Path">Repository-relative, <c>/</c>-separated.</param>
/// <param name="Sha">The blob id for a file, the tree id for a directory —
/// what decides whether a fetched file is still the commit's version.</param>
public sealed record DevbookSnapshotEntry(string Path, string Sha, bool IsDirectory);

/// <summary>
/// The index on disk: the commit, every path in it, and which files are here.
/// </summary>
/// <param name="Fetched">Repository-relative paths of the files on disk at the
/// commit's version, so a caller can tell "nothing fetched yet" from "fetched
/// and empty" without walking the folder.</param>
/// <param name="Truncated">GitHub cut the listing short — a repository past a
/// hundred thousand paths. What is listed still opens.</param>
public sealed record DevbookSnapshotIndex(
    DevbookSnapshot Snapshot,
    IReadOnlyList<DevbookSnapshotEntry> Entries,
    IReadOnlySet<string> Fetched,
    bool Truncated);

/// <summary>
/// What came of asking about, refreshing, or filling in a snapshot.
/// <para>
/// A message rather than a thrown exception for everything a person can act on —
/// a branch that no longer exists, a repository this machine cannot reach — and
/// the message is the one the adapter produced, so the reason a snapshot could
/// not be taken is GitHub's reason rather than a paraphrase.
/// </para>
/// </summary>
/// <param name="Snapshot">What is on disk now. Null when nothing is.</param>
/// <param name="Updated">Whether this call changed what is on disk.</param>
/// <param name="Behind">Whether the branch has moved past what is on disk.</param>
/// <param name="Message">What to tell somebody, or null when there is nothing to say.</param>
public sealed record DevbookSnapshotResult(
    DevbookSnapshot? Snapshot,
    bool Updated,
    bool Behind,
    string? Message)
{
    public static DevbookSnapshotResult Failed(DevbookSnapshot? snapshot, string message) =>
        new(snapshot, false, false, message);
}

/// <summary>
/// The little language <see cref="IDevbookSnapshotCache.EnsureAsync"/> takes,
/// kept to four forms because four is what the readers need: an area store
/// wants a folder, the menu wants every reading-order file wherever it sits, the
/// instructions area wants three folders and a few root files, and nobody wants
/// the rendered diagram artifacts beside a chapter until they ask for one.
/// </summary>
public static class DevbookSnapshotSelection
{
    public const string AnyDepthPrefix = "**/";

    public const char ExcludePrefix = '!';

    /// <summary>Repository-relative, <c>/</c>-separated, no leading <c>./</c>
    /// or <c>/</c> — the spelling the index uses, whatever the caller typed.</summary>
    public static string Normalize(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var normalized = path.Trim().Replace('\\', '/');

        while (normalized.StartsWith("./", StringComparison.Ordinal)) normalized = normalized[2..];

        return normalized.TrimStart('/');
    }

    /// <summary>A subtree: every file under a folder, itself included when the
    /// folder is the repository root (an empty path).</summary>
    public static string Subtree(string folder)
    {
        var normalized = Normalize(folder).TrimEnd('/');
        return normalized.Length == 0 ? "/" : normalized + "/";
    }

    /// <summary>Every file whose path ends in the given suffix, at any depth:
    /// <c>**/_reading-order.json</c>, <c>**/_meta/index.json</c>.</summary>
    public static string AnyDepth(string suffix) => AnyDepthPrefix + Normalize(suffix);

    /// <summary>No file whose path passes through a directory of this name.</summary>
    public static string Exclude(string directoryName) => ExcludePrefix + Normalize(directoryName).Trim('/');

    /// <summary>The files a selection names out of an index.</summary>
    public static IReadOnlyList<DevbookSnapshotEntry> Select(
        IEnumerable<DevbookSnapshotEntry> entries,
        IReadOnlyCollection<string> selection)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(selection);

        var includes = new List<string>();
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in selection)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;

            var term = raw.Trim();
            if (term[0] == ExcludePrefix)
            {
                var name = Normalize(term[1..]).Trim('/');
                if (name.Length > 0) excluded.Add(name);
                continue;
            }

            includes.Add(term.StartsWith(AnyDepthPrefix, StringComparison.Ordinal)
                ? AnyDepthPrefix + Normalize(term[AnyDepthPrefix.Length..])
                : term.EndsWith('/') ? Subtree(term) : Normalize(term));
        }

        if (includes.Count == 0) return [];

        return
        [
            .. entries.Where(entry => !entry.IsDirectory
                && includes.Any(include => Matches(entry.Path, include))
                && !PassesThrough(entry.Path, excluded))
        ];
    }

    private static bool Matches(string path, string include)
    {
        if (include == "/") return true;

        if (include.StartsWith(AnyDepthPrefix, StringComparison.Ordinal))
        {
            var suffix = include[AnyDepthPrefix.Length..];
            return string.Equals(path, suffix, StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("/" + suffix, StringComparison.OrdinalIgnoreCase);
        }

        if (include.EndsWith('/')) return path.StartsWith(include, StringComparison.OrdinalIgnoreCase);

        return string.Equals(path, include, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PassesThrough(string path, HashSet<string> excludedDirectories)
    {
        if (excludedDirectories.Count == 0) return false;

        var lastSeparator = path.LastIndexOf('/');
        if (lastSeparator < 0) return false;

        return path[..lastSeparator].Split('/').Any(excludedDirectories.Contains);
    }
}
