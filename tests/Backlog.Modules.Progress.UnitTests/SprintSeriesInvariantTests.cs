using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.DomainModels;
using Backlog.Modules.Progress.Services;

namespace Backlog.Modules.Progress.UnitTests;

/// <summary>
/// One test per rule of <c>.devbook/domain/progress/domain.invariants.md</c>, against the
/// aggregate, its value objects and the series assignment directly — the places each
/// rule is enforced — rather than through a handler.
/// </summary>
public class SprintSeriesInvariantTests
{
    private static readonly DateOnly Monday = Make.Monday;

    // --- Series Assignment -------------------------------------------------------

    [Fact]
    public void A_repository_belongs_to_at_most_one_series()
    {
        var core = Make.Series("backlog");
        var docs = Make.Series("docs");

        var assigned = SeriesAssignment.Assign([core, docs], docs, "BACKLOG");

        Assert.True(assigned.IsFailure);
        Assert.Equal("progress.repository_in_other_series", assigned.Error.Code);
        Assert.Equal(["docs"], docs.Repositories);
        Assert.Equal(["backlog"], core.Repositories);
    }

    [Fact]
    public void A_new_series_over_a_repository_another_series_has_is_refused()
    {
        var core = Make.Series("backlog");

        var free = SeriesAssignment.CanCreate([core], ["tools", "backlog"]);

        Assert.Equal("progress.repository_in_other_series", free.Error.Code);
    }

    [Fact]
    public void A_repository_in_no_series_can_be_given_to_one()
    {
        var core = Make.Series("backlog");

        Assert.True(SeriesAssignment.Assign([core], core, "docs").IsSuccess);
        Assert.Equal(["backlog", "docs"], core.Repositories);
    }

    // --- A series has at least one repository ------------------------------------

    [Fact]
    public void A_series_over_no_repository_is_refused()
    {
        var created = SprintSeries.Create(Guid.NewGuid(), [" ", ""]);

        Assert.Equal("progress.series_without_repository", created.Error.Code);
    }

    [Fact]
    public void Removing_a_series_last_repository_is_refused()
    {
        var series = Make.Series("backlog");

        var removed = series.RemoveRepository("backlog");

        Assert.Equal("progress.series_without_repository", removed.Error.Code);
        Assert.Equal(["backlog"], series.Repositories);
    }

    // --- Sprint length: one or two weeks -----------------------------------------

    [Theory]
    [InlineData(7)]
    [InlineData(14)]
    public void A_sprint_lasts_one_or_two_weeks(int days)
    {
        var window = SprintWindow.Create(Monday, (SprintLength)days, DayOfWeek.Monday);

        Assert.Equal(days, window.Value.Days);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(21)]
    public void A_sprint_of_any_other_length_is_refused(int days)
    {
        Assert.Equal(
            "progress.length_not_allowed",
            SprintSeries.Create(Guid.NewGuid(), ["backlog"], length: (SprintLength)days).Error.Code);
        Assert.Equal("progress.length_not_allowed", Make.Series().ChangeCadence((SprintLength)days, DayOfWeek.Monday).Error.Code);
        Assert.Equal(
            "progress.length_not_allowed",
            SprintWindow.Of(Monday, Monday.AddDays(days - 1), DayOfWeek.Monday).Error.Code);
    }

    // --- A sprint's window matches the series' cadence ----------------------------

    [Fact]
    public void A_sprint_starts_on_the_series_start_weekday()
    {
        var series = Make.Series();

        var started = series.StartSprint(Monday.AddDays(1), "Off by a day");

        Assert.Equal("progress.window_off_cadence", started.Error.Code);
        Assert.Empty(series.Sprints);
    }

    [Fact]
    public void A_sprint_window_lasts_exactly_the_series_length()
    {
        var series = Make.Series();

        var sprint = series.StartSprint(Monday, "Two weeks").Value;

        Assert.Equal(Monday, sprint.Window.Start);
        Assert.Equal(new DateOnly(2026, 10, 25), sprint.Window.End);
    }

    // --- A series has at most one open sprint -------------------------------------

    [Fact]
    public void Starting_a_sprint_while_one_is_open_is_refused()
    {
        var series = Make.SeriesWithOpenSprint();

        var second = series.StartSprint(Monday.AddDays(14), "Too soon");

        Assert.Equal("progress.sprint_already_open", second.Error.Code);
        Assert.Single(series.Sprints);
    }

    // --- Sprints are numbered without gaps ----------------------------------------

