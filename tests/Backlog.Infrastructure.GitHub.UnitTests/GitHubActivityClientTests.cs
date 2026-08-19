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
            .GetActivityAsync(Repository, From, To, "jsdotnet");

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
            .GetActivityAsync(Repository, From, To, "jsdotnet");

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
            .GetActivityAsync(Repository, From, To, "jsdotnet");

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
            .GetActivityAsync(Repository, From, To, "jsdotnet");

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
            .GetActivityAsync(Repository, From, To, "jsdotnet");

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
            .GetActivityAsync(Repository, From, To, "jsdotnet");

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
            .GetActivityAsync(Repository, From, To, "jsdotnet");

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
            .GetActivityAsync(Repository, From, To, "jsdotnet");

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
            .GetActivityAsync(Repository, From, To, "jsdotnet");

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
            .GetActivityAsync(Repository, From, To, "jsdotnet");

        Assert.Empty(activity.Issues);
    }

    [Fact]
    public async Task Activity_is_read_on_the_transports_default_api_version()
    {
        var transport = new RoutingTransport().Returns("/pulls?", "[]").Returns("/issues?", "[]");

        _ = await new GitHubActivityClient(transport).GetActivityAsync(Repository, From, To, "jsdotnet");

        Assert.NotEmpty(transport.ApiVersions);
        Assert.All(transport.ApiVersions, version => Assert.Null(version));
    }

    [Fact]
    public async Task An_unreachable_transport_explains_itself_rather_than_throwing()
    {
        var availability = await new GitHubActivityClient(new RoutingTransport { Available = false })
            .GetAvailabilityAsync();

        Assert.False(availability.IsAvailable);
        Assert.Contains("gh auth login", availability.Reason, StringComparison.Ordinal);
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
