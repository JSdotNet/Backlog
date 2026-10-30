using Backlog.Infrastructure.GitHub;
using Backlog.Infrastructure.GitHub.RepositoryManagement;
using Backlog.Modules.RepositoryManagement.Abstractions;
using Backlog.Modules.RepositoryManagement.Abstractions.DataTransferObjects;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// Repository Management's pull-request source and registry answered from GitHub
/// (local ADR 0042): the read by number through the pinned read, at most twenty-five
/// numbers a query, with the REST read backfilling a merged answer's default branch and
/// standing in for a server that refused the pinned read; every refusal said as a
/// failure rather than thrown; and the registry's archived repositories.
/// </summary>
public sealed class GitHubPullRequestSourceTests : IDisposable
{
    private const string Repository = "JSdotNet/Backlog";

    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-pull-request-source-tests", Guid.NewGuid().ToString("n"));
    private readonly StubClient _client = new();

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task A_read_by_number_asks_github_about_at_most_twenty_five_numbers_a_query()
    {
        _client.Finds(Open(3), Open(28));
        var source = new GitHubPullRequestSource(Integration());

        var read = await source.ReadAsync(Repository, [.. Enumerable.Range(1, 30)], TestContext.Current.CancellationToken);

        Assert.Equal([25, 5], _client.PinnedReads.Select(numbers => numbers.Count));
        Assert.Equal([3, 28], read.PullRequests.Select(pull => pull.Number).Order());
        Assert.All(read.PullRequests, pull => Assert.True(pull.StatusRead));
        Assert.Null(read.Failure);
    }

    [Fact]
    public async Task A_merged_answer_without_its_default_branch_is_backfilled_over_rest()
    {
        _client.Finds(Open(7) with { IsMerged = true, MergedAt = Now.AddHours(-1) });
        _client.Rest[7] = new GitHubPullRequest(7, Url(7), "Pull request 7", GitHubItemState.Merged, Repository)
        {
            BaseRefName = "main",
            DefaultBranch = "main"
        };
        var source = new GitHubPullRequestSource(Integration());

        var read = await source.ReadAsync(Repository, [7], TestContext.Current.CancellationToken);

        var pull = Assert.Single(read.PullRequests);
        Assert.Equal(PullRequestState.Merged, pull.State);
        Assert.Equal("main", pull.DefaultBranch);
        Assert.True(pull.StatusRead);
        Assert.Equal([7], _client.RestReads);
    }

    [Fact]
    public async Task An_open_answer_is_not_read_again_over_rest()
    {
        _client.Finds(Open(7));
        var source = new GitHubPullRequestSource(Integration());

        await source.ReadAsync(Repository, [7], TestContext.Current.CancellationToken);

        Assert.Empty(_client.RestReads);
    }

    /// <summary>An older GitHub Enterprise server refuses the pinned query; each number is
    /// then read over REST, which knows its state and branches and nothing a verdict is
    /// ranked from.</summary>
    [Fact]
    public async Task A_refused_pinned_read_falls_back_to_rest_with_no_verdict_facts()
    {
        _client.RefusesPinned = "Field 'mergeStateStatus' doesn't exist on type 'PullRequest'.";
        _client.Rest[4] = new GitHubPullRequest(4, Url(4), "Pull request 4", GitHubItemState.Closed, Repository) { BaseRefName = "main" };
        _client.Rest[5] = new GitHubPullRequest(5, Url(5), "Pull request 5", GitHubItemState.Open, Repository) { BaseRefName = "main" };
        var source = new GitHubPullRequestSource(Integration());

        var read = await source.ReadAsync(Repository, [4, 5], TestContext.Current.CancellationToken);

        Assert.Equal([PullRequestState.Closed, PullRequestState.Open], read.PullRequests.OrderBy(pull => pull.Number).Select(pull => pull.State));
        Assert.All(read.PullRequests, pull => Assert.False(pull.StatusRead));
        Assert.Null(read.Failure);
    }