    [Fact]
    public void Sprints_are_numbered_from_one_without_gaps()
    {
        var series = Make.SeriesWithOpenSprint();
        series.CloseSprint([], [], new Dictionary<Guid, CloseOutcome>(), "Second");
        series.CloseSprint([], [], new Dictionary<Guid, CloseOutcome>(), "Third");

        Assert.Equal([1, 2, 3], series.Sprints.Select(sprint => sprint.Number));
    }

    // --- Sprints of a series are contiguous and do not overlap ---------------------

    [Fact]
    public void A_sprint_starting_inside_the_one_before_it_is_refused()
    {
        var previous = SprintWindow.Create(Monday, SprintLength.TwoWeeks, DayOfWeek.Monday).Value;
        var overlapping = SprintWindow.Create(Monday.AddDays(7), SprintLength.TwoWeeks, DayOfWeek.Monday).Value;

        var follows = SprintSequence.Follows(previous, overlapping, DayOfWeek.Monday);

        Assert.Equal("progress.sprints_overlap", follows.Error.Code);
    }

    [Fact]
    public void A_sprint_leaving_a_gap_after_the_one_before_it_is_refused()
    {
        var previous = SprintWindow.Create(Monday, SprintLength.TwoWeeks, DayOfWeek.Monday).Value;
        var afterAGap = SprintWindow.Create(Monday.AddDays(21), SprintLength.TwoWeeks, DayOfWeek.Monday).Value;

        var follows = SprintSequence.Follows(previous, afterAGap, DayOfWeek.Monday);

        Assert.Equal("progress.sprints_not_contiguous", follows.Error.Code);
    }

    [Fact]
    public void Each_sprint_starts_the_day_after_the_one_before_it_ends()
    {
        var series = Make.SeriesWithOpenSprint();

        var next = series.CloseSprint([], [], new Dictionary<Guid, CloseOutcome>(), "Next").Value;

        Assert.Equal(series.Sprints[0].Window.End.AddDays(1), next.Window.Start);
        Assert.True(SprintSequence.Follows(series.Sprints[0].Window, next.Window, DayOfWeek.Monday).IsSuccess);
    }

    [Fact]
    public void A_changed_weekday_starts_the_next_sprint_on_the_first_such_day_after_the_last()
    {
        var series = Make.SeriesWithOpenSprint();
        series.ChangeCadence(SprintLength.OneWeek, DayOfWeek.Wednesday);

        var next = series.CloseSprint([], [], new Dictionary<Guid, CloseOutcome>(), "Next").Value;

        Assert.Equal(new DateOnly(2026, 10, 25), series.Sprints[0].Window.End);
        Assert.Equal(new DateOnly(2026, 10, 28), next.Window.Start);
        Assert.Equal(7, next.Window.Days);
    }

    // --- A closed sprint does not change ------------------------------------------

    [Fact]
    public void A_closed_sprint_refuses_every_change()
    {
        var series = Make.SeriesWithOpenSprint();
        var task = Guid.NewGuid();
        series.Change(1, sprint => sprint.AddTask(task, Monday));
        series.CloseSprint([task], [], new Dictionary<Guid, CloseOutcome>(), "Next");
        var closed = series.SprintNumber(1)!;

        Assert.Equal("progress.sprint_closed", series.Change(1, sprint => sprint.AddTask(Guid.NewGuid(), Monday)).Error.Code);
        Assert.Equal("progress.sprint_closed", series.Change(1, sprint => sprint.RemoveTask(task)).Error.Code);
        Assert.Equal("progress.sprint_closed", series.Change(1, sprint => sprint.LinkPlan("+plan", Monday)).Error.Code);
        Assert.Equal("progress.sprint_closed", series.Change(1, sprint => sprint.SetGoal("Rewritten")).Error.Code);
        Assert.Equal(
            "progress.sprint_closed",
            series.Change(1, sprint => sprint.Record(task, item => item.SetNote("Late note"))).Error.Code);
        Assert.Equal("Ship the sprint model", closed.Goal);
        Assert.Null(closed.ItemFor(task)!.Note);
    }

    // --- Closing settles every unfinished member ----------------------------------

    [Fact]
    public void Closing_with_an_unfinished_member_without_an_outcome_is_refused()
    {
        var series = Make.SeriesWithOpenSprint();
        var carried = Guid.NewGuid();
        var unsettled = Guid.NewGuid();

        var closed = series.CloseSprint(
            [carried, unsettled],
            [carried, unsettled],
            new Dictionary<Guid, CloseOutcome> { [carried] = CloseOutcome.Carry },
            "Next");

        Assert.Equal("progress.unsettled_item", closed.Error.Code);
        Assert.True(series.Sprints.Single().IsOpen);
    }

    // --- Closing starts the next sprint -------------------------------------------

