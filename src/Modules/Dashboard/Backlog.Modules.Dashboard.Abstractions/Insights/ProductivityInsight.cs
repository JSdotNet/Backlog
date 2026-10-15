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

    /// <summary>
    /// How many merged pull requests had a review - the population <see cref="ReworkRate"/>
    /// is a share of - or null where the producer did not say.
    /// <para>
    /// Carried because a rate of zero over none reviewed is not a figure: a surface prints
    /// "—" for it, where "0% came back" would claim clean work nobody looked at. Null
    /// rather than zero by default, so a fixture built positionally keeps reading its rate.
    /// </para>
    /// </summary>
    public int? PullRequestsReviewed { get; init; }

    /// <summary>
    /// How many pull requests were merged in the window of the same length just before
    /// this one — or null when that could not be counted.
    /// <para>
    /// Counted by the baseline source rather than by the activity read, because it is a
    /// count and nothing else: the tile compares two numbers, and walking a second
    /// window's pull requests one by one to produce the second number would spend the
    /// churn budget on detail nobody looks at.
    /// </para>
    /// <para>
    /// Null rather than zero when the baseline refused or failed, and that difference
    /// is the whole reason it is nullable. A zero would draw "up 41 against nothing" on
    /// the tile, which is a claim that the earlier window was empty; null draws no
    /// comparison, which is the honest answer to a question that went unanswered.
    /// </para>
    /// </summary>
    public int? PreviousPullRequestsMerged { get; init; }

    /// <summary>
    /// Commits on the merged pull requests, summed by the week each one merged — the
    /// Commits measure of the volume chart. A pull request whose detail was not read
    /// adds nothing, so a week holding one is a floor.
    /// </summary>
    public IReadOnlyList<InsightPoint> CommitsPerWeek { get; init; } = [];

    /// <summary>
    /// The median first-review turnaround of each week's merges, in hours, on
    /// <see cref="MedianReviewTurnaround"/>'s rule. A week with no reviewed merge has
    /// no point: there is no wait to report, and a zero would draw one nobody had.
    /// </summary>
    public IReadOnlyList<InsightPoint> ReviewTurnaroundPerWeek { get; init; } = [];

    /// <summary>
    /// The median commit count of each week's merges whose detail was read, on
    /// <see cref="MedianCommitsPerPullRequest"/>'s rule. A week with none has no point,
    /// for the reason <see cref="ReviewTurnaroundPerWeek"/> gives.
    /// </summary>
    public IReadOnlyList<InsightPoint> CommitsPerPullRequestPerWeek { get; init; } = [];

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
    IReadOnlyList<ReworkRepository> ByRepository)
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

    /// <summary>
    /// The window's sized merges cut by changed lines, XS to XL. Empty by default;
    /// ProductivityInsights always derives all five, in that order — an empty bucket is a
    /// reading, not a gap. Rework cut a new way, on
    /// <see cref="ByRepository"/>'s precedent, so it rides the same activity read.
    /// </summary>
    public IReadOnlyList<ReworkSize> BySize { get; init; } = [];

    /// <summary>
    /// Merged pull requests whose size was not read, and so sit in no bucket of
    /// <see cref="BySize"/>. Counted rather than dropped, so a part can say how many
    /// the buckets leave out instead of letting them read as the whole window.
    /// </summary>
    public int PullRequestsWithoutSize { get; init; }

    public static ReworkInsight Empty { get; } = new(0, 0, 0, 0, 0, true, []);

    /// <summary>Churned pull requests as a fraction of those that were reviewed
    /// at all. Zero when nothing was reviewed — a rate over no reviews is not
    /// zero churn, but it is also not a number worth inventing.</summary>
    public decimal Rate => PullRequestsReviewed == 0 ? 0m : (decimal)PullRequestsWithChurn / PullRequestsReviewed;
}

/// <summary>
/// One repository's share of the rework: how many of its reviewed pull requests came
/// back after review. The rows of <see cref="ReworkInsight.ByRepository"/> add up to
/// the insight's own totals, so a part can print them as the total under the ranking.
/// </summary>
/// <param name="Alias">The repository, by the alias it is configured under.</param>
/// <param name="PullRequestsWithChurn">Its reviewed pull requests that came back.</param>
/// <param name="PullRequestsReviewed">Its pull requests that were reviewed at all.</param>
public sealed record ReworkRepository(string Alias, int PullRequestsWithChurn, int PullRequestsReviewed);

/// <summary>
/// One size bucket of <see cref="ReworkInsight.BySize"/>: how many merged pull requests
/// changed this many lines, and how many of the reviewed ones came back after review.
/// </summary>
/// <param name="Label">The bucket's name, XS to XL.</param>
/// <param name="MinLines">The fewest changed lines a pull request in it has.</param>
/// <param name="MaxLinesExclusive">The line count the next bucket starts at, or null for
/// the last bucket, which has no ceiling.</param>
/// <param name="PullRequestsMerged">Every sized merge in the bucket, reviewed or not.</param>
/// <param name="PullRequestsReviewed">Of those, the ones that were reviewed at all — the
/// denominator of <see cref="Rate"/>, as it is of <see cref="ReworkInsight.Rate"/>.</param>
/// <param name="PullRequestsWithChurn">Of the reviewed ones, those that came back.</param>
public sealed record ReworkSize(
    string Label,
    int MinLines,
    int? MaxLinesExclusive,
    int PullRequestsMerged,
    int PullRequestsReviewed,
    int PullRequestsWithChurn)
{
    /// <summary>The share of the reviewed pull requests that came back, or null when
    /// none was reviewed. Null rather than <see cref="ReworkInsight.Rate"/>'s zero,
    /// because a bucket shows "—" for no share and "0%" would claim clean work nobody
    /// looked at.</summary>
    public decimal? Rate => PullRequestsReviewed == 0 ? null : (decimal)PullRequestsWithChurn / PullRequestsReviewed;
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
