using Backlog.Modules.Dashboard.Abstractions.Insights;

namespace Backlog.Modules.Dashboard.Abstractions.Services;

/// <summary>
/// PORT — the devbook sync units a sweep has spoken about in one repository, with the
/// last thing it said about each.
/// <para>
/// Nothing in this contract names a Devbook type; the adapter that answers it sees the
/// Devbook context's verdict store, nothing in this module may.
/// </para>
/// </summary>
public interface IDriftUnitSource
{
    /// <summary>False when this host keeps no sweep verdicts at all, which is a different
    /// answer from a repository no sweep has spoken about yet.</summary>
    bool IsAvailable { get; }

    /// <summary>One row per unit, the latest verdict each — never one per chapter. Empty
    /// for a repository no sweep has spoken about.</summary>
    IReadOnlyList<DriftUnit> Units(DashboardRepository repository);
}

/// <summary>What one read of the drift issues found.</summary>
/// <param name="Issues">The open <c>devbook-drift</c> issues, oldest first.</param>
/// <param name="Complete">False when a repository could not be read or search held
/// some back, so the list is a floor.</param>
/// <param name="Failure">What the first repository that could not be read failed with,
/// in words fit for the screen, or null.</param>
public sealed record DriftIssueRead(IReadOnlyList<DriftIssue> Issues, bool Complete, string? Failure = null)
{
    public static DriftIssueRead Empty { get; } = new([], true);
}

/// <summary>
/// PORT — the open <c>devbook-drift</c> issues the sweeps filed, and which of them carry
/// <c>sync-failed</c>.
/// </summary>
public interface IDriftIssueSource
{
    /// <summary>Whether the issues can be read at all, and why not.</summary>
    Task<InsightAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default);

    /// <summary>The open drift issues in <paramref name="repositories"/>. A repository
    /// that fails is skipped and the read marked incomplete; only every repository
    /// failing throws, because then nothing was read at all.</summary>
    Task<DriftIssueRead> GetOpenAsync(
        IReadOnlyList<DashboardRepository> repositories,
        CancellationToken cancellationToken = default);
}
