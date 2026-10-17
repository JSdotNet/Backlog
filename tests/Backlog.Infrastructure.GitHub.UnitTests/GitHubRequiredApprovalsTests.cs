using System.Text.Json;
using Backlog.Infrastructure.GitHub;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// The approvals a pull request's base branch requires, on the Pull requests list's rows
/// (<c>.devbook/domain/progress/features.md#pull-request-status</c>): read once per base
/// branch through <see cref="IGitHubClient.ReadRequiredApprovalsAsync"/> beside the list
/// read, kept for <see cref="GitHubIntegration.RequiredApprovalsFreshFor"/>, and never
/// a reason for the list to fail.
/// </summary>
public sealed class GitHubRequiredApprovalsTests : IDisposable
{
    private static readonly GitHubRepositoryRef Backlog = new("backlog", "JSdotNet", "Backlog");
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-required-approvals-tests", Guid.NewGuid().ToString("n"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        }
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    // --- The client's read ----------------------------------------------------

    [Fact]
    public async Task Each_branch_is_asked_once_by_variable_and_counted_as_the_status_read_counts_it()
    {
        var transport = new RoutingTransport().Returns("graphql", $$"""
            { "data": { "repository": {
                "r0": {{Ref(protection: """{ "requiresApprovingReviews": true, "requiredApprovingReviewCount": 2 }""")}},
                "r1": {{Ref(rules: """[ { "type": "PULL_REQUEST", "parameters": { "requiredApprovingReviewCount": 1 } } ]""")}} } } }
            """);

        var read = await new GitHubClient(transport).ReadRequiredApprovalsAsync(Backlog, ["main", "release", "main"], Cancellation);

        Assert.Equal(2, read["main"]);
        Assert.Equal(1, read["release"]);
        Assert.Equal(1, transport.CallsTo("graphql"));

        using var body = JsonDocument.Parse(transport.Bodies[0]!);
        var variables = body.RootElement.GetProperty("variables");
        Assert.Equal("refs/heads/main", variables.GetProperty("r0").GetString());
        Assert.Equal("refs/heads/release", variables.GetProperty("r1").GetString());
        Assert.False(variables.TryGetProperty("r2", out _));

        var query = body.RootElement.GetProperty("query").GetString()!;
        Assert.Contains("r0: ref(qualifiedName: $r0)", query, StringComparison.Ordinal);
        Assert.Contains("branchProtectionRule { requiresApprovingReviews requiredApprovingReviewCount }", query, StringComparison.Ordinal);
        Assert.DoesNotContain("release", query, StringComparison.Ordinal);
    }

    /// <summary>A branch whose rules ask for no approval is read, with no required total;
    /// a branch that is gone, or whose rules were refused, is left out — not read.</summary>
    [Fact]
    public async Task No_rule_is_read_as_none_required_and_a_refused_or_missing_branch_as_unread()
    {
        var transport = new RoutingTransport().Returns("graphql", $$"""
            { "data": { "repository": {
                "r0": {{Ref()}},
                "r1": null,
                "r2": { "branchProtectionRule": null, "rules": null } } },
              "errors": [ { "type": "FORBIDDEN", "message": "Resource not accessible by integration", "path": ["repository", "r2", "rules"] } ] }
            """);

        var read = await new GitHubClient(transport).ReadRequiredApprovalsAsync(Backlog, ["main", "gone", "locked"], Cancellation);

        Assert.True(read.ContainsKey("main"));
        Assert.Null(read["main"]);
        Assert.False(read.ContainsKey("gone"));
        Assert.False(read.ContainsKey("locked"));
    }

    /// <summary>A server without rulesets refuses the whole query; that is every branch
    /// unread, not an exception.</summary>
    [Fact]
    public async Task A_refused_query_reads_nothing_and_does_not_throw()
    {
        var transport = new RoutingTransport().Returns("graphql",
            """{ "data": null, "errors": [ { "message": "Field 'rules' doesn't exist on type 'Ref'" } ] }""");

        var read = await new GitHubClient(transport).ReadRequiredApprovalsAsync(Backlog, ["main"], Cancellation);

        Assert.Empty(read);
    }

