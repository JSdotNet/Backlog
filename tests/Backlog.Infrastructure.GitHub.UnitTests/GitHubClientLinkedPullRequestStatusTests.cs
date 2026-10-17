using System.Text.Json;
using System.Text.Json.Nodes;
using Backlog.Infrastructure.GitHub;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// The status read's facts (<see cref="GitHubClient.ReadLinkedPullRequestStatusesAsync"/>): the head commit's check counts and failing check
/// names, the reviews, and the approvals the base branch requires — read from its branch
/// protection rule and the pull request rules rulesets apply to it, the larger of the
/// two, and none where no rule requires an approval
/// (<c>.devbook/domain/progress/features.md#pull-request-status</c>).
/// </summary>
public sealed class GitHubClientLinkedPullRequestStatusTests
{
    private static readonly GitHubRepositoryRef Repository = new("backlog", "JSdotNet", "Backlog");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    // --- The required count ---------------------------------------------------

    [Fact]
    public void A_branch_protection_rule_requiring_approvals_gives_its_count()
    {
        Assert.Equal(2, Required(BaseRef(protection: """{ "requiresApprovingReviews": true, "requiredApprovingReviewCount": 2 }""")));
    }

    [Fact]
    public void A_ruleset_pull_request_rule_gives_its_count()
    {
        Assert.Equal(1, Required(BaseRef(rules: """[ { "type": "REQUIRED_STATUS_CHECKS", "parameters": {} }, { "type": "PULL_REQUEST", "parameters": { "requiredApprovingReviewCount": 1 } } ]""")));
    }

    [Fact]
    public void The_larger_of_the_protection_rule_and_every_ruleset_rule_is_required()
    {
        var baseRef = BaseRef(
            protection: """{ "requiresApprovingReviews": true, "requiredApprovingReviewCount": 1 }""",
            rules: """[ { "type": "PULL_REQUEST", "parameters": { "requiredApprovingReviewCount": 3 } }, { "type": "PULL_REQUEST", "parameters": { "requiredApprovingReviewCount": 2 } } ]""");

        Assert.Equal(3, Required(baseRef));
    }

    /// <summary>No rule means no required total — not zero. GitHub's own answer for
    /// <c>main</c> here: no protection rule, and a ruleset that requires status checks
    /// and no review.</summary>
    [Fact]
    public void A_base_branch_with_no_rule_has_no_required_total()
    {
        Assert.Null(Required(BaseRef(rules: """[ { "type": "REQUIRED_STATUS_CHECKS", "parameters": {} } ]""")));
        Assert.Null(Required(BaseRef()));
    }

    [Fact]
    public void A_protection_rule_that_does_not_require_approving_reviews_has_no_required_total()
    {
        Assert.Null(Required(BaseRef(protection: """{ "requiresApprovingReviews": false, "requiredApprovingReviewCount": 2 }""")));
        Assert.Null(Required(BaseRef(rules: """[ { "type": "PULL_REQUEST", "parameters": { "requiredApprovingReviewCount": 0 } } ]""")));
    }

    /// <summary>A merged pull request whose base branch was deleted, or rules the token
    /// was refused, come back without a base ref: unknown, which is null.</summary>
    [Fact]
    public void A_gone_or_refused_base_branch_has_no_required_total()
    {
        Assert.Null(Required("null"));
        Assert.Null(Required(null));
    }

    // --- The status read --------------------------------------------------------

    [Fact]
    public async Task The_status_read_asks_for_checks_reviews_and_the_base_branch_rules()
    {
        var transport = new RoutingTransport().Returns("graphql", Linked(("p12", LinkedNode(12))));

        await new GitHubClient(transport).ReadLinkedPullRequestStatusesAsync(Repository, [12], Cancellation);

        using var body = JsonDocument.Parse(transport.Bodies[0]!);
        var query = body.RootElement.GetProperty("query").GetString()!;
        Assert.Contains("branchProtectionRule { requiresApprovingReviews requiredApprovingReviewCount }", query, StringComparison.Ordinal);
        Assert.Contains("... on PullRequestParameters { requiredApprovingReviewCount }", query, StringComparison.Ordinal);
        Assert.Contains("latestOpinionatedReviews(first: 20, writersOnly: true)", query, StringComparison.Ordinal);
        Assert.Contains($"contexts(first: {GitHubClient.HeadCheckLimit})", query, StringComparison.Ordinal);
    }

