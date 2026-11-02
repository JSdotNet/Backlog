using Backlog.Modules.RepositoryManagement.Abstractions.DataTransferObjects;
using Backlog.Modules.RepositoryManagement.Abstractions.Services;

namespace Backlog.Modules.RepositoryManagement.UnitTests.Fakes;

/// <summary>The settled-fact memory, outliving the module instance a test builds over
/// it — which is how a test restarts the app: a new module over the same store.</summary>
internal sealed class InMemorySettledStore : ISettledPullRequestStore
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, SettledPullRequest> _kept = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How many times anything was written.</summary>
    public int Writes { get; private set; }

    public IReadOnlyList<SettledPullRequest> ReadAll()
    {
        lock (_gate) return [.. _kept.Values];
    }

    public void Keep(SettledPullRequest pullRequest)
    {
        lock (_gate)
        {
            _kept[pullRequest.Reference.Key] = pullRequest;
            Writes++;
        }
    }
}
