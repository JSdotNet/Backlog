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
/// <para>
/// The baseline is the exception to that, and deliberately so. It is a second
/// source, and it can only remove the three volume inputs from the score — the four
/// proportions still score without it. So a baseline that refuses is swallowed here
/// rather than turned into an unavailable part: a readable card missing its
/// throughput row is worth more to a reader than a sentence where the card was.
/// </para>
/// </remarks>
public sealed class ProductivityInsights(
    IActivitySource activity,
    IActivityBaselineSource baseline,
    IRepositoryDirectory repositories,
    ISessionInsights sessions,
    TimeProvider time) : IProductivityInsights
{
    /// <summary>
    /// How the reader's own record is measured out: six blocks of four weeks, so
    /// half a year of history.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Four weeks a block because that is the shorter of the two windows the surface
    /// offers, so the best block and the shortest reading are the same shape of
    /// thing. Six of them because a target from one block back would be a target that
    /// moved every month.
    /// </para>
    /// <para>
    /// The grid ends at NOW rather than at the start of the scored window, which
    /// means the last block overlaps the reading it sets the bar for. That is on
    /// purpose. A purely backward-looking bar cannot survive somebody whose output
    /// grew — this repository's owner went from 34 merges in one block to 304 in the
    /// next, and a bar drawn before the current window would have read "304 of 34":
    /// the same defect as "441 of 6", in a new costume. Including the current block
    /// means a record set today counts today, and the growth headroom in
    /// <c>ProductivityScoring</c> is what keeps the reading short of full marks
    /// anyway.
    /// </para>
    /// </remarks>
    private const int BaselineBlocks = 6;

    private const int BaselineBlockWeeks = 4;

    private readonly InsightCache _cache = new();

    public Task<InsightResult<ProductivityHeadline>> GetHeadlineAsync(
        DashboardScope scope,
        CancellationToken cancellationToken = default) =>
        DeriveAsync(
            scope,
            async token => Headline(await ActivityForAsync(scope, token).ConfigureAwait(false)),
            cancellationToken);

    public Task<InsightResult<ProductivityScoreInsight>> GetScoreAsync(
        DashboardScope scope,
        CancellationToken cancellationToken = default) =>
        DeriveAsync(scope, token => ScoreAsync(scope, token), cancellationToken);

    public Task<InsightResult<ProductivityTrend>> GetTrendAsync(
        DashboardScope scope,
        CancellationToken cancellationToken = default) =>
        DeriveAsync(scope, token => TrendAsync(scope, token), cancellationToken);

    public Task<InsightResult<ReworkInsight>> GetReworkAsync(
        DashboardScope scope,
        CancellationToken cancellationToken = default) =>
        DeriveAsync(
            scope,
            async token => Rework(await ActivityForAsync(scope, token).ConfigureAwait(false)),
            cancellationToken);

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
    /// The one shape all four parts share: ask whether the source can answer, then
    /// derive. Each derivation decides for itself what it has to fetch — the score
    /// wants a baseline and the sessions figure beside the window, the trend wants
    /// the unfocused window — but none of them has to repeat the availability check
    /// or the failure contract.
    /// </summary>
    private async Task<InsightResult<T>> DeriveAsync<T>(
        DashboardScope scope,
        Func<CancellationToken, Task<T>> derive,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        try
        {
            var availability = await activity.GetAvailabilityAsync(cancellationToken).ConfigureAwait(false);

            if (!availability.IsAvailable) return InsightResult<T>.Unavailable(availability.Reason);

            return InsightResult<T>.Ready(await derive(cancellationToken).ConfigureAwait(false));
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
        //
        // The machine is deliberately not in this key. GitHub does not report which
        // machine a pull request or an issue was worked from, so two scopes that
        // differ only in their machine focus produce the identical call, and putting
        // it in the key would spend the churn budget — a few hundred calls for a
        // quarter — a second time for the same answer. The parts say on screen that
        // the machine filter does not reach them; this is the other half of that
        // sentence.
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
    /// The reader's own record, and the block that set it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Cached under a key of its own rather than beside the activity, and the prefix
    /// is what makes that safe: <c>InsightCache.GetOrAddAsync&lt;T&gt;</c> casts the
    /// stored value to the type the caller asked for, so a baseline landing under an
    /// activity key would not read as a miss — it would throw at the cast.
    /// </para>
    /// <para>
    /// A refusal is null rather than an exception. This source can only take the
    /// volume inputs off the card, and a card missing a row still tells the reader
    /// something; a card replaced by a sentence does not.
    /// </para>
    /// </remarks>
    private Task<ProductivityBaseline?> BaselineForAsync(DashboardScope scope, CancellationToken cancellationToken)
    {
        var key = "baseline|" + (scope.RepositoryAlias ?? "*") + "|" + scope.Weeks;

        return _cache.GetOrAddAsync<ProductivityBaseline?>(key, async () =>
        {
            try
            {
                var answer = await baseline
                    .GetBaselineAsync(Scoped(scope), Blocks(), cancellationToken)
                    .ConfigureAwait(false);

                return ProductivityBaseline.From(answer, BaselineBlockWeeks);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return null;
            }
        });
    }

    /// <summary>The block grid, oldest first, ending at the moment the reader is
    /// looking — see <see cref="BaselineBlocks"/> for why it does not stop at the
    /// start of the scored window.</summary>
    private IReadOnlyList<ActivityWindow> Blocks()
    {
        var now = time.GetUtcNow();
        var blocks = new List<ActivityWindow>(BaselineBlocks);

        for (var back = BaselineBlocks; back >= 1; back--)
        {
            blocks.Add(new ActivityWindow(
                now.AddDays(-7 * BaselineBlockWeeks * back),
                now.AddDays(-7 * BaselineBlockWeeks * (back - 1))));
        }

        return blocks;
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
                bucket => bucket.Count == 0 ? 0m : (decimal)bucket.Count(pr => pr.HasChurn) / bucket.Count))
        {
            Complete = scoped.Report.Complete
        };
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

    /// <summary>
    /// The score: the window's activity read against the reader's own record, with
    /// the sessions figure beside it when the surface is entitled to score one.
    /// </summary>
    /// <remarks>
    /// The three fetches go out together rather than one after another. The baseline
    /// is a different provider call from the activity report and neither waits on the
    /// other, so awaiting them in sequence would add a round trip to the slowest part
    /// on the surface for nothing.
    /// </remarks>
    private async Task<ProductivityScoreInsight> ScoreAsync(DashboardScope scope, CancellationToken cancellationToken)
    {
        var activityTask = ActivityForAsync(scope, cancellationToken);
        var baselineTask = BaselineForAsync(scope, cancellationToken);
        var sessionsTask = SessionsForAsync(scope, cancellationToken);

        await Task.WhenAll(activityTask, baselineTask, sessionsTask).ConfigureAwait(false);

        var scoped = await activityTask.ConfigureAwait(false);
        var record = await baselineTask.ConfigureAwait(false);
        var ran = await sessionsTask.ConfigureAwait(false);

        var targets = new ProductivityTargets(
            record?.MergedPerWeek ?? 0m,
            record?.ClosedPerWeek ?? 0m,
            ran?.BestPerWeek ?? 0m);

        var inputs = ProductivityScoring.InputsFor(
            scoped.Report.PullRequests,
            scoped.Report.Issues,
            scope.Weeks,
            targets,
            ran?.Sessions);

        return new ProductivityScoreInsight(ProductivityScoring.Score(inputs), inputs)
        {
            Target = record?.TargetFor(scope.Weeks),
            TargetComplete = record?.Complete ?? true,
            Complete = scoped.Report.Complete
        };
    }

    /// <summary>
    /// How many sessions the window held, and the best four weeks of them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Absent under a repository focus, and that refusal is the same one
    /// <c>DashboardPane</c> and <c>SessionsPart</c> already make: no assistant records
    /// which repository a session was for, so scoring one against a single repository
    /// would claim a whole-machine figure belongs to it. A surface that refuses the
    /// dimension in two places and honours it in a third is a surface contradicting
    /// itself.
    /// </para>
    /// <para>
    /// The machine focus is blanked rather than honoured, so <c>ScorePart</c>'s
    /// <c>FollowsMachine = false</c> stays literally true: the score reads every
    /// machine, whatever the filter above it says.
    /// </para>
    /// <para>
    /// The best block comes off the quarter's weekly series rather than off the
    /// half-year the GitHub baseline can reach, because this port answers for one
    /// window at a time and a quarter is the widest one it offers. So the sessions
    /// record is the best four weeks of the last twelve — narrower history than the
    /// other two volume inputs get, and worth knowing when reading the input.
    /// </para>
    /// <para>
    /// A session source that refuses returns null rather than zero, which drops the
    /// input rather than scoring the reader as having run nothing.
    /// </para>
    /// </remarks>
    private async Task<SessionVolume?> SessionsForAsync(DashboardScope scope, CancellationToken cancellationToken)
    {
        if (!scope.IsAllRepositories) return null;

        try
        {
            var everyMachine = scope with { MachineId = null };

            var window = await sessions.GetSessionsAsync(everyMachine, cancellationToken).ConfigureAwait(false);

            if (!window.HasValue) return null;

            var quarter = everyMachine.Period == DashboardPeriod.TwelveWeeks
                ? window
                : await sessions
                    .GetSessionsAsync(everyMachine with { Period = DashboardPeriod.TwelveWeeks }, cancellationToken)
                    .ConfigureAwait(false);

            return new SessionVolume(
                window.Value!.Sessions,
                quarter.HasValue ? BestBlockPerWeek(quarter.Value!.SessionsPerWeek) : 0m);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// The busiest four consecutive weeks of a weekly series, as a rate per week.
    /// Blocks are cut back from the most recent week so the last block ends where the
    /// series does, which is the same anchoring the GitHub grid uses.
    /// </summary>
    private static decimal BestBlockPerWeek(IReadOnlyList<InsightPoint> perWeek)
    {
        if (perWeek is null || perWeek.Count < BaselineBlockWeeks) return 0m;

        var best = 0m;

        for (var end = perWeek.Count; end >= BaselineBlockWeeks; end -= BaselineBlockWeeks)
        {
            var block = 0m;

            for (var index = end - BaselineBlockWeeks; index < end; index++) block += perWeek[index].Value;

            if (block > best) best = block;
        }

        return best / BaselineBlockWeeks;
    }

    /// <summary>How many sessions the window held, and what the reader's best four
    /// weeks of sessions works out to per week.</summary>
    private sealed record SessionVolume(int Sessions, decimal BestPerWeek);

    /// <summary>
    /// The reader's own record, in the shape the scoring wants it.
    /// </summary>
    /// <remarks>
    /// Two rates and one block. The rates are the highest block of each measure taken
    /// independently — a quarter that merged most may not be the quarter that closed
    /// most, and pinning both to one block would hold one of them below the record it
    /// actually set. The block travelling on is the merge one, because that is the
    /// figure the card names when it explains where full marks came from.
    /// </remarks>
    private sealed record ProductivityBaseline(
        decimal MergedPerWeek,
        decimal ClosedPerWeek,
        ActivityVolume? Best,
        bool Complete)
    {
        internal static ProductivityBaseline? From(ActivityBaseline answer, int blockWeeks)
        {
            if (answer.Blocks.Count == 0) return null;

            var best = answer.Blocks
                .OrderByDescending(block => block.MergedPullRequests)
                .ThenByDescending(block => block.To)
                .First();

            return new ProductivityBaseline(
                (decimal)best.MergedPullRequests / blockWeeks,
                (decimal)answer.Blocks.Max(block => block.ClosedIssues) / blockWeeks,
                best.MergedPullRequests == 0 ? null : best,
                answer.Complete);
        }

        /// <summary>What the card says full marks is, and where it came from. Null
        /// when nothing was ever merged — there is no record to be read against, and
        /// naming a target of nothing would be worse than naming none.
        /// <para>
        /// The arithmetic is <see cref="ProductivityScoring.FullMarks"/> rather than
        /// a second copy of it, so the figure the card names is by construction the
        /// figure the input was scored against. Two copies is how a card ends up
        /// explaining a bar it is not using.
        /// </para></summary>
        internal ProductivityTarget? TargetFor(int weeks) =>
            Best is null
                ? null
                : new ProductivityTarget(
                    Best.From,
                    Best.To,
                    Best.MergedPullRequests,
                    ProductivityScoring.FullMarks(MergedPerWeek, weeks));
    }

    /// <summary>
    /// The score per repository per week — a small score over one week's activity,
    /// which is noisier than the headline figure and is meant to be: the point of
    /// the trend is to show a repository whose trajectory changed, and smoothing
    /// that away would leave five flat lines.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read over the UNFOCUSED window, whatever the filter says. Every repository is
    /// always present here — the comparison components show one series against the
    /// pack, and dropping the pack when somebody zooms in would remove the only thing
    /// that makes the focused line mean anything — so narrowing the fetch would have
    /// left a focused reader with a single line and a chart that could not answer the
    /// question it exists for. The focus travels as
    /// <see cref="ProductivityTrend.Highlight"/> instead of as a filter.
    /// </para>
    /// <para>
    /// One target for every repository, taken from the whole estate's record rather
    /// than from each repository's own best week. Per-series normalisation would make
    /// every repository's best week read 100 — the busiest and the quietest alike —
    /// and destroy the only comparison the spotlight exists to draw.
    /// </para>
    /// <para>
    /// Sessions are not in this composition and cannot be: no assistant records a
    /// repository against a session, so there is no per-repository session count to
    /// score. A point here is therefore not a slice of the card's number, and the
    /// part says so beside the chart.
    /// </para>
    /// </remarks>
    private async Task<ProductivityTrend> TrendAsync(DashboardScope scope, CancellationToken cancellationToken)
    {
        var estate = scope with { RepositoryAlias = null };

        var activityTask = ActivityForAsync(estate, cancellationToken);
        var baselineTask = BaselineForAsync(estate, cancellationToken);

        await Task.WhenAll(activityTask, baselineTask).ConfigureAwait(false);

        var scoped = await activityTask.ConfigureAwait(false);
        var record = await baselineTask.ConfigureAwait(false);

        var targets = new ProductivityTargets(
            record?.MergedPerWeek ?? 0m,
            record?.ClosedPerWeek ?? 0m,
            SessionsPerWeek: 0m);

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
                            weeks: 1,
                            targets))))
                    .ToList();

                return new InsightSeries(repository.Alias, points);
            })
            // A repository that reported nothing all quarter draws a flat zero line
            // that says only that it was quiet. Dropping it keeps the trellis about
            // the repositories actually worked in.
            .Where(one => one.Points.Any(point => point.Value > 0m))
            .ToList();

        return new ProductivityTrend(series, scope.RepositoryAlias)
        {
            Complete = scoped.Report.Complete
        };
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
            byRepository)
        {
            Complete = scoped.Report.Complete
        };
    }

    private static bool Matches(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