    /// <summary>Each pull request asks for its checks and its base branch's rules, so
    /// the status read sends half as many to a query as the slim read does.</summary>
    [Fact]
    public async Task Many_pull_requests_are_read_for_their_status_in_smaller_batches()
    {
        var transport = new RoutingTransport().Returns("graphql", Linked(("p1", LinkedNode(1))));

        await new GitHubClient(transport).ReadLinkedPullRequestStatusesAsync(Repository, [.. Enumerable.Range(1, GitHubClient.LinkedStatusBatchSize + 1)], Cancellation);

        Assert.Equal(2, transport.CallsTo("graphql"));
    }

    /// <summary>The slim read is unchanged: it asks for none of the status facts.</summary>
    [Fact]
    public async Task The_slim_linked_read_asks_for_no_status_fact()
    {
        var transport = new RoutingTransport().Returns("graphql", Linked(("p12", LinkedNode(12))));

        var pull = Assert.Single(await new GitHubClient(transport).ReadLinkedPullRequestsAsync(Repository, [12], Cancellation));

        Assert.DoesNotContain("baseRef", transport.Bodies[0]!, StringComparison.Ordinal);
        Assert.DoesNotContain("statusCheckRollup", transport.Bodies[0]!, StringComparison.Ordinal);
        Assert.Equal(12, pull.Number);
    }

    [Fact]
    public async Task A_linked_pull_request_carries_its_checks_failing_names_reviews_and_required_count()
    {
        var node = LinkedNode(
            12,
            baseRef: BaseRef(protection: """{ "requiresApprovingReviews": true, "requiredApprovingReviewCount": 2 }"""),
            reviews: """{ "nodes": [ { "state": "APPROVED" }, { "state": "COMMENTED" } ] }""",
            decision: "REVIEW_REQUIRED",
            contexts: """
                {
                  "totalCount": 4,
                  "checkRunCountsByState": [ { "state": "SUCCESS", "count": 1 }, { "state": "FAILURE", "count": 2 }, { "state": "IN_PROGRESS", "count": 1 } ],
                  "statusContextCountsByState": [],
                  "nodes": [
                    { "__typename": "CheckRun", "name": "Build and test .NET", "status": "COMPLETED", "conclusion": "FAILURE" },
                    { "__typename": "CheckRun", "name": "Build and test .NET", "status": "COMPLETED", "conclusion": "FAILURE" },
                    { "__typename": "CheckRun", "name": "Detect code changes", "status": "COMPLETED", "conclusion": "SUCCESS" },
                    { "__typename": "CheckRun", "name": "Build the app heads", "status": "IN_PROGRESS", "conclusion": null }
                  ]
                }
                """);

        var pull = Assert.Single(await new GitHubClient(new RoutingTransport().Returns("graphql", Linked(("p12", node))))
            .ReadLinkedPullRequestStatusesAsync(Repository, [12], Cancellation));

        Assert.Equal(new GitHubCheckCounts(1, 2, 1), pull.CheckCounts);
        Assert.Equal(["Build and test .NET"], pull.FailingChecks);
        Assert.Equal(GitHubReviewState.ReviewRequired, pull.Reviews.Decision);
        Assert.Equal(1, pull.Reviews.Approvals);
        Assert.Equal(2, pull.Reviews.Required);
    }

    [Fact]
    public async Task A_linked_pull_request_on_a_branch_with_no_rule_has_no_required_total()
    {
        var pull = Assert.Single(await new GitHubClient(new RoutingTransport().Returns("graphql", Linked(("p12", LinkedNode(12, baseRef: BaseRef())))))
            .ReadLinkedPullRequestStatusesAsync(Repository, [12], Cancellation));

        Assert.Null(pull.Reviews.Required);
        Assert.Null(pull.CheckCounts);
        Assert.Empty(pull.FailingChecks);
    }

