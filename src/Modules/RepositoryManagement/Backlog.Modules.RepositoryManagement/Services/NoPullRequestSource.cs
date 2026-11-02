using Backlog.Modules.RepositoryManagement.Abstractions;
using Backlog.Modules.RepositoryManagement.Abstractions.DataTransferObjects;
using Backlog.Modules.RepositoryManagement.Abstractions.Services;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.RepositoryManagement.Services;

/// <summary>The source of a host that composes no GitHub: every read answers nothing
/// and every act is not configured.</summary>
internal sealed class NoPullRequestSource : IPullRequestSource
{
    public Task<PullRequestSourceRead> ListOpenAsync(string repositoryFullName, CancellationToken cancellationToken) =>
        Task.FromResult(new PullRequestSourceRead([], null));

    public Task<PullRequestSourceRead> ReadAsync(string repositoryFullName, IReadOnlyCollection<int> numbers, CancellationToken cancellationToken) =>
        Task.FromResult(new PullRequestSourceRead([], null));

    public Task<MergedPullRequestSourceRead> ListMergedAsync(string repositoryFullName, CancellationToken cancellationToken) =>
        Task.FromResult(new MergedPullRequestSourceRead([], null, false));

    public Task<Result> MergeAsync(PullRequestStatus pullRequest, CancellationToken cancellationToken) => NotConfigured();

    public Task<Result> SetAutoMergeAsync(PullRequestStatus pullRequest, bool enable, CancellationToken cancellationToken) => NotConfigured();

    public Task<Result> UpdateBranchAsync(PullRequestStatus pullRequest, CancellationToken cancellationToken) => NotConfigured();

    public Task<Result> MarkReadyForReviewAsync(PullRequestStatus pullRequest, CancellationToken cancellationToken) => NotConfigured();

    public Task<Result> RerunFailedAsync(PullRequestStatus pullRequest, CancellationToken cancellationToken) => NotConfigured();

    private static Task<Result> NotConfigured() => Task.FromResult(Result.Failure(PullRequestErrors.NotConfigured));
}
