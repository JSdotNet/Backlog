using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;

namespace Backlog.Modules.Dashboard.Services;

/// <summary>
/// What full marks is worth per week, for the three inputs counted in whole items.
/// </summary>
/// <remarks>
/// <para>
/// Rates rather than totals, so one baseline serves both windows and the trend's
/// single week without anybody rescaling it, and so the growth headroom is applied
/// in exactly one place rather than once per caller.
/// </para>
/// <para>
/// Every rate here is the reader's own best recent block divided by the weeks in
/// it. A zero is the honest answer for a measure with no history behind it, and it
/// makes the input drop out rather than score; there is deliberately no constant to
/// fall back to, because a fallback that reads like a primary-path result is
/// exactly what the fixed targets it replaced were.
/// </para>
/// </remarks>
internal sealed record ProductivityTargets(
    decimal MergedPerWeek,
    decimal ClosedPerWeek,
    decimal SessionsPerWeek)
{
    /// <summary>No history at all. Every volume input drops out; the proportions
    /// still score, so the card stays readable.</summary>
    internal static ProductivityTargets None { get; } = new(0m, 0m, 0m);
}

/// <summary>
/// How the productivity score is worked out, and what it is made of.
/// </summary>
/// <remarks>
/// <para>
/// The formula is the same weighted, weight-normalised mean the metrics library's
/// <c>MetricScoring.Score</c> applies, and it is restated here rather than called
/// because this module may not reference a UI library. That duplication is a real
/// risk — two formulas that drift produce a card and a chart that disagree by a
/// rounding step, which is exactly how a dashboard loses its reader — so a unit
/// test asserts the two agree over the same inputs rather than trusting them to.
/// </para>
/// <para>
/// Weights are normalised by their total rather than assumed to add to one, so
/// adding an eighth input does not mean rebalancing the other seven, and setting
/// every weight to 1 gives a plain average.
/// </para>
/// <para>
/// <strong>The inputs come in two shapes, and only one of them ever needed a
/// target.</strong> The four middle inputs — review promptness, freedom from churn,
/// and the two size measures — are PROPORTIONS of an eligible set: full marks is
/// however many pull requests could have counted, so they move with the window's own
/// volume and can never pin dishonestly. They were never the broken ones. The three
/// volume inputs — merged, closed, sessions — are counts, and a count needs
/// something to be counted against; that something is the reader's own record rather
/// than a number somebody picked.
/// </para>
/// </remarks>
internal static class ProductivityScoring
{
    /// <summary>The scale every score is on. Not a percentage of anything — 0 is
    /// "none of the inputs moved" and 100 is "every input at full marks".</summary>
    internal const decimal MaxScore = 100m;

    /// <summary>
    /// How far above the reader's own record full marks sits.
    /// </summary>
    /// <remarks>
    /// This is the whole reason a personal target does not pin the way the fixed one
    /// did. At exactly your record you read 0.8, not 1.0, so there is somewhere left
    /// to go; beat the record and the reading moves up without ever arriving at a bar
    /// that cannot be cleared. A bar set at the record itself would be a new costume
    /// for the same defect — everybody at their best would read full marks, and an
    /// input everybody maxes has stopped saying anything.
    /// </remarks>
    private const decimal GrowthHeadroom = 1.25m;

    /// <summary>Where a change stops being one somebody can hold in their head. In
    /// the label as well as here, because that is what makes it arguable on screen —
    /// the same reason every other number in this file reaches the card.</summary>
    private const int SmallChangedLines = 400;

    /// <summary>And how many files that same change touches.</summary>
    private const int FewChangedFiles = 10;

    /// <summary>How quickly a first review has to arrive to count as prompt.</summary>
    private static readonly TimeSpan PromptReview = TimeSpan.FromDays(1);

