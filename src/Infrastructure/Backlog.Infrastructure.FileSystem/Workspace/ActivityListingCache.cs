using System.Text.Json;
using Backlog.Infrastructure.GitHub;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// The disk half of <see cref="IActivityListingCache"/>: one JSON file per
/// repository per author, under the same root as
/// <see cref="PullRequestDetailCache"/> and beside its per-repository folders.
/// <para>
/// One file rather than one per row, which is the opposite choice from the detail
/// cache and for the opposite reason. A detail entry is written once and read by
/// number; a listing is read whole and rewritten whole after every walk, because
/// what a walk changes is the set and its coverage, not one row. Splitting it
/// would turn "what do I know about this repository" into a directory scan.
/// </para>
/// <para>
/// Its own folder under the root rather than a file inside the repository's
/// detail folder, so that forgetting either one is a deletion of one folder —
/// and so that the two caches' layouts can be versioned apart.
/// </para>
/// <para>
/// Non-throwing on every path, exactly as its sibling: a corrupt or unreadable
/// file is a miss that costs one full walk, and a file that could not be written
/// costs the same walk again next time.
/// </para>
/// </summary>
public sealed class ActivityListingCache(Func<string> cacheRoot) : IActivityListingCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private const int Version = 1;

    private const string FolderName = "listings";

    private readonly Func<string> _cacheRoot = cacheRoot ?? throw new ArgumentNullException(nameof(cacheRoot));

    public ActivityListing? TryRead(GitHubRepositoryRef repository, string author)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(author);

        try
        {
            var path = EntryPath(repository, author);
            if (!File.Exists(path)) return null;

            var stored = JsonSerializer.Deserialize<StoredListing>(File.ReadAllText(path), JsonOptions);

            // A stored shape from another version says nothing this version can
            // read, so it is a miss rather than a guess.
            if (stored is null || stored.Version != Version) return null;

            return new ActivityListing(
                [.. stored.PullRequests.Select(pull => new ListedPullRequest(
                    pull.Number,
                    pull.Url ?? string.Empty,
                    pull.Title ?? string.Empty,
                    pull.CreatedAt,
                    pull.MergedAt))],
                [.. stored.Issues.Select(issue => new ListedIssue(
                    issue.Number,
                    issue.Url ?? string.Empty,
                    issue.Title ?? string.Empty,
                    issue.ClosedAt))],
                Coverage(stored.PullRequestCoverage),
                Coverage(stored.IssueCoverage));
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            // A half-written or unreadable listing is a miss. Throwing here would
            // fail a whole dashboard read over one file that costs one walk to
            // replace.
            return null;
        }
    }

    public void Write(GitHubRepositoryRef repository, string author, ActivityListing listing)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(author);
        ArgumentNullException.ThrowIfNull(listing);

        var path = EntryPath(repository, author);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            File.WriteAllText(path, JsonSerializer.Serialize(
                new StoredListing
                {
                    Version = Version,
                    PullRequests = [.. listing.PullRequests.Select(pull => new StoredPullRequest
                    {
                        Number = pull.Number,
                        Url = pull.Url,
                        Title = pull.Title,
                        CreatedAt = pull.CreatedAt,
                        MergedAt = pull.MergedAt
                    })],
                    Issues = [.. listing.Issues.Select(issue => new StoredIssue
                    {
                        Number = issue.Number,
                        Url = issue.Url,
                        Title = issue.Title,
                        ClosedAt = issue.ClosedAt
                    })],
                    PullRequestCoverage = Stored(listing.PullRequestCoverage),
                    IssueCoverage = Stored(listing.IssueCoverage)
                },
                JsonOptions));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A listing that could not be written is walked in full again next
            // time. Wasteful, not wrong, and far better than failing a read that
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
        Path.Combine(_cacheRoot(), FolderName, CachePaths.Safe($"{repository.Owner}-{repository.Name}"));

    /// <summary>Lower-cased before it names a file: GitHub logins are
    /// case-insensitive, and the same person signed in as <c>JSdotNet</c> and
    /// <c>jsdotnet</c> is one listing, not two.</summary>
    private string EntryPath(GitHubRepositoryRef repository, string author) =>
        Path.Combine(RepositoryRoot(repository), CachePaths.Safe(author.Trim().ToLowerInvariant()) + ".json");

    private static ActivityListingCoverage? Coverage(StoredCoverage? stored) =>
        stored is null ? null : new ActivityListingCoverage(stored.CoveredFrom, stored.WalkedThrough);

    private static StoredCoverage? Stored(ActivityListingCoverage? coverage) =>
        coverage is null ? null : new StoredCoverage { CoveredFrom = coverage.CoveredFrom, WalkedThrough = coverage.WalkedThrough };

    private sealed record StoredListing
    {
        public int Version { get; init; }

        public StoredPullRequest[] PullRequests { get; init; } = [];

        public StoredIssue[] Issues { get; init; } = [];

        public StoredCoverage? PullRequestCoverage { get; init; }

        public StoredCoverage? IssueCoverage { get; init; }
    }

    private sealed record StoredPullRequest
    {
        public int Number { get; init; }

        public string? Url { get; init; }

        public string? Title { get; init; }

        public DateTimeOffset CreatedAt { get; init; }

        public DateTimeOffset MergedAt { get; init; }
    }

    private sealed record StoredIssue
    {
        public int Number { get; init; }

        public string? Url { get; init; }

        public string? Title { get; init; }

        public DateTimeOffset ClosedAt { get; init; }
    }

    private sealed record StoredCoverage
    {
        public DateTimeOffset CoveredFrom { get; init; }

        public DateTimeOffset WalkedThrough { get; init; }
    }
}
