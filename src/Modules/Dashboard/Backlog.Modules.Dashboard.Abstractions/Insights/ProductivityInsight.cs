namespace Backlog.Modules.Dashboard.Abstractions.Insights;

/// <summary>
/// One input to the productivity score, and how much of the score it is allowed
/// to be worth.
/// <para>
/// <see cref="Max"/> is what counts as full marks for this input over the whole
/// window, so the score normalises to 0..1 before the weight applies. Without it
/// the score would be dominated by whichever input happens to be counted in the
/// largest units — lines changed would bury pull requests every time.
/// </para>
/// <para>
/// The shape matches the metrics library's score component on purpose. A score is
/// a claim rather than a measurement, and the only thing that makes it arguable is
/// showing what it is made of; the library's component renders exactly these three
/// numbers, so the module hands them over rather than handing over a bare figure
/// nobody can check.
/// </para>
/// </summary>
public sealed record ProductivityScoreInput(string Label, decimal Value, decimal Max, decimal Weight = 1m)
{
    /// <summary>This input as 0..1, clamped: an input past full marks does not
    /// earn extra, or one runaway fortnight would carry the quarter.</summary>
    public decimal Normalized => Max <= 0m ? 0m : Math.Clamp(Value / Max, 0m, 1m);
}

/// <summary>
/// The headline counts, one per tile, with a per-week series behind each so the
/// tile can carry a sparkline.
/// <para>
/// Every figure is the person's own work in the scoped repositories over the
/// scoped window. <see cref="ReworkRate"/> is a fraction of merged pull requests,
/// not a count, because "eleven" means nothing without knowing whether eleven is
/// out of twelve or out of two hundred.
/// </para>
/// </summary>
public sealed record ProductivityHeadline(
    int PullRequestsMerged,
    int IssuesClosed,
    decimal ReworkRate,
    TimeSpan? MedianReviewTurnaround,
    IReadOnlyList<InsightPoint> PullRequestsPerWeek,
    IReadOnlyList<InsightPoint> IssuesPerWeek,
    IReadOnlyList<InsightPoint> ReworkRatePerWeek)
{
    public static ProductivityHeadline Empty { get; } = new(0, 0, 0m, null, [], [], []);
}

/// <summary>
/// What post-review churn looked like, in the three measures GitHub can actually
/// answer for.
/// <para>
/// <see cref="ChurnComplete"/> is false when the per-pull-request commit
/// inspection hit its cap, which means <see cref="FilesRetouched"/> is a floor
/// rather than a total. It is carried rather than hidden because a capped figure
/// that reads as a whole one is how a dashboard quietly stops being trusted.
/// </para>
/// </summary>
public sealed record ReworkInsight(
    int PullRequestsWithChurn,
    int PullRequestsReviewed,
    int CommitsAfterFirstReview,
    int ForcePushesAfterFirstReview,
    int FilesRetouched,
    bool ChurnComplete,
    IReadOnlyList<InsightPoint> ChurnedPullRequestsPerWeek,
    IReadOnlyList<InsightRow> ByRepository)
{
    public static ReworkInsight Empty { get; } = new(0, 0, 0, 0, 0, true, [], []);

    /// <summary>Churned pull requests as a fraction of those that were reviewed
    /// at all. Zero when nothing was reviewed — a rate over no reviews is not
    /// zero churn, but it is also not a number worth inventing.</summary>
    public decimal Rate => PullRequestsReviewed == 0 ? 0m : (decimal)PullRequestsWithChurn / PullRequestsReviewed;
}

/// <summary>
/// The productivity score per repository per week, plus which repository is in
/// focus.
/// <para>
/// Every repository is always present, even when one is in focus. The comparison
/// components in the metrics library are built to show one series against the
/// pack, and dropping the pack when somebody zooms in would remove the only thing
/// that makes the focused line mean anything.
/// </para>
/// </summary>
public sealed record ProductivityTrend(IReadOnlyList<InsightSeries> ByRepository, string? Highlight)
{
    public static ProductivityTrend Empty { get; } = new([], null);
}

/// <summary>The score, and what it is made of.</summary>
public sealed record ProductivityScoreInsight(decimal Value, IReadOnlyList<ProductivityScoreInput> Inputs)
{
    public static ProductivityScoreInsight Empty { get; } = new(0m, []);
}

/// <summary>
/// A part's answer: the figures, or the reason there are none.
/// <para>
/// One generic wrapper rather than an availability field on each DTO, so a part
/// that cannot render has exactly one shape to check and the reason cannot go
/// missing from one of the four.
/// </para>
/// </summary>
public sealed record InsightResult<T>(T? Value, InsightAvailability Availability)
{
    public static InsightResult<T> Ready(T value) => new(value, InsightAvailability.Available);

    public static InsightResult<T> Unavailable(string reason) => new(default, InsightAvailability.Unavailable(reason));

    public bool HasValue => Availability.IsAvailable && Value is not null;
}
