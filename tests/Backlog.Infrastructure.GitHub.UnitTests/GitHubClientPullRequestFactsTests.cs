using System.Text.Json;
using System.Text.Json.Nodes;
using Backlog.Infrastructure.GitHub;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// What the pull requests list says about a pull request beyond whether it can merge:
/// its labels, how far its checks have got, what its reviewers decided, and which
/// issues it closes — and the pinned read, which asks for named pull requests whatever
/// state they are in.
/// <para>
/// Each extra fact is one a narrower token can be refused, and a refused fact is only
/// that fact missing: the row is still listed, the way a refused check roll-up has
/// always read as no checks.
/// </para>
/// </summary>
public sealed class GitHubClientPullRequestFactsTests
{
    private static readonly GitHubRepositoryRef Repository = new("backlog", "JSdotNet", "Backlog");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    // --- Labels ---------------------------------------------------------------

    [Fact]
    public async Task An_open_pull_request_carries_its_label_names()
    {
        var pull = Assert.Single(await Open(Node(labels: """{ "nodes": [ { "name": "frontend" }, { "name": "needs review" } ] }""")));

        Assert.Equal(["frontend", "needs review"], pull.Labels);
    }

    [Fact]
    public async Task An_open_pull_request_without_labels_has_none()
    {
        var pull = Assert.Single(await Open(Node(labels: "null")));

        Assert.Empty(pull.Labels);
    }

    // --- Check progress -------------------------------------------------------

    /// <summary>Check runs and commit statuses are counted together, the way GitHub's
    /// own "8/9" does: a run that succeeded, was neutral or was skipped has passed; one
    /// that failed, errored, timed out, was cancelled, wants action or never started
    /// has failed; anything else is still to come.</summary>
    [Fact]
    public async Task Check_progress_counts_passed_failed_and_pending_across_runs_and_statuses()
    {
        var counts = """
            {
              "totalCount": 12,
              "checkRunCountsByState": [
                { "state": "SUCCESS", "count": 5 },
                { "state": "NEUTRAL", "count": 1 },
                { "state": "SKIPPED", "count": 1 },
                { "state": "FAILURE", "count": 1 },
                { "state": "TIMED_OUT", "count": 1 },
                { "state": "IN_PROGRESS", "count": 1 }
              ],
              "statusContextCountsByState": [
                { "state": "SUCCESS", "count": 1 },
                { "state": "PENDING", "count": 1 }
              ]
            }
            """;

        var pull = Assert.Single(await Open(Node(contexts: counts)));

        Assert.Equal(new GitHubCheckCounts(Passed: 8, Failed: 2, Pending: 2), pull.CheckCounts);
        Assert.Equal(12, pull.CheckCounts!.Total);
    }

    [Theory]
    [InlineData("CANCELLED")]
    [InlineData("ACTION_REQUIRED")]
    [InlineData("STARTUP_FAILURE")]
    [InlineData("ERROR")]
    public async Task A_run_that_cannot_go_green_on_its_own_counts_as_failed(string state)
    {
        var counts = $$"""{ "totalCount": 1, "checkRunCountsByState": [ { "state": "{{state}}", "count": 1 } ], "statusContextCountsByState": [] }""";

        var pull = Assert.Single(await Open(Node(contexts: counts)));

        Assert.Equal(new GitHubCheckCounts(Passed: 0, Failed: 1, Pending: 0), pull.CheckCounts);
    }

    [Fact]
    public async Task A_head_commit_with_no_checks_has_no_progress()
    {
        var pull = Assert.Single(await Open(Node(rollup: "null")));

        Assert.Null(pull.CheckCounts);
        Assert.Equal(GitHubCheckState.None, pull.Checks);
    }

    // --- Reviews --------------------------------------------------------------

    [Theory]
    [InlineData("\"APPROVED\"", GitHubReviewDecision.Approved)]
    [InlineData("\"CHANGES_REQUESTED\"", GitHubReviewDecision.ChangesRequested)]
    [InlineData("\"REVIEW_REQUIRED\"", GitHubReviewDecision.ReviewRequired)]
    [InlineData("null", null)]
    public async Task The_review_decision_is_githubs_own(string decision, GitHubReviewDecision? expected)
    {
        var pull = Assert.Single(await Open(Node(reviewDecision: decision)));

        Assert.Equal(expected, pull.Reviews.Decision);
    }

