using Backlog.Modules.RepositoryManagement.Abstractions.DataTransferObjects;
using Backlog.Modules.RepositoryManagement.Abstractions.Services;

namespace Backlog.Modules.RepositoryManagement.Services;

/// <summary>The settled-fact memory of a host that keeps none on disk: merged pull
/// requests are remembered for the life of the process only.</summary>
internal sealed class InMemorySettledPullRequests : ISettledPullRequestStore
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, SettledPullRequest> _kept = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<SettledPullRequest> ReadAll()
    {
        lock (_gate) return [.. _kept.Values];
    }

    public void Keep(SettledPullRequest pullRequest)
    {
        ArgumentNullException.ThrowIfNull(pullRequest);
        lock (_gate) _kept[pullRequest.Reference.Key] = pullRequest;
    }
}
