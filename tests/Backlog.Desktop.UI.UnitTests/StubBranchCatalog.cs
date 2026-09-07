using Backlog.Infrastructure.GitHub;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// A branch catalog that reaches no network.
/// <para>
/// Shared rather than repeated per file, unlike the <c>NoGitHub</c> stubs beside
/// it: the settings screen takes this to fill its knowledge-source selector, so
/// every harness that renders the screen needs one, and every one of them needs
/// the same thing from it — nothing. A test that is actually about the branch
/// list sets <see cref="Branches"/>.
/// </para>
/// </summary>
internal sealed class StubBranchCatalog : IGitHubBranchCatalog
{
    /// <summary>What a listing returns. Empty is the ordinary case: the screen
    /// loads branches on request, so a harness that never presses the button
    /// never asks.</summary>
    public List<string> Branches { get; init; } = [];

    /// <summary>Where a resolved branch points, or null for "there is no such
    /// branch".</summary>
    public GitHubBranchHead? Head { get; init; }

    /// <summary>When set, listing refuses the way GitHub does — the path a
    /// repository this machine cannot reach takes.</summary>
    public string? Failure { get; init; }

    public int Listings { get; private set; }

    public Task<IReadOnlyList<string>> ListBranchesAsync(
        GitHubRepositoryRef repository,
        CancellationToken cancellationToken = default)
    {
        Listings++;

        if (Failure is not null) throw new GitHubException(Failure);

        return Task.FromResult<IReadOnlyList<string>>([.. Branches]);
    }

    public Task<GitHubBranchHead?> ResolveHeadAsync(
        GitHubRepositoryRef repository,
        string? branch,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Head);
}
