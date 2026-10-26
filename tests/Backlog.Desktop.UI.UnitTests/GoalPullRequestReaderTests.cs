using Backlog.Infrastructure.FileSystem.Progress;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Progress's pull request reader, answered from GitHub (<see cref="GitHubGoalPullRequests"/>):
/// state, merge day, author and opened day, the check counts and failing check names,
/// and the approvals against what the base branch requires
/// (<c>.devbook/domain/progress/features.md#pull-request-status</c>) — read through the
/// status read and its own cache, never WorkInProgressReader's slim read.
/// </summary>
public sealed class GoalPullRequestReaderTests
{
    [Fact]
    public async Task The_reader_answers_state_merge_day_author_and_opened_day_from_github_and_reads_a_merged_one_once()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-goal-pulls", Guid.NewGuid().ToString("n"));
        try
        {
            var settings = new GitHubSettingsStore(Path.Combine(root, "github.json"));
            var (parsed, _) = GitHubSettings.ParseText("JSdotNet/Backlog");
            settings.SetRepositories(parsed);
            var client = new TasksPaneHost.FakeGitHubClient();
            client.PullRequestsByNumber.Add(new GitHubLinkedPullRequest(4, "u", "t", "JSdotNet/Backlog", GitHubItemState.Merged, "jsdotnet", new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero)));
            client.PullRequestsByNumber.Add(new GitHubLinkedPullRequest(5, "u", "t", "JSdotNet/Backlog", GitHubItemState.Open, "octo", null, new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero)));
            // One adapter, as the host composes it: what has been read is kept there.
            var gitHub = new GitHubIntegration(settings, client, new ConnectedProbe());
            var reader = new GitHubGoalPullRequests(() => gitHub);

            var first = await reader.ReadAsync([new("JSdotNet/Backlog", "4"), new("JSdotNet/Backlog", "5"), new("JSdotNet/Backlog", "6")], TestContext.Current.CancellationToken);

            Assert.Equal([LinkedPullRequestState.Merged, LinkedPullRequestState.Open, LinkedPullRequestState.Unknown], first.Select(pull => pull.State));
            Assert.Equal(new DateOnly(2026, 10, 7), first[0].MergedOn);

            Assert.Equal(["JSdotNet/Backlog 4,5,6"], client.StatusReadByNumber);
            Assert.Empty(client.ReadByNumber);

            client.StatusReadByNumber.Clear();
            await reader.ReadAsync([new("JSdotNet/Backlog", "4"), new("JSdotNet/Backlog", "6")], TestContext.Current.CancellationToken);
            Assert.Empty(client.StatusReadByNumber);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>The checks and approvals ride the same cached read: a second reading
    /// inside the freshness window asks GitHub nothing and answers the same status.</summary>
    [Fact]
    public async Task The_reader_answers_checks_and_approvals_from_the_cached_linked_read()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-goal-pulls", Guid.NewGuid().ToString("n"));
        try
        {
            var settings = new GitHubSettingsStore(Path.Combine(root, "github.json"));
            var (parsed, _) = GitHubSettings.ParseText("JSdotNet/Backlog");
            settings.SetRepositories(parsed);
            var client = new TasksPaneHost.FakeGitHubClient();
            client.PullRequestsByNumber.Add(new GitHubLinkedPullRequest(1344, "u", "t", "JSdotNet/Backlog", GitHubItemState.Open, "octo", null)
            {
                CheckCounts = new GitHubCheckCounts(2, 1, 11),
                FailingChecks = ["Build and test .NET"],
                Reviews = new GitHubReviewSummary(GitHubReviewState.ReviewRequired, 1, 0) { Required = 2 }
            });
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero));
            var gitHub = new GitHubIntegration(settings, client, new ConnectedProbe(), time: clock);
            var reader = new GitHubGoalPullRequests(() => gitHub);

            var first = Assert.Single(await reader.ReadAsync([new("JSdotNet/Backlog", "#1344")], TestContext.Current.CancellationToken));
            Assert.Equal(["JSdotNet/Backlog 1344"], client.StatusReadByNumber);
            client.StatusReadByNumber.Clear();
            var again = Assert.Single(await reader.ReadAsync([new("JSdotNet/Backlog", "#1344")], TestContext.Current.CancellationToken));

            Assert.Empty(client.StatusReadByNumber);
            Assert.Empty(client.ReadByNumber);
            foreach (var answer in new[] { first, again })
            {
                Assert.Equal("#1344", answer.Number);
                Assert.Equal((2, 1, 11, 14), (answer.Checks!.Passed, answer.Checks.Failed, answer.Checks.Pending, answer.Checks.Total));
                Assert.Equal(["Build and test .NET"], answer.Checks.FailingNames);
                Assert.Equal(new GoalPullRequestApprovalsDto(1, 2), answer.Approvals);
            }
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>An archived repository is not read from GitHub (local ADR 0029): its pull
    /// requests are not asked about, and an answer already known is kept rather than
    /// overwritten as unknown once it goes stale. Progress keeps the repository; only the
    /// read is skipped.</summary>
    [Fact]
    public async Task The_reader_skips_an_archived_repository_and_keeps_what_it_already_knew()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-goal-pulls", Guid.NewGuid().ToString("n"));
        try
        {
            var settings = new GitHubSettingsStore(Path.Combine(root, "github.json"));
            var (parsed, _) = GitHubSettings.ParseText("JSdotNet/Backlog\nJSdotNet/Archify");
            Assert.Null(settings.SetRepositories(parsed));
            var client = new TasksPaneHost.FakeGitHubClient();
            client.PullRequestsByNumber.Add(new GitHubLinkedPullRequest(5, "u", "t", "JSdotNet/Archify", GitHubItemState.Open, "octo", null));
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero));
            // One adapter, as the host composes it: the shared cache that keeps what was read lives there.
            var gitHub = new GitHubIntegration(settings, client, new ConnectedProbe(), time: clock);
            var reader = new GitHubGoalPullRequests(() => gitHub);

            Assert.Equal(LinkedPullRequestState.Open, Assert.Single(await reader.ReadAsync([new("JSdotNet/Archify", "5")], TestContext.Current.CancellationToken)).State);

            Assert.Null(settings.SetRepositoryArchived("archify", archived: true));
            clock.Advance(GitHubGoalPullRequests.FreshFor + TimeSpan.FromMinutes(1));
            client.StatusReadByNumber.Clear();

            var answer = await reader.ReadAsync([new("JSdotNet/Archify", "5"), new("JSdotNet/Archify", "6")], TestContext.Current.CancellationToken);

            Assert.Empty(client.StatusReadByNumber);
            Assert.Equal([LinkedPullRequestState.Open, LinkedPullRequestState.Unknown], answer.Select(pull => pull.State));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>A repository GitHub refused — a typo, or one the token cannot read — reads
    /// its pull request as unknown, never as another repository's pull request that
    /// happens to carry the same number in the same reading.</summary>
    [Fact]
    public async Task A_refused_repository_does_not_borrow_another_repositorys_pull_request_with_the_same_number()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-goal-pulls", Guid.NewGuid().ToString("n"));
        try
        {
            var settings = new GitHubSettingsStore(Path.Combine(root, "github.json"));
            var (parsed, _) = GitHubSettings.ParseText("JSdotNet/Backlog");
            settings.SetRepositories(parsed);
            var client = new TasksPaneHost.FakeGitHubClient();
            client.PullRequestsByNumber.Add(new GitHubLinkedPullRequest(12, "u", "t", "JSdotNet/Backlog", GitHubItemState.Merged, "jsdotnet", new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero))
            {
                CheckCounts = new GitHubCheckCounts(9, 0, 0),
                Reviews = new GitHubReviewSummary(GitHubReviewState.Approved, 1, 0) { Required = 1 }
            });
            client.RefusedStatusReads.Add("JSdotNet/Backlogg");
            var gitHub = new GitHubIntegration(settings, client, new ConnectedProbe());
            var reader = new GitHubGoalPullRequests(() => gitHub);

            var answer = await reader.ReadAsync([new("JSdotNet/Backlog", "12"), new("JSdotNet/Backlogg", "12")], TestContext.Current.CancellationToken);

            Assert.Equal(LinkedPullRequestState.Merged, answer[0].State);
            Assert.Equal("JSdotNet/Backlogg", answer[1].Repository);
            Assert.Equal(LinkedPullRequestState.Unknown, answer[1].State);
            Assert.Null(answer[1].MergedOn);
            Assert.Null(answer[1].Checks);
            Assert.Null(answer[1].Approvals);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>A repository GitHub reads, but which holds no pull request with the number
    /// asked — an issue there, or a number not yet used — reads it as unknown, never as
    /// another repository's pull request with that number in the same reading.</summary>
    [Fact]
    public async Task A_readable_repository_without_the_number_does_not_borrow_another_repositorys_pull_request_with_it()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-goal-pulls", Guid.NewGuid().ToString("n"));
        try
        {
            var settings = new GitHubSettingsStore(Path.Combine(root, "github.json"));
            var (parsed, _) = GitHubSettings.ParseText("JSdotNet/Backlog\nJSdotNet/Marketplace");
            settings.SetRepositories(parsed);
            var client = new TasksPaneHost.FakeGitHubClient();
            client.PullRequestsByNumber.Add(new GitHubLinkedPullRequest(1477, "u", "t", "JSdotNet/Backlog", GitHubItemState.Merged, "jsdotnet", new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero))
            {
                CheckCounts = new GitHubCheckCounts(9, 0, 0),
                Reviews = new GitHubReviewSummary(GitHubReviewState.Approved, 1, 0) { Required = 1 }
            });
            var gitHub = new GitHubIntegration(settings, client, new ConnectedProbe());
            var reader = new GitHubGoalPullRequests(() => gitHub);

            var answer = await reader.ReadAsync([new("JSdotNet/Backlog", "1477"), new("JSdotNet/Marketplace", "1477")], TestContext.Current.CancellationToken);

            Assert.Equal(LinkedPullRequestState.Merged, answer[0].State);
            Assert.Equal("JSdotNet/Marketplace", answer[1].Repository);
            Assert.Equal(LinkedPullRequestState.Unknown, answer[1].State);
            Assert.Null(answer[1].MergedOn);
            Assert.Null(answer[1].Checks);
            Assert.Null(answer[1].Approvals);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Without_github_every_pull_request_reads_unknown_with_no_checks_and_no_approvals()
    {
        var reader = new GitHubGoalPullRequests(() => null);

        var answer = Assert.Single(await reader.ReadAsync([new("JSdotNet/Backlog", "4")], TestContext.Current.CancellationToken));

        Assert.Equal(LinkedPullRequestState.Unknown, answer.State);
        Assert.Null(answer.Checks);
        Assert.Null(answer.Approvals);
    }

    // ---- The mapping -----------------------------------------------------------------------

    [Fact]
    public void A_pull_request_maps_its_checks_failing_names_and_approvals_against_the_required_count()
    {
        var pull = new GitHubLinkedPullRequest(1341, "u", "t", "JSdotNet/Backlog", GitHubItemState.Open, "octo", null)
        {
            CheckCounts = new GitHubCheckCounts(9, 0, 5),
            Reviews = new GitHubReviewSummary(GitHubReviewState.ReviewRequired, 1, 0) { Required = 2 }
        };

        var mapped = GitHubGoalPullRequests.Map(pull);

        Assert.Equal("JSdotNet/Backlog", mapped.Repository);
        Assert.Equal("1341", mapped.Number);
        Assert.Equal(LinkedPullRequestState.Open, mapped.State);
        Assert.Null(mapped.MergedOn);
        Assert.Equal((9, 0, 5, 14), (mapped.Checks!.Passed, mapped.Checks.Failed, mapped.Checks.Pending, mapped.Checks.Total));
        Assert.Empty(mapped.Checks.FailingNames);
        Assert.Equal(new GoalPullRequestApprovalsDto(1, 2), mapped.Approvals);
    }

    /// <summary>No rule on the base branch means no required total: the approvals still
    /// count what was received, and Required stays null rather than zero.</summary>
    [Fact]
    public void A_base_branch_with_no_rule_maps_to_approvals_with_no_required_total()
    {
        var pull = new GitHubLinkedPullRequest(7, "u", "t", "JSdotNet/Backlog", GitHubItemState.Open, "octo", null)
        {
            Reviews = new GitHubReviewSummary(GitHubReviewState.None, 1, 0)
        };

        var mapped = GitHubGoalPullRequests.Map(pull);

        Assert.Equal(new GoalPullRequestApprovalsDto(1, null), mapped.Approvals);
        Assert.Null(mapped.Approvals!.Required);
    }

    /// <summary>A head commit with no checks, or checks the token was refused, maps to no
    /// checks rather than <c>0/0</c>.</summary>
    [Fact]
    public void A_pull_request_without_checks_maps_to_no_checks()
    {
        var pull = new GitHubLinkedPullRequest(8, "u", "t", "JSdotNet/Backlog", GitHubItemState.Draft, null, null);

        var mapped = GitHubGoalPullRequests.Map(pull);

        Assert.Equal(LinkedPullRequestState.Draft, mapped.State);
        Assert.Null(mapped.Checks);
        Assert.Equal(new GoalPullRequestApprovalsDto(0, null), mapped.Approvals);
    }

    [Fact]
    public void A_merged_pull_request_maps_its_merge_day_and_failing_check_names()
    {
        var pull = new GitHubLinkedPullRequest(9, "u", "t", "JSdotNet/Backlog", GitHubItemState.Merged, "jsdotnet", new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero))
        {
            CheckCounts = new GitHubCheckCounts(12, 2, 0),
            FailingChecks = ["Build the app heads", "Compile VS Code extension"]
        };

        var mapped = GitHubGoalPullRequests.Map(pull);

        Assert.Equal(LinkedPullRequestState.Merged, mapped.State);
        Assert.Equal(new DateOnly(2026, 10, 7), mapped.MergedOn);
        Assert.Equal(["Build the app heads", "Compile VS Code extension"], mapped.Checks!.FailingNames);
        Assert.Equal(2, mapped.Checks.Failed);
    }

    private sealed class ConnectedProbe : IGitHubConnectionProbe
    {
        public Task<GitHubConnection> DescribeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitHubConnection(true, "Connected."));

        public void Invalidate()
        {
        }
    }
}
