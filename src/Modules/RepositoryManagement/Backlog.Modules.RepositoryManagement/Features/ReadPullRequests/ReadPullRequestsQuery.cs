using Backlog.Modules.RepositoryManagement.Abstractions.DataTransferObjects;
using Backlog.Modules.RepositoryManagement.Services;
using Backlog.SharedKernel.Handlers;

namespace Backlog.Modules.RepositoryManagement.Features.ReadPullRequests;

/// <summary>The pull requests these references name, in any state, no older than
/// <paramref name="MaxAge"/> where they are not settled.</summary>
internal sealed record ReadPullRequestsQuery(IReadOnlyCollection<PullRequestReference> PullRequests, TimeSpan MaxAge);

/// <summary>Reads pull requests by number through the one cache (local ADR 0042 §3).</summary>
internal sealed class ReadPullRequestsQueryHandler(PullRequestCache cache) : IQueryHandler<ReadPullRequestsQuery, PullRequestListing>
{
    public Task<PullRequestListing> Handle(ReadPullRequestsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return cache.ReadAsync(query.PullRequests, query.MaxAge, cancellationToken);
    }
}
