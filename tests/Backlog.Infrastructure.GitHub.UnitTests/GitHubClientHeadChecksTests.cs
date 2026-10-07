using System.Text.Json;
using System.Text.Json.Nodes;
using Backlog.Infrastructure.GitHub;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// What the pull requests list reads about a head commit's checks one by one, how far
/// each branch is behind its base, and the REST call Re-run failed is made of.
/// <para>
/// The checks ride in the list query, under the alias the counts already use; the
/// distance is a second GraphQL query per repository, because GitHub's comparison
/// takes the other branch as an argument that a list query cannot fill in per row.
/// </para>
/// </summary>
public sealed class GitHubClientHeadChecksTests
{
    private static readonly GitHubRepositoryRef Repository = new("backlog", "JSdotNet", "Backlog");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    // --- The checks one by one -----------------------------------------------

    [Fact]
    public async Task Each_check_run_and_commit_status_is_read_with_its_name_state_and_duration()
    {
        var pull = Assert.Single(await Open(Node(contexts: Contexts(
            CheckRun("build", "COMPLETED", "SUCCESS", "2026-10-01T09:00:00Z", "2026-10-01T09:04:30Z", actionsRun: 9001),
            CheckRun("e2e", "IN_PROGRESS", conclusion: null, "2026-10-01T09:00:00Z", completedAt: null, actionsRun: 9002),
            Status("ci/sonar", "FAILURE")))));

        Assert.Equal(3, pull.HeadChecks.Count);

        var build = pull.HeadChecks[0];
        Assert.Equal("build", build.Name);
        Assert.Equal(GitHubCheckState.Passing, build.State);
        Assert.Equal(TimeSpan.FromSeconds(270), build.Duration);
        Assert.Equal("https://github.com/JSdotNet/Backlog/actions/runs/9001/job/1", build.Url);
        Assert.Equal(9001, build.WorkflowRunId);
        Assert.True(build.IsGitHubActions);

        var e2e = pull.HeadChecks[1];
        Assert.Equal(GitHubCheckState.Pending, e2e.State);
        Assert.Null(e2e.Duration);

        var sonar = pull.HeadChecks[2];
        Assert.Equal("ci/sonar", sonar.Name);
        Assert.Equal(GitHubCheckState.Failing, sonar.State);
        Assert.Null(sonar.Duration);
        Assert.Equal("https://sonar.example/run/1", sonar.Url);
        Assert.False(sonar.IsGitHubActions);
    }

    /// <summary>The same states as the counts: a run that cannot go green on its own
    /// has failed, a skipped or neutral one has passed, and one GitHub has not
    /// concluded is still to come.</summary>
    [Theory]
    [InlineData("SUCCESS", GitHubCheckState.Passing)]
    [InlineData("NEUTRAL", GitHubCheckState.Passing)]
    [InlineData("SKIPPED", GitHubCheckState.Passing)]
    [InlineData("FAILURE", GitHubCheckState.Failing)]
    [InlineData("TIMED_OUT", GitHubCheckState.Failing)]
    [InlineData("CANCELLED", GitHubCheckState.Failing)]
    [InlineData("STARTUP_FAILURE", GitHubCheckState.Failing)]
    [InlineData("ACTION_REQUIRED", GitHubCheckState.Failing)]
    [InlineData("STALE", GitHubCheckState.Pending)]
    public async Task A_completed_check_runs_state_is_its_conclusion(string conclusion, GitHubCheckState expected)
    {
        var pull = Assert.Single(await Open(Node(contexts: Contexts(
            CheckRun("build", "COMPLETED", conclusion, "2026-10-01T09:00:00Z", "2026-10-01T09:01:00Z", actionsRun: 1)))));

        Assert.Equal(expected, Assert.Single(pull.HeadChecks).State);
    }

    [Theory]
    [InlineData("SUCCESS", GitHubCheckState.Passing)]
    [InlineData("ERROR", GitHubCheckState.Failing)]
    [InlineData("PENDING", GitHubCheckState.Pending)]
    [InlineData("EXPECTED", GitHubCheckState.Pending)]
    public async Task A_commit_statuss_state_is_read_as_the_counts_read_it(string state, GitHubCheckState expected)
    {
        var pull = Assert.Single(await Open(Node(contexts: Contexts(Status("ci/external", state)))));

        Assert.Equal(expected, Assert.Single(pull.HeadChecks).State);
    }

