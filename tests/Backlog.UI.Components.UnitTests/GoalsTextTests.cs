using Backlog.UI.Components.Progress;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The fixed phrases the Goals view and its stand-up print, against the worked example of
/// <c>.devbook/domain/progress/requirements.md</c>: a Week stand-up on Monday 19 October
/// 2026, reading 12 to 16 October and 19 to 23 October.
/// </summary>
public sealed class GoalsTextTests
{
    private static DateOnly Oct(int day) => new(2026, 10, day);

    private static GoalCounts Counts(int done, int total, int doing = 0, int blocked = 0, int toDo = 0, int doneInWindow = 0) =>
        new(done, total, doing, blocked, toDo, doneInWindow);

    private static GoalCardModel Goal(GoalCounts counts, DateOnly due, DateOnly? landsOn) =>
        new("g1", "Customers get paid faster", due, landsOn, false, counts, [], [], []);

    [Fact]
    public void The_window_line_writes_a_shared_month_once()
    {
        Assert.Equal(
            "12–16 Oct → 19–23 Oct",
            GoalsText.Window(new GoalsWindow(Oct(12), Oct(16)), new GoalsWindow(Oct(19), Oct(23))));
    }

    [Fact]
    public void A_window_over_two_months_names_both()
    {
        Assert.Equal(
            "28 Sep – 2 Oct → 5–9 Oct",
            GoalsText.Window(new GoalsWindow(new DateOnly(2026, 9, 28), Oct(2)), new GoalsWindow(Oct(5), Oct(9))));
    }

    [Fact]
    public void A_day_window_is_one_date()
    {
        Assert.Equal("16 Oct → 19 Oct", GoalsText.Window(new GoalsWindow(Oct(16), Oct(16)), new GoalsWindow(Oct(19), Oct(19))));
    }

    [Fact]
    public void A_goal_line_reads_done_over_total_plus_the_window_due_and_landing()
    {
        var goal = Goal(Counts(9, 15, doneInWindow: 3), Oct(30), new DateOnly(2026, 11, 6));

        Assert.Equal("9/15 · +3 · due 30 Oct · lands ~6 Nov", GoalsText.GoalLine(goal));
    }

    [Fact]
    public void A_goal_line_drops_plus_nought_and_a_missing_landing()
    {
        Assert.Equal("5/6 · due 23 Oct", GoalsText.GoalLine(Goal(Counts(5, 6), Oct(23), null)));
    }

    [Fact]
    public void The_bar_label_says_every_segment_in_words()
    {
        Assert.Equal(
            "9 done, 3 in progress, 1 blocked, 2 not started",
            GoalsText.BarLabel(Counts(9, 15, doing: 3, blocked: 1, toDo: 2)));
    }

    [Theory]
    [InlineData(21, 18, "21 / 18 pts")]
    [InlineData(5, 4, "5 / 4 pts")]
    [InlineData(21, null, "21 pts")]
    public void The_points_tag_compares_planned_with_capacity(int planned, int? capacity, string expected) =>
        Assert.Equal(expected, GoalsText.PointsTag(planned, capacity));

    [Theory]
    [InlineData(GoalsIteration.Day, "21 points planned, you usually finish 18 a day")]
    [InlineData(GoalsIteration.Week, "21 points planned, you usually finish 18 a week")]
    [InlineData(GoalsIteration.TwoWeeks, "21 points planned, you usually finish 18 in two weeks")]
    [InlineData(GoalsIteration.Month, "21 points planned, you usually finish 18 a month")]
    public void The_points_title_follows_the_iteration_length(GoalsIteration length, string expected) =>
        Assert.Equal(expected, GoalsText.PointsTitle(21, 18, length));

    [Fact]
    public void Without_a_pace_the_points_tag_has_no_title() =>
        Assert.Null(GoalsText.PointsTitle(21, null, GoalsIteration.Week));

    /// <summary>Scenario "One escalated, one resolved".</summary>
    [Fact]
    public void An_impediment_line_names_who_it_is_with_and_its_age()
    {
        var impediment = Impediment(StandUpImpedimentState.Escalated, "Account manager", 3, checkAgain: null);

        Assert.Equal("Account manager · 3 days", GoalsText.ImpedimentMeta(impediment));
        Assert.Equal("Account manager · 3 days · Customers get paid faster", GoalsText.ImpedimentLine(impediment));
    }

    /// <summary>Scenario "A new impediment".</summary>
    [Fact]
    public void An_impediment_with_nobody_named_reads_not_reported_yet() =>
        Assert.StartsWith("Not reported yet", GoalsText.ImpedimentMeta(Impediment(StandUpImpedimentState.New, null, 1, null)), StringComparison.Ordinal);

    [Fact]
    public void A_check_again_day_replaces_the_age()
    {
        var impediment = Impediment(StandUpImpedimentState.BeingHandled, "Platform team", 4, checkAgain: Oct(20));

        Assert.Equal("Platform team · check Tue", GoalsText.ImpedimentMeta(impediment));
    }

    [Theory]
    [InlineData(1, "1 day")]
    [InlineData(3, "3 days")]
    public void An_age_is_counted_in_days(int days, string expected) => Assert.Equal(expected, GoalsText.Days(days));

    [Theory]
    [InlineData(StandUpImpedimentState.New, "New")]
    [InlineData(StandUpImpedimentState.Reported, "Reported")]
    [InlineData(StandUpImpedimentState.Escalated, "Escalated")]
    [InlineData(StandUpImpedimentState.BeingHandled, "Being handled")]
    [InlineData(StandUpImpedimentState.Resolved, "Resolved")]
    public void Each_state_has_its_display_words(StandUpImpedimentState state, string expected) =>
        Assert.Equal(expected, GoalsText.State(state));

    [Theory]
    [InlineData(GoalsIteration.Day, "Day")]
    [InlineData(GoalsIteration.Week, "Week")]
    [InlineData(GoalsIteration.TwoWeeks, "2 weeks")]
    [InlineData(GoalsIteration.Month, "Month")]
    public void Each_iteration_has_its_switch_label(GoalsIteration length, string expected) =>
        Assert.Equal(expected, GoalsText.Iteration(length));

    private static StandUpImpediment Impediment(StandUpImpedimentState state, string? with, int days, DateOnly? checkAgain) =>
        new("i1", "Bank sample files missing", state, [], with, days, checkAgain, "Customers get paid faster");
}
