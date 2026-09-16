using Backlog.Infrastructure.FileSystem;
using Backlog.Infrastructure.GitHub;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The saved listing of merged pull requests and closed issues: what comes back,
/// what does not, and what happens when the disk will not cooperate. Real temp
/// directories, as <see cref="PullRequestDetailCacheTests"/> does it and for the
/// same reason — the disk is the thing under test.
/// </summary>
public class ActivityListingCacheTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "activity-listing-cache-tests-" + Guid.NewGuid().ToString("N"));

    private static readonly GitHubRepositoryRef Backlog = new("backlog", "JSdotNet", "Backlog");

    private static readonly GitHubRepositoryRef Spec = new("spec", "innovadis-dev", "spec-manager");

    private static readonly DateTimeOffset June = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset August = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    public ActivityListingCacheTests() => Directory.CreateDirectory(_root);

    private ActivityListingCache Cache() => new(() => _root);

    private static ActivityListing Listing() => new(
        [new ListedPullRequest(412, "https://github.com/JSdotNet/Backlog/pull/412", "A pull request", June, June.AddDays(3))],
        [new ListedIssue(9, "https://github.com/JSdotNet/Backlog/issues/9", "An issue", June.AddDays(5))],
        new ActivityListingCoverage(June, August),
        new ActivityListingCoverage(June, August.AddHours(-1)));

    [Fact]
    public void What_was_written_comes_back()
    {
        var cache = Cache();

        cache.Write(Backlog, "jsdotnet", Listing());

        var read = cache.TryRead(Backlog, "jsdotnet");

        Assert.NotNull(read);
        var pull = Assert.Single(read.PullRequests);
        Assert.Equal(412, pull.Number);
        Assert.Equal("A pull request", pull.Title);
        Assert.Equal(June, pull.CreatedAt);
        Assert.Equal(June.AddDays(3), pull.MergedAt);
        var issue = Assert.Single(read.Issues);
        Assert.Equal(9, issue.Number);
        Assert.Equal(June.AddDays(5), issue.ClosedAt);
        Assert.Equal(new ActivityListingCoverage(June, August), read.PullRequestCoverage);
        Assert.Equal(new ActivityListingCoverage(June, August.AddHours(-1)), read.IssueCoverage);
    }

    /// <summary>
    /// The property the walk turns on. A listing whose walk ran out of pages is
    /// written with no coverage, and it has to come back with none — coverage that
    /// appeared on the round trip would let the next read trust rows nothing walked.
    /// </summary>
    [Fact]
    public void Absent_coverage_survives_the_round_trip_as_absent()
    {
        var cache = Cache();

        cache.Write(Backlog, "jsdotnet", Listing() with { PullRequestCoverage = null, IssueCoverage = null });

        var read = cache.TryRead(Backlog, "jsdotnet");

        Assert.NotNull(read);
        Assert.Null(read.PullRequestCoverage);
        Assert.Null(read.IssueCoverage);
        Assert.Single(read.PullRequests);
    }

    [Fact]
    public void A_repository_nothing_was_written_for_is_a_miss()
    {
        Assert.Null(Cache().TryRead(Backlog, "jsdotnet"));
    }

    /// <summary>One person, two spellings: GitHub logins are case-insensitive and
    /// so is the listing.</summary>
    [Fact]
    public void The_author_is_matched_without_regard_to_case()
    {
        var cache = Cache();
        cache.Write(Backlog, "JSdotNet", Listing());

        Assert.NotNull(cache.TryRead(Backlog, "jsdotnet"));
    }

    /// <summary>Two authors are two listings: "yours" is a fact about who asked.</summary>
    [Fact]
    public void Each_author_has_a_listing_of_their_own()
    {
        var cache = Cache();
        cache.Write(Backlog, "jsdotnet", Listing());

        Assert.Null(cache.TryRead(Backlog, "someone-else"));
    }

    [Fact]
    public void An_unreadable_listing_reads_as_a_miss_rather_than_throwing()
    {
        var cache = Cache();
        cache.Write(Backlog, "jsdotnet", Listing());

        File.WriteAllText(OnlyEntry(), "{ not json at all");

        Assert.Null(cache.TryRead(Backlog, "jsdotnet"));
    }

    [Fact]
    public void A_listing_written_under_an_older_version_reads_as_a_miss()
    {
        var cache = Cache();
        cache.Write(Backlog, "jsdotnet", Listing());

        var path = OnlyEntry();
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"version\": 1", "\"version\": 0", StringComparison.Ordinal));

        Assert.Null(cache.TryRead(Backlog, "jsdotnet"));
    }

    [Fact]
    public void A_write_that_cannot_land_is_not_fatal()
    {
        var blocked = Path.Combine(_root, "blocked");
        File.WriteAllText(blocked, "not a directory");

        var cache = new ActivityListingCache(() => blocked);

        cache.Write(Backlog, "jsdotnet", Listing());

        Assert.Null(cache.TryRead(Backlog, "jsdotnet"));
    }

    [Fact]
    public void Forgetting_one_repository_drops_every_author_and_leaves_the_others_alone()
    {
        var cache = Cache();
        cache.Write(Backlog, "jsdotnet", Listing());
        cache.Write(Backlog, "someone-else", Listing());
        cache.Write(Spec, "jsdotnet", Listing());

        cache.ForgetRepository(Backlog);

        Assert.Null(cache.TryRead(Backlog, "jsdotnet"));
        Assert.Null(cache.TryRead(Backlog, "someone-else"));
        Assert.NotNull(cache.TryRead(Spec, "jsdotnet"));
    }

    [Fact]
    public void Forgetting_a_repository_nothing_was_stored_for_is_not_an_error()
    {
        Cache().ForgetRepository(Backlog);
    }

    /// <summary>
    /// The two caches share a root, and forgetting a repository in one must not be
    /// what forgets it in the other — each is one deletion, and each is asked. What
    /// this pins is that they do not collide: writing both leaves both readable.
    /// </summary>
    [Fact]
    public void The_listing_lives_beside_the_detail_without_colliding()
    {
        var listings = Cache();
        var details = new PullRequestDetailCache(() => _root);

        listings.Write(Backlog, "jsdotnet", Listing());
        details.Write(Backlog, 412, new PullRequestDetail { ChurnComplete = true, SizeKnown = true, ChangedLines = 5 });

        Assert.NotNull(listings.TryRead(Backlog, "jsdotnet"));
        Assert.Equal(5, details.TryRead(Backlog, 412)!.ChangedLines);

        details.ForgetRepository(Backlog);

        Assert.NotNull(listings.TryRead(Backlog, "jsdotnet"));
    }

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
            // A temp folder left behind is not a failed test.
        }
    }
}
