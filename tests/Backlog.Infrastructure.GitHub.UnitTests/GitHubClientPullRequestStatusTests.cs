using System.Text.Json;
using Backlog.Infrastructure.GitHub;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// Reading one pull request's status — state, check roll-up, auto-merge — and the
/// three merge acts, all over GitHub's GraphQL API.
/// <para>
/// GraphQL rather than REST because the facts do not live together anywhere else:
/// the REST pull request carries no check roll-up and no auto-merge request, and
/// enabling auto-merge has no REST endpoint at all. One query answers what the link
/// shows and everything the menu needs to act on it — the node id the mutations
/// take and the merge method the repository allows.
/// </para>
/// </summary>
public sealed class GitHubClientPullRequestStatusTests
{
    private static readonly GitHubRepositoryRef Repository = new("backlog", "JSdotNet", "Backlog");

    [Theory]
    [InlineData("OPEN", false, GitHubItemState.Open)]
    [InlineData("OPEN", true, GitHubItemState.Draft)]
    [InlineData("MERGED", false, GitHubItemState.Merged)]
    [InlineData("CLOSED", false, GitHubItemState.Closed)]
    public async Task A_status_reads_the_pull_requests_state(string state, bool draft, GitHubItemState expected)
    {
        var client = Client(Status(state: state, isDraft: draft));

        var status = await client.GetPullRequestStatusAsync(Repository, 708, TestContext.Current.CancellationToken);

        Assert.Equal(708, status.Number);
        Assert.Equal(expected, status.State);
        Assert.Equal("PR_kwDOabc", status.NodeId);
    }

    [Theory]
    [InlineData("\"SUCCESS\"", GitHubCheckState.Passing)]
    [InlineData("\"FAILURE\"", GitHubCheckState.Failing)]
    [InlineData("\"ERROR\"", GitHubCheckState.Failing)]
    [InlineData("\"PENDING\"", GitHubCheckState.Pending)]
    [InlineData("\"EXPECTED\"", GitHubCheckState.Pending)]
    [InlineData("null", GitHubCheckState.None)]
    public async Task The_check_roll_up_of_the_head_commit_is_the_checks_state(string rollup, GitHubCheckState expected)
    {
        var client = Client(Status(rollup: rollup));

        var status = await client.GetPullRequestStatusAsync(Repository, 708, TestContext.Current.CancellationToken);

        Assert.Equal(expected, status.Checks);
    }

    /// <summary>A pull request with no commits read — an empty <c>nodes</c> — has
    /// no roll-up to report, which is "no checks", not a failure to read.</summary>
    [Fact]
    public async Task No_head_commit_is_no_checks()
    {
        var json = Status().Replace(
            """{ "commit": { "statusCheckRollup": { "state": "SUCCESS" } } }""",
            string.Empty,
            StringComparison.Ordinal);

        var status = await Client(json).GetPullRequestStatusAsync(Repository, 708, TestContext.Current.CancellationToken);

        Assert.Equal(GitHubCheckState.None, status.Checks);
    }

    [Fact]
    public async Task Auto_merge_is_on_when_github_holds_an_auto_merge_request()
    {
        var on = await Client(Status(autoMerge: """{ "enabledAt": "2026-09-29T10:00:00Z" }"""))
            .GetPullRequestStatusAsync(Repository, 708, TestContext.Current.CancellationToken);
        var off = await Client(Status(autoMerge: "null"))
            .GetPullRequestStatusAsync(Repository, 708, TestContext.Current.CancellationToken);

        Assert.True(on.AutoMergeEnabled);
        Assert.False(off.AutoMergeEnabled);
    }

