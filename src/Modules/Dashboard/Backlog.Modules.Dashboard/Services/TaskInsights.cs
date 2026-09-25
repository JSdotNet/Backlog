using Backlog.Modules.Dashboard.Abstractions;
using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;

namespace Backlog.Modules.Dashboard.Services;

/// <summary>
/// Tasks ticked off and their story points, per ISO week, over the repositories in scope.
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
/// </remarks>
internal sealed class TaskInsights(ICompletedTaskSource source, TimeProvider time) : ITaskInsights
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

    private static DateTimeOffset InstantOf(CompletedTask task) =>
        new(task.CompletedOn.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}
