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
/// A merged pull request is a settled fact (local ADR 0042 §3, V4): remembered once,
/// and read back after a restart with GitHub out of reach — the module built again over
/// the same memory and a source that fails any call.
/// </summary>
public sealed class SettledFactTests
{
    private readonly FakeTimeProvider _clock = new(Noon);
    private readonly InMemorySettledStore _settled = new();
    private readonly FakeRegisteredRepositories _registry = new(BacklogRepository);

    /// <summary>
    /// A merge read without the repository's default branch — the source's read of it
    /// was refused — is still merged, and still kept, but not for good: whether it
    /// finishes the task that recorded it turns on that branch (local ADR 0042 §3), so
    /// once it is older than the reader's age it is asked again, and the read that names
    /// the branch replaces it, in memory and in the settled facts. One that names it is
    /// final, as before.
    /// </summary>
    [Fact]
    public async Task A_merge_read_without_its_default_branch_is_asked_again_until_a_read_names_it()
    {
        var github = new CountingPullRequestSource().Holds(Pull(3, PullRequestState.Merged) with { DefaultBranch = null });
        var module = new PullRequests(github, _registry, _settled, _clock);

        var first = await module.ReadAsync([Ref(3)], cancellationToken: TestContext.Current.CancellationToken);
        _clock.Advance(TimeSpan.FromSeconds(30));
        await module.ReadAsync([Ref(3)], cancellationToken: TestContext.Current.CancellationToken);
        github.Holds(Pull(3, PullRequestState.Merged));
        _clock.Advance(TimeSpan.FromMinutes(1));
        var named = await module.ReadAsync([Ref(3)], cancellationToken: TestContext.Current.CancellationToken);
        _clock.Advance(TimeSpan.FromMinutes(5));
        await module.ReadAsync([Ref(3)], PullRequestReadOptions.Fresh, TestContext.Current.CancellationToken);

        Assert.Equal(PullRequestState.Merged, Assert.Single(first.PullRequests).State);
        Assert.Equal("main", Assert.Single(named.PullRequests).DefaultBranch);
        Assert.Equal([$"read {BacklogRepository} 3", $"read {BacklogRepository} 3"], github.Calls);
        Assert.Equal("main", Assert.Single(_settled.ReadAll()).DefaultBranch);
    }

    /// <summary>
    /// A merge no read can name the default branch of — the source's REST read of it is
    /// refused every time — is not asked about for as long as the app runs: after a few
    /// reads that could not name it, the merge is final as it stands, and no reader sends
    /// GitHub anything more for it.
    /// </summary>
    [Fact]
    public async Task A_merge_no_read_can_name_the_default_branch_of_is_final_after_a_few_reads()
    {
        var github = new CountingPullRequestSource().Holds(Pull(3, PullRequestState.Merged) with { DefaultBranch = null });
        var module = new PullRequests(github, _registry, _settled, _clock);

        for (var read = 0; read < 6; read++)
        {
            await module.ReadAsync([Ref(3)], cancellationToken: TestContext.Current.CancellationToken);
            _clock.Advance(TimeSpan.FromMinutes(2));
        }

        Assert.Equal(3, github.Calls.Count(call => call == $"read {BacklogRepository} 3"));
        Assert.Equal(PullRequestState.Merged, Assert.Single(module.Kept([Ref(3)]).PullRequests).State);
    }

    /// <summary>
    /// A merge kept without its default branch is still a merge: an open list that still
    /// carries the pull request — GitHub's list lagging the merge — never puts it back to
    /// open.
    /// </summary>
    [Fact]
    public async Task An_open_list_row_never_replaces_a_kept_merge()
    {
        var github = new CountingPullRequestSource().Holds(Pull(3, PullRequestState.Merged) with { DefaultBranch = null });
        var module = new PullRequests(github, _registry, _settled, _clock);
        await module.ReadAsync([Ref(3)], cancellationToken: TestContext.Current.CancellationToken);

        github.Holds(Pull(3));
        await module.ListOpenAsync([BacklogRepository], PullRequestReadOptions.Fresh, TestContext.Current.CancellationToken);

        Assert.Equal(PullRequestState.Merged, Assert.Single(module.Kept([Ref(3)]).PullRequests).State);
    }