    // --- On the list's rows ---------------------------------------------------

    [Fact]
    public async Task An_open_row_carries_the_required_total_of_its_base_branch()
    {
        var transport = new BodyRoutingTransport(rules: $$"""{ "data": { "repository": { "r0": {{Ref(protection: """{ "requiresApprovingReviews": true, "requiredApprovingReviewCount": 2 }""")}} } } }""");

        var listing = await Integration(transport).ListOpenPullRequestsAsync([Backlog], Cancellation);

        var reviews = Assert.Single(listing.PullRequests).Reviews;
        Assert.Equal(2, reviews.Required);
        Assert.True(reviews.RequiredRead);
        Assert.Equal(1, transport.RulesReads);
    }

    /// <summary>A branch's rules change seldom: the next refresh inside the window takes
    /// what was read, and one after it reads again.</summary>
    [Fact]
    public async Task A_branch_is_read_again_only_once_its_answer_is_older_than_the_window()
    {
        var clock = new FakeTimeProvider(Now);
        var transport = new BodyRoutingTransport(rules: $$"""{ "data": { "repository": { "r0": {{Ref()}} } } }""");
        var integration = Integration(transport, clock);

        await integration.ListOpenPullRequestsAsync([Backlog], Cancellation);
        clock.Advance(TimeSpan.FromMinutes(5));
        var again = await integration.ListOpenPullRequestsAsync([Backlog], Cancellation);

        Assert.Equal(1, transport.RulesReads);
        Assert.True(Assert.Single(again.PullRequests).Reviews.RequiredRead);
        Assert.Null(again.PullRequests[0].Reviews.Required);

        clock.Advance(GitHubIntegration.RequiredApprovalsFreshFor);
        await integration.ListOpenPullRequestsAsync([Backlog], Cancellation);

        Assert.Equal(2, transport.RulesReads);
    }

    /// <summary>Rules that cannot be read leave the row standing with its approvals and no
    /// verdict, and are not asked again on every refresh inside the window.</summary>
    [Fact]
    public async Task Unreadable_rules_leave_the_row_unread_and_the_list_standing()
    {
        var transport = new BodyRoutingTransport(rules: """{ "data": null, "errors": [ { "type": "FORBIDDEN", "message": "Resource not accessible" } ] }""");
        var integration = Integration(transport);

        var listing = await integration.ListOpenPullRequestsAsync([Backlog], Cancellation);

        Assert.Empty(listing.Failures);
        var pull = Assert.Single(listing.PullRequests);
        Assert.False(pull.Reviews.RequiredRead);
        Assert.Null(pull.Reviews.Required);
        Assert.Equal(1, pull.Reviews.Approvals);

        await integration.ListOpenPullRequestsAsync([Backlog], Cancellation);
        Assert.Equal(1, transport.RulesReads);
    }

    /// <summary>An answer past the window is not drawn on: a re-read that fails takes the
    /// row back to no verdict rather than keeping the old total.</summary>
    [Fact]
    public async Task A_failed_re_read_drops_the_old_verdict()
    {
        var clock = new FakeTimeProvider(Now);
        var transport = new BodyRoutingTransport(rules: $$"""{ "data": { "repository": { "r0": {{Ref(protection: """{ "requiresApprovingReviews": true, "requiredApprovingReviewCount": 2 }""")}} } } }""");
        var integration = Integration(transport, clock);

        var first = await integration.ListOpenPullRequestsAsync([Backlog], Cancellation);
        Assert.Equal(2, Assert.Single(first.PullRequests).Reviews.Required);

        clock.Advance(GitHubIntegration.RequiredApprovalsFreshFor);
        transport.Rules = """{ "data": null, "errors": [ { "type": "FORBIDDEN", "message": "Resource not accessible" } ] }""";
        var again = await integration.ListOpenPullRequestsAsync([Backlog], Cancellation);

        var reviews = Assert.Single(again.PullRequests).Reviews;
        Assert.False(reviews.RequiredRead);
        Assert.Null(reviews.Required);
        Assert.Equal(2, transport.RulesReads);
    }

