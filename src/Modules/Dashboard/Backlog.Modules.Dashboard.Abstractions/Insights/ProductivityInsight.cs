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
    /// <summary>
    /// Whether the window behind these figures was read whole.
    /// <para>
    /// False when a repository's pull request listing stopped before it ran out of
    /// pull requests, which makes every count, rate and median here a floor rather
    /// than a total. Carried on each insight rather than left on the report it came
    /// from, because the part that has to say so on screen is handed this and not
    /// that. Beside the primary constructor rather than in it, so the fixtures that
    /// build one positionally keep meaning what they meant.
    /// </para>
    /// </summary>
    public bool Complete { get; init; } = true;

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
    /// <summary>Whether the window behind these figures was read whole. A different
    /// fact from <paramref name="ChurnComplete"/>: that one is about how deeply each
    /// pull request was inspected, this one about whether every pull request in the
    /// window arrived at all.</summary>
    public bool Complete { get; init; } = true;

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
    /// <summary>Whether the window behind these series was read whole. Every point is
    /// a floor when it is false.</summary>
    public bool Complete { get; init; } = true;

    public static ProductivityTrend Empty { get; } = new([], null);
}

/// <summary>
/// Where full marks came from: the reader's own busiest block of recent history.
/// <para>
/// It exists so the target can be named on screen. A target nobody can see is
/// exactly as unarguable as the constant it replaced — "441 of 6" was indefensible
/// because 6 came from nowhere a reader could reach, and "441 of 551" would be no
/// better if 551 came from nowhere either. So the block that set the bar travels
/// with the score: how much was merged in it, and when it was.
/// </para>
/// </summary>
/// <param name="From">When the best block opened.</param>
/// <param name="To">When it closed.</param>
/// <param name="MergedPullRequests">How many pull requests were merged in it — the
/// record the reader is being read against.</param>
/// <param name="FullMarks">What that record works out to as full marks over the
/// scored window, growth headroom included.</param>
public sealed record ProductivityTarget(
    DateTimeOffset From,
    DateTimeOffset To,
    int MergedPullRequests,
    decimal FullMarks);

/// <summary>The score, and what it is made of.</summary>
public sealed record ProductivityScoreInsight(decimal Value, IReadOnlyList<ProductivityScoreInput> Inputs)
{
    /// <summary>The block full marks was derived from, or null when there is no
    /// history to derive one from — because it could not be read, or because there is
    /// none yet. Null is not a reason to invent a target; it is a reason for the
    /// volume inputs to be absent and for the part to say why.</summary>
    public ProductivityTarget? Target { get; init; }

    /// <summary>Whether the history behind <see cref="Target"/> was read whole. False
    /// makes the record a floor, and therefore the target one too — the reading is
    /// generous rather than wrong, and the part says so.</summary>
    public bool TargetComplete { get; init; } = true;

    /// <summary>Whether the scored window itself was read whole. False makes every
    /// counted input a floor.</summary>
    public bool Complete { get; init; } = true;

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