    /// <summary>CLEAN, UNSTABLE and HAS_HOOKS are the merge states GitHub refuses
    /// auto-merge for — in each the pull request can merge right now, UNSTABLE
    /// only because the failing checks are not required — so they are the states
    /// that turn the offer into "Merge now". <c>gh pr merge --auto</c> draws the same
    /// line: for these it merges directly.</summary>
    [Theory]
    [InlineData("CLEAN", true)]
    [InlineData("UNSTABLE", true)]
    [InlineData("HAS_HOOKS", true)]
    [InlineData("BLOCKED", false)]
    [InlineData("BEHIND", false)]
    [InlineData("DIRTY", false)]
    [InlineData("DRAFT", false)]
    [InlineData("UNKNOWN", false)]
    public async Task Merge_ready_is_a_state_github_would_merge_now(string mergeState, bool expected)
    {
        var status = await Client(Status(mergeStateStatus: mergeState))
            .GetPullRequestStatusAsync(Repository, 708, TestContext.Current.CancellationToken);

        Assert.Equal(expected, status.MergeReady);
    }

    /// <summary>The repository's own settings decide the method, in the order the
    /// merge button offers them. There is no picker; the one GitHub already lists
    /// first is the one a person pressing the button would get.</summary>
    [Theory]
    [InlineData(true, true, true, GitHubMergeMethod.Merge)]
    [InlineData(false, true, true, GitHubMergeMethod.Squash)]
    [InlineData(false, false, true, GitHubMergeMethod.Rebase)]
    public async Task The_preferred_merge_method_is_the_first_the_repository_allows(
        bool merge, bool squash, bool rebase, GitHubMergeMethod expected)
    {
        var status = await Client(Status(mergeAllowed: merge, squashAllowed: squash, rebaseAllowed: rebase))
            .GetPullRequestStatusAsync(Repository, 708, TestContext.Current.CancellationToken);

        Assert.Equal(expected, status.PreferredMergeMethod);
    }

    /// <summary>The owner, the name and the number travel as variables, never spliced
    /// into the query text: a repository name is a value, and a value in the query
    /// text is an injection waiting for an odd name.</summary>
    [Fact]
    public async Task The_query_is_one_post_to_graphql_with_its_values_as_variables()
    {
        var transport = new RoutingTransport().Returns("graphql", Status());

        await new GitHubClient(transport).GetPullRequestStatusAsync(Repository, 708, TestContext.Current.CancellationToken);

        var path = Assert.Single(transport.Paths);
        Assert.StartsWith("graphql", path, StringComparison.Ordinal);

        using var body = JsonDocument.Parse(transport.Bodies[0]!);
        var variables = body.RootElement.GetProperty("variables");
        Assert.Equal("JSdotNet", variables.GetProperty("owner").GetString());
        Assert.Equal("Backlog", variables.GetProperty("name").GetString());
        Assert.Equal(708, variables.GetProperty("number").GetInt32());

        var query = body.RootElement.GetProperty("query").GetString()!;
        Assert.DoesNotContain("JSdotNet", query, StringComparison.Ordinal);
        Assert.DoesNotContain("708", query, StringComparison.Ordinal);
    }

    /// <summary>
    /// GraphQL answers a refusal with 200 and an <c>errors</c> array. Read as a
    /// success, a refused merge would look like one that worked, so the first
    /// error's words become the exception and its type rides along for a caller
    /// with a plan for one.
    /// </summary>
    [Fact]
    public async Task An_errors_array_is_a_failure_never_a_success()
    {
        var transport = new RoutingTransport().Returns(
            "graphql",
            """
            {
              "data": { "enablePullRequestAutoMerge": null },
              "errors": [
                { "type": "UNPROCESSABLE", "message": "Pull request Auto merge is not allowed for this repository" },
                { "message": "A second error nobody reads" }
              ]
            }
            """);

        var refused = await Assert.ThrowsAsync<GitHubException>(() =>
            new GitHubClient(transport).EnableAutoMergeAsync(
                Repository, "PR_kwDOabc", GitHubMergeMethod.Merge, TestContext.Current.CancellationToken));

        Assert.Equal("Pull request Auto merge is not allowed for this repository", refused.Message);
        Assert.Equal("UNPROCESSABLE", refused.ErrorType);
    }