    [Fact]
    public async Task An_open_pin_carries_the_required_total_too()
    {
        var transport = new BodyRoutingTransport(
            rules: $$"""{ "data": { "repository": { "r0": {{Ref(rules: """[ { "type": "PULL_REQUEST", "parameters": { "requiredApprovingReviewCount": 3 } } ]""")}} } } }""",
            list: $$"""
                { "data": { "repository": {
                    "mergeCommitAllowed": true, "squashMergeAllowed": true, "rebaseMergeAllowed": true,
                    "p7": { "state": "OPEN", "mergedAt": null, "closedAt": null, {{NodeFields(7)}} } } } }
                """);

        var listing = await Integration(transport).ListPinnedPullRequestsAsync([new PullRequestPin("JSdotNet/Backlog", 7)], Cancellation);

        Assert.Equal(3, Assert.Single(listing.PullRequests).Reviews.Required);
    }

    private GitHubIntegration Integration(IGitHubTransport transport, TimeProvider? clock = null)
    {
        Directory.CreateDirectory(_root);
        return new GitHubIntegration(
            new GitHubSettingsStore(Path.Combine(_root, "github.json")),
            new GitHubClient(transport), new NoProbe(), time: clock ?? new FakeTimeProvider(Now));
    }

    private static string Ref(string protection = "null", string rules = "[]") =>
        $$"""{ "branchProtectionRule": {{protection}}, "rules": { "nodes": {{rules}} } }""";

    private static string NodeFields(int number) => $$"""
        "id": "PR_{{number}}", "number": {{number}}, "title": "#{{number}}", "url": "https://github.com/JSdotNet/Backlog/pull/{{number}}",
        "isDraft": false, "isCrossRepository": true, "headRefName": "branch-{{number}}", "headRefOid": "sha{{number}}", "baseRefName": "main",
        "mergeStateStatus": "CLEAN", "mergeable": "MERGEABLE", "viewerDidAuthor": true, "author": { "login": "JSdotNet" },
        "autoMergeRequest": null, "updatedAt": "2026-10-17T08:00:00Z",
        "reviewDecision": "REVIEW_REQUIRED", "latestOpinionatedReviews": { "nodes": [ { "state": "APPROVED" } ] },
        "commits": { "nodes": [] }
        """;

    /// <summary>Answers the rules query by what it asks, and every other GraphQL call
    /// with the list. A fork's pull request, so the list asks for no branch comparison.</summary>
    private sealed class BodyRoutingTransport(string rules, string? list = null) : IGitHubTransport
    {
        /// <summary>What the rules query is answered with; a test may change it between reads.</summary>
        public string Rules { get; set; } = rules;

        private readonly string _list = list ?? $$"""
            { "data": { "repository": {
                "mergeCommitAllowed": true, "squashMergeAllowed": true, "rebaseMergeAllowed": true,
                "pullRequests": { "nodes": [ { {{NodeFields(1)}} } ] } } } }
            """;

        public int RulesReads { get; private set; }

        public string Description => "stub";

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<JsonElement> SendAsync(HttpMethod method, string path, object? body = null, string? apiVersion = null, CancellationToken cancellationToken = default)
        {
            var asked = body is null ? string.Empty : JsonSerializer.Serialize(body);
            var asksRules = asked.Contains("r0: ref(qualifiedName: $r0)", StringComparison.Ordinal);
            if (asksRules) RulesReads++;

            return Task.FromResult(JsonDocument.Parse(asksRules ? Rules : _list).RootElement.Clone());
        }
    }

    private sealed class NoProbe : IGitHubConnectionProbe
    {
        public Task<GitHubConnection> DescribeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitHubConnection(true, "Connected."));

        public void Invalidate()
        {
        }
    }
}
