using Backlog.Infrastructure.GitHub;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// What the activity client reads, and — mostly — what it declines to conclude.
/// </summary>
public class GitHubActivityClientTests
{
    private static readonly GitHubRepositoryRef Repository = new("backlog", "JSdotNet", "Backlog");

    private static readonly DateTimeOffset From = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset To = new(2026, 8, 19, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Only_merged_pull_requests_inside_the_window_are_counted()
    {
        var transport = new RoutingTransport()
            .Returns("/pulls?", $"""
                [
                  {Pull(1, merged: "2026-07-01T10:00:00Z", updated: "2026-07-01T10:00:00Z")},
                  {Pull(2, merged: null, updated: "2026-07-02T10:00:00Z")},
                  {Pull(3, merged: "2026-05-01T10:00:00Z", updated: "2026-07-03T10:00:00Z")}
                ]
                """)
            .Returns("/reviews", "[]");

        var activity = await new GitHubActivityClient(transport)
            .GetActivityAsync(Repository, From, To, "jsdotnet", TestContext.Current.CancellationToken);

        var pull = Assert.Single(activity.PullRequests);
        Assert.Equal(1, pull.Number);
    }

    [Fact]
    public async Task Somebody_elses_pull_request_is_not_counted_as_yours()
    {
        var transport = new RoutingTransport()
            .Returns("/pulls?", $"""
                [
                  {Pull(1, merged: "2026-07-01T10:00:00Z", updated: "2026-07-01T10:00:00Z", author: "someone-else")},
                  {Pull(2, merged: "2026-07-02T10:00:00Z", updated: "2026-07-02T10:00:00Z")}
                ]
                """)
            .Returns("/reviews", "[]");

        var activity = await new GitHubActivityClient(transport)
            .GetActivityAsync(Repository, From, To, "jsdotnet", TestContext.Current.CancellationToken);

        var pull = Assert.Single(activity.PullRequests);
        Assert.Equal(2, pull.Number);
    }

    /// <summary>
    /// A comment is not a verdict. Counting COMMENTED reviews as rounds would make
    /// every conversation on a pull request look like rework.
    /// </summary>
    [Fact]
    public async Task A_comment_review_is_not_a_review_round()
    {
        var transport = new RoutingTransport()
            .Returns("/pulls?", $"[{Pull(1, merged: "2026-07-05T10:00:00Z", updated: "2026-07-05T10:00:00Z")}]")
            .Returns("/reviews", """
                [
                  { "state": "COMMENTED", "submitted_at": "2026-07-02T09:00:00Z" },
                  { "state": "CHANGES_REQUESTED", "submitted_at": "2026-07-03T09:00:00Z" },
                  { "state": "APPROVED", "submitted_at": "2026-07-04T09:00:00Z" }
                ]
                """)
            .Returns("/commits", "[]")
            .Returns("/timeline", "[]");

        var activity = await new GitHubActivityClient(transport)
            .GetActivityAsync(Repository, From, To, "jsdotnet", TestContext.Current.CancellationToken);

        var pull = Assert.Single(activity.PullRequests);
        Assert.Equal(2, pull.ReviewRounds);
        Assert.Equal(1, pull.ChangesRequested);
        Assert.Equal(new DateTimeOffset(2026, 7, 3, 9, 0, 0, TimeSpan.Zero), pull.FirstReviewedAt);
    }

    /// <summary>
    /// An unreviewed pull request has no "after the review" to look in, so the two
    /// extra calls are not made — and the record says it had no review rather than
    /// that it had no churn, which are different claims.
    /// </summary>
    [Fact]
    public async Task An_unreviewed_pull_request_costs_no_extra_calls_and_reports_no_review()
    {
        var transport = new RoutingTransport()
            .Returns("/pulls?", $"[{Pull(1, merged: "2026-07-05T10:00:00Z", updated: "2026-07-05T10:00:00Z")}]")
            .Returns("/reviews", "[]");

        var activity = await new GitHubActivityClient(transport)
            .GetActivityAsync(Repository, From, To, "jsdotnet", TestContext.Current.CancellationToken);

        var pull = Assert.Single(activity.PullRequests);
        Assert.Null(pull.FirstReviewedAt);
        Assert.Null(pull.ReviewTurnaround);
        Assert.Equal(0, pull.CommitsAfterFirstReview);
        Assert.Equal(0, transport.CallsTo("/commits"));
        Assert.Equal(0, transport.CallsTo("/timeline"));
    }

    [Fact]
    public async Task Commits_and_force_pushes_after_the_first_review_are_the_churn()
    {
        var transport = new RoutingTransport()
            .Returns("/pulls?", $"[{Pull(1, merged: "2026-07-10T10:00:00Z", updated: "2026-07-10T10:00:00Z")}]")
            .Returns("/reviews", """
                [{ "state": "CHANGES_REQUESTED", "submitted_at": "2026-07-05T09:00:00Z" }]
                """)
            .Returns("/pulls/1/commits", """
                [
                  { "sha": "aaa", "commit": { "committer": { "date": "2026-07-02T09:00:00Z" } } },
                  { "sha": "bbb", "commit": { "committer": { "date": "2026-07-06T09:00:00Z" } } },
                  { "sha": "ccc", "commit": { "committer": { "date": "2026-07-07T09:00:00Z" } } }
                ]
                """)
            .Returns("/timeline", """
                [
                  { "event": "head_ref_force_pushed", "created_at": "2026-07-01T09:00:00Z" },
                  { "event": "head_ref_force_pushed", "created_at": "2026-07-06T10:00:00Z" },
                  { "event": "labeled", "created_at": "2026-07-06T11:00:00Z" }
                ]
                """)
            .Returns("/commits/aaa", """{ "files": [ { "filename": "a.cs" }, { "filename": "b.cs" } ] }""")
            .Returns("/commits/bbb", """{ "files": [ { "filename": "b.cs" } ] }""")
            .Returns("/commits/ccc", """{ "files": [ { "filename": "new.cs" } ] }""");

        var activity = await new GitHubActivityClient(transport)
            .GetActivityAsync(Repository, From, To, "jsdotnet", TestContext.Current.CancellationToken);

        var pull = Assert.Single(activity.PullRequests);
        Assert.Equal(2, pull.CommitsAfterFirstReview);

        // Only the one after the first review; the one before it is not churn.
        Assert.Equal(1, pull.ForcePushesAfterFirstReview);

        // b.cs was touched on both sides of the review. new.cs was added after it,
        // which is more work rather than the same work again.
        Assert.Equal(1, pull.FilesRetouched);
        Assert.True(pull.ChurnComplete);
    }

    /// <summary>
    /// The committer date, not the author date. A rebase keeps the author date from
    /// before the review, which would make every post-review commit look like it
    /// predated the review and report zero churn on a heavily reworked branch.
    /// </summary>
    [Fact]
    public async Task A_rebased_commit_is_dated_by_when_it_was_committed()
    {
        var transport = new RoutingTransport()
            .Returns("/pulls?", $"[{Pull(1, merged: "2026-07-10T10:00:00Z", updated: "2026-07-10T10:00:00Z")}]")
            .Returns("/reviews", """
                [{ "state": "CHANGES_REQUESTED", "submitted_at": "2026-07-05T09:00:00Z" }]
                """)
            .Returns("/pulls/1/commits", """
                [{
                  "sha": "aaa",
                  "commit": {
                    "author": { "date": "2026-07-01T09:00:00Z" },
                    "committer": { "date": "2026-07-08T09:00:00Z" }
                  }
                }]
                """)
            .Returns("/timeline", "[]");

        var activity = await new GitHubActivityClient(transport)
            .GetActivityAsync(Repository, From, To, "jsdotnet", TestContext.Current.CancellationToken);

        Assert.Equal(1, Assert.Single(activity.PullRequests).CommitsAfterFirstReview);
    }

    /// <summary>
    /// A capped figure that reads as a whole one is how a dashboard stops being
    /// trusted, so the cap travels on the record.
    /// </summary>
    [Fact]
    public async Task A_pull_request_with_more_commits_than_are_inspected_is_reported_as_incomplete()
    {
        var before = string.Join(",", Enumerable.Range(1, 25).Select(number =>
            $$"""{ "sha": "b{{number}}", "commit": { "committer": { "date": "2026-07-0{{number % 5 + 1}}T09:00:00Z" } } }"""));

        var after = string.Join(",", Enumerable.Range(1, 25).Select(number =>
            $$"""{ "sha": "a{{number}}", "commit": { "committer": { "date": "2026-07-1{{number % 5 + 1}}T09:00:00Z" } } }"""));

        var transport = new RoutingTransport()
            .Returns("/pulls?", $"[{Pull(1, merged: "2026-08-01T10:00:00Z", updated: "2026-08-01T10:00:00Z")}]")
            .Returns("/reviews", """
                [{ "state": "CHANGES_REQUESTED", "submitted_at": "2026-07-09T09:00:00Z" }]
                """)
            .Returns("/pulls/1/commits", $"[{before},{after}]")
            .Returns("/timeline", "[]")
            .Returns("/commits/", """{ "files": [ { "filename": "a.cs" } ] }""");

        var activity = await new GitHubActivityClient(transport)
            .GetActivityAsync(Repository, From, To, "jsdotnet", TestContext.Current.CancellationToken);

        var pull = Assert.Single(activity.PullRequests);
        Assert.False(pull.ChurnComplete);
        Assert.Equal(25, pull.CommitsAfterFirstReview);
    }

    /// <summary>
    /// The timeline is the one endpoint here a token can be refused for while the
    /// rest work. Losing force-push counts is better than losing the pull request.
    /// </summary>
    [Fact]
    public async Task A_refused_timeline_costs_the_force_push_count_and_nothing_else()
    {
        var transport = new RoutingTransport()
            .Returns("/pulls?", $"[{Pull(1, merged: "2026-07-10T10:00:00Z", updated: "2026-07-10T10:00:00Z")}]")
            .Returns("/reviews", """
                [{ "state": "APPROVED", "submitted_at": "2026-07-05T09:00:00Z" }]
                """)
            .Returns("/pulls/1/commits", """
                [{ "sha": "aaa", "commit": { "committer": { "date": "2026-07-06T09:00:00Z" } } }]
                """)
            .Refuses("/timeline");

        var activity = await new GitHubActivityClient(transport)
            .GetActivityAsync(Repository, From, To, "jsdotnet", TestContext.Current.CancellationToken);

        var pull = Assert.Single(activity.PullRequests);
        Assert.Equal(1, pull.CommitsAfterFirstReview);
        Assert.Equal(0, pull.ForcePushesAfterFirstReview);
    }

    /// <summary>
    /// GitHub's issues endpoint returns pull requests too. Counting them would double
    /// every throughput figure on the dashboard.
    /// </summary>
    [Fact]
    public async Task A_pull_request_returned_by_the_issues_endpoint_is_not_counted_as_a_closed_issue()
    {
        var transport = new RoutingTransport()
            .Returns("/pulls?", "[]")
            .Returns("/issues?", """
                [
                  { "number": 10, "html_url": "u", "title": "issue", "closed_at": "2026-07-01T10:00:00Z" },
                  { "number": 11, "html_url": "u", "title": "pull", "closed_at": "2026-07-02T10:00:00Z",
                    "pull_request": { "url": "u" } }
                ]
                """);

        var activity = await new GitHubActivityClient(transport)
            .GetActivityAsync(Repository, From, To, "jsdotnet", TestContext.Current.CancellationToken);

        var issue = Assert.Single(activity.Issues);
        Assert.Equal(10, issue.Number);
    }

    /// <summary>
    /// <c>since</c> filters on last update, not on close, so the window has to be
    /// applied again on <c>closed_at</c>. Without it an issue closed months ago and
    /// commented on yesterday would count as closed this week.
    /// </summary>
    [Fact]
    public async Task An_old_issue_touched_recently_is_not_counted_as_closed_recently()
    {
        var transport = new RoutingTransport()
            .Returns("/pulls?", "[]")
            .Returns("/issues?", """
                [{ "number": 10, "html_url": "u", "title": "issue",
                   "closed_at": "2025-01-01T10:00:00Z", "updated_at": "2026-08-01T10:00:00Z" }]
                """);

        var activity = await new GitHubActivityClient(transport)
            .GetActivityAsync(Repository, From, To, "jsdotnet", TestContext.Current.CancellationToken);

        Assert.Empty(activity.Issues);
    }

    [Fact]
    public async Task Activity_is_read_on_the_transports_default_api_version()
    {
        var transport = new RoutingTransport().Returns("/pulls?", "[]").Returns("/issues?", "[]");

        _ = await new GitHubActivityClient(transport).GetActivityAsync(Repository, From, To, "jsdotnet", TestContext.Current.CancellationToken);

        Assert.NotEmpty(transport.ApiVersions);
        Assert.All(transport.ApiVersions, version => Assert.Null(version));
    }

    [Fact]
    public async Task An_unreachable_transport_explains_itself_rather_than_throwing()
    {
        var availability = await new GitHubActivityClient(new RoutingTransport { Available = false })
            .GetAvailabilityAsync(TestContext.Current.CancellationToken);

        Assert.False(availability.IsAvailable);
        Assert.Contains("gh auth login", availability.Reason, StringComparison.Ordinal);
    }

    // --- The listing walk -----------------------------------------------------

    /// <summary>
    /// The highest-value assertion in this class. Before this flag existed the walk
    /// stopped at the page cap in silence, and everything built on the list — the
    /// throughput count, the churn rate, the review turnaround — read as a whole
    /// answer while being a prefix of one. At a busy repository's volume a
    /// twelve-week window reaches that cap today.
    /// </summary>
    [Fact]
    public async Task A_listing_that_hit_the_page_cap_says_so_instead_of_looking_complete()
    {
        // Every page full, every row merged inside the window: nothing tells the
        // walk to stop except running out of pages it is willing to read.
        var transport = new RoutingTransport()
            .Returns("/pulls?", FullPage(merged: "2026-07-01T10:00:00Z", updated: "2026-07-01T10:00:00Z"))
            .Returns("/reviews", "[]");

        var activity = await new GitHubActivityClient(transport)
            .GetActivityAsync(Repository, From, To, "jsdotnet", TestContext.Current.CancellationToken);

        Assert.False(activity.ListingComplete);

        // And it still reports everything it did read. A truncated answer is worth
        // showing; only a truncated answer presented as a whole one is not.
        Assert.NotEmpty(activity.PullRequests);
    }

    [Fact]
    public async Task A_listing_that_ran_out_naturally_is_complete()
    {
        var transport = new RoutingTransport()
            .Returns("/pulls?", $"[{Pull(1, merged: "2026-07-01T10:00:00Z", updated: "2026-07-01T10:00:00Z")}]")
            .Returns("/reviews", "[]");

        var activity = await new GitHubActivityClient(transport)
            .GetActivityAsync(Repository, From, To, "jsdotnet", TestContext.Current.CancellationToken);

        Assert.True(activity.ListingComplete);
        Assert.Equal(1, transport.CallsTo("/pulls?"));
    }

    /// <summary>
    /// A full page that walked past the window is the ordinary way a busy
    /// repository's fetch ends, and it is a complete answer: sorted by
    /// <c>updated</c> descending, nothing after the first row older than the window
    /// can be inside it.
    /// </summary>
    [Fact]
    public async Task A_listing_that_walked_past_the_window_is_complete()
    {
        var transport = new RoutingTransport()
            .Returns("/pulls?", PageEndingBeforeTheWindow())
            .Returns("/reviews", "[]");

        var activity = await new GitHubActivityClient(transport)
            .GetActivityAsync(Repository, From, To, "jsdotnet", TestContext.Current.CancellationToken);

        Assert.True(activity.ListingComplete);

        // The page was full, so it was the window that stopped it and not a short page.
        Assert.Equal(1, transport.CallsTo("/pulls?"));
    }

    // --- Size -----------------------------------------------------------------

    [Fact]
    public async Task Merged_pull_requests_carry_their_size_and_files_touched()
    {
        var transport = new RoutingTransport()
            .Returns("/pulls?", $"[{Pull(1, merged: "2026-07-05T10:00:00Z", updated: "2026-07-05T10:00:00Z")}]")
            .Returns("/reviews", "[]")

            // Registered after the more specific routes, so it answers the pull
            // request itself rather than one of its sub-resources.
            .Returns("/pulls/1", """{ "additions": 120, "deletions": 30, "changed_files": 7 }""");

        var activity = await new GitHubActivityClient(transport)
            .GetActivityAsync(Repository, From, To, "jsdotnet", TestContext.Current.CancellationToken);

        var pull = Assert.Single(activity.PullRequests);
        Assert.True(pull.SizeKnown);
        Assert.Equal(150, pull.ChangedLines);
        Assert.Equal(7, pull.ChangedFiles);
        Assert.True(activity.DetailComplete);
    }

    /// <summary>
    /// A pull request whose size could not be read was still merged. Dropping it
    /// would lose a real merge; reporting its zero as a size would pull every size
    /// figure down towards nothing.
    /// </summary>
    [Fact]
    public async Task A_pull_request_whose_detail_could_not_be_read_says_its_size_is_unknown()
    {
        var transport = new RoutingTransport()
            .Returns("/pulls?", $"[{Pull(1, merged: "2026-07-05T10:00:00Z", updated: "2026-07-05T10:00:00Z")}]")
            .Returns("/reviews", "[]")
            .Refuses("/pulls/1");

        var activity = await new GitHubActivityClient(transport)
            .GetActivityAsync(Repository, From, To, "jsdotnet", TestContext.Current.CancellationToken);

        var pull = Assert.Single(activity.PullRequests);
        Assert.Equal(1, pull.Number);
        Assert.False(pull.SizeKnown);
        Assert.Equal(0, pull.ChangedLines);
        Assert.False(activity.DetailComplete);
    }

    // --- The detail cache -----------------------------------------------------

    [Fact]
    public async Task A_pull_request_already_in_the_cache_is_not_fetched_again()
    {
        var cache = new RememberingCache();
        cache.Write(Repository, 1, new PullRequestDetail { ChurnComplete = true, SizeKnown = true, ChangedLines = 42 });

        var transport = new RoutingTransport()
            .Returns("/pulls?", $"[{Pull(1, merged: "2026-07-05T10:00:00Z", updated: "2026-07-05T10:00:00Z")}]");

        var activity = await new GitHubActivityClient(transport, cache)
            .GetActivityAsync(Repository, From, To, "jsdotnet", TestContext.Current.CancellationToken);

        var pull = Assert.Single(activity.PullRequests);
        Assert.Equal(42, pull.ChangedLines);

        // The whole point: the listing still happened, and nothing per-pull-request did.
        Assert.Equal(1, transport.CallsTo("/pulls?"));
        Assert.Equal(0, transport.CallsTo("/reviews"));
        Assert.Equal(0, transport.CallsTo("/pulls/1"));
    }

    /// <summary>
    /// The single most important property of the cache. A capped figure that came
    /// back uncapped would make the second read of a window quietly more confident
    /// than the first — the same defect <c>ChurnComplete</c> exists to prevent,
    /// reintroduced one layer down.
    /// </summary>
    [Fact]
    public async Task A_cached_floor_comes_back_a_floor()
    {
        var cache = new RememberingCache();
        cache.Write(Repository, 1, new PullRequestDetail
        {
            FirstReviewedAt = new DateTimeOffset(2026, 7, 2, 9, 0, 0, TimeSpan.Zero),
            FilesRetouched = 20,
            ChurnComplete = false,
            SizeKnown = false
        });

        var transport = new RoutingTransport()
            .Returns("/pulls?", $"[{Pull(1, merged: "2026-07-05T10:00:00Z", updated: "2026-07-05T10:00:00Z")}]");

        var activity = await new GitHubActivityClient(transport, cache)
            .GetActivityAsync(Repository, From, To, "jsdotnet", TestContext.Current.CancellationToken);

        var pull = Assert.Single(activity.PullRequests);
        Assert.False(pull.ChurnComplete);
        Assert.Equal(20, pull.FilesRetouched);

        // And the size flag with it, which is what keeps DetailComplete honest on a
        // run that fetched nothing at all.
        Assert.False(pull.SizeKnown);
        Assert.False(activity.DetailComplete);
    }

    [Fact]
    public async Task A_freshly_read_pull_request_is_remembered_for_next_time()
    {
        var cache = new RememberingCache();

        var transport = new RoutingTransport()
            .Returns("/pulls?", $"[{Pull(1, merged: "2026-07-05T10:00:00Z", updated: "2026-07-05T10:00:00Z")}]")
            .Returns("/reviews", "[]")
            .Returns("/pulls/1", """{ "additions": 10, "deletions": 5, "changed_files": 2 }""");

        _ = await new GitHubActivityClient(transport, cache)
            .GetActivityAsync(Repository, From, To, "jsdotnet", TestContext.Current.CancellationToken);

        var remembered = cache.TryRead(Repository, 1);
        Assert.NotNull(remembered);
        Assert.Equal(15, remembered.ChangedLines);
        Assert.True(remembered.SizeKnown);
    }

    /// <summary>A hundred rows — a full page, which is what tells the walk there may
    /// be another one.</summary>
    private static string FullPage(string? merged, string updated) =>
        "[" + string.Join(",", Enumerable.Range(1, 100).Select(number => Pull(number, merged, updated))) + "]";

    /// <summary>A full page whose last row was last touched before the window, which
    /// is the signal the walk stops on.</summary>
    private static string PageEndingBeforeTheWindow() =>
        "["
        + string.Join(",", Enumerable.Range(1, 99).Select(number =>
            Pull(number, merged: null, updated: "2026-07-01T10:00:00Z")))
        + "," + Pull(100, merged: null, updated: "2025-01-01T10:00:00Z")
        + "]";

    /// <summary>
    /// The cache, in memory. What is pinned here is the client's use of the port —
    /// that a hit skips the calls and that the honesty flags survive the round trip.
    /// Whether a real file comes back is the file-system project's test, where the
    /// disk is the thing under test.
    /// </summary>
    private sealed class RememberingCache : IPullRequestDetailCache
    {
        private readonly Dictionary<string, PullRequestDetail> _entries = [];

        public PullRequestDetail? TryRead(GitHubRepositoryRef repository, int number) =>
            _entries.GetValueOrDefault(Key(repository, number));

        public void Write(GitHubRepositoryRef repository, int number, PullRequestDetail detail) =>
            _entries[Key(repository, number)] = detail;

        public void ForgetRepository(GitHubRepositoryRef repository)
        {
            foreach (var key in _entries.Keys
                         .Where(key => key.StartsWith(repository.FullName + "#", StringComparison.Ordinal))
                         .ToList())
            {
                _entries.Remove(key);
            }
        }

        private static string Key(GitHubRepositoryRef repository, int number) => $"{repository.FullName}#{number}";
    }

    private static string Pull(int number, string? merged, string updated, string author = "jsdotnet") =>
        $$"""
        {
          "number": {{number}},
          "html_url": "https://github.com/JSdotNet/Backlog/pull/{{number}}",
          "title": "A pull request",
          "user": { "login": "{{author}}" },
          "created_at": "2026-06-28T10:00:00Z",
          "updated_at": "{{updated}}",
          "merged_at": {{(merged is null ? "null" : $"\"{merged}\"")}}
        }
        """;
}