    /// <summary>
    /// A fine-grained token without access to checks is refused the roll-up alone:
    /// GitHub answers FORBIDDEN with a <c>path</c> into <c>statusCheckRollup</c> and
    /// still returns the pull request. The status is read with no checks rather
    /// than lost — the state, the auto-merge request and the merge act do not
    /// depend on the one field the token cannot see.
    /// </summary>
    [Fact]
    public async Task A_refusal_confined_to_the_check_roll_up_reads_as_no_checks()
    {
        var json = WithErrors(
            Status(rollup: "null", autoMerge: """{ "enabledAt": "2026-09-29T10:00:00Z" }"""),
            """{ "type": "FORBIDDEN", "message": "Resource not accessible by personal access token", "path": ["repository", "pullRequest", "commits", "nodes", 0, "commit", "statusCheckRollup"] }""");

        var status = await Client(json).GetPullRequestStatusAsync(Repository, 708, TestContext.Current.CancellationToken);

        Assert.Equal(GitHubCheckState.None, status.Checks);
        Assert.Equal(GitHubItemState.Open, status.State);
        Assert.True(status.AutoMergeEnabled);
    }

    /// <summary>Only the roll-up is forgiven. An error anywhere else in the status
    /// query is a failure, as it is for every mutation.</summary>
    [Fact]
    public async Task A_refusal_outside_the_check_roll_up_is_still_a_failure()
    {
        var json = WithErrors(
            Status(),
            """{ "type": "FORBIDDEN", "message": "Resource not accessible by personal access token", "path": ["repository", "pullRequest", "autoMergeRequest"] }""");

        await Assert.ThrowsAsync<GitHubException>(() =>
            Client(json).GetPullRequestStatusAsync(Repository, 708, TestContext.Current.CancellationToken));
    }

    /// <summary>And a roll-up refusal with no pull request to read beside it is no
    /// status at all.</summary>
    [Fact]
    public async Task A_roll_up_refusal_without_a_pull_request_is_still_a_failure()
    {
        var json = """
            {
              "data": { "repository": { "mergeCommitAllowed": true, "squashMergeAllowed": true, "rebaseMergeAllowed": true, "pullRequest": null } },
              "errors": [ { "type": "FORBIDDEN", "message": "Resource not accessible", "path": ["repository", "pullRequest", "commits", "nodes", 0, "commit", "statusCheckRollup"] } ]
            }
            """;

        await Assert.ThrowsAsync<GitHubException>(() =>
            Client(json).GetPullRequestStatusAsync(Repository, 708, TestContext.Current.CancellationToken));
    }