    /// <summary>Only a run GitHub Actions made names a workflow run: another app's
    /// check run cannot be re-run from here, even when its suite says otherwise.</summary>
    [Fact]
    public async Task A_check_run_another_app_made_names_no_workflow_run()
    {
        var pull = Assert.Single(await Open(Node(contexts: Contexts(
            CheckRun("codecov", "COMPLETED", "FAILURE", "2026-10-01T09:00:00Z", "2026-10-01T09:01:00Z", actionsRun: 77, app: "codecov")))));

        var check = Assert.Single(pull.HeadChecks);
        Assert.Null(check.WorkflowRunId);
        Assert.False(pull.CanRerunFailed);
    }

    [Fact]
    public async Task Re_run_failed_is_possible_when_a_failed_check_is_a_github_actions_run()
    {
        var pull = Assert.Single(await Open(Node(contexts: Contexts(
            CheckRun("build", "COMPLETED", "FAILURE", "2026-10-01T09:00:00Z", "2026-10-01T09:01:00Z", actionsRun: 10),
            CheckRun("lint", "COMPLETED", "FAILURE", "2026-10-01T09:00:00Z", "2026-10-01T09:01:00Z", actionsRun: 10),
            CheckRun("e2e", "COMPLETED", "TIMED_OUT", "2026-10-01T09:00:00Z", "2026-10-01T09:01:00Z", actionsRun: 11),
            CheckRun("docs", "COMPLETED", "SUCCESS", "2026-10-01T09:00:00Z", "2026-10-01T09:01:00Z", actionsRun: 12)))));

        Assert.True(pull.CanRerunFailed);
        Assert.Equal([10L, 11L], pull.FailedWorkflowRunIds);
    }

    [Fact]
    public async Task Re_run_failed_is_not_possible_when_only_another_services_check_failed()
    {
        var pull = Assert.Single(await Open(Node(contexts: Contexts(
            CheckRun("build", "COMPLETED", "SUCCESS", "2026-10-01T09:00:00Z", "2026-10-01T09:01:00Z", actionsRun: 10),
            Status("ci/external", "FAILURE")))));

        Assert.False(pull.CanRerunFailed);
        Assert.Empty(pull.FailedWorkflowRunIds);
    }

    [Fact]
    public async Task A_pull_request_without_a_check_roll_up_has_no_checks()
    {
        var pull = Assert.Single(await Open(Node(contexts: null)));

        Assert.Empty(pull.HeadChecks);
        Assert.False(pull.CanRerunFailed);
    }

    /// <summary>A token refused the contexts is refused them under the counts' alias,
    /// and that reads as no checks on the row, never as no row.</summary>
    [Fact]
    public async Task A_refusal_of_the_checks_reads_as_none()
    {
        var node = JsonNode.Parse(List(Node(contexts: null)))!;
        node["errors"] = new JsonArray(JsonNode.Parse(
            """{ "type": "FORBIDDEN", "message": "Resource not accessible by integration", "path": ["repository", "pullRequests", "nodes", 0, "commits", "nodes", 0, "commit", "checkCounts", "contexts", "nodes", 0, "checkSuite"] }"""));

        var pull = Assert.Single(await new GitHubClient(new RoutingTransport().Returns("graphql", node.ToJsonString()))
            .ListOpenPullRequestsAsync(Repository, Cancellation));

        Assert.Empty(pull.HeadChecks);
    }

