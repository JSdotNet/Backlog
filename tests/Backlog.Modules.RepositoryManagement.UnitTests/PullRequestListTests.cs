using Backlog.Modules.RepositoryManagement.Abstractions;
using Backlog.Modules.RepositoryManagement.Abstractions.DataTransferObjects;
using Backlog.Modules.RepositoryManagement.Abstractions.Services;
using Backlog.Modules.RepositoryManagement.Services;
using Backlog.Modules.RepositoryManagement.UnitTests.Fakes;
using Microsoft.Extensions.Time.Testing;
using static Backlog.Modules.RepositoryManagement.UnitTests.Fakes.Statuses;

namespace Backlog.Modules.RepositoryManagement.UnitTests;

/// <summary>
/// The open and recently merged lists of <see cref="IPullRequests"/> (local ADR 0042
/// §3): through the same cache as the reads by number, so a pull request the pane
/// listed is not read again by the next reader who asks for it by number (V3).
/// </summary>
public sealed class PullRequestListTests
{
    private readonly FakeTimeProvider _clock = new(Noon);
    private readonly CountingPullRequestSource _github = new();

    private IPullRequests Module() =>
        new PullRequests(_github, new FakeRegisteredRepositories(BacklogRepository, ArchifyRepository), new InMemorySettledStore(), _clock);

    [Fact]
    public async Task An_open_row_answers_a_read_of_its_number_with_no_second_call()
    {
        _github.Holds(Pull(1), Pull(2));
        var module = Module();

        await module.ListOpenAsync([BacklogRepository], cancellationToken: TestContext.Current.CancellationToken);
        _clock.Advance(TimeSpan.FromSeconds(30));
        var read = await module.ReadAsync([Ref(2)], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, Assert.Single(read.PullRequests).Number);
        Assert.Equal([$"list {BacklogRepository}"], _github.Calls);
    }

    /// <summary>The module decides which repositories are listed (local ADR 0042 §1): a
    /// name no one registered — a session's pull request upstream, a pin in a repository
    /// since dropped — is not listed, however a caller names it, and is no failure. Its
    /// pull requests are still read by number.</summary>
    [Fact]
    public async Task A_repository_nobody_registered_is_not_listed_by_name()
    {
        _github.Holds(Pull(1), Pull(7, repository: "octo/upstream"));
        var module = Module();

        var listing = await module.ListOpenAsync(["octo/upstream", " jsdotnet/backlog "], cancellationToken: TestContext.Current.CancellationToken);
        var read = await module.ReadAsync([Ref(7, "octo/upstream")], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal([1], listing.PullRequests.Select(pull => pull.Number));
        Assert.Empty(listing.Failures);
        Assert.Equal(7, Assert.Single(read.PullRequests).Number);
        Assert.Equal([$"list {BacklogRepository}", "read octo/upstream 7"], _github.Calls);
    }

    [Fact]
    public async Task Every_active_repository_is_listed_with_the_newest_update_first()
    {
        _github.Holds(Pull(5), Pull(1, repository: ArchifyRepository));
        var module = Module();

        var listing = await module.ListOpenAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal([1, 5], listing.PullRequests.Select(pull => pull.Number));
        Assert.Equal([$"list {ArchifyRepository}", $"list {BacklogRepository}"], _github.Calls.Order());
    }

    [Fact]
    public async Task A_list_younger_than_the_age_asked_for_is_reused_and_a_fresh_one_is_read()
    {
        _github.Holds(Pull(1));
        var module = Module();

        await module.ListOpenAsync([BacklogRepository], cancellationToken: TestContext.Current.CancellationToken);
        await module.ListOpenAsync([BacklogRepository], cancellationToken: TestContext.Current.CancellationToken);
        await module.ListOpenAsync([BacklogRepository], PullRequestReadOptions.Fresh, TestContext.Current.CancellationToken);

        Assert.Equal(2, _github.Calls.Count);
    }

    [Fact]
    public async Task A_stacked_pull_request_is_listed_with_the_parent_it_waits_on()
    {
        _github.Holds(Pull(1, head: "bottom"), Pull(2, baseRef: "bottom"));
        var module = Module();

        var listing = await module.ListOpenAsync([BacklogRepository], cancellationToken: TestContext.Current.CancellationToken);

        var top = listing.PullRequests.Single(pull => pull.Number == 2);
        Assert.Equal(Ref(1), top.StackParent);
        Assert.Null(listing.PullRequests.Single(pull => pull.Number == 1).StackParent);
        Assert.Contains("#1", PullRequestVerdict.Of(top)!.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_listing_names_the_repositories_it_holds_whole()
    {
        _github.Holds(Pull(1), Pull(2, repository: ArchifyRepository)).Caps(ArchifyRepository);
        var module = Module();

        var listing = await module.ListOpenAsync(cancellationToken: TestContext.Current.CancellationToken);
        _github.Refuses(BacklogRepository, "Refused.");
        var refused = await module.ListOpenAsync([BacklogRepository], PullRequestReadOptions.Fresh, TestContext.Current.CancellationToken);

        Assert.Equal([BacklogRepository], listing.CompleteRepositories);
        Assert.Empty(refused.CompleteRepositories);
    }

    [Fact]
    public async Task A_repository_that_refused_is_its_own_failure_and_the_others_still_come_back()
    {
        _github.Holds(Pull(1, repository: ArchifyRepository)).Refuses(BacklogRepository, "Could not resolve to a Repository.");
        var module = Module();

        var listing = await module.ListOpenAsync(cancellationToken: TestContext.Current.CancellationToken);
        _github.Answers(BacklogRepository);
        await module.ListOpenAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, Assert.Single(listing.PullRequests).Number);
        var failure = Assert.Single(listing.Failures);
        Assert.Equal(BacklogRepository, failure.RepositoryFullName);
        Assert.Equal("Could not resolve to a Repository.", failure.Message);
        Assert.Equal(2, _github.Calls.Count(call => call == $"list {BacklogRepository}"));
    }

    [Fact]
    public async Task The_recently_merged_list_is_read_once_within_the_age_asked_for()
    {
        _github.HoldsMerged(new MergedPullRequest(BacklogRepository, 9, "u", "t", "h", "main", "octocat", true, Noon.AddHours(-2), "octocat"));
        var module = Module();

        var first = await module.ListMergedAsync([BacklogRepository], cancellationToken: TestContext.Current.CancellationToken);
        var second = await module.ListMergedAsync([BacklogRepository], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(9, Assert.Single(first.PullRequests).Number);
        Assert.Equal(9, Assert.Single(second.PullRequests).Number);
        Assert.Equal([$"merged {BacklogRepository}"], _github.Calls);
    }
}
