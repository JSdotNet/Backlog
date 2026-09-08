using Backlog.Modules.Dashboard.Services;

namespace Backlog.Modules.Dashboard.UnitTests;

/// <summary>
/// The sweep behind the activity grid: intervals into (local date, local hour) cells,
/// carrying both how many were running at once and how much interval-time landed there.
/// <para>
/// Two things are worth asserting here and everything below is one of them. First, that
/// the two measures come off one pass — the total is the integral of the concurrency the
/// peak reads, so a test that let them be computed separately would be asserting nothing
/// about the thing that keeps a tile and the grid under it honest. Second, that the local
/// clock is a parameter: every zone in this file is a fixed made-up one, because a grid
/// asserted against whatever zone CI happens to run in is a grid asserted against
/// nothing.
/// </para>
/// </summary>
public class LocalHourBucketsTests
{
    /// <summary>
    /// A fixed two-hour zone rather than a real one. <c>FindSystemTimeZoneById</c> would
    /// need a different id on Linux and would bring a daylight-saving rule with it, which
    /// would make these assertions depend on which date they were written for.
    /// </summary>
    private static readonly TimeZoneInfo PlusTwo =
        TimeZoneInfo.CreateCustomTimeZone("test-plus-two", TimeSpan.FromHours(2), "+02", "+02");