    internal static decimal Score(IReadOnlyList<ProductivityScoreInput>? inputs)
    {
        if (inputs is null || inputs.Count == 0) return 0m;

        var totalWeight = inputs.Sum(input => input.Weight);

        if (totalWeight <= 0m) return 0m;

        var weighted = inputs.Sum(input => input.Normalized * input.Weight);

        return Math.Round(weighted / totalWeight * MaxScore, 1, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// The inputs behind a score for one window of activity: up to seven of them,
    /// weighted 3-2-2-1-1-1-1.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An input whose full marks works out to zero is left out rather than scored
    /// as zero. A quarter with no reviewed pull request has nothing to say about
    /// review promptness, and scoring that silence as a nil would drag the whole
    /// figure down for an absence of evidence. Leaving it out is safe precisely
    /// because the weights are normalised by their total — and it is also what
    /// happens to a volume input with no history to target it against, which is why
    /// no constant is needed for that case.
    /// </para>
    /// <para>
    /// <paramref name="sessions"/> is nullable rather than defaulted to zero, and
    /// the difference matters on screen. Absent means the input is not scored at
    /// all, which is what a repository focus does to it: no assistant records which
    /// repository a session was for, so scoring one against a single repository
    /// would claim a whole-machine figure belongs to it. Zero would mean the reader
    /// ran no sessions, which is a different claim entirely.
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<ProductivityScoreInput> InputsFor(
        IReadOnlyList<ActivityPullRequest> pullRequests,
        IReadOnlyList<ActivityIssue> issues,
        int weeks,
        ProductivityTargets? targets = null,
        int? sessions = null)
    {
        ArgumentNullException.ThrowIfNull(pullRequests);
        ArgumentNullException.ThrowIfNull(issues);

        var against = targets ?? ProductivityTargets.None;

        var reviewed = pullRequests.Where(pr => pr.FirstReviewedAt is not null).ToList();
        var timed = reviewed.Where(pr => pr.ReviewTurnaround is not null).ToList();

        // Only the pull requests whose size was actually read. One whose diff could
        // not be fetched carries a zero that is not a small change, so it belongs to
        // neither the numerator nor the denominator: the input is a proportion of
        // what could be read, and the card shows that denominator.
        var sized = pullRequests.Where(pr => pr.SizeKnown).ToList();

        var candidates = new List<ProductivityScoreInput>
        {
            new(
                "Pull requests merged",
                pullRequests.Count,
                FullMarks(against.MergedPerWeek, weeks),
                3m),
            new(
                "Issues closed",
                issues.Count,
                FullMarks(against.ClosedPerWeek, weeks),
                2m),
            new(
                "First review within a day",
                timed.Count(pr => pr.ReviewTurnaround <= PromptReview),
                timed.Count,
                2m),
            new(
                "Merged without post-review churn",
                reviewed.Count(pr => !pr.HasChurn),
                reviewed.Count,
                1m),
            new(
                $"Merged under {SmallChangedLines} changed lines",
                sized.Count(pr => pr.ChangedLines <= SmallChangedLines),
                sized.Count,
                1m),
            new(
                $"Merged touching {FewChangedFiles} files or fewer",
                sized.Count(pr => pr.ChangedFiles <= FewChangedFiles),
                sized.Count,
                1m)
        };

        // Last and lightest. A session counts effort rather than output, so it is
        // worth one weight point against throughput's three.
        if (sessions is { } count)
        {
            candidates.Add(new ProductivityScoreInput(
                "Assistant sessions",
                count,
                FullMarks(against.SessionsPerWeek, weeks),
                1m));
        }

        return [.. candidates.Where(input => input.Max > 0m)];
    }

    /// <summary>
    /// One volume input's full marks: the reader's own best rate, over this window,
    /// with room to grow. Zero rate in, zero out — and an input whose full marks is
    /// zero is dropped rather than scored, which is how "no history" reaches the
    /// screen as an absent input instead of as a nil.
    /// </summary>
    internal static decimal FullMarks(decimal ratePerWeek, int weeks) =>
        ratePerWeek <= 0m ? 0m : ratePerWeek * weeks * GrowthHeadroom;
}
