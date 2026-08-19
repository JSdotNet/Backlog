using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;

namespace Backlog.Modules.Dashboard.Services;

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
/// adding a fifth input does not mean rebalancing the other four, and setting
/// every weight to 1 gives a plain average.
/// </para>
/// </remarks>
internal static class ProductivityScoring
{
    /// <summary>The scale every score is on. Not a percentage of anything — 0 is
    /// "none of the inputs moved" and 100 is "every input at full marks".</summary>
    internal const decimal MaxScore = 100m;

    /// <summary>
    /// What full marks looks like per week, for the two inputs counted in whole
    /// items. Both are a starting position and nothing more; the point of putting
    /// them on screen through <see cref="ProductivityScoreInput"/> is that they
    /// are arguable.
    /// </summary>
    private const decimal MergedPullRequestsPerWeek = 1.5m;

    private const decimal ClosedIssuesPerWeek = 2.5m;

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
    /// The four inputs behind a score for one window of activity.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The last two are proportions of the pull requests that were reviewed at
    /// all, so their full marks moves with the window's volume rather than being
    /// a fixed count that a quiet fortnight could never reach.
    /// </para>
    /// <para>
    /// An input whose full marks works out to zero is left out rather than scored
    /// as zero. A quarter with no reviewed pull request has nothing to say about
    /// review promptness, and scoring that silence as a nil would drag the whole
    /// figure down for an absence of evidence. Leaving it out is safe precisely
    /// because the weights are normalised by their total.
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<ProductivityScoreInput> InputsFor(
        IReadOnlyList<ActivityPullRequest> pullRequests,
        IReadOnlyList<ActivityIssue> issues,
        int weeks)
    {
        ArgumentNullException.ThrowIfNull(pullRequests);
        ArgumentNullException.ThrowIfNull(issues);

        var reviewed = pullRequests.Where(pr => pr.FirstReviewedAt is not null).ToList();
        var timed = reviewed.Where(pr => pr.ReviewTurnaround is not null).ToList();

        var candidates = new[]
        {
            new ProductivityScoreInput(
                "Pull requests merged",
                pullRequests.Count,
                MergedPullRequestsPerWeek * weeks,
                3m),
            new ProductivityScoreInput(
                "Issues closed",
                issues.Count,
                ClosedIssuesPerWeek * weeks,
                2m),
            new ProductivityScoreInput(
                "First review within a day",
                timed.Count(pr => pr.ReviewTurnaround <= PromptReview),
                timed.Count,
                2m),
            new ProductivityScoreInput(
                "Merged without post-review churn",
                reviewed.Count(pr => !pr.HasChurn),
                reviewed.Count,
                1m)
        };

        return [.. candidates.Where(input => input.Max > 0m)];
    }
}
