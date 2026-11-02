namespace Backlog.Modules.RepositoryManagement.Abstractions.DataTransferObjects;

/// <summary>
/// What a command changed, said once its re-read has landed (local ADR 0042 §3): the
/// repositories read again, by <c>owner/name</c>, and the pull requests acted on. A
/// reader that draws any of them reads them again, from the cache that re-read filled.
/// </summary>
public sealed record PullRequestsChanged(
    IReadOnlyCollection<string> Repositories,
    IReadOnlyCollection<PullRequestReference> PullRequests);
