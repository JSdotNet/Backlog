using Backlog.Desktop.UI.Goals;
using Backlog.Infrastructure.FileSystem.Progress;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Sessions.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// One cache for every reader of a pull request (local ADR 0042 §3, V3): a task's side
/// panel and the Goals view read the same pull request through Repository Management's
/// contract, composed as the hosts compose it over one GitHub client that counts what it
/// is sent — and within the minute GitHub is asked about it once.
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class PullRequestOneCacheTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private const string Repository = "JSdotNet/Backlog";

    [Fact]
    public async Task A_pull_request_the_side_panel_read_is_not_read_again_by_progress_within_the_minute()
    {
        using var host = await TasksPaneHost.CreateAsync(Repository);
        host.Client.OpenPullRequests.Add(Open(700));
        host.Client.PullRequestsByNumber.Add(new PullRequestByNumber(
            656, $"https://github.com/{Repository}/pull/656", "Pull request 656", Repository, GitHubItemState.Closed, "octocat", null));
        var clock = new FakeTimeProvider(Noon);
        var contract = RepositoryManagementChain.For(host.GitHub, clock);
        var sidePanel = new WorkInProgressReader(contract, new NoSessions(), clock, () => [new(Repository, 656), new(Repository, 700)]);
        var goals = new RepositoryManagementGoalPullRequests(() => contract);

        await sidePanel.ReadAsync();
        await sidePanel.LinkedRead;
        clock.Advance(TimeSpan.FromSeconds(30));
        var answer = await goals.ReadAsync([new(Repository, "656"), new(Repository, "700")], TestContext.Current.CancellationToken);

        // The side panel's open read answered for 700, its read by number for 656, and
        // Progress asked GitHub about neither again.
        Assert.Equal([$"{Repository} 656"], host.Client.PinnedReadByNumber);
        Assert.Equal([LinkedPullRequestState.Closed, LinkedPullRequestState.Open], answer.Select(pull => pull.State));
        Assert.Equal(656, Assert.Single(sidePanel.LinkedPullRequests).Number);
    }

    /// <summary>Past the minute, the next reader asks GitHub again: one cache, not one
    /// answer for good.</summary>
    [Fact]
    public async Task Past_the_minute_progress_reads_it_again()
    {
        using var host = await TasksPaneHost.CreateAsync(Repository);
        host.Client.PullRequestsByNumber.Add(new PullRequestByNumber(
            656, $"https://github.com/{Repository}/pull/656", "Pull request 656", Repository, GitHubItemState.Closed, "octocat", null));
        var clock = new FakeTimeProvider(Noon);
        var contract = RepositoryManagementChain.For(host.GitHub, clock);
        var sidePanel = new WorkInProgressReader(contract, new NoSessions(), clock, () => [new(Repository, 656)]);
        var goals = new RepositoryManagementGoalPullRequests(() => contract);

        await sidePanel.ReadAsync();
        await sidePanel.LinkedRead;
        clock.Advance(TimeSpan.FromMinutes(1));
        await goals.ReadAsync([new(Repository, "656")], TestContext.Current.CancellationToken);

        Assert.Equal([$"{Repository} 656", $"{Repository} 656"], host.Client.PinnedReadByNumber);
    }

    private static GitHubOpenPullRequest Open(int number) =>
        new(
            number,
            $"https://github.com/{Repository}/pull/{number}",
            $"Pull request {number}",
            Repository,
            $"PR_{number}",
            IsDraft: false,
            HeadRefName: $"feature-{number}",
            HeadSha: "abc",
            BaseRefName: "main",
            AuthorLogin: "octocat",
            ViewerDidAuthor: false,
            Checks: GitHubCheckState.Passing,
            AutoMergeEnabled: false,
            MergeReady: false,
            IsBehind: false,
            HasConflicts: false,
            MergeStateStatus: "BLOCKED",
            PreferredMergeMethod: GitHubMergeMethod.Squash,
            UpdatedAt: Noon.AddMinutes(-number));

    private sealed class NoSessions : IAgentSessionSource
    {
        public Task<AgentSessionCatalog> GetSessionsAsync(AgentSessionQuery query, CancellationToken cancellationToken = default) =>
            GetSessionsAsync(cancellationToken);

        public Task<AgentSessionCatalog> GetSessionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AgentSessionCatalog([], [], 0));
    }
}
