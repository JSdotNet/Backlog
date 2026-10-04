namespace Backlog.SharedKernel.UnitTests;

/// <summary>
/// The working week as a count of hours (local ADR 0019): which days are worked, how
/// long each is, how long the week is, and the default week an empty one reads as.
/// </summary>
public class WorkingHoursTests
{
    private static WorkingHours With(params WorkingDay[] days) =>
        new() { Days = [.. WorkingHours.Week.Select(day => days.FirstOrDefault(own => own.Day == day) ?? WorkingHours.Default.On(day))] };

    private static WorkingHours NoDayWorked() =>
        new() { Days = [.. WorkingHours.Week.Select(day => WorkingHours.Default.On(day) with { Working = false })] };

    [Fact]
    public void TheDefaultWeek_IsFortyTwoAndAHalfHours_MondayToFriday()
    {
        var week = WorkingHours.Default;

        Assert.Equal(TimeSpan.FromHours(42.5), week.PerWeek);
        Assert.Equal(TimeSpan.FromHours(8.5), week.WorkedOn(DayOfWeek.Wednesday));
        Assert.True(week.IsWorked(DayOfWeek.Friday));
        Assert.False(week.IsWorked(DayOfWeek.Saturday));
        Assert.Equal(TimeSpan.Zero, week.WorkedOn(DayOfWeek.Sunday));
    }

    [Fact]
    public void ADayEndingAtOrBeforeItsStart_IsNotWorked()
    {
        var week = With(new WorkingDay(DayOfWeek.Monday, true, new TimeOnly(17, 0), new TimeOnly(9, 0)));

        Assert.False(week.IsWorked(DayOfWeek.Monday));
        Assert.Equal(TimeSpan.Zero, week.WorkedOn(DayOfWeek.Monday));
        Assert.Equal(TimeSpan.FromHours(34), week.PerWeek);
    }

    [Fact]
    public void AShortFriday_CountsItsOwnHours()
    {
        var week = With(new WorkingDay(DayOfWeek.Friday, true, new TimeOnly(9, 0), new TimeOnly(13, 0)));

        Assert.Equal(TimeSpan.FromHours(4), week.WorkedOn(DayOfWeek.Friday));
        Assert.Equal(TimeSpan.FromHours(38), week.PerWeek);
        Assert.Same(week, week.Effective);
    }

    [Fact]
    public void AWeekWithNoWorkingHours_ReadsAsTheDefaultWeek()
    {
        var empty = NoDayWorked();

        Assert.Equal(TimeSpan.Zero, empty.PerWeek);
        Assert.Same(WorkingHours.Default, empty.Effective);
    }

    // --- Day overrides (local ADR 0019, §5) ---------------------------------

    private static readonly DateOnly Monday5Oct = new(2026, 10, 5);
    private static readonly DateOnly Wednesday7Oct = new(2026, 10, 7);
    private static readonly DateOnly Friday9Oct = new(2026, 10, 9);
    private static readonly DateOnly Saturday10Oct = new(2026, 10, 10);
    private static readonly DateOnly Monday12Oct = new(2026, 10, 12);

    [Fact]
    public void ADateWithNoOverride_ReadsThePattern()
    {
        var week = WorkingHours.Default;

        Assert.True(week.IsWorked(Friday9Oct));
        Assert.Equal(TimeSpan.FromHours(8.5), week.WorkedOn(Friday9Oct));
        Assert.False(week.IsWorked(Saturday10Oct));
        Assert.Equal(TimeSpan.Zero, week.WorkedOn(Saturday10Oct));
        Assert.Null(week.OverrideOn(Friday9Oct));
    }

    [Fact]
    public void ABlockedDate_CountsNoHours_AndLeavesTheWeekdayAlone()
    {
        var week = WorkingHours.Default.Toggled(Friday9Oct);

        Assert.Equal(new DayOverride(Friday9Oct, false), week.OverrideOn(Friday9Oct));
        Assert.False(week.IsWorked(Friday9Oct));
        Assert.Equal(TimeSpan.Zero, week.WorkedOn(Friday9Oct));

        // The pattern and every other Friday are untouched; H never moves.
        Assert.True(week.IsWorked(DayOfWeek.Friday));
        Assert.True(week.IsWorked(Friday9Oct.AddDays(7)));
        Assert.Equal(TimeSpan.FromHours(42.5), week.PerWeek);
    }

