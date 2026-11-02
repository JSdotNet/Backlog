using Backlog.Modules.RepositoryManagement.Abstractions;
using Backlog.Modules.RepositoryManagement.Abstractions.DataTransferObjects;
using Backlog.Modules.RepositoryManagement.Abstractions.Services;
using Backlog.Modules.RepositoryManagement.Services;
using Backlog.Modules.RepositoryManagement.UnitTests.Fakes;
using Microsoft.Extensions.Time.Testing;
using static Backlog.Modules.RepositoryManagement.UnitTests.Fakes.Statuses;

namespace Backlog.Modules.RepositoryManagement.UnitTests;

/// <summary>
/// The one cache behind <see cref="IPullRequests.ReadAsync"/> (local ADR 0042 §3), case
/// for case where the two caches it replaces were tested: what it keeps, for how long,
/// that a read still out is waited for rather than sent twice, and that a refusal is
/// kept nowhere.
/// </summary>
public sealed class PullRequestCacheTests
{
    private readonly FakeTimeProvider _clock = new(Noon);
    private readonly CountingPullRequestSource _github = new();
    private readonly InMemorySettledStore _settled = new();

    private IPullRequests Module() => new PullRequests(_github, new FakeRegisteredRepositories(BacklogRepository, ArchifyRepository), _settled, _clock);

    private static readonly PullRequestReadOptions Minute = PullRequestReadOptions.Default;

    [Fact]
    public async Task A_merged_one_and_a_number_github_answered_without_are_kept_and_any_other_until_it_is_stale()
    {
        _github.Holds(Pull(1, PullRequestState.Merged), Pull(2));
        var module = Module();

        await module.ReadAsync([Ref(1), Ref(2), Ref(3)], Minute, TestContext.Current.CancellationToken);
        _clock.Advance(TimeSpan.FromSeconds(30));
        var again = await module.ReadAsync([Ref(1), Ref(2), Ref(3)], Minute, TestContext.Current.CancellationToken);
        _clock.Advance(TimeSpan.FromSeconds(30));
        await module.ReadAsync([Ref(1), Ref(2), Ref(3)], Minute, TestContext.Current.CancellationToken);

        Assert.Equal([$"read {BacklogRepository} 1,2,3", $"read {BacklogRepository} 2"], _github.Calls);
        Assert.Equal([1, 2], again.PullRequests.Select(pull => pull.Number).Order());
        Assert.Empty(again.Failures);
    }

    [Fact]
    public async Task A_read_still_out_is_waited_for_rather_than_sent_again()
    {
        _github.Holds(Pull(2));
        _github.Hold = new TaskCompletionSource();
        var module = Module();

        var first = module.ReadAsync([Ref(2)], Minute, TestContext.Current.CancellationToken);
        _clock.Advance(TimeSpan.FromSeconds(1));
        var second = module.ReadAsync([Ref(2)], new PullRequestReadOptions(TimeSpan.FromSeconds(5)), TestContext.Current.CancellationToken);
        _github.Hold.SetResult();

        Assert.Equal(2, Assert.Single((await first).PullRequests).Number);
        Assert.Equal(2, Assert.Single((await second).PullRequests).Number);
        Assert.Single(_github.Calls);
    }

    /// <summary>A read still out that began before the caller's freshness — an earlier
    /// Refresh's — is not taken for this one: the caller asks for itself.</summary>
    [Fact]
    public async Task A_read_still_out_that_began_too_long_ago_is_not_waited_for()
    {
        _github.Holds(Pull(2));
        _github.Hold = new TaskCompletionSource();
        var module = Module();

        var first = module.ReadAsync([Ref(2)], Minute, TestContext.Current.CancellationToken);
        _clock.Advance(TimeSpan.FromSeconds(10));
        _github.Hold = null;
        var second = await module.ReadAsync([Ref(2)], new PullRequestReadOptions(TimeSpan.FromSeconds(5)), TestContext.Current.CancellationToken);

        Assert.Equal(2, Assert.Single(second.PullRequests).Number);
        Assert.Equal(2, _github.Calls.Count);
        Assert.False(first.IsCompleted);
    }

    [Fact]
    public async Task One_caller_leaving_does_not_cancel_the_read_another_is_waiting_for()
    {
        _github.Holds(Pull(2, PullRequestState.Merged));
        _github.Hold = new TaskCompletionSource();
        var module = Module();
        using var leaving = new CancellationTokenSource();

        var first = module.ReadAsync([Ref(2)], Minute, leaving.Token);
        _clock.Advance(TimeSpan.FromSeconds(1));
        var second = module.ReadAsync([Ref(2)], Minute, TestContext.Current.CancellationToken);
        await leaving.CancelAsync();
        _github.Hold.SetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.Equal(2, Assert.Single((await second).PullRequests).Number);
        Assert.Single(_github.Calls);
    }

    [Fact]
    public async Task A_repository_that_refused_is_kept_nowhere_and_asked_again()
    {
        _github.Refuses(BacklogRepository, "Not found.");
        var module = Module();

        var refused = await module.ReadAsync([Ref(4)], Minute, TestContext.Current.CancellationToken);
        _github.Answers(BacklogRepository).Holds(Pull(4, PullRequestState.Closed));
        var read = await module.ReadAsync([Ref(4)], Minute, TestContext.Current.CancellationToken);

        var failure = Assert.Single(refused.Failures);
        Assert.Equal(BacklogRepository, failure.RepositoryFullName);
        Assert.Equal("Not found.", failure.Message);
        Assert.Equal(PullRequestState.Closed, Assert.Single(read.PullRequests).State);
        Assert.Equal(2, _github.Calls.Count);
    }

