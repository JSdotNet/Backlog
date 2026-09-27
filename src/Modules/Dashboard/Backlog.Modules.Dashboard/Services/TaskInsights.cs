using Backlog.Modules.Dashboard.Abstractions;
using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;

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
/// The read reaches back to the Monday the first bucket starts on rather than to the
/// window's own start, so the oldest week is a whole week and not the tail of one.
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

        IReadOnlyList<CompletedTask> completed;
        try
        {
            completed = await source.GetCompletedAsync(DateOnly.FromDateTime(buckets[0].Start.UtcDateTime), cancellationToken);
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

        return InsightResult<TaskThroughputInsight>.Ready(new TaskThroughputInsight(counted, effort, unestimated));
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
            .Select(item => Outlook(item, today))
            .ToList();

        return InsightResult<PlanInsight>.Ready(new PlanInsight(true, reading.Pace, items));
    }

    /// <summary>
    /// How an item's work stands: the effort left, at the item's own pace, laid out from
    /// today and judged against its planned end.
    /// </summary>
    /// <remarks>
    /// The projection is the roadmap's own arithmetic for a window drawn from its work —
    /// effort × 7 ÷ points a week in calendar days, never under one nor over ten years,
    /// the pace read per item — so a dashboard that says behind never contradicts the bar
    /// drawn past its end. It departs in one place: an open entry nobody estimated counts
    /// nothing here where the bar counts it a point, because the section reports it as
    /// unestimated and a figure that also guessed at it would count it twice. An item
    /// placed by effort is not judged at all: ADR 0013 ruling 4, as amended with ruling 5, has its window
    /// re-projected from the effort not yet done at the pace in use, so its end is
    /// already the projection and it could never read as behind.
    /// </remarks>
    private static PlanItemInsight Outlook(PlanItemProgress item, DateOnly today)
    {
        if (item.IsFinished) return new(item, PlanOutlook.Finished, item.LastCompletedOn);
        if (item.PlacedByEffort) return new(item, PlanOutlook.PlacedByEffort, item.End);
        if (item.TotalEffort == 0) return new(item, PlanOutlook.Unsized, null);
        if (item.PacePointsPerWeek <= 0) return new(item, PlanOutlook.NoPace, null);

        var remaining = Math.Max(0, item.TotalEffort - item.DoneEffort);
        var days = (int)Math.Clamp(Math.Ceiling(remaining * 7m / item.PacePointsPerWeek), 1, 3650);
        var projected = today.AddDays(days - 1);

        var outlook = item.End < today
            ? PlanOutlook.Overdue
            : projected <= item.End ? PlanOutlook.OnTrack : PlanOutlook.Behind;

        return new(item, outlook, projected);
    }

    private static DateTimeOffset InstantOf(CompletedTask task) =>
        new(task.CompletedOn.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}
