using Backlog.Modules.Dashboard.Abstractions;
using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;

namespace Backlog.Modules.Dashboard.Services;

/// <summary>
/// Turns one window of the person's own pull requests and issues into the four
/// productivity parts.
/// </summary>
/// <remarks>
/// <para>
/// All four parts derive from a single activity fetch per scope, cached for the
/// session. They are still four separate methods because they are four separate
/// parts on screen and each shows its own status — but behind them they join one
/// call rather than making four, which is what the cache's task-valued entries are
/// for.
/// </para>
/// <para>
/// Availability is asked before data, and a source that says no produces an
/// unavailable result carrying that source's own words. A throw is treated the
/// same way rather than propagating: one provider having a bad minute must not be
/// able to take the surface down, and the reader needs a sentence either way.
/// </para>
/// </remarks>
public sealed class ProductivityInsights(
    IActivitySource activity,
    IRepositoryDirectory repositories,
    TimeProvider time) : IProductivityInsights
{
    private readonly InsightCache _cache = new();

    public Task<InsightResult<ProductivityHeadline>> GetHeadlineAsync(
        DashboardScope scope,
        CancellationToken cancellationToken = default) =>
        DeriveAsync(scope, Headline, cancellationToken);

    public Task<InsightResult<ProductivityScoreInsight>> GetScoreAsync(
        DashboardScope scope,
        CancellationToken cancellationToken = default) =>
        DeriveAsync(scope, Score, cancellationToken);

    public Task<InsightResult<ProductivityTrend>> GetTrendAsync(
        DashboardScope scope,
        CancellationToken cancellationToken = default) =>
        DeriveAsync(scope, Trend, cancellationToken);

    public Task<InsightResult<ReworkInsight>> GetReworkAsync(
        DashboardScope scope,
        CancellationToken cancellationToken = default) =>
        DeriveAsync(scope, Rework, cancellationToken);

    public void Invalidate(DashboardScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);

        // Every scope, not only this one. A refresh means "the figures may have
        // moved", and that is true of the repository the reader is not looking at
        // as well; dropping only the focused scope would leave the rest of the
        // session showing figures from before the refresh.
        _cache.Clear();
    }

    /// <summary>
    /// The one shape all four parts share: ask whether the source can answer, get
    /// the window's activity, then derive. The derivation itself is synchronous and
    /// pure, which is what makes each part testable without a provider.
    /// </summary>
    private async Task<InsightResult<T>> DeriveAsync<T>(
        DashboardScope scope,
        Func<ScopedActivity, T> derive,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        try
        {
            var availability = await activity.GetAvailabilityAsync(cancellationToken).ConfigureAwait(false);

            if (!availability.IsAvailable) return InsightResult<T>.Unavailable(availability.Reason);

            var scoped = await ActivityForAsync(scope, cancellationToken).ConfigureAwait(false);

            return InsightResult<T>.Ready(derive(scoped));
        }
        catch (OperationCanceledException)
        {
            // A cancelled fetch is the reader closing the dashboard or changing the
            // filter, not a source failing. Let it travel.
            throw;
        }
        catch (Exception exception)
        {
            return InsightResult<T>.Unavailable(exception.Message);
        }
    }

    /// <summary>One window's activity plus the axis and scope it was read for, so
    /// a derivation has everything it needs and reads no clock of its own.</summary>
    private sealed record ScopedActivity(
        ActivityReport Report,
        IReadOnlyList<WeekBucket> Buckets,
        IReadOnlyList<DashboardRepository> Repositories,
        DashboardScope Scope);

    private Task<ScopedActivity> ActivityForAsync(DashboardScope scope, CancellationToken cancellationToken)
    {
        var (from, to) = scope.Window(time.GetUtcNow());

        // Keyed on what actually changes the fetch — the focus and the window —
        // rather than on the whole scope, so two scopes that would produce the
        // same call share one.
        var key = "activity|" + (scope.RepositoryAlias ?? "*") + "|" + scope.Weeks;

        return _cache.GetOrAddAsync(key, async () =>
        {
            var scoped = Scoped(scope);

            var report = await activity
                .GetActivityAsync(scoped, from, to, cancellationToken)
                .ConfigureAwait(false);

            return new ScopedActivity(report, WeekBuckets.Buckets(from, to), scoped, scope);
        });
    }

    /// <summary>
    /// Which repositories the fetch covers. A focused scope narrows to one; an
    /// alias that no longer matches anything narrows to nothing rather than
    /// silently widening back to everything, because a filter that fails open is
    /// worse than one that shows an empty part.
    /// </summary>
    private IReadOnlyList<DashboardRepository> Scoped(DashboardScope scope) =>
        scope.IsAllRepositories
            ? repositories.Repositories
            : [.. repositories.Repositories.Where(repository => Matches(repository.Alias, scope.RepositoryAlias))];

    private static ProductivityHeadline Headline(ScopedActivity scoped)
    {
        var pullRequests = scoped.Report.PullRequests;
        var reviewed = pullRequests.Where(pr => pr.FirstReviewedAt is not null).ToList();

        return new ProductivityHeadline(
            pullRequests.Count,
            scoped.Report.Issues.Count,
            reviewed.Count == 0 ? 0m : (decimal)reviewed.Count(pr => pr.HasChurn) / reviewed.Count,
            MedianTurnaround(pullRequests),
            WeekBuckets.Count(scoped.Buckets, pullRequests, pr => pr.MergedAt),
            WeekBuckets.Count(scoped.Buckets, scoped.Report.Issues, issue => issue.ClosedAt),
            WeekBuckets.Reduce(
                scoped.Buckets,
                reviewed,
                pr => pr.MergedAt,
                bucket => bucket.Count == 0 ? 0m : (decimal)bucket.Count(pr => pr.HasChurn) / bucket.Count));
    }

    /// <summary>
    /// The median rather than the mean, because one pull request that sat over a
    /// holiday would drag a mean far enough to make the figure useless. Even counts
    /// take the lower of the two middles rather than averaging them, so the answer
    /// stays a turnaround that actually happened.
    /// </summary>
    private static TimeSpan? MedianTurnaround(IReadOnlyList<ActivityPullRequest> pullRequests)
    {
        var turnarounds = pullRequests
            .Select(pr => pr.ReviewTurnaround)
            .OfType<TimeSpan>()
            .OrderBy(span => span)
            .ToList();

        return turnarounds.Count == 0 ? null : turnarounds[(turnarounds.Count - 1) / 2];
    }

    private static ProductivityScoreInsight Score(ScopedActivity scoped)
    {
        var inputs = ProductivityScoring.InputsFor(
            scoped.Report.PullRequests,
            scoped.Report.Issues,
            scoped.Scope.Weeks);

        return new ProductivityScoreInsight(ProductivityScoring.Score(inputs), inputs);
    }

    /// <summary>
    /// The score per repository per week — a small score over one week's activity,
    /// which is noisier than the headline figure and is meant to be: the point of
    /// the trend is to show a repository whose trajectory changed, and smoothing
    /// that away would leave five flat lines.
    /// </summary>
    /// <remarks>
    /// Every repository that reported anything stays in the series list even when
    /// one is in focus, because the comparison components show one series against
    /// the pack and the pack is what makes the focused line legible. The focus
    /// travels as <see cref="ProductivityTrend.Highlight"/> instead of as a filter.
    /// </remarks>
    private static ProductivityTrend Trend(ScopedActivity scoped)
    {
        var series = scoped.Repositories
            .Select(repository =>
            {
                var pullRequests = scoped.Report.PullRequests
                    .Where(pr => Matches(pr.RepositoryAlias, repository.Alias))
                    .ToList();

                var issues = scoped.Report.Issues
                    .Where(issue => Matches(issue.RepositoryAlias, repository.Alias))
                    .ToList();

                var points = scoped.Buckets
                    .Select(bucket => new InsightPoint(
                        bucket.Label,
                        ProductivityScoring.Score(ProductivityScoring.InputsFor(
                            [.. pullRequests.Where(pr => WeekBuckets.Of(pr.MergedAt).Key == bucket.Key)],
                            [.. issues.Where(issue => WeekBuckets.Of(issue.ClosedAt).Key == bucket.Key)],
                            weeks: 1))))
                    .ToList();

                return new InsightSeries(repository.Alias, points);
            })
            // A repository that reported nothing all quarter draws a flat zero line
            // that says only that it was quiet. Dropping it keeps the trellis about
            // the repositories actually worked in.
            .Where(one => one.Points.Any(point => point.Value > 0m))
            .ToList();

        return new ProductivityTrend(series, scoped.Scope.RepositoryAlias);
    }

    /// <summary>
    /// The churn figures, and the repositories they came from.
    /// </summary>
    /// <remarks>
    /// The denominator is pull requests that were reviewed at all, not every merged
    /// one. A pull request nobody reviewed cannot have churned after a review, and
    /// counting it as clean would let a quarter of unreviewed merges read as a
    /// quarter of good ones.
    /// </remarks>
    private static ReworkInsight Rework(ScopedActivity scoped)
    {
        var reviewed = scoped.Report.PullRequests.Where(pr => pr.FirstReviewedAt is not null).ToList();
        var churned = reviewed.Where(pr => pr.HasChurn).ToList();

        var byRepository = reviewed
            .GroupBy(pr => pr.RepositoryAlias, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count(pr => pr.HasChurn))
            .Select(group => new InsightRow(
                group.Key,
                Tokens: group.Sum(pr => pr.CommitsAfterFirstReview),
                Cost: null,
                Detail: group.Count(pr => pr.HasChurn) + " of " + group.Count() + " reviewed"))
            .ToList();

        return new ReworkInsight(
            churned.Count,
            reviewed.Count,
            reviewed.Sum(pr => pr.CommitsAfterFirstReview),
            reviewed.Sum(pr => pr.ForcePushesAfterFirstReview),
            reviewed.Sum(pr => pr.FilesRetouched),
            reviewed.All(pr => pr.ChurnComplete),
            WeekBuckets.Count(scoped.Buckets, churned, pr => pr.MergedAt),
            byRepository);
    }

    private static bool Matches(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