    [Fact]
    public void AnUnblockedDate_CountsItsWeekdaysStoredHours()
    {
        var week = With(new WorkingDay(DayOfWeek.Saturday, false, new TimeOnly(10, 0), new TimeOnly(14, 0)))
            .Toggled(Saturday10Oct);

        Assert.Equal(new DayOverride(Saturday10Oct, true), week.OverrideOn(Saturday10Oct));
        Assert.True(week.IsWorked(Saturday10Oct));
        Assert.Equal(TimeSpan.FromHours(4), week.WorkedOn(Saturday10Oct));
        Assert.Equal(TimeSpan.Zero, week.WorkedOn(Saturday10Oct.AddDays(7)));
        Assert.False(week.IsWorked(DayOfWeek.Saturday));
    }

    [Fact]
    public void AnUnblockedDateWithAnEmptyWeekdayRange_IsStillNotWorked()
    {
        var week = With(new WorkingDay(DayOfWeek.Saturday, false, new TimeOnly(14, 0), new TimeOnly(10, 0)))
            .Toggled(Saturday10Oct);

        Assert.False(week.IsWorked(Saturday10Oct));
        Assert.Equal(TimeSpan.Zero, week.WorkedOn(Saturday10Oct));

        // Pressing again still returns the date to its pattern.
        Assert.Empty(week.Toggled(Saturday10Oct).Overrides);
    }

    [Fact]
    public void ToggleBack_RemovesTheOverride()
    {
        var blocked = WorkingHours.Default.Toggled(Friday9Oct);
        var back = blocked.Toggled(Friday9Oct);

        Assert.Single(blocked.Overrides);
        Assert.Empty(back.Overrides);
        Assert.Equal(TimeSpan.FromHours(8.5), back.WorkedOn(Friday9Oct));
    }

    [Fact]
    public void Overrides_StaySortedByDate_OnePerDate()
    {
        var week = WorkingHours.Default
            .Toggled(Saturday10Oct)
            .Toggled(Wednesday7Oct)
            .Toggled(Friday9Oct)
            .Toggled(Saturday10Oct)
            .Toggled(Saturday10Oct);

        Assert.Equal(
            new[] { new DayOverride(Wednesday7Oct, false), new DayOverride(Friday9Oct, false), new DayOverride(Saturday10Oct, true) },
            week.Overrides);
    }

    [Fact]
    public void AnEmptyPattern_FallsBackToTheDefault_KeepingItsOverrides()
    {
        var empty = NoDayWorked() with { Overrides = [new DayOverride(Friday9Oct, false)] };

        var effective = empty.Effective;

        Assert.Equal(TimeSpan.FromHours(42.5), effective.PerWeek);
        Assert.Equal(empty.Overrides, effective.Overrides);
        Assert.False(effective.IsWorked(Friday9Oct));
        Assert.True(effective.IsWorked(Friday9Oct.AddDays(-1)));
    }

    [Fact]
    public void TheFirstWorkedDay_SkipsABlockedDate()
    {
        var week = WorkingHours.Default.Toggled(Monday12Oct);

        Assert.Equal(Monday12Oct.AddDays(1), week.FirstWorkedDay(Monday12Oct));
        Assert.Equal(Monday12Oct.AddDays(1), week.FirstWorkedDay(Saturday10Oct));
    }

    [Fact]
    public void TheFirstWorkedDay_FindsAnUnblockedWeekend()
    {
        var week = WorkingHours.Default.Toggled(Saturday10Oct);

        Assert.Equal(Saturday10Oct, week.FirstWorkedDay(Saturday10Oct));
    }

    [Fact]
    public void TheFirstWorkedDay_WalksPastMoreThanAWeekOfLeave()
    {
        var week = WorkingHours.Default;
        for (var day = Monday5Oct; day < Monday5Oct.AddDays(12); day = day.AddDays(1))
        {
            if (week.IsWorked(day)) week = week.Toggled(day);
        }

        Assert.Equal(Monday5Oct.AddDays(14), week.FirstWorkedDay(Monday5Oct));
    }