    [Fact]
    public void Closing_starts_the_next_sprint_with_the_next_goal_and_only_the_carried_tasks()
    {
        var series = Make.SeriesWithOpenSprint();
        var carry = Guid.NewGuid();
        var back = Guid.NewGuid();
        var drop = Guid.NewGuid();
        var done = Guid.NewGuid();

        var next = series.CloseSprint(
            [carry, back, drop, done],
            [carry, back, drop],
            new Dictionary<Guid, CloseOutcome>
            {
                [carry] = CloseOutcome.Carry,
                [back] = CloseOutcome.BackToBacklog,
                [drop] = CloseOutcome.Drop
            },
            "Ship the sprint view").Value;

        Assert.Equal(2, next.Number);
        Assert.True(next.IsOpen);
        Assert.Equal(new DateOnly(2026, 10, 26), next.Window.Start);
        Assert.Equal(new DateOnly(2026, 11, 8), next.Window.End);
        Assert.Equal("Ship the sprint view", next.Goal);
        Assert.Equal([carry], next.Items.Where(item => item.AddedDirectly).Select(item => item.TaskId));
        Assert.Empty(next.PlanLinks);

        var closed = series.SprintNumber(1)!;
        Assert.False(closed.IsOpen);
        Assert.Equal(CloseOutcome.Drop, closed.ItemFor(drop)!.Outcome);
        Assert.Equal(CloseOutcome.BackToBacklog, closed.ItemFor(back)!.Outcome);
        Assert.Null(closed.ItemFor(done)!.Outcome);
    }

    [Fact]
    public void A_carried_item_starts_the_next_sprint_without_its_note_or_question()
    {
        var series = Make.SeriesWithOpenSprint();
        var task = Guid.NewGuid();
        series.Change(1, sprint => sprint.Record(task, item => item.SetNote("Waiting on review")));
        series.Change(1, sprint => sprint.Record(task, item => item.SetOpenQuestion(true)));

        var next = series.CloseSprint([task], [task], new Dictionary<Guid, CloseOutcome> { [task] = CloseOutcome.Carry }, "Next").Value;

        Assert.Null(next.ItemFor(task)!.Note);
        Assert.False(next.ItemFor(task)!.OpenQuestion);
        Assert.Equal("Waiting on review", series.SprintNumber(1)!.ItemFor(task)!.Note);
        Assert.True(series.SprintNumber(1)!.ItemFor(task)!.OpenQuestion);
    }

    // --- A note is one line -------------------------------------------------------

    [Theory]
    [InlineData("first line\nsecond line")]
    [InlineData("first line\r\nsecond line")]
    public void A_note_with_a_line_break_is_refused(string note)
    {
        var series = Make.SeriesWithOpenSprint();
        var task = Guid.NewGuid();

        var noted = series.Change(1, sprint => sprint.Record(task, item => item.SetNote(note)));

        Assert.Equal("progress.note_not_one_line", noted.Error.Code);
        Assert.Null(series.SprintNumber(1)!.ItemFor(task));
    }

    // --- A name pattern carries the number ----------------------------------------

    [Theory]
    [InlineData("Sprint")]
    [InlineData("{n}")]
    [InlineData("  {n}  ")]
    [InlineData("Sprint {n} of {n}")]
    [InlineData("")]
    public void A_name_pattern_without_the_number_once_is_refused(string pattern)
    {
        Assert.Equal("progress.pattern_without_number", NamePattern.Create(pattern).Error.Code);
    }

    [Fact]
    public void A_sprint_is_named_by_the_pattern_and_renaming_it_renames_every_sprint()
    {
        var series = Make.SeriesWithOpenSprint();

        Assert.Equal("Sprint 4", series.NamePattern.NameOf(4));

        series.Rename(null, "Core {n}");

        Assert.Equal("Core 4", series.NamePattern.NameOf(4));
    }

    // --- A goal share is a whole percentage ---------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    [InlineData(-5)]
    public void A_goal_share_outside_one_to_a_hundred_is_refused(int share)
    {
        Assert.Equal("progress.share_out_of_range", GoalThreshold.Create(share).Error.Code);
        Assert.Equal("progress.share_out_of_range", Make.Series().SetGoalRules([], share).Error.Code);
    }

    [Fact]
    public void A_goal_rule_on_plans_or_tags_names_at_least_one()
    {
        Assert.Equal("progress.goal_rule_without_choice", GoalRule.Create(GoalRuleKind.InPlans, ["+", " "]).Error.Code);
        Assert.Equal("progress.goal_rule_without_choice", GoalRule.Create(GoalRuleKind.Tagged, []).Error.Code);
        Assert.Equal(["progress-sprints"], GoalRule.Create(GoalRuleKind.InPlans, ["+progress-sprints"]).Value.Choices);
    }
}
