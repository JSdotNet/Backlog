using Backlog.Modules.Repositories.Abstractions.DataTransferObjects;
using Backlog.Modules.Repositories.Abstractions.Services;
using Backlog.Modules.Repositories.Services;
using Backlog.SharedKernel.Handlers;

namespace Backlog.Modules.Repositories.Features.ListMergedPullRequests;

/// <summary>The recently merged pull requests of these repositories, by
/// <c>owner/name</c>, or of every active one when null, no older than
/// <paramref name="MaxAge"/>.</summary>
internal sealed record ListMergedPullRequestsQuery(IReadOnlyCollection<string>? Repositories, TimeSpan MaxAge);

/// <summary>Lists the recently merged pull requests through the one cache.</summary>
internal sealed class ListMergedPullRequestsQueryHandler(PullRequestCache cache, IRegisteredRepositories registry)
    : IQueryHandler<ListMergedPullRequestsQuery, MergedPullRequestListing>
{
    public Task<MergedPullRequestListing> Handle(ListMergedPullRequestsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return cache.ListMergedAsync(query.Repositories ?? registry.Active, query.MaxAge, cancellationToken);
    }
}
