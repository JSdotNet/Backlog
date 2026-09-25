using Backlog.Modules.Dashboard.Abstractions;
using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Dashboard.Services;

namespace Backlog.Modules.Dashboard.UnitTests;

/// <summary>
/// The tasks section: tasks ticked off and their story points per ISO week, narrowed by
/// the repository scope in this module rather than by the source.
/// </summary>
public class TaskInsightsTests
{
    /// <summary>Thursday of ISO week 39, 2026. Four weeks back is Thursday of week 35,
    /// so the window's buckets are W35 to W39 and the first starts Monday 24 August.</summary>
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    private static readonly DashboardScope FourWeeks = new(Period: DashboardPeriod.FourWeeks);

    [Fact]
    public async Task Counts_and_effort_land_in_the_iso_week_each_task_was_ticked_off_in()
    {
        var source = new StubSource(
            Ticked(2026, 9, 21, 3),
            Ticked(2026, 9, 22, null),
            Ticked(2026, 9, 14, 5),
            Ticked(2026, 8, 24, 2),
            Ticked(2026, 8, 23, null));

        var result = await Insights(source).GetThroughputAsync(FourWeeks, TestContext.Current.CancellationToken);

        Assert.True(result.HasValue);
        Assert.Equal(["W35", "W36", "W37", "W38", "W39"], result.Value!.CompletedPerWeek.Select(point => point.Label));
        Assert.Equal([1m, 0m, 0m, 1m, 2m], result.Value.CompletedPerWeek.Select(point => point.Value));
        Assert.Equal([2m, 0m, 0m, 5m, 3m], result.Value.EffortPerWeek.Select(point => point.Value));
        Assert.Equal(4, result.Value.Completed);
        Assert.Equal(10m, result.Value.Effort);

        // The unestimated task from the Sunday before the window is not counted as one.
        Assert.Equal(1, result.Value.Unestimated);
    }

    [Fact]
    public async Task The_source_is_asked_from_the_monday_the_first_week_starts_on()
    {
        var source = new StubSource();

        _ = await Insights(source).GetThroughputAsync(FourWeeks, TestContext.Current.CancellationToken);

        Assert.Equal(new DateOnly(2026, 8, 24), source.Since);
    }

    [Fact]
    public async Task A_task_counts_when_any_of_its_repositories_is_in_scope()
    {
        var source = new StubSource(
            Ticked(2026, 9, 21, 1, "backlog"),
            Ticked(2026, 9, 21, 2, "backlog-ide", "backlog"),
            Ticked(2026, 9, 21, 4),
            Ticked(2026, 9, 21, 8, "other"));

        var narrowed = await Insights(source).GetThroughputAsync(
            FourWeeks with { Repositories = RepositoryFocus.Of("BACKLOG") },
            TestContext.Current.CancellationToken);
        var all = await Insights(source).GetThroughputAsync(FourWeeks, TestContext.Current.CancellationToken);

        Assert.Equal(2, narrowed.Value!.Completed);
        Assert.Equal(3m, narrowed.Value.Effort);

        // No chips is the whole backlog, including a task that names no repository.
        Assert.Equal(4, all.Value!.Completed);
        Assert.Equal(15m, all.Value.Effort);
    }

    [Fact]
    public async Task A_source_that_fails_reads_as_unavailable_with_its_reason()
    {
        var source = new StubSource { Throw = new InvalidOperationException("The database is locked.") };

        var result = await Insights(source).GetThroughputAsync(FourWeeks, TestContext.Current.CancellationToken);

        Assert.False(result.HasValue);
        Assert.Contains("The database is locked.", result.Availability.Reason, StringComparison.Ordinal);
    }

    private static TaskInsights Insights(ICompletedTaskSource source) => new(source, new FixedClock(Now));

    private static CompletedTask Ticked(int year, int month, int day, int? effort, params string[] aliases) =>
        new(new DateOnly(year, month, day), effort, aliases);

    private sealed class StubSource(params CompletedTask[] tasks) : ICompletedTaskSource
    {
        public DateOnly? Since { get; private set; }

        public Exception? Throw { get; init; }

        public Task<IReadOnlyList<CompletedTask>> GetCompletedAsync(
            DateOnly since,
            CancellationToken cancellationToken = default)
        {
            Since = since;
            if (Throw is not null) throw Throw;
            return Task.FromResult<IReadOnlyList<CompletedTask>>(tasks);
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
