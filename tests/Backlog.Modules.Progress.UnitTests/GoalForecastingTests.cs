using Backlog.Modules.Progress.Abstractions;
using Backlog.SharedKernel;

namespace Backlog.Modules.Progress.UnitTests;

/// <summary>
/// The goal forecast of <c>.devbook/domain/progress/domain.md#goal-forecast</c>, one test per
/// scenario of the Goals view's forecast and At-risk requirements
/// (<c>.devbook/domain/progress/requirements.md#the-goals-view</c>).
/// </summary>
public sealed class GoalForecastingTests
{
    private static readonly WorkingHours Week = WorkingHours.Default;

    private static DateOnly Oct(int day) => new(2026, 10, day);

    private static IterationWindow NextWeek(DateOnly today) => Iterations.Current(today, IterationLength.Week, null, Week);

    /// <summary>1, 3, 2 and 2 done in the four weeks up to 16 October, one day per item.</summary>
    private static readonly DateOnly[] TwoAWeek =
    [
        new(2026, 9, 21),
        new(2026, 9, 30), Oct(1), Oct(2),
        Oct(6), Oct(8),
        Oct(13), Oct(15)
    ];

    private static GoalForecast Forecast(int open, IEnumerable<DateOnly> done, DateOnly? due, bool heldBack = false, DateOnly? today = null) =>
        GoalForecasting.Forecast(open, done, NextWeek(today ?? Oct(17)), due, heldBack, Week);

    /// <summary>Scenario "Two items a week, six open".</summary>
    [Fact]
    public void Two_a_week_with_six_open_lands_at_the_end_of_the_third_week_from_now()
    {
        var forecast = Forecast(6, TwoAWeek, new DateOnly(2026, 11, 30));

        Assert.Equal(new DateOnly(2026, 11, 6), forecast.LandsOn);
        Assert.Equal(GoalStanding.OnTrack, forecast.Standing);
    }

    /// <summary>Scenario "Landing a week late".</summary>
    [Fact]
    public void Landing_after_the_due_date_is_at_risk()
    {
        var forecast = Forecast(6, TwoAWeek, Oct(30));

        Assert.Equal(GoalStanding.AtRisk, forecast.Standing);
        Assert.Equal(new DateOnly(2026, 11, 6), forecast.LandsOn);
    }

    /// <summary>Scenario "Landing in time": landing on the due date is in time.</summary>
    [Fact]
    public void Landing_on_the_due_date_is_on_track()
    {
        var forecast = Forecast(2, TwoAWeek, Oct(23));

        Assert.Equal(Oct(23), forecast.LandsOn);
        Assert.Equal(GoalStanding.OnTrack, forecast.Standing);
    }

    /// <summary>Scenario "Nothing done for a month".</summary>
    [Fact]
    public void Nothing_done_in_four_iterations_with_open_items_is_at_risk_with_no_landing_date()
    {
        var forecast = Forecast(7, [new DateOnly(2026, 9, 1)], new DateOnly(2027, 1, 1));

        Assert.Equal(GoalStanding.AtRisk, forecast.Standing);
        Assert.Null(forecast.LandsOn);
    }

    /// <summary>Scenario "An impediment on a plan's entry".</summary>
    [Fact]
    public void An_open_impediment_puts_a_goal_landing_in_time_at_risk()
    {
        var forecast = Forecast(2, TwoAWeek, Oct(30), heldBack: true);

        Assert.Equal(GoalStanding.AtRisk, forecast.Standing);
        Assert.Equal(Oct(23), forecast.LandsOn);
    }

    /// <summary>Scenario "The impediment is resolved".</summary>
    [Fact]
    public void Without_an_open_impediment_a_goal_landing_in_time_is_on_track()
    {
        Assert.Equal(GoalStanding.OnTrack, Forecast(2, TwoAWeek, Oct(30), heldBack: false).Standing);
    }

    [Fact]
    public void A_goal_with_no_open_item_is_on_track_with_no_landing_date()
    {
        var forecast = Forecast(0, [], Oct(1), heldBack: true);

        Assert.Equal(GoalStanding.OnTrack, forecast.Standing);
        Assert.Null(forecast.LandsOn);
    }

    /// <summary>A Saturday lies between two Week windows and in neither, so an item done on
    /// one adds nothing to the done rate.</summary>
    [Fact]
    public void An_item_done_on_a_Saturday_is_not_in_a_week_history()
    {
        var forecast = Forecast(1, [Oct(10), Oct(13)], new DateOnly(2027, 1, 1));

        Assert.Equal(0.25m, forecast.DoneRate);
    }