    /// <summary>A token that may not read the branch's protection is refused it on that
    /// field: the pull request is still read, and the required count is unknown.</summary>
    [Fact]
    public async Task A_refused_branch_protection_rule_leaves_the_pull_request_read_with_no_required_total()
    {
        var node = JsonNode.Parse(Linked(("p12", LinkedNode(12, baseRef: BaseRef(protection: "null")))))!;
        node["errors"] = new JsonArray(JsonNode.Parse(
            """{ "type": "FORBIDDEN", "message": "Resource not accessible by integration", "path": ["repository", "p12", "baseRef", "branchProtectionRule"] }"""));

        var pull = Assert.Single(await new GitHubClient(new RoutingTransport().Returns("graphql", node.ToJsonString()))
            .ReadLinkedPullRequestStatusesAsync(Repository, [12], Cancellation));

        Assert.Equal(12, pull.Number);
        Assert.Null(pull.Reviews.Required);
    }

    [Fact]
    public async Task A_refused_check_roll_up_is_read_around_and_the_pull_request_has_no_checks()
    {
        var node = JsonNode.Parse(Linked(("p12", LinkedNode(12))))!;
        node["errors"] = new JsonArray(JsonNode.Parse(
            """{ "type": "FORBIDDEN", "message": "Resource not accessible by integration", "path": ["repository", "p12", "commits", "nodes", 0, "commit", "checkCounts"] }"""));

        var pull = Assert.Single(await new GitHubClient(new RoutingTransport().Returns("graphql", node.ToJsonString()))
            .ReadLinkedPullRequestStatusesAsync(Repository, [12], Cancellation));

        Assert.Null(pull.CheckCounts);
    }

    /// <summary>Any other refusal is still a failed read, as it is for the slim read.</summary>
    [Fact]
    public async Task A_refusal_outside_the_status_facts_still_fails_the_read()
    {
        var node = JsonNode.Parse(Linked(("p12", LinkedNode(12))))!;
        node["errors"] = new JsonArray(JsonNode.Parse(
            """{ "type": "FORBIDDEN", "message": "Resource not accessible by integration", "path": ["repository", "p12", "author"] }"""));

        await Assert.ThrowsAsync<GitHubException>(() => new GitHubClient(new RoutingTransport().Returns("graphql", node.ToJsonString()))
            .ReadLinkedPullRequestStatusesAsync(Repository, [12], Cancellation));
    }

    // --- Fixtures -------------------------------------------------------------------

    private static int? Required(string? baseRef)
    {
        var pull = baseRef is null ? "{ \"number\": 1 }" : $$"""{ "number": 1, "baseRef": {{baseRef}} }""";
        using var json = JsonDocument.Parse(pull);
        return GitHubClient.ReadRequiredApprovals(json.RootElement);
    }

    private static string BaseRef(string protection = "null", string rules = "[]") =>
        $$"""{ "branchProtectionRule": {{protection}}, "rules": { "nodes": {{rules}} } }""";

    private static string LinkedNode(
        int number,
        string? baseRef = null,
        string reviews = """{ "nodes": [] }""",
        string? decision = null,
        string? contexts = null) => $$"""
        {
          "number": {{number}},
          "title": "Read checks",
          "url": "https://github.com/JSdotNet/Backlog/pull/{{number}}",
          "state": "OPEN",
          "isDraft": false,
          "mergedAt": null,
          "createdAt": "2026-10-16T09:00:00Z",
          "author": { "login": "octo" },
          "reviewDecision": {{(decision is null ? "null" : $"\"{decision}\"")}},
          "latestOpinionatedReviews": {{reviews}},
          "baseRef": {{baseRef ?? "null"}},
          "commits": { "nodes": [ { "commit": { "checkCounts": {{(contexts is null ? "null" : $$"""{ "contexts": {{contexts}} }""")}} } } ] }
        }
        """;

    private static string Linked(params (string Alias, string Node)[] pulls) =>
        $$"""{ "data": { "repository": { {{string.Join(", ", pulls.Select(pull => $"\"{pull.Alias}\": {pull.Node}"))}} } } }""";
}
