using Backlog.Infrastructure.GitHub;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// A branch catalog that reaches no network.
/// <para>
/// Shared rather than repeated per file, unlike the <c>NoGitHub</c> stubs beside
/// it: the settings screen takes this to fill its devbook-source selector, so
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

    /// <summary>Heads per branch, for a test that resolves more than one — the
    /// default branch under the empty key. A branch not listed here resolves to
    /// <see cref="Head"/>.</summary>
    public Dictionary<string, GitHubBranchHead> Heads { get; init; } = new(StringComparer.Ordinal);

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
        Gates.TryGetValue(branch ?? string.Empty, out var gate)
            ? Resolve(gate.Task, branch)
            : Task.FromResult(Heads.TryGetValue(branch ?? string.Empty, out var head) ? head : Head);

    /// <summary>Branches whose head answers only once their gate is set — and
    /// then answers even if the caller gave up on it, so a test can make a stale
    /// answer land after a newer one.</summary>
    public Dictionary<string, TaskCompletionSource> Gates { get; init; } = new(StringComparer.Ordinal);

    private async Task<GitHubBranchHead?> Resolve(Task gate, string? branch)
    {
        await gate;
        return Heads.TryGetValue(branch ?? string.Empty, out var head) ? head : Head;
    }
}
