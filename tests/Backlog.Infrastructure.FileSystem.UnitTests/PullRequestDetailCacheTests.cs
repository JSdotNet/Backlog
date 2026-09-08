using Backlog.Infrastructure.FileSystem;
using Backlog.Infrastructure.GitHub;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The saved detail of merged pull requests: what comes back, what does not, and
/// what happens when the disk will not cooperate.
/// <para>
/// Real temp directories rather than a filesystem abstraction, the way
/// <see cref="KnowledgeSnapshotCacheTests"/> does it and for the same reason — the
/// behaviour under test <em>is</em> the disk, and a fake would be asserting the
/// fake.
/// </para>
/// </summary>
public class PullRequestDetailCacheTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "pull-request-detail-cache-tests-" + Guid.NewGuid().ToString("N"));

    private static readonly GitHubRepositoryRef Backlog = new("backlog", "JSdotNet", "Backlog");

    private static readonly GitHubRepositoryRef Spec = new("spec", "innovadis-dev", "spec-manager");

    public PullRequestDetailCacheTests() => Directory.CreateDirectory(_root);

    private PullRequestDetailCache Cache() => new(() => _root);

    [Fact]
    public void What_was_written_comes_back()
    {
        var cache = Cache();

        cache.Write(Backlog, 412, new PullRequestDetail
        {
            FirstReviewedAt = new DateTimeOffset(2026, 7, 3, 9, 0, 0, TimeSpan.Zero),
            ReviewRounds = 2,
            ChangesRequested = 1,
            CommitsAfterFirstReview = 4,
            ForcePushesAfterFirstReview = 1,
            FilesRetouched = 3,
            ChurnComplete = true,
            ChangedLines = 260,
            ChangedFiles = 11,
            SizeKnown = true
        });

        var read = cache.TryRead(Backlog, 412);

        Assert.NotNull(read);
        Assert.Equal(new DateTimeOffset(2026, 7, 3, 9, 0, 0, TimeSpan.Zero), read.FirstReviewedAt);
        Assert.Equal(2, read.ReviewRounds);
        Assert.Equal(1, read.ChangesRequested);
        Assert.Equal(4, read.CommitsAfterFirstReview);
        Assert.Equal(1, read.ForcePushesAfterFirstReview);
        Assert.Equal(3, read.FilesRetouched);
        Assert.True(read.ChurnComplete);
        Assert.Equal(260, read.ChangedLines);
        Assert.Equal(11, read.ChangedFiles);
        Assert.True(read.SizeKnown);
    }

    /// <summary>
    /// The property the whole cache turns on. A floor that came back a total would
    /// make the second read of a window more confident than the first, with nothing
    /// on screen to say why.
    /// </summary>
    [Fact]
    public void A_floor_survives_the_round_trip_as_a_floor()
    {
        var cache = Cache();

        cache.Write(Backlog, 9, new PullRequestDetail
        {
            FilesRetouched = 20,
            ChurnComplete = false,
            SizeKnown = false
        });

        var read = cache.TryRead(Backlog, 9);

        Assert.NotNull(read);
        Assert.False(read.ChurnComplete);
        Assert.False(read.SizeKnown);
    }

    [Fact]
    public void A_pull_request_nothing_was_written_for_is_a_miss()
    {
        Assert.Null(Cache().TryRead(Backlog, 1));
    }

    /// <summary>An entry that cannot be read costs one call to replace. Throwing
    /// would fail a whole dashboard read over one corrupt file.</summary>
    [Fact]
    public void An_unreadable_entry_reads_as_a_miss_rather_than_throwing()
    {
        var cache = Cache();
        cache.Write(Backlog, 5, new PullRequestDetail { ChurnComplete = true, SizeKnown = true });

        File.WriteAllText(OnlyEntry(), "{ not json at all");

        Assert.Null(cache.TryRead(Backlog, 5));
    }

    /// <summary>
    /// A field added later would deserialize to its type's default and read as a
    /// claim nothing ever established — <c>SizeKnown = false</c> means "GitHub was
    /// asked and would not say", which an older file never said. Reading it as a
    /// miss costs one fetch and states nothing untrue.
    /// </summary>
    [Fact]
    public void An_entry_written_under_an_older_version_reads_as_a_miss()
    {
        var cache = Cache();
        cache.Write(Backlog, 5, new PullRequestDetail { ChurnComplete = true, SizeKnown = true });

        var path = OnlyEntry();
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"version\": 1", "\"version\": 0", StringComparison.Ordinal));

        Assert.Null(cache.TryRead(Backlog, 5));
    }

    /// <summary>An entry that could not be written is fetched again next time.
    /// Wasteful, not wrong — and far better than failing a read that succeeded.</summary>
    [Fact]
    public void A_write_that_cannot_land_is_not_fatal()
    {
        // A file where the cache expects a folder: nothing can be created under it.
        var blocked = Path.Combine(_root, "blocked");
        File.WriteAllText(blocked, "not a directory");

        var cache = new PullRequestDetailCache(() => blocked);

        cache.Write(Backlog, 1, new PullRequestDetail { ChurnComplete = true, SizeKnown = true });

        Assert.Null(cache.TryRead(Backlog, 1));
    }

    [Fact]
    public void Forgetting_one_repository_leaves_the_others_alone()
    {
        var cache = Cache();
        cache.Write(Backlog, 1, new PullRequestDetail { ChangedLines = 10, SizeKnown = true });
        cache.Write(Backlog, 2, new PullRequestDetail { ChangedLines = 20, SizeKnown = true });
        cache.Write(Spec, 1, new PullRequestDetail { ChangedLines = 30, SizeKnown = true });

        cache.ForgetRepository(Backlog);

        Assert.Null(cache.TryRead(Backlog, 1));
        Assert.Null(cache.TryRead(Backlog, 2));

        // The other repository is untouched. Re-reading it would be a long fetch
        // for a problem it does not have.
        Assert.Equal(30, cache.TryRead(Spec, 1)!.ChangedLines);
    }

    [Fact]
    public void Forgetting_a_repository_nothing_was_stored_for_is_not_an_error()
    {
        Cache().ForgetRepository(Backlog);
    }

    /// <summary>Two repositories with the same name under different owners are two
    /// caches, which the digest in the folder name is what guarantees.</summary>
    [Fact]
    public void The_same_repository_name_under_two_owners_is_two_caches()
    {
        var cache = Cache();
        var mine = new GitHubRepositoryRef("mine", "JSdotNet", "tools");
        var theirs = new GitHubRepositoryRef("theirs", "innovadis-dev", "tools");

        cache.Write(mine, 1, new PullRequestDetail { ChangedLines = 1, SizeKnown = true });
        cache.Write(theirs, 1, new PullRequestDetail { ChangedLines = 2, SizeKnown = true });

        Assert.Equal(1, cache.TryRead(mine, 1)!.ChangedLines);
        Assert.Equal(2, cache.TryRead(theirs, 1)!.ChangedLines);
    }

    /// <summary>The one entry on disk, found rather than spelled out — where the
    /// cache puts it is the cache's business, not this test's.</summary>
    private string OnlyEntry() => Assert.Single(Directory.GetFiles(_root, "*.json", SearchOption.AllDirectories));

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
