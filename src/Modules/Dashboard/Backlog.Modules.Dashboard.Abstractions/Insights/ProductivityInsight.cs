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

    /// <summary>
    /// How many commits a merged pull request carried, at the median — or null when no
    /// pull request's detail could be read.
    /// <para>
    /// The median rather than the mean, on <see cref="MedianReviewTurnaround"/>'s
    /// reasoning: one long-lived branch squashed late would drag a mean past anything
    /// the other pull requests look like. Over <see cref="PullRequestsWithCommitCount"/>
    /// rather than over every merge, because the count comes off the same detail call
    /// as the size and a pull request whose detail was not read has no commit count
    /// rather than a count of zero.
    /// </para>
    /// </summary>
    public int? MedianCommitsPerPullRequest { get; init; }

    /// <summary>How many merged pull requests the median above was taken over.</summary>
    public int PullRequestsWithCommitCount { get; init; }

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

    /// <summary>
    /// Review verdicts submitted across the reviewed pull requests — approvals and
    /// change requests, never bare comments. Summed rather than averaged, on
    /// <see cref="CommitsAfterFirstReview"/>'s precedent: the tile beside it says how
    /// many pull requests were reviewed, and a reader can divide.
    /// </summary>
    public int ReviewRounds { get; init; }

    /// <summary>How many of those verdicts asked for changes. The one review outcome
    /// that means rework was requested rather than merely happened.</summary>
    public int ChangesRequested { get; init; }

    /// <summary>
    /// Pull requests whose branch was synced with its base at least once — the
    /// denominator of the conflict figures, on the same principle as
    /// <see cref="PullRequestsReviewed"/>: a branch that never synced cannot have
    /// hit a conflict syncing, and counting it as clean would let a window of
    /// short-lived branches read as a window of clean merges.
    /// </summary>
    public int PullRequestsSynced { get; init; }

    /// <summary>Of those, how many resolved a conflict in at least one sync.</summary>
    public int PullRequestsWithConflictedSync { get; init; }

    /// <summary>Every sync merge across the synced pull requests.</summary>
    public int SyncMerges { get; init; }

    /// <summary>How many of those syncs resolved a conflict. A floor: a merge
    /// whose message is silent reads as clean.</summary>
    public int ConflictedSyncMerges { get; init; }

    public static ReworkInsight Empty { get; } = new(0, 0, 0, 0, 0, true, [], []);

    /// <summary>Churned pull requests as a fraction of those that were reviewed
    /// at all. Zero when nothing was reviewed — a rate over no reviews is not
    /// zero churn, but it is also not a number worth inventing.</summary>
    public decimal Rate => PullRequestsReviewed == 0 ? 0m : (decimal)PullRequestsWithChurn / PullRequestsReviewed;
}

/// <summary>
/// The volume score per repository per week, plus which repository is in focus.
/// <para>
/// Narrowed by the repository filter like every other part: with nothing in
/// focus every repository that reported anything is here, with a focus only
/// those repositories are. Every series is scored against the same estate-wide
/// target whichever way it was narrowed, so a repository reads the same alone as
/// it does beside the pack.
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

/// <summary>One of the two scores, and what it is made of.</summary>
public sealed record ProductivityScore(decimal Value, IReadOnlyList<ProductivityScoreInput> Inputs)
{
    public static ProductivityScore Empty { get; } = new(0m, []);
}

/// <summary>
/// The two scores, kept apart all the way to the screen.
/// <para>
/// Two rather than one, and that is the decision this record carries. The volume
/// inputs are counts read against the reader's own record — a bar that moves with
/// them, so a quiet fortnight reads low and a record month reads high. The quality
/// inputs are proportions of the pull requests that could have counted — a bar that
/// never moves. One number over both could fall because less shipped or because
/// what shipped came back from review, and a reader could not tell which; that was
/// the whole of what made the single figure unreadable. Assistant sessions are in
/// neither: they count effort rather than output, and a score that rose with the
/// hours an assistant ran was measuring the wrong thing.
/// </para>
/// </summary>
/// <param name="Volume">Merged pull requests and closed issues against the reader's
/// own best block. Its inputs are absent when there is no history to set a bar from.</param>
/// <param name="Quality">Review promptness, freedom from churn and the two size
/// measures, each a proportion of what could have counted.</param>
public sealed record ProductivityScoreInsight(ProductivityScore Volume, ProductivityScore Quality)
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

    /// <summary>Whether either score has anything behind it. A window nothing could
    /// read is an absence; a zero over real inputs is a reading.</summary>
    public bool HasInputs => Volume.Inputs.Count > 0 || Quality.Inputs.Count > 0;

    public static ProductivityScoreInsight Empty { get; } = new(ProductivityScore.Empty, ProductivityScore.Empty);
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