    /// <summary>Requirement "An effort window counts the overrides": 7 points at 7 a
    /// week from Monday 5 October end on Friday 9; blocking Wednesday 7 moves the end to
    /// Monday 12; unblocking Saturday 10 (09:00 to 17:30) then ends it on Saturday 10.</summary>
    [Fact]
    public void ABlockedThenAnUnblockedDate_MoveTheEndOfAWindow()
    {
        var week = WorkingHours.Default;
        Assert.Equal(Friday9Oct, week.LastDayOf(Monday5Oct, 7m, 7m));

        var blocked = week.Toggled(Wednesday7Oct);
        Assert.Equal(Monday12Oct, blocked.LastDayOf(Monday5Oct, 7m, 7m));

        var unblocked = blocked.Toggled(Saturday10Oct);
        Assert.Equal(Saturday10Oct, unblocked.LastDayOf(Monday5Oct, 7m, 7m));
    }

    /// <summary>The whole-week skip never jumps an override: a blocked date weeks ahead
    /// still lengthens a long window by one worked date.</summary>
    [Fact]
    public void ALongWindow_CountsABlockedDateWeeksAhead()
    {
        var week = WorkingHours.Default;
        var plain = week.LastDayOf(Monday5Oct, 50m, 5m); // ten weeks: Friday 11 December
        Assert.Equal(new DateOnly(2026, 12, 11), plain);

        var blocked = week.Toggled(new DateOnly(2026, 11, 18));
        Assert.Equal(new DateOnly(2026, 12, 14), blocked.LastDayOf(Monday5Oct, 50m, 5m));

        var blockedLastDay = week.Toggled(plain);
        Assert.Equal(new DateOnly(2026, 12, 14), blockedLastDay.LastDayOf(Monday5Oct, 50m, 5m));
    }

    [Fact]
    public void ALongWindow_EndsWhereADayByDayWalkEnds()
    {
        var week = WorkingHours.Default
            .Toggled(new DateOnly(2026, 10, 20))
            .Toggled(new DateOnly(2026, 10, 24))
            .Toggled(new DateOnly(2027, 1, 1))
            .Toggled(new DateOnly(2027, 3, 3));

        foreach (var points in new[] { 1m, 6m, 13m, 40m, 87m, 160m })
        {
            Assert.Equal(DayByDay(week, Monday5Oct, points * week.PerWeek.Ticks / 5m), week.LastDayOf(Monday5Oct, points, 5m));
        }
    }

    [Fact]
    public void AnAbsurdAmount_WithOverrides_IsClampedToTheCalendar()
    {
        var week = WorkingHours.Default.Toggled(Friday9Oct);

        Assert.Equal(DateOnly.MaxValue, week.LastDayOf(Monday5Oct, int.MaxValue, 0.0001m));
    }

    // --- Days off in a list (local ADR 0019, §4: the Days off dialog) ---------------

    private static readonly DateOnly Sunday18Oct = new(2026, 10, 18);
    private static readonly DateOnly Saturday17Oct = new(2026, 10, 17);
    private static readonly DateOnly Thursday8Oct = new(2026, 10, 8);

    /// <summary>ADR 0019 Verification 35: on the default week, Mon 12 to Sun 18 October
    /// blocks the five weekdays and adds nothing for the weekend.</summary>
    [Fact]
    public void ARangeOfDaysOff_BlocksOnlyTheDatesThePatternWorks()
    {
        var week = WorkingHours.Default.WithDaysOff(Monday12Oct, Sunday18Oct);

        Assert.Equal(
            [.. Enumerable.Range(0, 5).Select(offset => new DayOverride(Monday12Oct.AddDays(offset), Worked: false))],
            week.Overrides);
        Assert.All(Enumerable.Range(0, 7), offset => Assert.False(week.IsWorked(Monday12Oct.AddDays(offset))));
    }

    /// <summary>Requirement "A range of days off blocks only the worked dates", scenario
    /// "A range over an unblocked Saturday": the Saturday's override goes, so it reads
    /// its pattern — not worked — again.</summary>
    [Fact]
    public void ARangeOfDaysOff_RemovesAnUnblockedDateInsideIt()
    {
        var week = WorkingHours.Default.Toggled(Saturday17Oct).WithDaysOff(Monday12Oct, Sunday18Oct);

        Assert.Null(week.OverrideOn(Saturday17Oct));
        Assert.False(week.IsWorked(Saturday17Oct));
        Assert.Equal(5, week.Overrides.Count);
    }

