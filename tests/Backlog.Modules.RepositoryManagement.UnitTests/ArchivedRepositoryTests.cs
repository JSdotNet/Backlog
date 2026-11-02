using Backlog.Modules.RepositoryManagement.Abstractions;
using Backlog.Modules.RepositoryManagement.Abstractions.DataTransferObjects;
using Backlog.Modules.RepositoryManagement.Abstractions.Services;
using Backlog.Modules.RepositoryManagement.Services;
using Backlog.Modules.RepositoryManagement.UnitTests.Fakes;
using Backlog.SharedKernel.Results;
using Microsoft.Extensions.Time.Testing;
using static Backlog.Modules.RepositoryManagement.UnitTests.Fakes.Statuses;

namespace Backlog.Modules.RepositoryManagement.UnitTests;

/// <summary>
/// An archived repository's pull requests are never read, whichever reader asks and
/// however (local ADR 0029, ADR 0042 §1, V9): the lists skip it with no failure, a read
/// by number answers what was kept before it was archived, and every command fails with
/// no call to GitHub.
/// </summary>
public sealed class ArchivedRepositoryTests
{
    private readonly FakeTimeProvider _clock = new(Noon);
    private readonly CountingPullRequestSource _github = new();
    private readonly FakeRegisteredRepositories _registry = new(BacklogRepository, ArchifyRepository);

    private IPullRequests Module() => new PullRequests(_github, _registry, new InMemorySettledStore(), _clock);

    [Fact]
    public async Task The_lists_skip_an_archived_repository_with_no_failure()
    {
        _github.Holds(Pull(1), Pull(2, repository: ArchifyRepository));
        _registry.Archive(BacklogRepository);
        var module = Module();

        var open = await module.ListOpenAsync(cancellationToken: TestContext.Current.CancellationToken);
        var named = await module.ListOpenAsync([BacklogRepository], cancellationToken: TestContext.Current.CancellationToken);
        var merged = await module.ListMergedAsync([BacklogRepository], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, Assert.Single(open.PullRequests).Number);
        Assert.Empty(open.Failures);
        Assert.Empty(named.PullRequests);
        Assert.Empty(named.Failures);
        Assert.Empty(merged.PullRequests);
        Assert.DoesNotContain(_github.Calls, call => call.Contains(BacklogRepository, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_read_by_number_answers_what_was_kept_before_the_archive_and_reads_nothing()
    {
        _github.Holds(Pull(4));
        var module = Module();
        await module.ReadAsync([Ref(4)], cancellationToken: TestContext.Current.CancellationToken);

        _registry.Archive(BacklogRepository);
        _clock.Advance(TimeSpan.FromHours(1));
        var read = await module.ReadAsync([Ref(4), Ref(5)], PullRequestReadOptions.Fresh, TestContext.Current.CancellationToken);

        Assert.Equal(4, Assert.Single(read.PullRequests).Number);
        Assert.Empty(read.Failures);
        Assert.Single(_github.Calls);
    }

    [Fact]
    public async Task Every_command_on_an_archived_repositorys_pull_request_fails_with_no_call()
    {
        _github.Holds(Pull(4));
        _registry.Archive(BacklogRepository);
        var module = Module();
        var raised = 0;
        module.Changed += _ => raised++;

        Result[] results =
        [
            await module.MergeAsync(Ref(4), TestContext.Current.CancellationToken),
            await module.SetAutoMergeAsync(Ref(4), enable: true, TestContext.Current.CancellationToken),
            await module.SetAutoMergeAsync(Ref(4), enable: false, TestContext.Current.CancellationToken),
            await module.UpdateBranchAsync(Ref(4), TestContext.Current.CancellationToken),
            await module.MarkReadyForReviewAsync(Ref(4), TestContext.Current.CancellationToken),
            await module.RerunFailedAsync(Ref(4), TestContext.Current.CancellationToken)
        ];

        Assert.All(results, result =>
        {
            Assert.True(result.IsFailure);
            Assert.Equal(PullRequestErrors.ArchivedCode, result.Error.Code);
            Assert.Equal(ErrorType.Conflict, result.Error.Type);
        });
        Assert.Empty(_github.Calls);
        Assert.Equal(0, raised);
    }
}
