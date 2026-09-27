using Backlog.Infrastructure.GitHub;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// Reading one pull request by number — what an entry's recorded pull request is
/// asked, so its link can say whether it merged. Merged is read from
/// <c>merged_at</c>, because the plain <c>state</c> says "closed" for both.
/// </summary>
public sealed class GitHubClientPullRequestTests
{
    private static readonly GitHubRepositoryRef Repository = new("backlog", "JSdotNet", "Backlog");

    [Theory]
    [InlineData("""{ "number": 708, "state": "closed", "merged_at": "2026-09-26T10:00:00Z", "draft": false }""", GitHubItemState.Merged)]
    [InlineData("""{ "number": 708, "state": "closed", "merged_at": null, "draft": false }""", GitHubItemState.Closed)]
    [InlineData("""{ "number": 708, "state": "open", "merged_at": null, "draft": true }""", GitHubItemState.Draft)]
    [InlineData("""{ "number": 708, "state": "open", "merged_at": null, "draft": false }""", GitHubItemState.Open)]
    public async Task A_pull_request_reads_as_its_state(string json, GitHubItemState expected)
    {
        var transport = new RoutingTransport().Returns("repos/JSdotNet/Backlog/pulls/708", json);
        var client = new GitHubClient(transport);

        var pull = await client.GetPullRequestAsync(Repository, 708, TestContext.Current.CancellationToken);

        Assert.Equal(708, pull.Number);
        Assert.Equal(expected, pull.State);
        Assert.Equal("JSdotNet/Backlog", pull.RepositoryFullName);
    }

    [Fact]
    public async Task A_refusal_is_a_github_exception()
    {
        var transport = new RoutingTransport().Refuses("pulls/708", "Not Found");
        var client = new GitHubClient(transport);

        await Assert.ThrowsAsync<GitHubException>(() =>
            client.GetPullRequestAsync(Repository, 708, TestContext.Current.CancellationToken));
    }
}