    /// <summary>Scenario "A longer iteration reads a longer history": by Month on 19 October
    /// the done rate reads June to September, and neither May nor October.</summary>
    [Fact]
    public void Month_reads_the_done_rate_from_June_to_September()
    {
        var next = Iterations.Current(Oct(19), IterationLength.Month, null, Week);
        DateOnly[] done = [new(2026, 5, 29), new(2026, 6, 1), new(2026, 9, 30), Oct(2)];

        var forecast = GoalForecasting.Forecast(1, done, next, new DateOnly(2027, 1, 31), false, Week);

        Assert.Equal(0.5m, forecast.DoneRate);
        Assert.Equal(new DateOnly(2026, 11, 30), forecast.LandsOn);
    }

    // --- A goal without a due date is due at the end of next ---------------------
    // Today Monday 19 October, Week: next is 19-23 October.

    /// <summary>Scenario "Lands within the iteration".</summary>
    [Fact]
    public void A_goal_without_a_due_date_landing_inside_next_is_on_track()
    {
        var forecast = Forecast(2, TwoAWeek, null, today: Oct(19));

        Assert.Equal(Oct(23), forecast.LandsOn);
        Assert.Equal(GoalStanding.OnTrack, forecast.Standing);
    }

    /// <summary>Scenario "Lands after the iteration".</summary>
    [Fact]
    public void A_goal_without_a_due_date_landing_after_next_is_at_risk()
    {
        var forecast = Forecast(4, TwoAWeek, null, today: Oct(19));

        Assert.Equal(Oct(30), forecast.LandsOn);
        Assert.Equal(GoalStanding.AtRisk, forecast.Standing);
    }

    [Fact]
    public void A_goal_without_a_due_date_held_back_by_an_open_impediment_is_at_risk()
    {
        var forecast = Forecast(2, TwoAWeek, null, heldBack: true, today: Oct(19));

        Assert.Equal(Oct(23), forecast.LandsOn);
        Assert.Equal(GoalStanding.AtRisk, forecast.Standing);
    }

    [Fact]
    public void A_goal_without_a_due_date_and_nothing_open_is_on_track()
    {
        var forecast = Forecast(0, [], null, today: Oct(19));

        Assert.Equal(GoalStanding.OnTrack, forecast.Standing);
        Assert.Null(forecast.LandsOn);
    }

    [Fact]
    public void A_goal_without_a_due_date_and_nothing_done_in_four_iterations_is_at_risk_with_no_landing_date()
    {
        var forecast = Forecast(7, [new DateOnly(2026, 9, 1)], null, today: Oct(19));

        Assert.Equal(GoalStanding.AtRisk, forecast.Standing);
        Assert.Null(forecast.LandsOn);
    }

    /// <summary>Per length, today Monday 19 October, one item done in each window of the
    /// history: the last day of next, and the last day of the window after it.</summary>
    public static TheoryData<IterationLength, DateOnly?, DateOnly[], DateOnly, DateOnly> Lengths => new()
    {
        { IterationLength.Day, null, [Oct(13), Oct(14), Oct(15), Oct(16)], Oct(19), Oct(20) },
        { IterationLength.Week, null, [new(2026, 9, 21), new(2026, 9, 28), Oct(5), Oct(12)], Oct(23), Oct(30) },
        // Counted from Monday 28 September, next is 12-25 October; with no start it would be 19 October-1 November.
        { IterationLength.TwoWeeks, new DateOnly(2026, 9, 28), [new(2026, 8, 17), new(2026, 8, 31), new(2026, 9, 14), new(2026, 9, 28)], Oct(25), new DateOnly(2026, 11, 8) },
        { IterationLength.Month, null, [new(2026, 6, 1), new(2026, 7, 1), new(2026, 8, 3), new(2026, 9, 1)], Oct(31), new DateOnly(2026, 11, 30) },
    };

    [Theory]
    [MemberData(nameof(Lengths))]
    public void A_goal_without_a_due_date_is_due_on_the_last_day_of_next_at_every_length(
        IterationLength length,
        DateOnly? twoWeekStart,
        DateOnly[] done,
        DateOnly endOfNext,
        DateOnly endOfFollowing)
    {
        var next = Iterations.Current(Oct(19), length, twoWeekStart, Week);

        var inside = GoalForecasting.Forecast(1, done, next, null, false, Week);
        var after = GoalForecasting.Forecast(2, done, next, null, false, Week);

        Assert.Equal((endOfNext, GoalStanding.OnTrack), (inside.LandsOn!.Value, inside.Standing));
        Assert.Equal((endOfFollowing, GoalStanding.AtRisk), (after.LandsOn!.Value, after.Standing));
        Assert.Equal(endOfNext, GoalForecasting.DueOn(null, next));
    }

    [Fact]
    public void A_set_due_date_is_the_day_a_goal_is_due_on()
    {
        Assert.Equal(new DateOnly(2026, 11, 30), GoalForecasting.DueOn(new DateOnly(2026, 11, 30), NextWeek(Oct(19))));
    }

    [Fact]
    public void Without_a_due_date_a_goal_is_due_on_the_last_day_of_next()
    {
        Assert.Equal(Oct(23), GoalForecasting.DueOn(null, NextWeek(Oct(19))));
    }
}