    /// <summary>GitHub can reopen a closed pull request, so a closed answer is reused only
    /// as long as any open one is, and asked about again once it is stale.</summary>
    [Fact]
    public async Task A_closed_one_is_asked_about_again_once_it_is_stale()
    {
        _github.Holds(Pull(5, PullRequestState.Closed));
        var module = Module();

        await module.ReadAsync([Ref(5)], Minute, TestContext.Current.CancellationToken);
        _clock.Advance(TimeSpan.FromMinutes(2));
        _github.Holds(Pull(5));
        var reopened = await module.ReadAsync([Ref(5)], Minute, TestContext.Current.CancellationToken);

        Assert.Equal(PullRequestState.Open, Assert.Single(reopened.PullRequests).State);
        Assert.Equal(2, _github.Calls.Count);
    }

    /// <summary>A repository that answered without the number, read in one batch beside
    /// another repository's merged pull request with that number, is kept as answered
    /// without — never as that merged pull request, which would be kept for good.</summary>
    [Fact]
    public async Task A_repository_that_answered_without_the_number_does_not_keep_another_repositorys_pull_request_with_it()
    {
        _github.Holds(Pull(1477, PullRequestState.Merged));
        var module = Module();

        await module.ReadAsync([Ref(1477), Ref(1477, ArchifyRepository)], Minute, TestContext.Current.CancellationToken);
        var alone = await module.ReadAsync([Ref(1477, ArchifyRepository)], Minute, TestContext.Current.CancellationToken);

        Assert.Empty(alone.PullRequests);
        Assert.Empty(alone.Failures);
        Assert.Equal([$"read {ArchifyRepository} 1477", $"read {BacklogRepository} 1477"], _github.Calls.Order());
    }

    /// <summary>Refresh asks GitHub again for everything but a merged pull request, which
    /// cannot change again.</summary>
    [Fact]
    public async Task A_fresh_read_asks_again_for_all_but_a_merged_one()
    {
        _github.Holds(Pull(1, PullRequestState.Merged), Pull(2));
        var module = Module();

        await module.ReadAsync([Ref(1), Ref(2)], Minute, TestContext.Current.CancellationToken);
        await module.ReadAsync([Ref(1), Ref(2)], PullRequestReadOptions.Fresh, TestContext.Current.CancellationToken);

        Assert.Equal([$"read {BacklogRepository} 1,2", $"read {BacklogRepository} 2"], _github.Calls);
    }

    [Fact]
    public async Task One_read_asks_github_about_at_most_twenty_five_numbers_at_once()
    {
        var module = Module();

        await module.ReadAsync([.. Enumerable.Range(1, 60).Select(number => Ref(number))], Minute, TestContext.Current.CancellationToken);

        Assert.Equal(3, _github.Calls.Count);
        Assert.All(_github.Calls, call => Assert.True(call.Split(' ')[2].Split(',').Length <= 25, call));
    }

    [Fact]
    public async Task The_answer_comes_back_in_the_order_asked()
    {
        _github.Holds(Pull(1), Pull(2), Pull(3));
        var module = Module();

        var read = await module.ReadAsync([Ref(3), Ref(1), Ref(2)], Minute, TestContext.Current.CancellationToken);

        Assert.Equal([3, 1, 2], read.PullRequests.Select(pull => pull.Number));
    }

    /// <summary>A reference differing only in the case of its repository, or padded,
    /// names the same pull request and is answered from what is kept.</summary>
    [Fact]
    public async Task A_repository_spelled_another_way_is_the_same_pull_request()
    {
        _github.Holds(Pull(7));
        var module = Module();

        await module.ReadAsync([Ref(7)], Minute, TestContext.Current.CancellationToken);
        var again = await module.ReadAsync([new PullRequestReference(" jsdotnet/backlog ", 7)], Minute, TestContext.Current.CancellationToken);

        Assert.Equal(7, Assert.Single(again.PullRequests).Number);
        Assert.Single(_github.Calls);
    }

    /// <summary>A read the transport gave up on — an HTTP timeout surfaces as a
    /// cancellation nobody asked for — is the repository's failure, never "answered
    /// without", which would be kept for good; and the next read asks again.</summary>
    [Fact]
    public async Task A_read_the_transport_gave_up_on_is_a_failure_and_is_asked_again()
    {
        _github.Holds(Pull(6));
        _github.ReadThrows = new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.");
        var module = Module();

        var gaveUp = await module.ReadAsync([Ref(6)], Minute, TestContext.Current.CancellationToken);
        _github.ReadThrows = null;
        var read = await module.ReadAsync([Ref(6)], Minute, TestContext.Current.CancellationToken);

        Assert.Empty(gaveUp.PullRequests);
        Assert.Equal(BacklogRepository, Assert.Single(gaveUp.Failures).RepositoryFullName);
        Assert.Equal(6, Assert.Single(read.PullRequests).Number);
    }

    [Fact]
    public async Task Kept_answers_what_was_read_however_old_and_reads_nothing()
    {
        _github.Holds(Pull(2));
        var module = Module();

        await module.ReadAsync([Ref(2)], Minute, TestContext.Current.CancellationToken);
        _clock.Advance(TimeSpan.FromHours(3));
        var kept = module.Kept([Ref(2), Ref(9)]);

        Assert.Equal(2, Assert.Single(kept.PullRequests).Number);
        Assert.Single(_github.Calls);
    }
}