    /// <summary>A mutation forgives nothing, whatever the error's path says.</summary>
    [Fact]
    public async Task A_mutation_fails_on_a_roll_up_shaped_error_too()
    {
        var transport = new RoutingTransport().Returns(
            "graphql",
            """{ "data": { "mergePullRequest": null }, "errors": [ { "type": "FORBIDDEN", "message": "No", "path": ["statusCheckRollup"] } ] }""");

        await Assert.ThrowsAsync<GitHubException>(() =>
            new GitHubClient(transport).MergePullRequestAsync(Repository, "PR_kwDOabc", GitHubMergeMethod.Merge, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_pull_request_github_does_not_have_is_not_found()
    {
        var transport = new RoutingTransport().Returns(
            "graphql",
            """{ "data": { "repository": { "mergeCommitAllowed": true, "squashMergeAllowed": true, "rebaseMergeAllowed": true, "pullRequest": null } } }""");

        var refused = await Assert.ThrowsAsync<GitHubException>(() =>
            new GitHubClient(transport).GetPullRequestStatusAsync(Repository, 708, TestContext.Current.CancellationToken));

        Assert.True(refused.IsNotFound);
    }

    [Fact]
    public async Task Enabling_auto_merge_names_the_pull_request_and_the_method()
    {
        var transport = new RoutingTransport().Returns("graphql", """{ "data": { "enablePullRequestAutoMerge": { "clientMutationId": null } } }""");

        await new GitHubClient(transport).EnableAutoMergeAsync(
            Repository, "PR_kwDOabc", GitHubMergeMethod.Squash, TestContext.Current.CancellationToken);

        var (query, variables) = Sent(transport);
        Assert.Contains("enablePullRequestAutoMerge", query, StringComparison.Ordinal);
        Assert.Equal("PR_kwDOabc", variables.GetProperty("id").GetString());
        Assert.Equal("SQUASH", variables.GetProperty("method").GetString());
    }

    [Fact]
    public async Task Disabling_auto_merge_names_the_pull_request()
    {
        var transport = new RoutingTransport().Returns("graphql", """{ "data": { "disablePullRequestAutoMerge": { "clientMutationId": null } } }""");

        await new GitHubClient(transport).DisableAutoMergeAsync(Repository, "PR_kwDOabc", TestContext.Current.CancellationToken);

        var (query, variables) = Sent(transport);
        Assert.Contains("disablePullRequestAutoMerge", query, StringComparison.Ordinal);
        Assert.Equal("PR_kwDOabc", variables.GetProperty("id").GetString());
    }

    [Fact]
    public async Task Merging_names_the_pull_request_and_the_method()
    {
        var transport = new RoutingTransport().Returns("graphql", """{ "data": { "mergePullRequest": { "clientMutationId": null } } }""");

        await new GitHubClient(transport).MergePullRequestAsync(
            Repository, "PR_kwDOabc", GitHubMergeMethod.Rebase, TestContext.Current.CancellationToken);

        var (query, variables) = Sent(transport);
        Assert.Contains("mergePullRequest", query, StringComparison.Ordinal);
        Assert.Equal("PR_kwDOabc", variables.GetProperty("id").GetString());
        Assert.Equal("REBASE", variables.GetProperty("method").GetString());
    }

    /// <summary>
    /// The path names the repository, for routing only. <c>graphql</c> alone names
    /// nobody, and a call that names nobody leaves as this machine's default
    /// identity — so in a workspace where this repository is bound to another
    /// account, the merge would go out as the wrong person. The hint after the
    /// <c>#</c> is what the credential resolver reads, and neither transport sends it.
    /// </summary>
    [Fact]
    public async Task The_path_carries_the_repository_for_the_credential_resolver()
    {
        var transport = new RoutingTransport().Returns("graphql", Status());

        await new GitHubClient(transport).GetPullRequestStatusAsync(Repository, 708, TestContext.Current.CancellationToken);

        Assert.Equal("graphql#JSdotNet/Backlog", Assert.Single(transport.Paths));
    }

    // --- Helpers --------------------------------------------------------------

    private static GitHubClient Client(string json) =>
        new(new RoutingTransport().Returns("graphql", json));

    private static (string Query, JsonElement Variables) Sent(RoutingTransport transport)
    {
        using var body = JsonDocument.Parse(Assert.Single(transport.Bodies)!);
        return (
            body.RootElement.GetProperty("query").GetString()!,
            body.RootElement.GetProperty("variables").Clone());
    }

    private static string Status(
        string state = "OPEN",
        bool isDraft = false,
        string mergeStateStatus = "BLOCKED",
        string autoMerge = "null",
        string rollup = "\"SUCCESS\"",
        bool mergeAllowed = true,
        bool squashAllowed = true,
        bool rebaseAllowed = true)
    {
        var commit = rollup == "null"
            ? """{ "commit": { "statusCheckRollup": null } }"""
            : "{ \"commit\": { \"statusCheckRollup\": { \"state\": " + rollup + " } } }";

        return $$"""
        {
          "data": {
            "repository": {
              "mergeCommitAllowed": {{Bool(mergeAllowed)}},
              "squashMergeAllowed": {{Bool(squashAllowed)}},
              "rebaseMergeAllowed": {{Bool(rebaseAllowed)}},
              "pullRequest": {
                "id": "PR_kwDOabc",
                "number": 708,
                "state": "{{state}}",
                "isDraft": {{Bool(isDraft)}},
                "mergeStateStatus": "{{mergeStateStatus}}",
                "autoMergeRequest": {{autoMerge}},
                "commits": { "nodes": [ {{commit}} ] }
              }
            }
          }
        }
        """;
    }

    private static string Bool(bool value) => value ? "true" : "false";

    /// <summary>A status answer with an <c>errors</c> array beside its data.</summary>
    private static string WithErrors(string status, string error)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(status)!;
        node["errors"] = new System.Text.Json.Nodes.JsonArray(System.Text.Json.Nodes.JsonNode.Parse(error));
        return node.ToJsonString();
    }
}
