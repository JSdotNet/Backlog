namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// A schedule answers two questions about a moment — the next slot after it
/// and the last slot at or before it — and the worker builds a timer and a
/// catch-up from those. Both are asked in local time, so the offset the moment
/// carries is the offset the answer carries.
/// </summary>
public sealed class BackupScheduleTests
{
    private static readonly TimeSpan Amsterdam = TimeSpan.FromHours(2);

    // Tuesday 15 September 2026.
    private static DateTimeOffset Tuesday(int hour, int minute) => new(2026, 9, 15, hour, minute, 0, Amsterdam);

    [Fact]
    public void Off_has_no_slots()
    {
        Assert.Null(BackupSchedule.Off.NextSlotAfter(Tuesday(9, 0)));
        Assert.Null(BackupSchedule.Off.LastSlotAtOrBefore(Tuesday(9, 0)));
        Assert.False(BackupSchedule.Off.IsScheduled);
    }

    [Fact]
    public void Daily_before_the_time_is_today_and_after_it_is_tomorrow()
    {
        var daily = BackupSchedule.Off with { Cadence = BackupCadence.Daily, At = new TimeOnly(18, 0) };

        Assert.Equal(Tuesday(18, 0), daily.NextSlotAfter(Tuesday(9, 0)));
        Assert.Equal(Tuesday(18, 0).AddDays(1), daily.NextSlotAfter(Tuesday(18, 0)));
        Assert.Equal(Tuesday(18, 0).AddDays(1), daily.NextSlotAfter(Tuesday(18, 1)));
    }

    [Fact]
    public void Daily_last_slot_is_today_once_the_time_has_passed_and_yesterday_before_it()
    {
        var daily = BackupSchedule.Off with { Cadence = BackupCadence.Daily, At = new TimeOnly(18, 0) };

        Assert.Equal(Tuesday(18, 0).AddDays(-1), daily.LastSlotAtOrBefore(Tuesday(9, 0)));
        Assert.Equal(Tuesday(18, 0), daily.LastSlotAtOrBefore(Tuesday(18, 0)));
        Assert.Equal(Tuesday(18, 0), daily.LastSlotAtOrBefore(Tuesday(23, 59)));
    }

    [Fact]
    public void Weekly_walks_to_the_chosen_day_in_either_direction()
    {
        var friday = new BackupSchedule(BackupCadence.Weekly, new TimeOnly(17, 30), DayOfWeek.Friday);

        Assert.Equal(new DateTimeOffset(2026, 9, 18, 17, 30, 0, Amsterdam), friday.NextSlotAfter(Tuesday(9, 0)));
        Assert.Equal(new DateTimeOffset(2026, 9, 11, 17, 30, 0, Amsterdam), friday.LastSlotAtOrBefore(Tuesday(9, 0)));
    }

    [Fact]
    public void Weekly_on_the_day_itself_turns_on_the_time()
    {
        var tuesday = new BackupSchedule(BackupCadence.Weekly, new TimeOnly(12, 0), DayOfWeek.Tuesday);

        Assert.Equal(Tuesday(12, 0), tuesday.NextSlotAfter(Tuesday(11, 59)));
        Assert.Equal(Tuesday(12, 0).AddDays(7), tuesday.NextSlotAfter(Tuesday(12, 0)));
        Assert.Equal(Tuesday(12, 0).AddDays(-7), tuesday.LastSlotAtOrBefore(Tuesday(11, 59)));
        Assert.Equal(Tuesday(12, 0), tuesday.LastSlotAtOrBefore(Tuesday(12, 0)));
    }

    [Fact]
    public void The_answer_carries_the_offset_it_was_asked_in()
    {
        var daily = BackupSchedule.Off with { Cadence = BackupCadence.Daily, At = new TimeOnly(6, 0) };
        var tokyo = new DateTimeOffset(2026, 9, 15, 5, 0, 0, TimeSpan.FromHours(9));

        var next = daily.NextSlotAfter(tokyo);

        Assert.Equal(TimeSpan.FromHours(9), next!.Value.Offset);
        Assert.Equal(6, next.Value.Hour);
    }
}
