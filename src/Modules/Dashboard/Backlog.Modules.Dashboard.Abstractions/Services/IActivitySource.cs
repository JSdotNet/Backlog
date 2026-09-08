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

    /// <summary>Additions plus deletions. Meaningless unless <see cref="SizeKnown"/>.</summary>
    public int ChangedLines { get; init; }

    /// <summary>Files the diff touches. Meaningless unless <see cref="SizeKnown"/>.</summary>
    public int ChangedFiles { get; init; }

    /// <summary>Whether the size above was actually read. False and zero rather than
    /// absent, for the reason <see cref="ChurnComplete"/> exists: the pull request
    /// still happened, and its zero must not be averaged in as a very small one.</summary>
    public bool SizeKnown { get; init; }

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

    /// <summary>
    /// Whether every repository in the fetch reported its whole window.
    /// <para>
    /// False means at least one source stopped early — it ran out of pages it was
    /// willing to read before it ran out of pull requests — so
    /// <see cref="PullRequests"/> is a prefix and every count, rate and average
    /// built on it is a floor. It is on the report rather than per repository
    /// because a screen that mixes a complete repository with a truncated one is
    /// showing one number, and that number is the truncated one.
    /// </para>
    /// </summary>
    public bool Complete { get; init; } = true;
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