    [Fact]
    public async Task A_merged_pull_request_reads_merged_after_a_restart_with_github_out_of_reach()
    {
        var github = new CountingPullRequestSource().Holds(Pull(3, PullRequestState.Merged) with
        {
            BaseRefName = "release",
            Labels = ["frontend"],
            ClosingIssues = [new IssueReference(BacklogRepository, 42)]
        });
        await new PullRequests(github, _registry, _settled, _clock).ReadAsync([Ref(3)], cancellationToken: TestContext.Current.CancellationToken);

        var restarted = new PullRequests(new UnreachableSource(), _registry, _settled, _clock);
        var read = await restarted.ReadAsync([Ref(3)], PullRequestReadOptions.Fresh, TestContext.Current.CancellationToken);
        var kept = restarted.Kept([Ref(3)]);

        var pull = Assert.Single(read.PullRequests);
        Assert.Equal(PullRequestState.Merged, pull.State);
        Assert.Equal("release", pull.BaseRefName);
        Assert.Equal("main", pull.DefaultBranch);
        Assert.Equal("Pull request 3", pull.Title);

        // What the pane matches its task and session by, and draws its branch line from.
        Assert.Equal("feature-3", pull.HeadRefName);
        Assert.Equal([new IssueReference(BacklogRepository, 42)], pull.ClosingIssues);
        Assert.Equal(["frontend"], pull.Labels);
        Assert.False(pull.StatusRead);
        Assert.Equal(PullRequestState.Merged, Assert.Single(kept.PullRequests).State);
    }

    [Fact]
    public async Task A_merged_pull_request_is_remembered_once_however_often_it_is_read()
    {
        var github = new CountingPullRequestSource().Holds(Pull(3, PullRequestState.Merged));
        var module = new PullRequests(github, _registry, _settled, _clock);

        await module.ReadAsync([Ref(3)], cancellationToken: TestContext.Current.CancellationToken);
        await module.ReadAsync([Ref(3)], PullRequestReadOptions.Fresh, TestContext.Current.CancellationToken);

        Assert.Equal(1, _settled.Writes);
        var remembered = Assert.Single(_settled.ReadAll());
        Assert.Equal(Ref(3), remembered.Reference);
        Assert.Equal(Noon.AddHours(-1), remembered.MergedAt);
    }

    [Fact]
    public async Task An_open_or_closed_pull_request_is_never_remembered()
    {
        var github = new CountingPullRequestSource().Holds(Pull(1), Pull(2, PullRequestState.Closed));
        var module = new PullRequests(github, _registry, _settled, _clock);

        await module.ReadAsync([Ref(1), Ref(2)], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(_settled.ReadAll());
    }

    /// <summary>A source that fails the test on any call: GitHub is out of reach, and
    /// the module must not need it.</summary>
    private sealed class UnreachableSource : IPullRequestSource
    {
        public Task<PullRequestSourceRead> ListOpenAsync(string repositoryFullName, CancellationToken cancellationToken) => throw Unreachable();

        public Task<PullRequestSourceRead> ReadAsync(string repositoryFullName, IReadOnlyCollection<int> numbers, CancellationToken cancellationToken) => throw Unreachable();

        public Task<MergedPullRequestSourceRead> ListMergedAsync(string repositoryFullName, CancellationToken cancellationToken) => throw Unreachable();

        public Task<Result> MergeAsync(PullRequestStatus pullRequest, CancellationToken cancellationToken) => throw Unreachable();

        public Task<Result> SetAutoMergeAsync(PullRequestStatus pullRequest, bool enable, CancellationToken cancellationToken) => throw Unreachable();

        public Task<Result> UpdateBranchAsync(PullRequestStatus pullRequest, CancellationToken cancellationToken) => throw Unreachable();

        public Task<Result> MarkReadyForReviewAsync(PullRequestStatus pullRequest, CancellationToken cancellationToken) => throw Unreachable();

        public Task<Result> RerunFailedAsync(PullRequestStatus pullRequest, CancellationToken cancellationToken) => throw Unreachable();

        private static InvalidOperationException Unreachable() => new("GitHub is out of reach.");
    }
}
