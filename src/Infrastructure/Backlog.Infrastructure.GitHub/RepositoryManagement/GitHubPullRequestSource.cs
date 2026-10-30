using Backlog.Modules.RepositoryManagement.Abstractions;
using Backlog.Modules.RepositoryManagement.Abstractions.DataTransferObjects;
using Backlog.Modules.RepositoryManagement.Abstractions.Services;
using Backlog.SharedKernel.Results;

namespace Backlog.Infrastructure.GitHub.RepositoryManagement;

/// <summary>
/// Repository Management's pull-request source, answered by GitHub through
/// <see cref="GitHubIntegration"/> (local ADR 0042): the open list, the read by number,
/// the recently merged list and the acts, one repository per call, each record mapped
/// to the module's status by <see cref="GitHubPullRequestStatuses"/>.
/// <para>
/// The read by number is the pinned read, because only it carries what the verdict
/// and the acts need — the node id, the head, the merge state, auto-merge. It is asked
/// at most <see cref="ReadBatchSize"/> numbers a query, since the query names each one.
/// A merged answer it gives without the repository's default branch is read once more
/// over REST for it, because a task's auto-Done tells a merge into a stacked parent's
/// branch by it. A server that refuses the pinned query — an older GitHub Enterprise —
/// has each number read over REST instead: its state and branches, with no verdict
/// facts.
/// </para>
/// <para>
/// Nothing here throws for GitHub's sake: a refused read is the read's failure, and a
/// refused act a failed result in the words <see cref="GitHubIntegration"/> chose.
/// Cancellation still throws.
/// </para>
/// </summary>
public sealed class GitHubPullRequestSource(GitHubIntegration gitHub) : IPullRequestSource
{
    /// <summary>The most numbers one pinned query names.</summary>
    public const int ReadBatchSize = GitHubClient.LinkedStatusBatchSize;

    public async Task<PullRequestSourceRead> ListOpenAsync(string repositoryFullName, CancellationToken cancellationToken)
    {
        if (RepositoryOf(repositoryFullName) is not { } repository) return NotAPair(repositoryFullName);

        var listing = GitHubPullRequestStatuses.Map(
            await gitHub.ListOpenPullRequestsAsync([repository], cancellationToken).ConfigureAwait(false));
        return new PullRequestSourceRead(listing.PullRequests, listing.Failures.FirstOrDefault());
    }

    public async Task<PullRequestSourceRead> ReadAsync(string repositoryFullName, IReadOnlyCollection<int> numbers, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(numbers);
        if (RepositoryOf(repositoryFullName) is null) return NotAPair(repositoryFullName);

        var asked = numbers.Where(number => number > 0).Distinct().ToList();
        var pulls = new List<PullRequestStatus>();
        RepositoryReadFailure? failure = null;

        foreach (var batch in asked.Chunk(ReadBatchSize))
        {
            var listing = GitHubPullRequestStatuses.Map(await gitHub.ListPinnedPullRequestsAsync(
                [.. batch.Select(number => new PullRequestPin(repositoryFullName, number))],
                cancellationToken).ConfigureAwait(false));
            pulls.AddRange(listing.PullRequests);

            if (listing.Failures.FirstOrDefault() is not { } refused) continue;

            foreach (var number in batch)
            {
                if (await ReadOverRestAsync(repositoryFullName, number, cancellationToken).ConfigureAwait(false) is { } rest) pulls.Add(rest);
                else failure ??= refused;
            }
        }

        for (var index = 0; index < pulls.Count; index++)
        {
            var pull = pulls[index];
            if (pull is not { State: PullRequestState.Merged, DefaultBranch: null, StatusRead: true }) continue;

            if (await ReadOverRestAsync(repositoryFullName, pull.Number, cancellationToken).ConfigureAwait(false) is { } rest)
            {
                pulls[index] = pull with
                {
                    DefaultBranch = rest.DefaultBranch,
                    BaseRefName = string.IsNullOrEmpty(pull.BaseRefName) ? rest.BaseRefName : pull.BaseRefName
                };
            }
        }

        return new PullRequestSourceRead(pulls, failure);
    }