    /// <summary>Wednesday 19 August 2026, midnight UTC.</summary>
    private static readonly DateTimeOffset Midnight = new(2026, 8, 19, 0, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly TheDay = new(2026, 8, 19);

    [Fact]
    public void An_interval_inside_one_hour_lands_in_that_hour()
    {
        var cells = Sweep([(At(9, 10), At(9, 40))]);

        var cell = Assert.Single(cells);

        Assert.Equal(new HourCell(TheDay, 9), cell.Key);
        Assert.Equal(TimeSpan.FromMinutes(30), cell.Value.Total);
        Assert.Equal(1, cell.Value.Peak);
    }

    [Fact]
    public void An_interval_spanning_three_hours_is_split_across_three_cells()
    {
        var cells = Sweep([(At(9, 0), At(12, 0))]);

        Assert.Equal(3, cells.Count);

        Assert.Equal(
            [new HourCell(TheDay, 9), new HourCell(TheDay, 10), new HourCell(TheDay, 11)],
            cells.Keys.OrderBy(cell => cell.Hour));

        // Split, not spread: each hour gets the part of the interval that actually fell
        // in it rather than a third of it.
        Assert.All(cells.Values, reading => Assert.Equal(TimeSpan.FromHours(1), reading.Total));
    }

    /// <summary>
    /// The whole reason the zone is a parameter. The same instant is a different column
    /// on two clocks, and both halves are asserted so the test cannot pass by the zone
    /// being ignored.
    /// </summary>
    [Fact]
    public void An_hour_of_local_time_is_not_an_hour_of_UTC()
    {
        var interval = (At(9, 0), At(10, 0));

        Assert.Equal(new HourCell(TheDay, 9), Assert.Single(Sweep([interval])).Key);
        Assert.Equal(new HourCell(TheDay, 11), Assert.Single(Sweep([interval], PlusTwo)).Key);
    }

    /// <summary>
    /// Two agents through the same hour is two at once and two agent-hours. The total is
    /// concurrency times duration rather than duration, which is what makes the figure
    /// capable of exceeding the hour it describes.
    /// </summary>
    [Fact]
    public void Two_intervals_that_overlap_are_two_at_once_and_two_hours()
    {
        var cells = Sweep([(At(9, 0), At(10, 0)), (At(9, 0), At(10, 0))]);

        var cell = Assert.Single(cells);

        Assert.Equal(2, cell.Value.Peak);
        Assert.Equal(TimeSpan.FromHours(2), cell.Value.Total);
    }

    /// <summary>
    /// The tie-break at equal instants, and it is not cosmetic: sorting the open before
    /// the close would read a handover as two agents and stamp a peak of two on an hour
    /// only one thing ever ran in.
    /// </summary>
    [Fact]
    public void An_interval_that_ends_as_another_begins_is_one_at_a_time()
    {
        var cells = Sweep([(At(9, 0), At(9, 30)), (At(9, 30), At(10, 0))]);

        var cell = Assert.Single(cells);

        Assert.Equal(1, cell.Value.Peak);
        Assert.Equal(TimeSpan.FromHours(1), cell.Value.Total);
    }

    [Fact]
    public void Twelve_overlapping_sessions_read_as_twelve_at_once()
    {
        var cells = Sweep([.. Enumerable.Repeat((At(9, 0), At(10, 0)), 12)]);

        var cell = Assert.Single(cells);

        Assert.Equal(12, cell.Value.Peak);

        // And twelve agents for an hour is twelve agent-hours, off the same sweep.
        Assert.Equal(TimeSpan.FromHours(12), cell.Value.Total);
    }

    /// <summary>
    /// A zero and an absence are two different answers and a heatmap draws them
    /// differently, so the two halves live in two places on purpose: the grid supplies
    /// every hour it covers, and the sweep reports only what landed somewhere. A sweep
    /// that returned 168 zeros would make "not reported" unsayable.
    /// </summary>
    [Fact]
    public void An_hour_nobody_worked_is_a_zero_cell_and_not_a_missing_one()
    {
        var quiet = new HourCell(TheDay, 3);

        Assert.Contains(quiet, LocalHourBuckets.Cells(At(9, 0), TimeZoneInfo.Utc));
        Assert.DoesNotContain(quiet, Sweep([(At(9, 0), At(10, 0))]).Keys);
    }

    /// <summary>
    /// The grid is the literal last seven dated days ending on the day the window closes
    /// — one row per day that happened, not seven days of the week folded across the
    /// window. Nothing about the period control reaches this method, which is the shape
    /// of the decision: the tiles widen to twelve weeks and the grid cannot.
    /// </summary>
    [Fact]
    public void The_grid_is_the_last_seven_dated_days_whatever_the_period()
    {
        var cells = LocalHourBuckets.Cells(At(9, 0), TimeZoneInfo.Utc);

        Assert.Equal(168, cells.Count);

        // Seven dated days, oldest first, ending on the day the window closes. Not four
        // or twelve weeks of them, and not seven weekday names.
        Assert.Equal(
            [.. Enumerable.Range(0, 7).Select(day => TheDay.AddDays(day - 6))],
            cells.Select(cell => cell.Day).Distinct());

        // Hour 00 first within a day, so the render order is the read order.
        Assert.Equal(new HourCell(TheDay.AddDays(-6), 0), cells[0]);
        Assert.Equal(new HourCell(TheDay, 23), cells[^1]);
    }

    /// <summary>
    /// The days are dated on the local clock as well as the hours. An instant late enough
    /// in the UTC evening belongs to the next day two hours east, and a grid that dated
    /// its rows in UTC while shading its columns in local time would be labelled wrong
    /// for two hours out of every twenty-four.
    /// </summary>
    [Fact]
    public void The_seven_days_are_dated_on_the_local_clock_too()
    {
        var lateEvening = At(23, 0);

        Assert.Equal(TheDay, LocalHourBuckets.Cells(lateEvening, TimeZoneInfo.Utc)[^1].Day);
        Assert.Equal(TheDay.AddDays(1), LocalHourBuckets.Cells(lateEvening, PlusTwo)[^1].Day);
    }

    /// <summary>
    /// A window's edge is the window's edge. Folding the outside part into the nearest
    /// cell would put work in an hour nobody worked, which is the one thing a grid must
    /// not invent.
    /// </summary>
    [Fact]
    public void Time_outside_the_window_is_clipped_rather_than_folded_into_the_nearest_cell()
    {
        var cells = LocalHourBuckets.Sweep(
            [(At(8, 0), At(12, 0))],
            At(9, 0),
            At(11, 0),
            TimeZoneInfo.Utc);

        Assert.Equal([9, 10], cells.Keys.Select(cell => cell.Hour).Order());
        Assert.All(cells.Values, reading => Assert.Equal(TimeSpan.FromHours(1), reading.Total));
    }

    /// <summary>
    /// The guard <c>WeekBuckets.Buckets</c> already carries, for its reason: a caller
    /// handing over a reversed window should get an empty answer, not a hang.
    /// </summary>
    [Fact]
    public void A_reversed_window_gives_an_empty_grid_rather_than_a_hang()
    {
        var cells = LocalHourBuckets.Sweep(
            [(At(9, 0), At(10, 0))],
            At(12, 0),
            At(9, 0),
            TimeZoneInfo.Utc);

        Assert.Empty(cells);
    }

    /// <summary>
    /// The invariant that keeps the tile and the grid honest: the total the tile shows is
    /// the sum of the cells, because both come off this one sweep. Asserted over a set
    /// that overlaps, straddles hour boundaries and runs past the window, so the equality
    /// is not an accident of tidy inputs.
    /// </summary>
    [Fact]
    public void The_cells_sum_to_the_total_the_tile_shows()
    {
        (DateTimeOffset From, DateTimeOffset To)[] intervals =
        [
            (At(9, 10), At(11, 40)),
            (At(9, 30), At(10, 5)),
            (At(13, 0), At(13, 20)),
            (At(23, 30), At(23, 55))
        ];

        var expected = intervals.Aggregate(TimeSpan.Zero, (running, interval) => running + (interval.To - interval.From));

        var cells = Sweep(intervals, PlusTwo);

        Assert.Equal(expected, cells.Values.Aggregate(TimeSpan.Zero, (running, cell) => running + cell.Total));

        // Not one bucket that happened to add up: the set really was spread about.
        Assert.True(cells.Count > 3);
    }

    private static IReadOnlyDictionary<HourCell, HourReading> Sweep(
        IReadOnlyList<(DateTimeOffset From, DateTimeOffset To)> intervals,
        TimeZoneInfo? zone = null) =>
        LocalHourBuckets.Sweep(intervals, Midnight, Midnight.AddDays(1), zone ?? TimeZoneInfo.Utc);

    private static DateTimeOffset At(int hour, int minute) => Midnight.AddHours(hour).AddMinutes(minute);
}
