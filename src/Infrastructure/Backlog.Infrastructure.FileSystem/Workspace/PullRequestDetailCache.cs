using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Backlog.Infrastructure.GitHub;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// The disk half of <see cref="IPullRequestDetailCache"/>: one small JSON file per
/// merged pull request, under one folder per repository.
/// <para>
/// It lives here rather than beside the port for the reason
/// <see cref="KnowledgeSnapshotCache"/> does: the half that talks to GitHub stays
/// in <c>Backlog.Infrastructure.GitHub</c> and may not reach for this one, which
/// the architecture tests enforce. The cache root arrives as a delegate for the
/// same reason — it is a workspace setting, and a setting read once at
/// construction would not follow somebody who moved it.
/// </para>
/// <para>
/// A file per pull request rather than one file per repository, so that a write
/// during a fetch can never lose an entry another write added, and so that
/// forgetting a repository is a directory delete rather than a rewrite.
/// </para>
/// </summary>
public sealed class PullRequestDetailCache(Func<string> cacheRoot) : IPullRequestDetailCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>
    /// The shape stored on disk. Bumped whenever a field is added, removed, or
    /// changes meaning.
    /// <para>
    /// Version rather than tolerant deserialization, because the honest default
    /// for a field that was not there is not the type's default. An entry written
    /// before the size fields existed would deserialize to
    /// <c>SizeKnown = false</c> and read as "the size could not be fetched" —
    /// which is a claim about GitHub that nothing ever established. Reading such
    /// an entry as a miss costs one fetch and states nothing untrue.
    /// </para>
    /// </summary>
    private const int Version = 1;

    private readonly Func<string> _cacheRoot = cacheRoot ?? throw new ArgumentNullException(nameof(cacheRoot));

    public PullRequestDetail? TryRead(GitHubRepositoryRef repository, int number)
    {
        ArgumentNullException.ThrowIfNull(repository);

        try
        {
            var path = EntryPath(repository, number);
            if (!File.Exists(path)) return null;

            var stored = JsonSerializer.Deserialize<StoredDetail>(File.ReadAllText(path), JsonOptions);

            // A stored shape from another version says nothing this version can
            // read, so it is a miss rather than a guess.
            if (stored is null || stored.Version != Version) return null;

            return new PullRequestDetail
            {
                FirstReviewedAt = stored.FirstReviewedAt,
                ReviewRounds = stored.ReviewRounds,
                ChangesRequested = stored.ChangesRequested,
                CommitsAfterFirstReview = stored.CommitsAfterFirstReview,
                ForcePushesAfterFirstReview = stored.ForcePushesAfterFirstReview,
                FilesRetouched = stored.FilesRetouched,
                ChurnComplete = stored.ChurnComplete,
                ChangedLines = stored.ChangedLines,
                ChangedFiles = stored.ChangedFiles,
                SizeKnown = stored.SizeKnown
            };
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            // A half-written or unreadable entry is a miss. Throwing here would
            // fail a whole dashboard read over one corrupt file that costs a
            // single call to replace.
            return null;
        }
    }

    public void Write(GitHubRepositoryRef repository, int number, PullRequestDetail detail)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(detail);

        var path = EntryPath(repository, number);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            File.WriteAllText(path, JsonSerializer.Serialize(
                new StoredDetail
                {
                    Version = Version,
                    FirstReviewedAt = detail.FirstReviewedAt,
                    ReviewRounds = detail.ReviewRounds,
                    ChangesRequested = detail.ChangesRequested,
                    CommitsAfterFirstReview = detail.CommitsAfterFirstReview,
                    ForcePushesAfterFirstReview = detail.ForcePushesAfterFirstReview,
                    FilesRetouched = detail.FilesRetouched,
                    ChurnComplete = detail.ChurnComplete,
                    ChangedLines = detail.ChangedLines,
                    ChangedFiles = detail.ChangedFiles,
                    SizeKnown = detail.SizeKnown
                },
                JsonOptions));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // An entry that could not be written is fetched again next time.
            // Wasteful, not wrong, and far better than failing a read that
            // succeeded.
        }
    }

    public void ForgetRepository(GitHubRepositoryRef repository)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var root = RepositoryRoot(repository);

        try
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Left behind rather than fatal. The worst outcome is that the
            // repository is still answered from disk, which is the state the
            // person was already in.
        }
    }

    private string RepositoryRoot(GitHubRepositoryRef repository) =>
        Path.Combine(_cacheRoot(), Safe($"{repository.Owner}-{repository.Name}"));

    private string EntryPath(GitHubRepositoryRef repository, int number) =>
        Path.Combine(RepositoryRoot(repository), $"{number}.json");

    /// <summary>
    /// One path segment standing for a name that may contain anything — the same
    /// treatment <see cref="KnowledgeSnapshotCache"/> gives a branch name, and for
    /// the same two reasons. The readable part is what makes the folder something
    /// a person can look at and understand; the digest of the original is what
    /// keeps two names that fold to the same readable form in different folders.
    /// <para>
    /// Trimmed because a long owner and name plus the configured cache root can
    /// otherwise pass the path limit on Windows before a single entry is written
    /// under it.
    /// </para>
    /// </summary>
    private static string Safe(string name)
    {
        var readable = new StringBuilder(name.Length);

        foreach (var character in name)
        {
            readable.Append(char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_' ? character : '-');
        }

        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name)))[..8].ToLowerInvariant();

        var trimmed = readable.ToString().Trim('-');
        if (trimmed.Length > 40) trimmed = trimmed[..40];

        return trimmed.Length == 0 ? digest : $"{trimmed}-{digest}";
    }

    /// <summary>The stored shape. Separate from <see cref="PullRequestDetail"/>
    /// so that the file format is a decision this class makes rather than a
    /// consequence of a record somebody refactors.</summary>
    private sealed record StoredDetail
    {
        public int Version { get; init; }

        public DateTimeOffset? FirstReviewedAt { get; init; }

        public int ReviewRounds { get; init; }

        public int ChangesRequested { get; init; }

        public int CommitsAfterFirstReview { get; init; }

        public int ForcePushesAfterFirstReview { get; init; }

        public int FilesRetouched { get; init; }

        public bool ChurnComplete { get; init; }

        public int ChangedLines { get; init; }

        public int ChangedFiles { get; init; }

        public bool SizeKnown { get; init; }
    }
}