    [Fact]
    public async Task The_list_query_reads_the_checks_up_to_the_limit_in_the_same_query()
    {
        var transport = new RoutingTransport().Returns("graphql", List(Node()));

        await new GitHubClient(transport).ListOpenPullRequestsAsync(Repository, Cancellation);

        using var body = JsonDocument.Parse(transport.Bodies[0]!);
        var query = body.RootElement.GetProperty("query").GetString()!;
        Assert.Contains("pullRequests(states: OPEN", query, StringComparison.Ordinal);
        Assert.Contains($"contexts(first: {GitHubClient.HeadCheckLimit})", query, StringComparison.Ordinal);
        Assert.Contains("... on CheckRun", query, StringComparison.Ordinal);
        Assert.Contains("... on StatusContext", query, StringComparison.Ordinal);
        Assert.Contains("workflowRun { databaseId }", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_pinned_pull_request_carries_its_checks_too()
    {
        var pinned = $$"""
            { "data": { "repository": {
                "mergeCommitAllowed": true, "squashMergeAllowed": true, "rebaseMergeAllowed": true,
                "p712": {{Node(contexts: Contexts(CheckRun("build", "COMPLETED", "FAILURE", "2026-10-01T09:00:00Z", "2026-10-01T09:01:00Z", actionsRun: 5)), state: "OPEN")}} } } }
            """;

        var pull = Assert.Single(await new GitHubClient(new RoutingTransport().Returns("graphql", pinned))
            .ListPullRequestsAsync(Repository, [712], Cancellation));

        Assert.True(pull.CanRerunFailed);
    }

    // --- How far behind -------------------------------------------------------

    /// <summary>A pin is compared too, while it is open: a merged or closed one has
    /// no branch left to bring up to date.</summary>
    [Fact]
    public async Task An_open_pin_is_compared_and_a_merged_one_is_not()
    {
        var pinned = $$"""
            { "data": { "repository": {
                "mergeCommitAllowed": true, "squashMergeAllowed": true, "rebaseMergeAllowed": true,
                "p1": {{Node(number: 1, id: "PR_1", head: "open-one", state: "OPEN")}},
                "p2": {{Node(number: 2, id: "PR_2", head: "merged-one", state: "MERGED")}} } } }
            """;
        var transport = new RoutingTransport().ReturnsInTurn(
            "graphql",
            pinned,
            """{ "data": { "repository": { "c0": { "compare": { "behindBy": 4 } } } } }""");

        var pulls = await new GitHubClient(transport).ListPullRequestsAsync(Repository, [1, 2], Cancellation);

        Assert.Equal([4, null], pulls.Select(pull => pull.BehindBy));

        using var body = JsonDocument.Parse(transport.Bodies[1]!);
        var variables = body.RootElement.GetProperty("variables");
        Assert.Equal("open-one", variables.GetProperty("h0").GetString());
        Assert.False(variables.TryGetProperty("h1", out _));
    }

    [Fact]
    public async Task Each_open_pull_request_carries_how_far_it_is_behind_its_base()
    {
        var transport = new RoutingTransport()
            .ReturnsInTurn(
                "graphql",
                List(Node(number: 1, id: "PR_1", head: "feature/a"), Node(number: 2, id: "PR_2", head: "feature/b", @base: "release")),
                """{ "data": { "repository": { "c0": { "compare": { "behindBy": 3 } }, "c1": { "compare": { "behindBy": 0 } } } } }""");

        var pulls = await new GitHubClient(transport).ListOpenPullRequestsAsync(Repository, Cancellation);

        Assert.Equal([3, 0], pulls.Select(pull => pull.BehindBy));

        Assert.Equal(2, transport.CallsTo("graphql"));
        using var body = JsonDocument.Parse(transport.Bodies[1]!);
        var variables = body.RootElement.GetProperty("variables");
        Assert.Equal("refs/heads/main", variables.GetProperty("b0").GetString());
        Assert.Equal("feature/a", variables.GetProperty("h0").GetString());
        Assert.Equal("refs/heads/release", variables.GetProperty("b1").GetString());
        Assert.Equal("feature/b", variables.GetProperty("h1").GetString());

        var query = body.RootElement.GetProperty("query").GetString()!;
        Assert.Contains("c0: ref(qualifiedName: $b0) { compare(headRef: $h0) { behindBy } }", query, StringComparison.Ordinal);
        Assert.DoesNotContain("feature/a", query, StringComparison.Ordinal);
    }

    /// <summary>The base repository cannot name a fork's branch, so a pull request
    /// from a fork is not compared and its distance stays unknown.</summary>
    [Fact]
    public async Task A_pull_request_from_a_fork_is_not_compared()
    {
        var transport = new RoutingTransport().Returns("graphql", List(Node(crossRepository: true)));

        var pull = Assert.Single(await new GitHubClient(transport).ListOpenPullRequestsAsync(Repository, Cancellation));

        Assert.Null(pull.BehindBy);
        Assert.True(pull.IsCrossRepository);
        Assert.Equal(1, transport.CallsTo("graphql"));
    }

    /// <summary>A branch deleted since, or a comparison GitHub would not make, is that
    /// row's distance unknown; the others still carry theirs.</summary>
    [Fact]
    public async Task A_comparison_that_came_back_null_leaves_that_distance_unknown()
    {
        var transport = new RoutingTransport()
            .ReturnsInTurn(
                "graphql",
                List(Node(number: 1, id: "PR_1", head: "gone"), Node(number: 2, id: "PR_2", head: "kept")),
                """{ "data": { "repository": { "c0": null, "c1": { "compare": { "behindBy": 7 } } } }, "errors": [ { "type": "NOT_FOUND", "message": "Could not resolve", "path": ["repository", "c0"] } ] }""");

        var pulls = await new GitHubClient(transport).ListOpenPullRequestsAsync(Repository, Cancellation);

        Assert.Equal([null, 7], pulls.Select(pull => pull.BehindBy));
    }

    [Fact]
    public async Task A_refused_comparison_leaves_the_list_standing()
    {
        var transport = new RoutingTransport()
            .ReturnsInTurn(
                "graphql",
                List(Node()),
                """{ "data": null, "errors": [ { "type": "FORBIDDEN", "message": "Resource not accessible" } ] }""");

        var pull = Assert.Single(await new GitHubClient(transport).ListOpenPullRequestsAsync(Repository, Cancellation));

        Assert.Equal(712, pull.Number);
        Assert.Null(pull.BehindBy);
    }

    // --- Re-run failed --------------------------------------------------------

    [Fact]
    public async Task Re_running_failed_jobs_posts_to_the_workflow_runs_rerun_failed_jobs()
    {
        var transport = new RoutingTransport().Returns(HttpMethod.Post, "rerun-failed-jobs", "{}");

        await new GitHubClient(transport).RerunFailedJobsAsync(Repository, 9001, Cancellation);

        Assert.Equal("repos/JSdotNet/Backlog/actions/runs/9001/rerun-failed-jobs", Assert.Single(transport.Paths));
        Assert.Equal(HttpMethod.Post, Assert.Single(transport.Methods));
    }

    [Fact]
    public async Task A_refused_rerun_is_githubs_refusal()
    {
        var transport = new RoutingTransport().Refuses("rerun-failed-jobs", "This workflow is already running");

        var refused = await Assert.ThrowsAsync<GitHubException>(() =>
            new GitHubClient(transport).RerunFailedJobsAsync(Repository, 9001, Cancellation));

        Assert.Equal("This workflow is already running", refused.Message);
    }

    // --- Helpers --------------------------------------------------------------

    private static async Task<IReadOnlyList<GitHubOpenPullRequest>> Open(string node) =>
        await new GitHubClient(new RoutingTransport().Returns("graphql", List(node))).ListOpenPullRequestsAsync(Repository, Cancellation);

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

    private static string Contexts(params string[] nodes) => $$"""
        { "totalCount": {{nodes.Length}}, "checkRunCountsByState": [], "statusContextCountsByState": [], "nodes": [ {{string.Join(",", nodes)}} ] }
        """;

    private static string CheckRun(
        string name,
        string status,
        string? conclusion,
        string? startedAt,
        string? completedAt,
        long actionsRun,
        string app = "github-actions") => $$"""
        {
          "__typename": "CheckRun",
          "name": "{{name}}",
          "status": "{{status}}",
          "conclusion": {{Quoted(conclusion)}},
          "startedAt": {{Quoted(startedAt)}},
          "completedAt": {{Quoted(completedAt)}},
          "detailsUrl": "https://github.com/JSdotNet/Backlog/actions/runs/{{actionsRun}}/job/1",
          "checkSuite": { "app": { "slug": "{{app}}" }, "workflowRun": { "databaseId": {{actionsRun}} } }
        }
        """;

    private static string Status(string context, string state) => $$"""
        { "__typename": "StatusContext", "context": "{{context}}", "state": "{{state}}", "targetUrl": "https://sonar.example/run/1" }
        """;

    private static string Quoted(string? value) => value is null ? "null" : $"\"{value}\"";

    private static string Node(
        int number = 712,
        string id = "PR_kwDOlist",
        string head = "claude/pr-page",
        string @base = "main",
        bool crossRepository = false,
        string? contexts = "{ \"totalCount\": 0, \"nodes\": [] }",
        string? state = null)
    {
        var rollup = contexts is null
            ? "null"
            : $$"""{ "contexts": {{contexts}} }""";
        var stateField = state is null ? string.Empty : $"\"state\": \"{state}\",";

        return $$"""
            {
              {{stateField}}
              "id": "{{id}}",
              "number": {{number}},
              "title": "Pull requests page",
              "url": "https://github.com/JSdotNet/Backlog/pull/{{number}}",
              "isDraft": false,
              "isCrossRepository": {{(crossRepository ? "true" : "false")}},
              "headRefName": "{{head}}",
              "headRefOid": "abc123",
              "baseRefName": "{{@base}}",
              "mergeStateStatus": "BLOCKED",
              "mergeable": "MERGEABLE",
              "viewerDidAuthor": true,
              "author": { "login": "JSdotNet" },
              "autoMergeRequest": null,
              "updatedAt": "2026-10-01T09:30:00Z",
              "commits": { "nodes": [ { "commit": { "statusCheckRollup": { "state": "FAILURE" }, "checkCounts": {{rollup}} } } ] }
            }
            """;
    }
}