    public async Task<MergedPullRequestSourceRead> ListMergedAsync(string repositoryFullName, CancellationToken cancellationToken)
    {
        if (RepositoryOf(repositoryFullName) is not { } repository)
        {
            return new MergedPullRequestSourceRead([], NotAPair(repositoryFullName).Failure, false);
        }

        var listing = GitHubPullRequestStatuses.Map(
            await gitHub.ListMergedPullRequestsAsync([repository], cancellationToken).ConfigureAwait(false));
        return new MergedPullRequestSourceRead(listing.PullRequests, listing.Failures.FirstOrDefault(), listing.Truncated.Count > 0);
    }

    public Task<Result> MergeAsync(PullRequestStatus pullRequest, CancellationToken cancellationToken) =>
        ActAsync(() => gitHub.MergePullRequestAsync(GitHubPullRequestStatuses.ToGitHub(pullRequest).ToStatus(), cancellationToken));

    public Task<Result> SetAutoMergeAsync(PullRequestStatus pullRequest, bool enable, CancellationToken cancellationToken) =>
        ActAsync(() =>
        {
            var status = GitHubPullRequestStatuses.ToGitHub(pullRequest).ToStatus();
            return enable
                ? gitHub.EnableAutoMergeAsync(status, cancellationToken)
                : gitHub.DisableAutoMergeAsync(status, cancellationToken);
        });

    public Task<Result> UpdateBranchAsync(PullRequestStatus pullRequest, CancellationToken cancellationToken) =>
        ActAsync(() => gitHub.UpdateBranchAsync(GitHubPullRequestStatuses.ToGitHub(pullRequest), cancellationToken));

    public Task<Result> MarkReadyForReviewAsync(PullRequestStatus pullRequest, CancellationToken cancellationToken) =>
        ActAsync(() => gitHub.MarkReadyForReviewAsync(GitHubPullRequestStatuses.ToGitHub(pullRequest), cancellationToken));

    public Task<Result> RerunFailedAsync(PullRequestStatus pullRequest, CancellationToken cancellationToken) =>
        ActAsync(() => gitHub.RerunFailedChecksAsync(GitHubPullRequestStatuses.ToGitHub(pullRequest), cancellationToken));

    /// <summary>One act, its refusal said as a failed result in the words it came in.</summary>
    private static async Task<Result> ActAsync(Func<Task> act)
    {
        try
        {
            await act().ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception exception) when (exception is GitHubException or GitHubNotConfiguredException or HttpRequestException)
        {
            return Result.Failure(PullRequestErrors.Refused(exception.Message));
        }
    }

    /// <summary>One pull request over REST — state and branches — or null where GitHub
    /// refused it.</summary>
    private async Task<PullRequestStatus?> ReadOverRestAsync(string repositoryFullName, int number, CancellationToken cancellationToken)
    {
        try
        {
            var status = GitHubPullRequestStatuses.Map(
                await gitHub.ReadPullRequestAsync(repositoryFullName, number, cancellationToken).ConfigureAwait(false));

            // The REST answer may not name its repository; it is the one it was asked of.
            return string.IsNullOrEmpty(status.RepositoryFullName) ? status with { RepositoryFullName = repositoryFullName } : status;
        }
        catch (Exception exception) when (exception is GitHubException or GitHubNotConfiguredException or HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>The repository an <c>owner/name</c> names — configured or not, the way
    /// <see cref="GitHubIntegration"/> resolves a link — or null for a name that is no
    /// such pair.</summary>
    private GitHubRepositoryRef? RepositoryOf(string repositoryFullName)
    {
        var parts = (repositoryFullName ?? "").Trim().Split('/', 2);
        return parts.Length == 2 && parts[0].Length > 0 && parts[1].Length > 0 ? gitHub.RepositoryFor(parts[0], parts[1]) : null;
    }

    private static PullRequestSourceRead NotAPair(string repositoryFullName) =>
        new([], new RepositoryReadFailure(repositoryFullName, $"'{repositoryFullName}' is not an owner/repo pair."));
}
