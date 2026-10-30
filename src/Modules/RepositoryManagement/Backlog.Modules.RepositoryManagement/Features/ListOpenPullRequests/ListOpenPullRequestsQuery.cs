using Backlog.Modules.RepositoryManagement.Abstractions;
using Backlog.Modules.RepositoryManagement.Abstractions.DataTransferObjects;
using Backlog.Modules.RepositoryManagement.Abstractions.Services;
using Backlog.Modules.RepositoryManagement.Services;
using Backlog.SharedKernel.Handlers;

namespace Backlog.Modules.RepositoryManagement.Features.ListOpenPullRequests;

/// <summary>The open pull requests of these repositories, by <c>owner/name</c>, or of
/// every active one when null, no older than <paramref name="MaxAge"/>. A name that is
/// not an active repository is not listed.</summary>
internal sealed record ListOpenPullRequestsQuery(IReadOnlyCollection<string>? Repositories, TimeSpan MaxAge);

/// <summary>
/// Lists the open pull requests through the one cache and places each in its stack
/// (<see cref="PullRequestStacks.WithParents"/>), so every reader of the list hands the
/// verdict the parent a stacked pull request waits on.
/// <para>
/// Which repositories are listed is the module's to decide (local ADR 0042 §1): only the
/// active ones the registry names, however a caller spells or names them. A repository
/// nobody registered — a session's pull request upstream, a pin in a repository since
/// dropped — and an archived one are left out with no failure; their pull requests are
/// still read by number.
/// </para>
/// </summary>
internal sealed class ListOpenPullRequestsQueryHandler(PullRequestCache cache, IRegisteredRepositories registry)
    : IQueryHandler<ListOpenPullRequestsQuery, PullRequestListing>
{
    public async Task<PullRequestListing> Handle(ListOpenPullRequestsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var listing = await cache.ListOpenAsync(Listed(query.Repositories), query.MaxAge, cancellationToken).ConfigureAwait(false);
        return listing with { PullRequests = PullRequestStacks.WithParents(listing.PullRequests) };
    }

    /// <summary>The active repositories, in the registry's spelling, that
    /// <paramref name="named"/> names — every one when it is null.</summary>
    private IReadOnlyCollection<string> Listed(IReadOnlyCollection<string>? named)
    {
        var active = registry.Active;
        if (named is null) return active;

        var asked = named
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return [.. active.Where(repository => asked.Contains(repository.Trim()))];
    }
}
