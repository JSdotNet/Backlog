using Backlog.Modules.Dashboard.Abstractions.Insights;

namespace Backlog.Modules.Dashboard.Abstractions.Services;

/// <summary>
/// One merged pull request of the person's own, reduced to what productivity and
/// churn are worked out from.
/// <para>
/// <see cref="FirstReviewedAt"/> is null for a pull request nobody reviewed, which
/// is a different fact from one reviewed and never churned: the first excludes it
/// from the churn rate's denominator, the second puts it in as a clean one.
/// </para>
/// <para>
/// <see cref="ChurnComplete"/> is false when the adapter stopped inspecting
/// commits before it ran out of them, so <see cref="FilesRetouched"/> is a floor.
/// </para>
/// </summary>
public sealed record ActivityPullRequest(
    string RepositoryAlias,
    int Number,
    DateTimeOffset MergedAt,
    DateTimeOffset? FirstReviewedAt,
    int ReviewRounds,
    int CommitsAfterFirstReview,
    int ForcePushesAfterFirstReview,
    int FilesRetouched,
    bool ChurnComplete)
{
    /// <summary>How long the first review took to arrive. Null when there was none.</summary>
    public TimeSpan? ReviewTurnaround { get; init; }

    /// <summary>True when anything happened after the first review — the
    /// definition of rework this dashboard uses.</summary>
    public bool HasChurn => CommitsAfterFirstReview > 0 || ForcePushesAfterFirstReview > 0;
}

/// <summary>One closed issue of the person's own.</summary>
public sealed record ActivityIssue(string RepositoryAlias, int Number, DateTimeOffset ClosedAt);

/// <summary>Everything one window's activity fetch produced.</summary>
public sealed record ActivityReport(
    IReadOnlyList<ActivityPullRequest> PullRequests,
    IReadOnlyList<ActivityIssue> Issues)
{
    public static ActivityReport Empty { get; } = new([], []);
}

/// <summary>
/// PORT — the person's own merged pull requests and closed issues, with the
/// review and commit detail post-review churn is counted from.
/// <para>
/// Whose activity is not a parameter: the source resolves the signed-in identity
/// itself, because the dashboard is a personal view and letting a caller pass a
/// login would make it a reporting tool on other people.
/// </para>
/// </summary>
public interface IActivitySource
{
    /// <summary>Whether this source can answer, and why not when it cannot.</summary>
    Task<InsightAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Activity in the given repositories between the two instants. An empty
    /// repository list means every configured repository.
    /// </summary>
    Task<ActivityReport> GetActivityAsync(
        IReadOnlyList<DashboardRepository> repositories,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