    /// <summary>Scenario "A range over a date already blocked": each date is listed once,
    /// and the overrides outside the range are kept.</summary>
    [Fact]
    public void ARangeOfDaysOff_ListsADateAlreadyBlockedOnce_AndKeepsTheRest()
    {
        var week = WorkingHours.Default
            .Toggled(Wednesday7Oct)
            .Toggled(new DateOnly(2026, 10, 14))
            .WithDaysOff(Monday12Oct, new DateOnly(2026, 10, 16));

        Assert.Equal(
            [Wednesday7Oct, .. Enumerable.Range(0, 5).Select(offset => Monday12Oct.AddDays(offset))],
            week.Overrides.Select(entry => entry.Date));
        Assert.All(week.Overrides, entry => Assert.False(entry.Worked));
    }

    /// <summary>A range of one date is that date.</summary>
    [Fact]
    public void ARangeOfOneDate_BlocksThatDate()
    {
        var week = WorkingHours.Default.WithDaysOff(Friday9Oct, Friday9Oct);

        Assert.Equal([new DayOverride(Friday9Oct, Worked: false)], week.Overrides);
    }

    /// <summary>A range that ends before it starts is no range.</summary>
    [Fact]
    public void ARangeEndingBeforeItStarts_IsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WorkingHours.Default.WithDaysOff(Sunday18Oct, Monday12Oct));
    }

    /// <summary>A range longer than <see cref="WorkingHours.MaxDaysOffRange"/> days is
    /// refused rather than written as thousands of overrides.</summary>
    [Fact]
    public void ARangeLongerThanAYear_IsRefused()
    {
        var through = Monday12Oct.AddDays(WorkingHours.MaxDaysOffRange - 1);

        // 366 days from a Monday: 52 whole weeks and a Monday and a Tuesday.
        Assert.Equal(262, WorkingHours.Default.WithDaysOff(Monday12Oct, through).Overrides.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => WorkingHours.Default.WithDaysOff(Monday12Oct, through.AddDays(1)));
    }

    /// <summary>ADR 0019 Verification 36: a worked Saturday is one unblocked override.</summary>
    [Fact]
    public void AWorkedDay_UnblocksADateThePatternLeavesOff()
    {
        var week = WorkingHours.Default.WithWorkedDay(Saturday17Oct);

        Assert.Equal([new DayOverride(Saturday17Oct, Worked: true)], week.Overrides);
        Assert.True(week.IsWorked(Saturday17Oct));
    }

    /// <summary>Scenario "A date the pattern already works": no override is added.</summary>
    [Fact]
    public void AWorkedDay_ThePatternAlreadyWorks_AddsNothing()
    {
        var week = WorkingHours.Default.WithWorkedDay(Thursday8Oct);

        Assert.Empty(week.Overrides);
        Assert.True(week.IsWorked(Thursday8Oct));
    }

    /// <summary>A worked day on a weekday the person blocked brings it back to its
    /// pattern: the block goes, and nothing is stored in its place.</summary>
    [Fact]
    public void AWorkedDay_OnABlockedWeekday_RemovesTheBlock()
    {
        var week = WorkingHours.Default.Toggled(Friday9Oct).WithWorkedDay(Friday9Oct);

        Assert.Empty(week.Overrides);
        Assert.True(week.IsWorked(Friday9Oct));
    }

    /// <summary>ADR 0019 Verification 37: removing the blocked Friday empties the set.</summary>
    [Fact]
    public void RemovingAnOverride_ReturnsTheDateToItsPattern()
    {
        var week = WorkingHours.Default.Toggled(Friday9Oct).Toggled(Saturday17Oct).WithoutOverride(Friday9Oct);

        Assert.Equal([new DayOverride(Saturday17Oct, Worked: true)], week.Overrides);
        Assert.True(week.IsWorked(Friday9Oct));
        Assert.Empty(week.WithoutOverride(Saturday17Oct).Overrides);
    }

    private static DateOnly DayByDay(WorkingHours week, DateOnly start, decimal neededTicks)
    {
        var day = week.FirstWorkedDay(start);
        var left = neededTicks;
        while (true)
        {
            left -= week.WorkedOn(day).Ticks;
            if (left <= 0) return day;
            day = day.AddDays(1);
        }
    }
}
