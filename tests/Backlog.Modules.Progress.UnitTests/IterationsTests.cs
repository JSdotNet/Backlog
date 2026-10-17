using Backlog.Modules.Progress.Abstractions;
using Backlog.SharedKernel;

namespace Backlog.Modules.Progress.UnitTests;

/// <summary>
/// The iteration windows of <c>.devbook/domain/progress/domain.md#iteration</c>, one test per
/// scenario of <c>.devbook/domain/progress/requirements.md#choosing-the-iteration</c>: next is
/// the window holding today, or the first after it, and last is the one before next.
/// </summary>
public sealed class IterationsTests
{
    private static readonly WorkingHours Week = WorkingHours.Default;

    private static DateOnly Oct(int day) => new(2026, 10, day);

    private static (IterationWindow Last, IterationWindow Next) Read(DateOnly today, IterationLength length, DateOnly? anchor = null, WorkingHours? week = null)
    {
        var next = Iterations.Current(today, length, anchor, week ?? Week);
        return (Iterations.Previous(next, week ?? Week), next);
    }

    /// <summary>Scenario "A Monday stand-up".</summary>
    [Fact]
    public void Day_on_a_Monday_reads_the_Friday_before_and_the_Monday()
    {
        var (last, next) = Read(Oct(19), IterationLength.Day);

        Assert.Equal((Oct(16), Oct(16)), (last.First, last.Last));
        Assert.Equal((Oct(19), Oct(19)), (next.First, next.Last));
    }

    [Fact]
    public void Day_on_a_Saturday_reads_the_next_Monday_and_the_Friday_before()
    {
        var (last, next) = Read(Oct(17), IterationLength.Day);

        Assert.Equal(Oct(16), last.First);
        Assert.Equal(Oct(19), next.First);
    }

    [Fact]
    public void Day_steps_over_a_blocked_date()
    {
        var week = WorkingHours.Default.Toggled(Oct(16));

        var (last, _) = Read(Oct(19), IterationLength.Day, week: week);

        Assert.Equal(Oct(15), last.First);
    }

    [Fact]
    public void A_week_with_no_worked_day_reads_Day_as_Monday_to_Friday()
    {
        var none = new WorkingHours
        {
            Days = [.. WorkingHours.Week.Select(day => new WorkingDay(day, false, WorkingHours.DefaultStart, WorkingHours.DefaultEnd))]
        };

        var (last, next) = Read(Oct(17), IterationLength.Day, week: none);

        Assert.Equal((Oct(16), Oct(19)), (last.First, next.First));
    }

    /// <summary>Scenario "Read on a Saturday": "12–16 Oct → 19–23 Oct".</summary>
    [Fact]
    public void Week_on_a_Saturday_reads_the_week_before_and_the_week_after()
    {
        var (last, next) = Read(Oct(17), IterationLength.Week);

        Assert.Equal((Oct(12), Oct(16)), (last.First, last.Last));
        Assert.Equal((Oct(19), Oct(23)), (next.First, next.Last));
    }

    [Fact]
    public void Week_on_a_Wednesday_reads_its_own_week_as_next()
    {
        var (last, next) = Read(Oct(14), IterationLength.Week);

        Assert.Equal((Oct(5), Oct(9)), (last.First, last.Last));
        Assert.Equal((Oct(12), Oct(16)), (next.First, next.Last));
    }

    /// <summary>Scenario "Anchored on 5 October".</summary>
    [Fact]
    public void Two_weeks_keep_to_the_start_date()
    {
        var (last, next) = Read(Oct(19), IterationLength.TwoWeeks, Oct(5));

        Assert.Equal((Oct(5), Oct(18)), (last.First, last.Last));
        Assert.Equal((Oct(19), new DateOnly(2026, 11, 1)), (next.First, next.Last));
    }

    [Fact]
    public void Two_weeks_count_back_from_a_start_after_today()
    {
        var (last, next) = Read(Oct(19), IterationLength.TwoWeeks, new DateOnly(2026, 11, 2));

        Assert.Equal((Oct(19), new DateOnly(2026, 11, 1)), (next.First, next.Last));
        Assert.Equal((Oct(5), Oct(18)), (last.First, last.Last));
    }

    [Fact]
    public void Two_weeks_without_a_start_count_from_the_Monday_of_this_week()
    {
        var (_, next) = Read(Oct(21), IterationLength.TwoWeeks);

        Assert.Equal((Oct(19), new DateOnly(2026, 11, 1)), (next.First, next.Last));
    }

    /// <summary>Scenario "Read in October".</summary>
    [Fact]
    public void Month_reads_the_calendar_month()
    {
        var (last, next) = Read(Oct(19), IterationLength.Month);

        Assert.Equal((new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)), (last.First, last.Last));
        Assert.Equal((Oct(1), Oct(31)), (next.First, next.Last));
    }

    [Fact]
    public void Month_in_January_reads_December_of_the_year_before_as_last()
    {
        var (last, _) = Read(new DateOnly(2027, 1, 12), IterationLength.Month);

        Assert.Equal((new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 31)), (last.First, last.Last));
    }

    [Fact]
    public void Following_steps_one_window_forward()
    {
        var next = Iterations.Current(Oct(17), IterationLength.Week, null, Week);

        var after = Iterations.Following(next, Week);

        Assert.Equal((Oct(26), Oct(30)), (after.First, after.Last));
    }

    [Fact]
    public void History_reads_four_windows_ending_with_last_oldest_first()
    {
        var last = Iterations.Previous(Iterations.Current(Oct(19), IterationLength.Month, null, Week), Week);

        var history = Iterations.History(last, 4, Week);

        Assert.Equal([6, 7, 8, 9], history.Select(window => window.First.Month));
    }

    [Fact]
    public void Worked_days_count_the_worked_dates_of_the_window()
    {
        var next = Iterations.Current(Oct(19), IterationLength.Week, null, Week);

        Assert.Equal(5, Iterations.WorkedDays(next, Week));
        Assert.Equal(4, Iterations.WorkedDays(next, Week.Toggled(Oct(21))));
    }

    [Fact]
    public void Worked_days_between_two_dates_count_both_ends_and_none_backwards()
    {
        Assert.Equal(3, Iterations.WorkedDays(Oct(14), Oct(16), Week));
        Assert.Equal(1, Iterations.WorkedDays(Oct(16), Oct(18), Week));
        Assert.Equal(0, Iterations.WorkedDays(Oct(16), Oct(15), Week));
    }

    /// <summary>Scenarios "A week at 18 points" and "A day at 18 points a week": the pace
    /// scaled to the next window's worked days, rounded to whole points.</summary>
    [Theory]
    [InlineData(IterationLength.Week, 18)]
    [InlineData(IterationLength.Day, 4)]
    public void Capacity_scales_the_pace_to_the_window(IterationLength length, int expected)
    {
        var next = Iterations.Current(Oct(19), length, null, Week);

        Assert.Equal(expected, Iterations.Capacity(18m, next, Week));
    }

    [Fact]
    public void Monday_of_reads_the_Monday_of_the_week()
    {
        Assert.Equal(Oct(12), Iterations.MondayOf(Oct(18)));
        Assert.Equal(Oct(19), Iterations.MondayOf(Oct(19)));
    }
}