    [Fact]
    public async Task Approvals_and_change_requests_are_counted_from_the_latest_opinionated_reviews()
    {
        var reviews = """{ "nodes": [ { "state": "APPROVED" }, { "state": "APPROVED" }, { "state": "CHANGES_REQUESTED" }, { "state": "COMMENTED" } ] }""";

        var pull = Assert.Single(await Open(Node(reviews: reviews)));

        Assert.Equal(2, pull.Reviews.Approvals);
        Assert.Equal(1, pull.Reviews.ChangesRequested);
    }

    // --- Closing issues -------------------------------------------------------

    [Fact]
    public async Task The_issues_a_pull_request_closes_are_named_by_repository_and_number()
    {
        var closing = """{ "nodes": [ { "number": 42, "repository": { "nameWithOwner": "JSdotNet/Backlog" } }, { "number": 7, "repository": { "nameWithOwner": "innovadis-dev/spec-manager" } } ] }""";

        var pull = Assert.Single(await Open(Node(closingIssues: closing)));

        Assert.Equal(
            [new GitHubIssueReference("JSdotNet/Backlog", 42), new GitHubIssueReference("innovadis-dev/spec-manager", 7)],
            pull.ClosingIssues);
    }

    // --- The query --------------------------------------------------------------

    [Fact]
    public async Task The_open_query_asks_for_the_extra_facts()
    {
        var transport = new RoutingTransport().Returns("graphql", List(Node()));

        await new GitHubClient(transport).ListOpenPullRequestsAsync(Repository, Cancellation);

        using var body = JsonDocument.Parse(transport.Bodies[0]!);
        var query = body.RootElement.GetProperty("query").GetString()!;
        Assert.Contains("labels(first:", query, StringComparison.Ordinal);
        Assert.Contains("reviewDecision", query, StringComparison.Ordinal);
        Assert.Contains("latestOpinionatedReviews(", query, StringComparison.Ordinal);
        Assert.Contains("checkRunCountsByState", query, StringComparison.Ordinal);
        Assert.Contains("closingIssuesReferences(", query, StringComparison.Ordinal);
    }

    // --- Refusals -----------------------------------------------------------------

    /// <summary>A token without access to checks, reviews or the closing references is
    /// refused each of them once per pull request; the pull request is still listed and
    /// only the refused fact is missing.</summary>
    [Theory]
    [InlineData("checkCounts", "contexts")]
    [InlineData("latestOpinionatedReviews", null)]
    [InlineData("closingIssuesReferences", null)]
    [InlineData("labels", null)]
    [InlineData("reviewDecision", null)]
    public async Task A_refusal_confined_to_an_extra_fact_keeps_the_row(string field, string? inner)
    {
        var node = JsonNode.Parse(List(Node(
            contexts: "null",
            reviews: "null",
            closingIssues: "null")))!;

        var path = field == "checkCounts"
            ? $"""["repository", "pullRequests", "nodes", 0, "commits", "nodes", 0, "commit", "{field}", "{inner}"]"""
            : $"""["repository", "pullRequests", "nodes", 0, "{field}"]""";
        node["errors"] = new JsonArray(JsonNode.Parse(
            $$"""{ "type": "FORBIDDEN", "message": "Resource not accessible by personal access token", "path": {{path}} }"""));

        var pull = Assert.Single(await new GitHubClient(new RoutingTransport().Returns("graphql", node.ToJsonString()))
            .ListOpenPullRequestsAsync(Repository, Cancellation));

        Assert.Equal(712, pull.Number);
        Assert.Null(pull.CheckCounts);
        Assert.Equal(0, pull.Reviews.Approvals);
        Assert.Empty(pull.ClosingIssues);
    }

    // --- Merged -------------------------------------------------------------------

