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
}
