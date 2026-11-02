using Backlog.Modules.RepositoryManagement.Abstractions.DataTransferObjects;

namespace Backlog.Modules.RepositoryManagement.Abstractions.Services;

/// <summary>
/// Where merged pull requests are remembered across a restart (local ADR 0042 §3): a
/// settled-fact cache in app storage, never in a repository (local ADR 0004, 0015).
/// <para>
/// Synchronous, and never throws: an unreadable store answers empty, so the pull
/// request is read from GitHub once more, and a failed write is dropped for the same
/// reason.
/// </para>
/// </summary>
public interface ISettledPullRequestStore
{
    /// <summary>Every merged pull request remembered.</summary>
    IReadOnlyList<SettledPullRequest> ReadAll();

    /// <summary>Remembers one merged pull request, over any earlier record of it.</summary>
    void Keep(SettledPullRequest pullRequest);
}
