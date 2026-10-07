using Backlog.Modules.Dashboard.Abstractions;
using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.SharedKernel;

namespace Backlog.Modules.Dashboard.Services;

/// <summary>
/// Tasks ticked off and their story points, per ISO week, and the roadmap items the
/// window shows, over the repositories in scope.
/// </summary>
/// <remarks>
/// <para>
/// No cache, unlike the productivity half: the source is the local backlog, so a read
/// costs a database query rather than a rate-limited call, and a refresh that reads
/// again is exactly what somebody who just ticked a task off expects.
/// </para>
/// <para>
/// A task counts when any repository it targets is in scope, which is how the task
/// list's own chips match a row. With no chips every task counts, including one that
/// names no repository — "all repositories" is the whole backlog, not the part of it
/// somebody filed against one.
/// </para>
/// <para>
/// The buckets start on the Monday of the window's first week rather than on the
/// window's own start, so the oldest week is a whole week and not the tail of one. The
/// read reaches further back again, by as many days as that span through today, for the
/// previous window the effort is compared with.
/// </para>
/// <para>
/// The plan is narrowed the same way with one difference: an item that names no
/// repository is plan-wide and every scope keeps it, where a task naming none only
/// counts under "all repositories". A plan-wide item is work every repository takes
/// part in; a task nobody filed is work nobody placed.
/// </para>
/// </remarks>
internal sealed class TaskInsights(ICompletedTaskSource source, IPlanProgressSource plan, TimeProvider time) : ITaskInsights
{
    public async Task<InsightResult<TaskThroughputInsight>> GetThroughputAsync(
        DashboardScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var (from, to) = scope.Window(time.GetUtcNow());
        var buckets = WeekBuckets.Buckets(from, to);
        if (buckets.Count == 0) return InsightResult<TaskThroughputInsight>.Ready(new([], [], 0));

        // The window before this one: as many days as the figure counts — the first
        // column's Monday through today — ending the day before that Monday. See
        // TaskThroughputInsight.PreviousEffort for why both edges sit there. Read in the
        // same query, because the source is one local read however far back it goes.
        var firstMonday = DateOnly.FromDateTime(buckets[0].Start.UtcDateTime);
        var today = DateOnly.FromDateTime(to.UtcDateTime);
        var previousStart = firstMonday.AddDays(-(today.DayNumber - firstMonday.DayNumber + 1));

        IReadOnlyList<CompletedTask> completed;
        try
        {
            completed = await source.GetCompletedAsync(previousStart, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return InsightResult<TaskThroughputInsight>.Unavailable($"The backlog could not be read: {exception.Message}");
        }

        var inScope = completed
            .Where(task => scope.IsAllRepositories || task.RepositoryAliases.Any(scope.Repositories.Contains))
            .ToList();

        var counted = WeekBuckets.Count(buckets, inScope, InstantOf);
        var effort = WeekBuckets.Reduce(buckets, inScope, InstantOf, tasks => tasks.Sum(task => task.Effort ?? 0));

        // Only the tasks that landed in a bucket, so the footnote counts what the
        // figures count and not a task ticked off after the window closed.
        var keys = buckets.Select(bucket => bucket.Key).ToHashSet(StringComparer.Ordinal);
        var unestimated = inScope.Count(task => task.Effort is null && keys.Contains(WeekBuckets.Of(InstantOf(task)).Key));

        // The figures above count only what lands in one of the window's buckets, so the
        // earlier tasks the wider read brought in reach nothing but this sum.
        var previous = inScope
            .Where(task => task.CompletedOn >= previousStart && task.CompletedOn < firstMonday)
            .Sum(task => (decimal)(task.Effort ?? 0));

        return InsightResult<TaskThroughputInsight>.Ready(
            new TaskThroughputInsight(counted, effort, unestimated) { PreviousEffort = previous });
    }

    public async Task<InsightResult<PlanInsight>> GetPlanAsync(
        DashboardScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var (from, to) = scope.Window(time.GetUtcNow());

        PlanReading reading;
        try
        {
            reading = await plan.ReadAsync(
                DateOnly.FromDateTime(from.UtcDateTime),
                DateOnly.FromDateTime(to.UtcDateTime),
                scope.IsAllRepositories ? [] : scope.Repositories.Aliases,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return InsightResult<PlanInsight>.Unavailable($"The roadmap could not be read: {exception.Message}");
        }

        if (!reading.RoadmapEnabled) return InsightResult<PlanInsight>.Ready(PlanInsight.Off);

        // The local date, not the UTC one the window is cut on: the roadmap projects
        // from the day on the person's calendar, and the outlook has to agree with it.
        var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);

        var items = reading.Items
            .Where(item => scope.IsAllRepositories
                || item.RepositoryAliases.Count == 0
                || item.RepositoryAliases.Any(scope.Repositories.Contains))
            .OrderBy(item => item.Start)
            .ThenBy(item => item.Title, StringComparer.Ordinal)
            .Select(item => Outlook(item, today, reading.Week))
            .ToList();

        return InsightResult<PlanInsight>.Ready(new PlanInsight(true, reading.Pace, items));
    }

    /// <summary>
    /// How an item's work stands: the effort left in each of its parts, at that part's own
    /// pace, laid out from today after the parts it waits on, and the latest part end
    /// judged against the item's planned end.
    /// </summary>
    /// <remarks>
    /// The projection is the roadmap's own arithmetic for a window drawn from its work —
    /// one part per repository, each part's effort left at its repository's points a
    /// working week, counted through the working week's hours (local ADR 0019) from the
    /// later of today and the day after the parts it waits on end, never under a day nor
    /// over ten years (ADR 0013, ruling 4 as amended on 2026-10-07) — so a dashboard that
    /// says behind never contradicts the bars drawn past its end. The parts come from the
    /// roadmap, the one place they are formed; this module may not name its types
    /// (guideline ADR 0005), so it chains the rows it is handed. It departs in one place:
    /// an open entry nobody estimated counts nothing here where the bar counts it a point,
    /// because the section reports it as unestimated and a figure that also guessed at it
    /// would count it twice. An item placed by effort is not judged at all: its end is not
    /// a date anybody set but the one the keep-up projection lays out each day — each
    /// part's gathered effort left at its repository's pace in use, from today (ADR 0013,
    /// ruling 5 as amended; local ADR 0018) — so there is no promised end for it to be
    /// behind; this section reports that end.
    /// </remarks>
    private static PlanItemInsight Outlook(PlanItemProgress item, DateOnly today, WorkingHours week)
    {
        if (item.IsFinished) return new(item, PlanOutlook.Finished, item.LastCompletedOn);
        if (item.PlacedByEffort) return new(item, PlanOutlook.PlacedByEffort, item.End);
        if (item.TotalEffort == 0) return new(item, PlanOutlook.Unsized, null);

        var left = item.Parts.Where(part => part.RemainingEffort > 0).ToList();
        if (left.Count > 0 && left.All(part => part.PacePointsPerWeek <= 0)) return new(item, PlanOutlook.NoPace, null);

        var counted = PartsEnd(item.Parts, today, week) ?? week.FirstWorkedDay(today);
        var latest = today.AddDays(LongestProjectionDays - 1);
        var projected = counted > latest ? latest : counted;

        var outlook = item.End < today
            ? PlanOutlook.Overdue
            : projected <= item.End ? PlanOutlook.OnTrack : PlanOutlook.Behind;

        return new(item, outlook, projected);
    }

    /// <summary>
    /// The day the last part's work lands, each part's estimated work left laid out at its
    /// own pace from the later of today and the day after the parts it waits on end — or
    /// <c>null</c> when no part has work left to lay out.
    /// </summary>
    /// <remarks>
    /// A part with nothing left, or with no pace to count at, takes no time: it ends where
    /// the parts it waits on end, so what waits on it still waits on them. The parts come
    /// in the roadmap's order, each after the parts it waits on, so one pass sees every
    /// wait's end first; a position that does not point earlier in the list is ignored.
    /// </remarks>
    private static DateOnly? PartsEnd(IReadOnlyList<PlanPartProgress> parts, DateOnly today, WorkingHours week)
    {
        var ends = new DateOnly?[parts.Count];
        DateOnly? last = null;

        for (var index = 0; index < parts.Count; index++)
        {
            var part = parts[index];
            DateOnly? waited = null;
            foreach (var position in part.WaitsOn)
            {
                if (position < 0 || position >= index || ends[position] is not { } end) continue;
                if (waited is null || end > waited) waited = end;
            }

            if (part.RemainingEffort <= 0 || part.PacePointsPerWeek <= 0)
            {
                ends[index] = waited;
                continue;
            }

            var from = waited is { } after && after >= today ? after.AddDays(1) : today;
            var landed = week.LastDayOf(from, part.RemainingEffort, part.PacePointsPerWeek);

            ends[index] = landed;
            if (last is null || landed > last) last = landed;
        }

        return last;
    }

    /// <summary>Ten years: the longest a projection runs, as the roadmap's forecast.</summary>
    private const int LongestProjectionDays = 3650;

    private static DateTimeOffset InstantOf(CompletedTask task) =>
        new(task.CompletedOn.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}