    [Fact]
    public async Task A_pinned_read_and_its_rest_fallback_both_refused_is_the_repositorys_failure()
    {
        _client.RefusesPinned = "Could not resolve to a Repository with the name 'JSdotNet/Backlog'.";
        var source = new GitHubPullRequestSource(Integration());

        var read = await source.ReadAsync(Repository, [4], TestContext.Current.CancellationToken);

        Assert.Empty(read.PullRequests);
        Assert.Equal(Repository, read.Failure?.RepositoryFullName);
        Assert.Contains("Could not resolve", read.Failure?.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_open_list_is_mapped_and_a_refusal_is_its_failure()
    {
        _client.Lists = [Open(1)];
        var source = new GitHubPullRequestSource(Integration());

        var listed = await source.ListOpenAsync(Repository, TestContext.Current.CancellationToken);
        _client.RefusesList = "Bad credentials.";
        var refused = await source.ListOpenAsync(Repository, TestContext.Current.CancellationToken);

        Assert.Equal(1, Assert.Single(listed.PullRequests).Number);
        Assert.Null(listed.Failure);
        Assert.Empty(refused.PullRequests);
        Assert.Equal("Bad credentials.", refused.Failure?.Message);
    }

    [Fact]
    public async Task A_refused_act_is_a_failed_result_in_github_s_words_not_a_throw()
    {
        _client.RefusesActs = "Pull Request is not mergeable";
        var source = new GitHubPullRequestSource(Integration());

        var result = await source.MergeAsync(GitHubPullRequestStatuses.Map(Open(9)), TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(PullRequestErrors.RefusedCode, result.Error.Code);
        Assert.Contains("#9", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Each_act_names_the_pull_request_by_what_its_status_read()
    {
        var source = new GitHubPullRequestSource(Integration());
        var pull = GitHubPullRequestStatuses.Map(Open(9));

        Assert.True((await source.MergeAsync(pull, TestContext.Current.CancellationToken)).IsSuccess);
        Assert.True((await source.SetAutoMergeAsync(pull, enable: true, TestContext.Current.CancellationToken)).IsSuccess);
        Assert.True((await source.SetAutoMergeAsync(pull, enable: false, TestContext.Current.CancellationToken)).IsSuccess);
        Assert.True((await source.UpdateBranchAsync(pull, TestContext.Current.CancellationToken)).IsSuccess);
        Assert.True((await source.MarkReadyForReviewAsync(pull, TestContext.Current.CancellationToken)).IsSuccess);

        Assert.Equal(
            ["merge PR_9 Squash", "auto-merge PR_9 Squash", "cancel auto-merge PR_9", $"update {Repository}#9 sha-9", "ready PR_9"],
            _client.Acts);
    }

    /// <summary>Every failed Actions run is asked, even after one is refused, and the
    /// failure says how many of the others did start again.</summary>
    [Fact]
    public async Task A_rerun_with_one_run_refused_asks_every_run_and_says_how_many_went_through()
    {
        _client.RefusesRun = 101;
        var source = new GitHubPullRequestSource(Integration());
        var pull = GitHubPullRequestStatuses.Map(Open(9) with
        {
            Checks = GitHubCheckState.Failing,
            HeadChecks =
            [
                new GitHubCheck("build", GitHubCheckState.Failing, null, null, 101),
                new GitHubCheck("test", GitHubCheckState.Failing, null, null, 102)
            ]
        });

        var result = await source.RerunFailedAsync(pull, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Contains("1 of the 2 workflow runs did start again", result.Error.Message, StringComparison.Ordinal);
        Assert.Equal([$"rerun {Repository} run 101", $"rerun {Repository} run 102"], _client.Acts);
    }

    [Fact]
    public void The_registry_reports_the_active_repositories_and_the_archived_ones_as_they_stand()
    {
        var settings = Settings();
        var registry = new GitHubRegisteredRepositories(settings);

        Assert.Null(settings.SetRepositoryArchived("archify", archived: true));

        Assert.True(registry.IsConfigured);
        Assert.Equal([Repository], registry.Active);
        Assert.True(registry.IsArchived(" jsdotnet/archify "));
        Assert.False(registry.IsArchived(Repository));
        Assert.False(registry.IsArchived("someone/elsewhere"));
    }

    private GitHubSettingsStore Settings()
    {
        Directory.CreateDirectory(_root);
        var settings = new GitHubSettingsStore(Path.Combine(_root, Guid.NewGuid().ToString("n"), "github.json"));
        Assert.Null(settings.SetRepositories([new GitHubRepositoryRef("backlog", "JSdotNet", "Backlog"), new GitHubRepositoryRef("archify", "JSdotNet", "Archify")]));
        return settings;
    }

    private GitHubIntegration Integration() =>
        new(Settings(), _client, new Backlog.Tests.StubProbe(new GitHubConnection(true, "Connected.")), time: new FakeTimeProvider(Now));

    private static string Url(int number) => $"https://github.com/{Repository}/pull/{number}";

    private static GitHubOpenPullRequest Open(int number) =>
        new(
            number,
            Url(number),
            $"Pull request {number}",
            Repository,
            $"PR_{number}",
            IsDraft: false,
            HeadRefName: $"feature-{number}",
            HeadSha: $"sha-{number}",
            BaseRefName: "main",
            AuthorLogin: "JSdotNet",
            ViewerDidAuthor: true,
            Checks: GitHubCheckState.Passing,
            AutoMergeEnabled: false,
            MergeReady: true,
            IsBehind: false,
            HasConflicts: false,
            MergeStateStatus: "CLEAN",
            PreferredMergeMethod: GitHubMergeMethod.Squash,
            UpdatedAt: Now.AddMinutes(-number));

    /// <summary>GitHub as far as the pull-request reads and acts go: what each read finds,
    /// what it refuses, and every act in order.</summary>
    private sealed class StubClient : IGitHubClient
    {
        private readonly List<GitHubOpenPullRequest> _found = [];
        private readonly List<string> _acts = [];

        public List<IReadOnlyCollection<int>> PinnedReads { get; } = [];

        public List<int> RestReads { get; } = [];

        public Dictionary<int, GitHubPullRequest> Rest { get; } = [];

        public IReadOnlyList<GitHubOpenPullRequest> Lists { get; set; } = [];

        public string? RefusesPinned { get; set; }

        public string? RefusesList { get; set; }

        public string? RefusesActs { get; set; }

        public long? RefusesRun { get; set; }

        public IReadOnlyList<string> Acts
        {
            get
            {
                lock (_acts) return [.. _acts];
            }
        }

        public void Finds(params GitHubOpenPullRequest[] pulls) => _found.AddRange(pulls);

        public Task<IReadOnlyList<GitHubOpenPullRequest>> ListPullRequestsAsync(GitHubRepositoryRef repository, IReadOnlyCollection<int> numbers, CancellationToken cancellationToken = default)
        {
            lock (PinnedReads) PinnedReads.Add(numbers);
            if (RefusesPinned is { } refusal) return Task.FromException<IReadOnlyList<GitHubOpenPullRequest>>(new GitHubException(refusal));

            return Task.FromResult<IReadOnlyList<GitHubOpenPullRequest>>([.. _found.Where(pull => numbers.Contains(pull.Number))]);
        }

        public Task<GitHubPullRequest> GetPullRequestAsync(GitHubRepositoryRef repository, int number, CancellationToken cancellationToken = default)
        {
            lock (RestReads) RestReads.Add(number);
            return Rest.TryGetValue(number, out var pull)
                ? Task.FromResult(pull)
                : Task.FromException<GitHubPullRequest>(new GitHubException($"Not Found: {repository.FullName}#{number}"));
        }

        public Task<IReadOnlyList<GitHubOpenPullRequest>> ListOpenPullRequestsAsync(GitHubRepositoryRef repository, CancellationToken cancellationToken = default) =>
            RefusesList is { } refusal
                ? Task.FromException<IReadOnlyList<GitHubOpenPullRequest>>(new GitHubException(refusal))
                : Task.FromResult(Lists);

        public Task MergePullRequestAsync(GitHubRepositoryRef repository, string pullRequestId, GitHubMergeMethod method, CancellationToken cancellationToken = default) =>
            Act($"merge {pullRequestId} {method}");

        public Task EnableAutoMergeAsync(GitHubRepositoryRef repository, string pullRequestId, GitHubMergeMethod method, CancellationToken cancellationToken = default) =>
            Act($"auto-merge {pullRequestId} {method}");

        public Task DisableAutoMergeAsync(GitHubRepositoryRef repository, string pullRequestId, CancellationToken cancellationToken = default) =>
            Act($"cancel auto-merge {pullRequestId}");

        public Task UpdateBranchAsync(GitHubRepositoryRef repository, int number, string? expectedHeadSha, CancellationToken cancellationToken = default) =>
            Act($"update {repository.FullName}#{number} {expectedHeadSha}");

        public Task MarkReadyForReviewAsync(GitHubRepositoryRef repository, string pullRequestId, CancellationToken cancellationToken = default) =>
            Act($"ready {pullRequestId}");

        public Task RerunFailedJobsAsync(GitHubRepositoryRef repository, long workflowRunId, CancellationToken cancellationToken = default)
        {
            lock (_acts) _acts.Add($"rerun {repository.FullName} run {workflowRunId}");
            return workflowRunId == RefusesRun
                ? Task.FromException(new GitHubException("This workflow run is already running."))
                : Task.CompletedTask;
        }

        private Task Act(string what)
        {
            lock (_acts) _acts.Add(what);
            return RefusesActs is { } refusal ? Task.FromException(new GitHubException(refusal)) : Task.CompletedTask;
        }

        public Task<GitHubIssue> CreateIssueAsync(GitHubRepositoryRef repository, string title, string? body, IEnumerable<string>? labels = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubIssueSnapshot> GetIssueAsync(GitHubRepositoryRef repository, int number, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubUploadedFile> UploadFileAsync(GitHubRepositoryRef repository, string path, string branch, byte[] content, string commitMessage, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubCommittedFile> CommitFileAsync(GitHubRepositoryRef repository, string path, byte[] content, string commitMessage, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