    [Fact]
    public async Task A_merged_pull_request_carries_its_labels_and_closing_issues()
    {
        var json = $$"""
            { "data": { "repository": { "pullRequests": { "nodes": [
              { "id": "PR_9", "number": 9, "title": "Ship", "url": "https://github.com/JSdotNet/Backlog/pull/9",
                "headRefName": "feature/FIN-8428", "baseRefName": "main", "viewerDidAuthor": false,
                "author": { "login": "someone" }, "updatedAt": "{{Recent}}", "mergedAt": "{{Recent}}", "mergedBy": { "login": "someone" },
                "labels": { "nodes": [ { "name": "backend" } ] },
                "closingIssuesReferences": { "nodes": [ { "number": 5, "repository": { "nameWithOwner": "JSdotNet/Backlog" } } ] } }
            ] } } } }
            """;

        var read = await new GitHubClient(new RoutingTransport().Returns("graphql", json))
            .ListMergedPullRequestsAsync(Repository, DateTimeOffset.UtcNow.AddDays(-14), Cancellation);

        var pull = Assert.Single(read.PullRequests);
        Assert.Equal(["backend"], pull.Labels);
        Assert.Equal([new GitHubIssueReference("JSdotNet/Backlog", 5)], pull.ClosingIssues);
    }

    // --- Pinned ---------------------------------------------------------------------

    [Fact]
    public async Task A_pinned_read_names_each_number_in_one_query()
    {
        var transport = new RoutingTransport().Returns("graphql", Pinned(("p12", PinnedNode(12, "OPEN")), ("p34", PinnedNode(34, "MERGED"))));

        await new GitHubClient(transport).ListPullRequestsAsync(Repository, [12, 34], Cancellation);

        Assert.Equal("graphql#JSdotNet/Backlog", Assert.Single(transport.Paths));
        using var body = JsonDocument.Parse(transport.Bodies[0]!);
        var query = body.RootElement.GetProperty("query").GetString()!;
        Assert.Contains("p12: pullRequest(number: 12)", query, StringComparison.Ordinal);
        Assert.Contains("p34: pullRequest(number: 34)", query, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("OPEN", false, GitHubItemState.Open)]
    [InlineData("OPEN", true, GitHubItemState.Draft)]
    [InlineData("MERGED", false, GitHubItemState.Merged)]
    [InlineData("CLOSED", false, GitHubItemState.Closed)]
    public async Task A_pinned_pull_request_is_read_in_whatever_state_it_is_in(string state, bool draft, GitHubItemState expected)
    {
        var pulls = await new GitHubClient(new RoutingTransport().Returns("graphql", Pinned(("p12", PinnedNode(12, state, draft)))))
            .ListPullRequestsAsync(Repository, [12], Cancellation);

        var pull = Assert.Single(pulls);
        Assert.Equal(expected, pull.State);
        Assert.Equal(expected is GitHubItemState.Open or GitHubItemState.Draft, pull.IsOpen);
    }

    [Fact]
    public async Task A_merged_pinned_pull_request_says_when_it_merged()
    {
        var pull = Assert.Single(await new GitHubClient(new RoutingTransport().Returns("graphql", Pinned(("p12", PinnedNode(12, "MERGED")))))
            .ListPullRequestsAsync(Repository, [12], Cancellation));

        Assert.Equal(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero), pull.MergedAt);
    }

    /// <summary>A pin on a pull request GitHub can no longer find — deleted with its
    /// repository's history, or a number typed wrong — is left out; the others are
    /// still read.</summary>
    [Fact]
    public async Task A_pinned_number_github_cannot_find_is_skipped()
    {
        var node = JsonNode.Parse(Pinned(("p12", PinnedNode(12, "OPEN")), ("p99", "null")))!;
        node["errors"] = new JsonArray(JsonNode.Parse(
            """{ "type": "NOT_FOUND", "message": "Could not resolve to a PullRequest with the number of 99.", "path": ["repository", "p99"] }"""));

        var pulls = await new GitHubClient(new RoutingTransport().Returns("graphql", node.ToJsonString()))
            .ListPullRequestsAsync(Repository, [12, 99], Cancellation);

        Assert.Equal(12, Assert.Single(pulls).Number);
    }

