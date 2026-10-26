using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// The cache of linked pull requests read by number — one instance before the slim read
/// the In progress reader asks through, another before the status read Progress asks
/// through: what it keeps, for how long, and that a read still out is waited for rather
/// than sent twice.
/// </summary>
public sealed class GitHubLinkedPullRequestCacheTests
{
    private const string Repository = "JSdotNet/Backlog";

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 15, 9, 0, 0, TimeSpan.Zero));
    private readonly List<string> _asked = [];
    private readonly List<GitHubLinkedPullRequest> _github = [];
    private readonly List<GitHubRepositoryFailure> _refusals = [];
    private TaskCompletionSource? _hold;

    private GitHubLinkedPullRequestCache Cache() => new(ReadAsync, _clock);

    [Fact]
    public async Task A_merged_one_and_a_number_github_answered_without_are_kept_and_any_other_until_it_is_stale()
    {
        _github.AddRange([Pull(1, GitHubItemState.Merged), Pull(2, GitHubItemState.Open)]);
        var cache = Cache();
        var freshFor = TimeSpan.FromMinutes(1);

        await cache.ReadAsync([Link(1), Link(2), Link(3)], freshFor, TestContext.Current.CancellationToken);
        _clock.Advance(TimeSpan.FromSeconds(30));
        var again = await cache.ReadAsync([Link(1), Link(2), Link(3)], freshFor, TestContext.Current.CancellationToken);
        _clock.Advance(TimeSpan.FromSeconds(30));
        await cache.ReadAsync([Link(1), Link(2), Link(3)], freshFor, TestContext.Current.CancellationToken);

        Assert.Equal(["1,2,3", "2"], _asked);
        Assert.Equal([1, 2], again.PullRequests.Select(pull => pull.Number).Order());
        Assert.Empty(again.Failures);
    }

    [Fact]
    public async Task A_read_still_out_is_waited_for_rather_than_sent_again()
    {
        _github.Add(Pull(2, GitHubItemState.Open));
        _hold = new TaskCompletionSource();
        var cache = Cache();

        var first = cache.ReadAsync([Link(2)], TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);
        _clock.Advance(TimeSpan.FromSeconds(1));
        var second = cache.ReadAsync([Link(2)], TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        _hold.SetResult();

        Assert.Equal(2, Assert.Single((await first).PullRequests).Number);
        Assert.Equal(2, Assert.Single((await second).PullRequests).Number);
        Assert.Single(_asked);
    }

    /// <summary>A read still out that began before the caller's freshness — an earlier
    /// Refresh's — is not taken for this one: the caller asks for itself.</summary>
    [Fact]
    public async Task A_read_still_out_that_began_too_long_ago_is_not_waited_for()
    {
        _github.Add(Pull(2, GitHubItemState.Open));
        _hold = new TaskCompletionSource();
        var cache = Cache();

        var first = cache.ReadAsync([Link(2)], TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);
        _clock.Advance(TimeSpan.FromSeconds(10));
        _hold = null;
        var second = await cache.ReadAsync([Link(2)], TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(2, Assert.Single(second.PullRequests).Number);
        Assert.Equal(2, _asked.Count);
        Assert.False(first.IsCompleted);
    }

    [Fact]
    public async Task One_caller_leaving_does_not_cancel_the_read_another_is_waiting_for()
    {
        _github.Add(Pull(2, GitHubItemState.Merged));
        _hold = new TaskCompletionSource();
        var cache = Cache();
        using var leaving = new CancellationTokenSource();

        var first = cache.ReadAsync([Link(2)], TimeSpan.FromMinutes(1), leaving.Token);
        _clock.Advance(TimeSpan.FromSeconds(1));
        var second = cache.ReadAsync([Link(2)], TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);
        await leaving.CancelAsync();
        _hold.SetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.Equal(2, Assert.Single((await second).PullRequests).Number);
        Assert.Single(_asked);
    }

    [Fact]
    public async Task A_repository_that_refused_and_a_read_that_threw_are_kept_nowhere_and_asked_again()
    {
        _refusals.Add(new GitHubRepositoryFailure(Repository, "Not found."));
        var cache = Cache();

        var refused = await cache.ReadAsync([Link(4)], TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);
        _refusals.Clear();
        _github.Add(Pull(4, GitHubItemState.Closed));
        var read = await cache.ReadAsync([Link(4)], TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);

        Assert.Equal(Repository, Assert.Single(refused.Failures).RepositoryFullName);
        Assert.Equal(GitHubItemState.Closed, Assert.Single(read.PullRequests).State);
        Assert.Equal(2, _asked.Count);
    }

    /// <summary>A repository that answered without the number, read in one batch beside
    /// another repository's merged pull request with that number, is kept as answered
    /// without — never as that merged pull request, which would be kept for good and
    /// handed to everyone who later asks for the link on its own.</summary>
    [Fact]
    public async Task A_repository_that_answered_without_the_number_does_not_keep_another_repositorys_pull_request_with_it()
    {
        _github.Add(Pull(1477, GitHubItemState.Merged));
        var cache = Cache();
        var elsewhere = new GitHubPullRequestReference("JSdotNet/Marketplace", 1477);

        await cache.ReadAsync([Link(1477), elsewhere], TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);
        var alone = await cache.ReadAsync([elsewhere], TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);

        Assert.Empty(alone.PullRequests);
        Assert.Empty(alone.Failures);
        Assert.Single(_asked);
    }

    private async Task<GitHubLinkedPullRequestListing> ReadAsync(IReadOnlyList<GitHubPullRequestReference> links, CancellationToken cancellationToken)
    {
        _asked.Add(string.Join(",", links.Select(link => link.Number).Order()));
        if (_hold is { } hold) await hold.Task;

        var refused = _refusals.Select(failure => failure.RepositoryFullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new GitHubLinkedPullRequestListing(
            [.. _github.Where(pull => links.Any(link => link.Number == pull.Number) && !refused.Contains(pull.RepositoryFullName))],
            [.. _refusals]);
    }

    private static GitHubPullRequestReference Link(int number) => new(Repository, number);

    private static GitHubLinkedPullRequest Pull(int number, GitHubItemState state) =>
        new(number, $"https://github.com/{Repository}/pull/{number}", $"Pull request {number}", Repository, state, "octocat", null);
}
