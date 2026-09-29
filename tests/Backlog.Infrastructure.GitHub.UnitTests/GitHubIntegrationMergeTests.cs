using System.Text.Json;
using Backlog.Infrastructure.GitHub;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// The merge acts as the app reaches them — through <see cref="GitHubIntegration"/>,
/// over the real client and a stubbed transport — and what a person is told when
/// GitHub says no.
/// <para>
/// GitHub's refusals are written for an API caller: "Pull request Auto merge is not
/// allowed for this repository" names neither the pull request nor the switch that
/// would fix it. The façade is where they become sentences somebody can act on,
/// because it is the one place that knows both which pull request was asked about
/// and which act was attempted. A refusal it does not recognise keeps GitHub's own
/// words rather than being replaced with a vaguer one.
/// </para>
/// </summary>
public sealed class GitHubIntegrationMergeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-merge-tests", Guid.NewGuid().ToString("n"));

    private static readonly GitHubPullRequestStatus Pull = new(
        708,
        "JSdotNet/Backlog",
        "PR_kwDOabc",
        GitHubItemState.Open,
        GitHubCheckState.Pending,
        AutoMergeEnabled: false,
        MergeReady: false,
        GitHubMergeMethod.Squash);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Enabling_auto_merge_sends_the_pull_requests_node_and_the_repositorys_method()
    {
        var transport = new RoutingTransport().Returns("graphql", """{ "data": { "enablePullRequestAutoMerge": { "clientMutationId": null } } }""");

        await Integration(transport).EnableAutoMergeAsync(Pull, TestContext.Current.CancellationToken);

        Assert.Equal("graphql#JSdotNet/Backlog", Assert.Single(transport.Paths));
        using var body = JsonDocument.Parse(transport.Bodies[0]!);
        Assert.Equal("PR_kwDOabc", body.RootElement.GetProperty("variables").GetProperty("id").GetString());
        Assert.Equal("SQUASH", body.RootElement.GetProperty("variables").GetProperty("method").GetString());
    }

    [Fact]
    public async Task Reading_a_status_goes_to_the_repository_the_link_names()
    {
        var transport = new RoutingTransport().Returns(
            "graphql",
            """
            { "data": { "repository": {
                "mergeCommitAllowed": true, "squashMergeAllowed": false, "rebaseMergeAllowed": false,
                "pullRequest": { "id": "PR_x", "number": 12, "state": "OPEN", "isDraft": false,
                                 "mergeStateStatus": "CLEAN", "autoMergeRequest": null,
                                 "commits": { "nodes": [] } } } } }
            """);

        var status = await Integration(transport).ReadPullRequestStatusAsync("octo/demo", 12, TestContext.Current.CancellationToken);

        Assert.Equal("graphql#octo/demo", Assert.Single(transport.Paths));
        Assert.Equal("octo/demo", status.RepositoryFullName);
        Assert.True(status.MergeReady);
    }

    [Fact]
    public async Task Auto_merge_turned_off_for_the_repository_says_which_switch_to_turn_on()
    {
        var refused = await RefusedAsync(
            integration => integration.EnableAutoMergeAsync(Pull, TestContext.Current.CancellationToken),
            "Pull request Auto merge is not allowed for this repository");

        Assert.Contains("JSdotNet/Backlog#708", refused.Message, StringComparison.Ordinal);
        Assert.Contains("Allow auto-merge", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>GitHub refuses auto-merge for a pull request that could merge right
    /// now. The menu offers "Merge now" in that state, so this is a status that
    /// changed between the read and the click — the sentence says what to do
    /// instead.</summary>
    [Theory]
    [InlineData("Pull request is in clean status")]
    [InlineData("Pull request is in unstable status")]
    [InlineData("Pull request is in has_hooks status")]
    public async Task A_pull_request_that_can_already_merge_is_pointed_at_merge_now(string message)
    {
        var refused = await RefusedAsync(
            integration => integration.EnableAutoMergeAsync(Pull, TestContext.Current.CancellationToken),
            message);

        Assert.Contains("Merge #708 now", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>A repository with a merge queue takes pull requests through the
    /// queue, and GitHub says so in its own terms; the sentence names the pull
    /// request and where it has to go.</summary>
    [Fact]
    public async Task A_merge_queue_refusal_says_the_pull_request_must_go_through_the_queue()
    {
        var refused = await RefusedAsync(
            integration => integration.MergePullRequestAsync(Pull, TestContext.Current.CancellationToken),
            "Changes must be made through the merge queue");

        Assert.Contains("#708 must go through the merge queue", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_base_branch_without_required_checks_says_so()
    {
        var refused = await RefusedAsync(
            integration => integration.EnableAutoMergeAsync(Pull, TestContext.Current.CancellationToken),
            "Pull request Protected branch rules not configured for this branch");

        Assert.Contains("branch protection", refused.Message, StringComparison.Ordinal);
        Assert.Contains("required", refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Resource not accessible by integration", null)]
    [InlineData("Viewer must have write permission to merge this pull request", null)]
    [InlineData("Something GitHub phrased differently", "FORBIDDEN")]
    public async Task A_permission_refusal_names_the_permission(string message, string? type)
    {
        var refused = await RefusedAsync(
            integration => integration.MergePullRequestAsync(Pull, TestContext.Current.CancellationToken),
            message,
            type);

        Assert.Contains("doesn't have permission", refused.Message, StringComparison.Ordinal);
        Assert.Contains("JSdotNet/Backlog#708", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_403_is_a_permission_refusal()
    {
        var transport = new RoutingTransport().Refuses(HttpMethod.Post, "graphql", System.Net.HttpStatusCode.Forbidden, "Forbidden");

        var refused = await Assert.ThrowsAsync<GitHubException>(() =>
            Integration(transport).MergePullRequestAsync(Pull, TestContext.Current.CancellationToken));

        Assert.Contains("doesn't have permission", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>The word "permission" alone is not a permission refusal: GitHub uses
    /// it in sentences about other things, and misreporting one as "you lack write
    /// access" would send somebody to the wrong settings page. Such a sentence keeps
    /// GitHub's own words.</summary>
    [Fact]
    public async Task A_message_that_merely_mentions_permissions_keeps_githubs_words()
    {
        var refused = await RefusedAsync(
            integration => integration.MergePullRequestAsync(Pull, TestContext.Current.CancellationToken),
            "Required status check permissions are being recalculated");

        Assert.Equal(
            "Couldn't merge JSdotNet/Backlog#708: Required status check permissions are being recalculated",
            refused.Message);
    }

    [Fact]
    public async Task A_pull_request_github_will_not_merge_says_what_usually_stops_one()
    {
        var refused = await RefusedAsync(
            integration => integration.MergePullRequestAsync(Pull, TestContext.Current.CancellationToken),
            "Pull Request is not mergeable");

        Assert.Contains("conflicts", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>What the façade does not recognise, it does not paraphrase: GitHub's
    /// own sentence, with the act and the pull request in front of it.</summary>
    [Fact]
    public async Task An_unrecognised_refusal_keeps_githubs_own_words()
    {
        var refused = await RefusedAsync(
            integration => integration.DisableAutoMergeAsync(Pull, TestContext.Current.CancellationToken),
            "Something new GitHub started saying");

        Assert.Equal(
            "Couldn't cancel auto-merge for JSdotNet/Backlog#708: Something new GitHub started saying",
            refused.Message);
    }

    /// <summary>A transport refusal — the CLI's stderr, a token's 403 — is mapped
    /// the same way as a GraphQL <c>errors</c> entry: <c>gh api graphql</c> reports
    /// GraphQL errors on its error line and exits non-zero, so both routes arrive as
    /// the same exception.</summary>
    [Fact]
    public async Task A_transport_refusal_is_mapped_like_a_graphql_error()
    {
        var transport = new RoutingTransport().Refuses("graphql", "gh: Pull request Auto merge is not allowed for this repository");

        var refused = await Assert.ThrowsAsync<GitHubException>(() =>
            Integration(transport).EnableAutoMergeAsync(Pull, TestContext.Current.CancellationToken));

        Assert.Contains("Allow auto-merge", refused.Message, StringComparison.Ordinal);
    }

    private async Task<GitHubException> RefusedAsync(
        Func<GitHubIntegration, Task> act,
        string message,
        string? type = null)
    {
        var error = type is null
            ? JsonSerializer.Serialize(new { message })
            : JsonSerializer.Serialize(new { type, message });

        var transport = new RoutingTransport().Returns("graphql", $$"""{ "data": null, "errors": [ {{error}} ] }""");

        return await Assert.ThrowsAsync<GitHubException>(() => act(Integration(transport)));
    }

    private GitHubIntegration Integration(RoutingTransport transport)
    {
        Directory.CreateDirectory(_root);
        return new GitHubIntegration(
            new GitHubSettingsStore(Path.Combine(_root, "github.json")),
            new GitHubClient(transport),
            new NoProbe());
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
