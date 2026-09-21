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
    /// An unreviewed pull request has no "after the review" to look in, so the
    /// timeline is not read — and the record says it had no review rather than
    /// that it had no churn, which are different claims. Its commits are still
    /// listed, once, because the branch's sync merges are there whether anybody
    /// reviewed it or not.
    /// </summary>
    [Fact]
    public async Task An_unreviewed_pull_request_skips_the_timeline_and_reports_no_review()
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
        Assert.Equal(1, transport.CallsTo("/pulls/1/commits"));
        Assert.Equal(0, transport.CallsTo("/timeline"));
        Assert.True(pull.SyncsKnown);
        Assert.Equal(0, pull.SyncMerges);
    }

    // --- Sync merges -----------------------------------------------------------

    /// <summary>
    /// A two-parent commit on the branch is a sync with its base. Whether it
    /// conflicted is only in its message: git's own <c>Conflicts:</c> trailer, which
    /// a <c>--no-edit</c> commit keeps, or a body somebody wrote that names what
    /// conflicted. A merge that says nothing reads as clean, and one that says there
    /// was no conflict reads as clean too — it is the sentence a person writes when
    /// a merge went well, and counting it would turn every reassurance into its
    /// opposite.
    /// </summary>
    [Fact]
    public async Task Merge_commits_on_the_branch_are_syncs_and_only_their_messages_say_which_conflicted()
    {
        var transport = new RoutingTransport()
            .Returns("/pulls?", $"[{Pull(1, merged: "2026-07-10T10:00:00Z", updated: "2026-07-10T10:00:00Z")}]")
            .Returns("/reviews", "[]")
            .Returns("/pulls/1/commits", """
                [
                  { "sha": "aaa", "parents": [ { "sha": "p1" } ],
                    "commit": { "message": "Add the thing", "committer": { "date": "2026-07-02T09:00:00Z" } } },
                  { "sha": "bbb", "parents": [ { "sha": "aaa" }, { "sha": "main1" } ],
                    "commit": { "message": "Merge origin/main into claude/thing\n\n# Conflicts:\n#\tsrc/Thing.cs", "committer": { "date": "2026-07-03T09:00:00Z" } } },
                  { "sha": "ccc", "parents": [ { "sha": "bbb" }, { "sha": "main2" } ],
                    "commit": { "message": "Merge origin/main into claude/thing\n\nThing.cs conflicted: main renamed the field this branch reads.", "committer": { "date": "2026-07-05T09:00:00Z" } } },
                  { "sha": "ddd", "parents": [ { "sha": "ccc" }, { "sha": "main3" } ],
                    "commit": { "message": "Merge origin/main into claude/thing", "committer": { "date": "2026-07-07T09:00:00Z" } } },
                  { "sha": "eee", "parents": [ { "sha": "ddd" }, { "sha": "main4" } ],
                    "commit": { "message": "Merge origin/main into claude/thing\n\nNo conflicts, main only touched docs.", "committer": { "date": "2026-07-09T09:00:00Z" } } }
                ]
                """)
            .Returns("/pulls/1", """{ "additions": 10, "deletions": 5, "changed_files": 2 }""");

        var activity = await new GitHubActivityClient(transport)
            .GetActivityAsync(Repository, From, To, "jsdotnet", TestContext.Current.CancellationToken);

        var pull = Assert.Single(activity.PullRequests);
        Assert.True(pull.SyncsKnown);
        Assert.Equal(4, pull.SyncMerges);
        Assert.Equal(2, pull.ConflictedSyncMerges);
        Assert.True(activity.DetailComplete);
    }

    /// <summary>
    /// The commits of an unreviewed pull request feed nothing but the sync figures,
    /// so a refusal there costs those figures and not the pull request: it stays in
    /// the listing with its syncs declared unknown, and the repository says its
    /// detail is incomplete. Zero would be a claim — "never synced" — that nothing
    /// established.
    /// </summary>
    [Fact]
    public async Task A_refused_commits_listing_leaves_an_unreviewed_pull_requests_syncs_unknown()
    {
        var transport = new RoutingTransport()
            .Returns("/pulls?", $"[{Pull(1, merged: "2026-07-05T10:00:00Z", updated: "2026-07-05T10:00:00Z")}]")
            .Returns("/reviews", "[]")
            .Refuses("/pulls/1/commits")
            .Returns("/pulls/1", """{ "additions": 10, "deletions": 5, "changed_files": 2 }""");

        var activity = await new GitHubActivityClient(transport)
            .GetActivityAsync(Repository, From, To, "jsdotnet", TestContext.Current.CancellationToken);

        var pull = Assert.Single(activity.PullRequests);
        Assert.False(pull.SyncsKnown);
        Assert.Equal(0, pull.SyncMerges);
        Assert.True(pull.SizeKnown);
        Assert.False(activity.DetailComplete);
    }

    /// <summary>
    /// A reviewed pull request's churn is counted from the same commits, so there
    /// the refusal is what it always was: the repository cannot be answered for.
    /// Reading the commits earlier did not make that failure quieter.
    /// </summary>
    [Fact]
    public async Task A_refused_commits_listing_still_fails_a_reviewed_pull_request()
    {
        var transport = new RoutingTransport()
            .Returns("/pulls?", $"[{Pull(1, merged: "2026-07-05T10:00:00Z", updated: "2026-07-05T10:00:00Z")}]")
            .Returns("/reviews", """[{ "state": "APPROVED", "submitted_at": "2026-07-03T09:00:00Z" }]""")
            .Refuses("/pulls/1/commits");

        await Assert.ThrowsAsync<GitHubException>(() => new GitHubActivityClient(transport)
            .GetActivityAsync(Repository, From, To, "jsdotnet", TestContext.Current.CancellationToken));
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
            .Returns("/pulls/1", """{ "additions": 120, "deletions": 30, "changed_files": 7, "commits": 5 }""");

        var activity = await new GitHubActivityClient(transport)
            .GetActivityAsync(Repository, From, To, "jsdotnet", TestContext.Current.CancellationToken);

        var pull = Assert.Single(activity.PullRequests);
        Assert.True(pull.SizeKnown);
        Assert.Equal(150, pull.ChangedLines);
        Assert.Equal(7, pull.ChangedFiles);

        // Off the same response, so it costs no call and is known exactly when the
        // size is.
        Assert.Equal(5, pull.Commits);
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
        cache.Write(Repository, 1, new PullRequestDetail { ChurnComplete = true, SizeKnown = true, ChangedLines = 42, Commits = 6 });

        var transport = new RoutingTransport()
            .Returns("/pulls?", $"[{Pull(1, merged: "2026-07-05T10:00:00Z", updated: "2026-07-05T10:00:00Z")}]");

        var activity = await new GitHubActivityClient(transport, cache)
            .GetActivityAsync(Repository, From, To, "jsdotnet", TestContext.Current.CancellationToken);

        var pull = Assert.Single(activity.PullRequests);
        Assert.Equal(42, pull.ChangedLines);
        Assert.Equal(6, pull.Commits);

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
            .Returns("/pulls/1", """{ "additions": 10, "deletions": 5, "changed_files": 2, "commits": 3 }""");

        _ = await new GitHubActivityClient(transport, cache)
            .GetActivityAsync(Repository, From, To, "jsdotnet", TestContext.Current.CancellationToken);

        var remembered = cache.TryRead(Repository, 1);
        Assert.NotNull(remembered);
        Assert.Equal(15, remembered.ChangedLines);
        Assert.True(remembered.SizeKnown);
        Assert.Equal(3, remembered.Commits);
        Assert.True(remembered.SyncsKnown);
    }

    /// <summary>
    /// A refusal is not a fact about the pull request. The cache has no age —
    /// a merged pull request's size never changes, so an entry is kept until
    /// the repository is forgotten — which is exactly why a size that could not
    /// be read must not be written into it: remembered, the rate limit that
    /// refused one read would keep every later open reporting a floor for ever.
    /// </summary>
    [Fact]
    public async Task A_pull_request_whose_detail_could_not_be_read_is_not_remembered()
    {
        var cache = new RememberingCache();

        var transport = new RoutingTransport()
            .Returns("/pulls?", $"[{Pull(1, merged: "2026-07-05T10:00:00Z", updated: "2026-07-05T10:00:00Z")}]")
            .Returns("/reviews", "[]")
            .Refuses("/pulls/1");

        var activity = await new GitHubActivityClient(transport, cache)
            .GetActivityAsync(Repository, From, To, "jsdotnet", TestContext.Current.CancellationToken);

        Assert.False(Assert.Single(activity.PullRequests).SizeKnown);
        Assert.Null(cache.TryRead(Repository, 1));
    }

    /// <summary>The sync figures ride the cache both ways, unknown included — a
    /// remembered "could not be read" must not come back as "never synced".</summary>
    [Fact]
    public async Task Remembered_sync_figures_come_back_as_they_were_stored()
    {
        var cache = new RememberingCache();
        cache.Write(Repository, 1, new PullRequestDetail
        {
            ChurnComplete = true,
            SizeKnown = true,
            SyncMerges = 3,
            ConflictedSyncMerges = 1,
            SyncsKnown = true
        });
        cache.Write(Repository, 2, new PullRequestDetail { ChurnComplete = true, SizeKnown = true, SyncsKnown = false });

        var transport = new RoutingTransport()
            .Returns("/pulls?", $"""
                [
                  {Pull(1, merged: "2026-07-05T10:00:00Z", updated: "2026-07-05T10:00:00Z")},
                  {Pull(2, merged: "2026-07-06T10:00:00Z", updated: "2026-07-06T10:00:00Z")}
                ]
                """);

        var activity = await new GitHubActivityClient(transport, cache)
            .GetActivityAsync(Repository, From, To, "jsdotnet", TestContext.Current.CancellationToken);

        var synced = Assert.Single(activity.PullRequests, pull => pull.Number == 1);
        Assert.Equal(3, synced.SyncMerges);
        Assert.Equal(1, synced.ConflictedSyncMerges);
        Assert.True(synced.SyncsKnown);

        var unknown = Assert.Single(activity.PullRequests, pull => pull.Number == 2);
        Assert.False(unknown.SyncsKnown);
        Assert.False(activity.DetailComplete);
        Assert.Equal(0, transport.CallsTo("/commits"));
    }

    /// <summary>A hundred rows — a full page, which is what tells the walk there may
    /// be another one.</summary>
    // --- The listing cache ----------------------------------------------------

    /// <summary>
    /// The saving the listing cache exists for. A window inside what the last walk
    /// covered starts from that walk's high-water mark, so a row touched before it
    /// is never read — the stored copy is the answer for that row.
    /// </summary>
    [Fact]
    public async Task A_remembered_listing_is_walked_only_back_to_where_the_last_walk_stopped()
    {
        var listings = new RememberingListings();
        listings.Write(Repository, "jsdotnet", new ActivityListing(
            [new ListedPullRequest(1, "u", "remembered", At("2026-06-28T10:00:00Z"), At("2026-07-01T10:00:00Z"))],
            [],
            new ActivityListingCoverage(From, WalkedThrough: At("2026-08-01T12:00:00Z")),
            new ActivityListingCoverage(From, WalkedThrough: At("2026-08-01T12:00:00Z"))));

        // Sorted as GitHub sorts it: the row merged since the last walk first, then
        // one touched before the mark — inside the window, but the walk must not
        // get that far, because a complete previous walk would already hold it.
        var transport = new RoutingTransport()
            .Returns("/pulls?", $"""
                [
                  {Pull(5, merged: "2026-08-10T10:00:00Z", updated: "2026-08-10T10:00:00Z")},
                  {Pull(9, merged: "2026-07-20T10:00:00Z", updated: "2026-07-20T10:00:00Z")}
                ]
                """)
            .Returns("/reviews", "[]");

        var activity = await new GitHubActivityClient(transport, listings: listings)
            .GetActivityAsync(Repository, From, To, "jsdotnet", TestContext.Current.CancellationToken);

        Assert.Equal([5, 1], activity.PullRequests.Select(pull => pull.Number));
        Assert.True(activity.ListingComplete);
        Assert.Equal(1, transport.CallsTo("/pulls?"));

        // And the issues walk asks GitHub for what changed since the mark, minus the
        // slack, rather than since the window opened.
        var issues = Assert.Single(transport.Paths.Where(path => path.Contains("/issues?", StringComparison.Ordinal)));
        Assert.Contains("since=2026-08-01T11%3A55%3A00Z", issues, StringComparison.Ordinal);
        Assert.Contains("state=all", issues, StringComparison.Ordinal);
    }

    /// <summary>
    /// A wider window than the listing covers is not a delta. It walks back to its
    /// own start, which picks up the row the narrower walk was allowed to stop
    /// before, and the coverage it leaves behind reaches that far.
    /// </summary>
    [Fact]
    public async Task A_window_starting_before_the_coverage_walks_back_to_its_own_start()
    {
        var listings = new RememberingListings();
        listings.Write(Repository, "jsdotnet", new ActivityListing(
            [],
            [],
            new ActivityListingCoverage(CoveredFrom: At("2026-08-01T00:00:00Z"), WalkedThrough: At("2026-08-15T12:00:00Z")),
            null));

        var transport = new RoutingTransport()
            .Returns("/pulls?", $"[{Pull(9, merged: "2026-07-20T10:00:00Z", updated: "2026-07-20T10:00:00Z")}]")
            .Returns("/reviews", "[]");

        var activity = await new GitHubActivityClient(transport, listings: listings)
            .GetActivityAsync(Repository, From, To, "jsdotnet", TestContext.Current.CancellationToken);

        var pull = Assert.Single(activity.PullRequests);
        Assert.Equal(9, pull.Number);

        var written = listings.TryRead(Repository, "jsdotnet")!;
        Assert.Equal(From, written.PullRequestCoverage!.CoveredFrom);

        // Forward never retreats: the newest row this walk saw is older than the
        // mark the last one left, and the mark stays.
        Assert.Equal(At("2026-08-15T12:00:00Z"), written.PullRequestCoverage.WalkedThrough);
    }

    /// <summary>
    /// The property everything else rests on. A walk that ran out of pages has not
    /// seen the rows past them, so it keeps what it found and covers only from the
    /// oldest row it reached — never the window it was asked for. The next read of
    /// this window walks it again; a narrower window inside what was reached is a
    /// delta.
    /// </summary>
    [Fact]
    public async Task An_incomplete_walk_keeps_its_rows_and_covers_only_what_it_reached()
    {
        var listings = new RememberingListings();

        var transport = new RoutingTransport()
            .Returns("/pulls?", FullPage(merged: "2026-07-01T10:00:00Z", updated: "2026-07-01T10:00:00Z"))
            .Returns("/reviews", "[]");

        var activity = await new GitHubActivityClient(transport, listings: listings)
            .GetActivityAsync(Repository, From, To, "jsdotnet", TestContext.Current.CancellationToken);

        Assert.False(activity.ListingComplete);

        var written = listings.TryRead(Repository, "jsdotnet")!;
        Assert.NotEmpty(written.PullRequests);
        Assert.NotNull(written.PullRequestCoverage);
        Assert.True(written.PullRequestCoverage.CoveredFrom > From);
        Assert.Equal(At("2026-07-01T10:05:00Z"), written.PullRequestCoverage.CoveredFrom);
    }

    /// <summary>
    /// An incomplete walk may not widen what was covered before, either: a delta
    /// walk that ran out of pages has unread rows between its last page and the
    /// old mark, and a merge in there would be trusted on the strength of a walk
    /// that never saw it. Coverage narrows to what this walk proved.
    /// </summary>
    [Fact]
    public async Task An_incomplete_delta_walk_narrows_the_coverage_to_what_it_reached()
    {
        var listings = new RememberingListings();
        listings.Write(Repository, "jsdotnet", new ActivityListing(
            [],
            [],
            new ActivityListingCoverage(From, WalkedThrough: At("2026-06-15T12:00:00Z")),
            null));

        var transport = new RoutingTransport()
            .Returns("/pulls?", FullPage(merged: "2026-08-01T10:00:00Z", updated: "2026-08-01T10:00:00Z"))
            .Returns("/reviews", "[]");

        var activity = await new GitHubActivityClient(transport, listings: listings)
            .GetActivityAsync(Repository, From, To, "jsdotnet", TestContext.Current.CancellationToken);

        Assert.False(activity.ListingComplete);

        var written = listings.TryRead(Repository, "jsdotnet")!;
        Assert.Equal(At("2026-08-01T10:05:00Z"), written.PullRequestCoverage!.CoveredFrom);
        Assert.Equal(At("2026-08-01T10:00:00Z"), written.PullRequestCoverage.WalkedThrough);
    }

    [Fact]
    public async Task A_complete_walk_records_the_window_and_the_newest_row_it_saw()
    {
        var listings = new RememberingListings();

        var transport = new RoutingTransport()
            .Returns("/pulls?", $"""
                [
                  {Pull(2, merged: null, updated: "2026-08-18T09:00:00Z")},
                  {Pull(1, merged: "2026-07-01T10:00:00Z", updated: "2026-07-01T10:00:00Z")}
                ]
                """)
            .Returns("/reviews", "[]");

        _ = await new GitHubActivityClient(transport, listings: listings)
            .GetActivityAsync(Repository, From, To, "jsdotnet", TestContext.Current.CancellationToken);

        var written = listings.TryRead(Repository, "jsdotnet")!;
        Assert.Equal(new ActivityListingCoverage(From, At("2026-08-18T09:00:00Z")), written.PullRequestCoverage);

        // The open pull request was the newest row and is not a fact yet; only the
        // merged one is kept.
        var kept = Assert.Single(written.PullRequests);
        Assert.Equal(1, kept.Number);
    }

    /// <summary>
    /// A merged pull request touched before the window is a fact all the same, and
    /// keeping it is what lets a later, wider window be a delta rather than a walk.
    /// It is kept, and it is not reported for this window.
    /// </summary>
    [Fact]
    public async Task A_pull_request_merged_before_the_window_is_kept_but_not_reported()
    {
        var listings = new RememberingListings();

        var transport = new RoutingTransport()
            .Returns("/pulls?", $"[{Pull(3, merged: "2026-05-01T10:00:00Z", updated: "2026-07-03T10:00:00Z")}]")
            .Returns("/reviews", "[]");

        var activity = await new GitHubActivityClient(transport, listings: listings)
            .GetActivityAsync(Repository, From, To, "jsdotnet", TestContext.Current.CancellationToken);

        Assert.Empty(activity.PullRequests);
        Assert.Equal(3, Assert.Single(listings.TryRead(Repository, "jsdotnet")!.PullRequests).Number);
    }

    /// <summary>
    /// The one way a stored row stops being true. Reopening touches the issue, so
    /// it comes back in the walk — open — and the stored closed row goes with it.
    /// </summary>
    [Fact]
    public async Task A_reopened_issue_is_dropped_from_the_listing()
    {
        var listings = new RememberingListings();
        listings.Write(Repository, "jsdotnet", new ActivityListing(
            [],
            [
                new ListedIssue(3, "u", "reopened since", At("2026-07-01T10:00:00Z")),
                new ListedIssue(4, "u", "still closed", At("2026-07-02T10:00:00Z"))
            ],
            null,
            new ActivityListingCoverage(From, At("2026-08-01T12:00:00Z"))));

        var transport = new RoutingTransport()
            .Returns("/pulls?", "[]")
            .Returns("/issues?", """
                [
                  { "number": 3, "html_url": "u", "title": "reopened since", "state": "open", "closed_at": null,
                    "updated_at": "2026-08-10T10:00:00Z" }
                ]
                """);

        var activity = await new GitHubActivityClient(transport, listings: listings)
            .GetActivityAsync(Repository, From, To, "jsdotnet", TestContext.Current.CancellationToken);

        Assert.Equal(4, Assert.Single(activity.Issues).Number);
        Assert.Equal(4, Assert.Single(listings.TryRead(Repository, "jsdotnet")!.Issues).Number);
    }

    /// <summary>Without a cache the client is the client it was: every window is a
    /// full walk from its own start.</summary>
    [Fact]
    public async Task Without_a_listing_cache_the_issues_walk_starts_at_the_window()
    {
        var transport = new RoutingTransport().Returns("/pulls?", "[]");

        _ = await new GitHubActivityClient(transport)
            .GetActivityAsync(Repository, From, To, "jsdotnet", TestContext.Current.CancellationToken);

        var issues = Assert.Single(transport.Paths.Where(path => path.Contains("/issues?", StringComparison.Ordinal)));
        Assert.Contains("since=2026-06-01T00%3A00%3A00Z", issues, StringComparison.Ordinal);
    }

    private static DateTimeOffset At(string instant) =>
        DateTimeOffset.Parse(instant, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The listing cache, in memory, on the same terms as
    /// <see cref="RememberingCache"/>: the client's use of the port is what is
    /// pinned here, and the disk is the file-system project's test.</summary>
    private sealed class RememberingListings : IActivityListingCache
    {
        private readonly Dictionary<string, ActivityListing> _entries = [];

        public ActivityListing? TryRead(GitHubRepositoryRef repository, string author) =>
            _entries.GetValueOrDefault(Key(repository, author));

        public void Write(GitHubRepositoryRef repository, string author, ActivityListing listing) =>
            _entries[Key(repository, author)] = listing;

        public void ForgetRepository(GitHubRepositoryRef repository)
        {
            foreach (var key in _entries.Keys
                         .Where(key => key.StartsWith(repository.FullName + "#", StringComparison.Ordinal))
                         .ToList())
            {
                _entries.Remove(key);
            }
        }

        private static string Key(GitHubRepositoryRef repository, string author) =>
            $"{repository.FullName}#{author.ToLowerInvariant()}";
    }

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
