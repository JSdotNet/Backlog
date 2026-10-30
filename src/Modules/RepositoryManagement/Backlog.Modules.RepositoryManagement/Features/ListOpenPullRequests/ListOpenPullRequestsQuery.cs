using Backlog.Modules.RepositoryManagement.Abstractions;
using Backlog.Modules.RepositoryManagement.Abstractions.DataTransferObjects;
using Backlog.Modules.RepositoryManagement.Abstractions.Services;
using Backlog.Modules.RepositoryManagement.Services;
using Backlog.SharedKernel.Handlers;

namespace Backlog.Modules.RepositoryManagement.Features.ListOpenPullRequests;

/// <summary>The open pull requests of these repositories, by <c>owner/name</c>, or of
/// every active one when null, no older than <paramref name="MaxAge"/>.</summary>
internal sealed record ListOpenPullRequestsQuery(IReadOnlyCollection<string>? Repositories, TimeSpan MaxAge);

/// <summary>
/// Lists the open pull requests through the one cache and places each in its stack
/// (<see cref="PullRequestStacks.WithParents"/>), so every reader of the list hands the
/// verdict the parent a stacked pull request waits on.
/// </summary>
internal sealed class ListOpenPullRequestsQueryHandler(PullRequestCache cache, IRegisteredRepositories registry)
    : IQueryHandler<ListOpenPullRequestsQuery, PullRequestListing>
{
    public async Task<PullRequestListing> Handle(ListOpenPullRequestsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var listing = await cache.ListOpenAsync(query.Repositories ?? registry.Active, query.MaxAge, cancellationToken).ConfigureAwait(false);
        return listing with { PullRequests = PullRequestStacks.WithParents(listing.PullRequests) };
    }
}