    /// <summary>A not-found on an alias nobody asked for is not a missing pin, and is
    /// not read around.</summary>
    [Fact]
    public async Task A_not_found_on_an_alias_that_was_not_asked_for_is_a_failure()
    {
        var node = JsonNode.Parse(Pinned(("p12", PinnedNode(12, "OPEN"))))!;
        node["errors"] = new JsonArray(JsonNode.Parse(
            """{ "type": "NOT_FOUND", "message": "Could not resolve.", "path": ["repository", "p77"] }"""));

        await Assert.ThrowsAsync<GitHubException>(() => new GitHubClient(new RoutingTransport().Returns("graphql", node.ToJsonString()))
            .ListPullRequestsAsync(Repository, [12], Cancellation));
    }

    [Fact]
    public async Task A_pinned_read_of_no_numbers_asks_nothing()
    {
        var transport = new RoutingTransport();

        var pulls = await new GitHubClient(transport).ListPullRequestsAsync(Repository, [], Cancellation);

        Assert.Empty(pulls);
        Assert.Empty(transport.Paths);
    }

    // --- Helpers --------------------------------------------------------------

    private static readonly string Recent = DateTimeOffset.UtcNow.AddDays(-1).ToString("O");

    private static Task<IReadOnlyList<GitHubOpenPullRequest>> Open(string node) =>
        new GitHubClient(new RoutingTransport().Returns("graphql", List(node))).ListOpenPullRequestsAsync(Repository, Cancellation);

    private static string List(params string[] nodes) => $$"""
        {
          "data": {
            "repository": {
              "mergeCommitAllowed": true,
              "squashMergeAllowed": true,
              "rebaseMergeAllowed": false,
              "pullRequests": { "nodes": [ {{string.Join(",", nodes)}} ] }
            }
          }
        }
        """;

    private static string Node(
        string labels = "null",
        string rollup = "\"SUCCESS\"",
        string contexts = "null",
        string reviewDecision = "null",
        string reviews = "null",
        string closingIssues = "null") => $$"""
        {
          "id": "PR_kwDOlist",
          "number": 712,
          "title": "Pull requests page",
          "url": "https://github.com/JSdotNet/Backlog/pull/712",
          "isDraft": false,
          "headRefName": "claude/pr-page",
          "headRefOid": "abc123",
          "baseRefName": "main",
          "mergeStateStatus": "BLOCKED",
          "mergeable": "MERGEABLE",
          "viewerDidAuthor": true,
          "author": { "login": "JSdotNet" },
          "autoMergeRequest": null,
          "updatedAt": "2026-10-01T09:30:00Z",
          "labels": {{labels}},
          "reviewDecision": {{reviewDecision}},
          "latestOpinionatedReviews": {{reviews}},
          "closingIssuesReferences": {{closingIssues}},
          "commits": { "nodes": [ { "commit": {
            "statusCheckRollup": {{(rollup == "null" ? "null" : "{ \"state\": " + rollup + " }")}},
            "checkCounts": {{(rollup == "null" ? "null" : "{ \"contexts\": " + contexts + " }")}}
          } } ] }
        }
        """;

    private static string Pinned(params (string Alias, string Node)[] pulls) => $$"""
        {
          "data": {
            "repository": {
              "mergeCommitAllowed": true,
              "squashMergeAllowed": false,
              "rebaseMergeAllowed": false,
              {{string.Join(",", pulls.Select(pull => $"\"{pull.Alias}\": {pull.Node}"))}}
            }
          }
        }
        """;

    private static string PinnedNode(int number, string state, bool draft = false) => $$"""
        {
          "id": "PR_{{number}}",
          "number": {{number}},
          "title": "Pinned {{number}}",
          "url": "https://github.com/JSdotNet/Backlog/pull/{{number}}",
          "state": "{{state}}",
          "isDraft": {{(draft ? "true" : "false")}},
          "mergedAt": {{(state == "MERGED" ? "\"2026-10-02T09:00:00Z\"" : "null")}},
          "closedAt": {{(state is "MERGED" or "CLOSED" ? "\"2026-10-02T09:00:00Z\"" : "null")}},
          "headRefName": "pin-{{number}}",
          "headRefOid": "sha{{number}}",
          "baseRefName": "main",
          "mergeStateStatus": "UNKNOWN",
          "mergeable": "UNKNOWN",
          "viewerDidAuthor": false,
          "author": { "login": "someone" },
          "autoMergeRequest": null,
          "updatedAt": "2026-10-02T09:00:00Z",
          "commits": { "nodes": [] }
        }
        """;
}
