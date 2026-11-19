using System.Text.Json;
using Backlog.Infrastructure.GitHub;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// The three merge acts over GitHub's GraphQL API — enabling auto-merge has no REST
/// endpoint at all — and the REST read of one pull request's branches.
/// </summary>
public sealed class GitHubClientMergeActTests
{
    private static readonly GitHubRepositoryRef Repository = new("backlog", "JSdotNet", "Backlog");

    /// <summary>The REST read answers the base branch and the default branch, so it
    /// can still tell where a merge landed.</summary>
    [Fact]
    public async Task The_rest_read_carries_the_base_and_the_default_branch()
    {
        var transport = new RoutingTransport().Returns("pulls/708", """
            {
              "number": 708,
              "html_url": "https://github.com/JSdotNet/Backlog/pull/708",
              "title": "A pull request",
              "state": "closed",
              "merged_at": "2026-10-21T01:22:00Z",
              "base": { "ref": "plan/devbook-domain-view", "repo": { "default_branch": "main" } }
            }
            """);

        var pull = await new GitHubClient(transport).GetPullRequestAsync(Repository, 708, TestContext.Current.CancellationToken);

        Assert.Equal(GitHubItemState.Merged, pull.State);
        Assert.Equal("plan/devbook-domain-view", pull.BaseRefName);
        Assert.Equal("main", pull.DefaultBranch);
    }

    [Fact]
    public async Task A_rest_read_without_a_base_reads_the_branches_as_unknown()
    {
        var transport = new RoutingTransport().Returns("pulls/708", """
            { "number": 708, "state": "open" }
            """);

        var pull = await new GitHubClient(transport).GetPullRequestAsync(Repository, 708, TestContext.Current.CancellationToken);

        Assert.Null(pull.BaseRefName);
        Assert.Null(pull.DefaultBranch);
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
        var transport = new RoutingTransport().Returns("graphql", """{ "data": { "mergePullRequest": { "clientMutationId": null } } }""");

        await new GitHubClient(transport).MergePullRequestAsync(
            Repository, "PR_kwDOabc", GitHubMergeMethod.Merge, TestContext.Current.CancellationToken);

        Assert.Equal("graphql#JSdotNet/Backlog", Assert.Single(transport.Paths));
    }

    // --- Helpers --------------------------------------------------------------

    private static (string Query, JsonElement Variables) Sent(RoutingTransport transport)
    {
        using var body = JsonDocument.Parse(Assert.Single(transport.Bodies)!);
        return (
            body.RootElement.GetProperty("query").GetString()!,
            body.RootElement.GetProperty("variables").Clone());
    }
}
