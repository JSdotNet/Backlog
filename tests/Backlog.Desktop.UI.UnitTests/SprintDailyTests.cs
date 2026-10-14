using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.UI.InProgress;
using Backlog.UI.Components.Progress;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The sprint view's Since yesterday strip (<c>.devbook/domain/progress/features.md#the-daily-strip</c>):
/// every sentence a fixed template over one sprint reading — since the previous working
/// day, on today, blocked and open questions, and the pace — shown only for the open
/// sprint of a series that shows in the daily brief, and copied for the stand-up as text.
/// </summary>
public sealed class SprintDailyTests
{
    private static readonly DateOnly Start = new(2026, 10, 5);

    /// <summary>Tuesday of the sprint's second week: day 7 of 10.</summary>
    private static readonly DateOnly Tuesday = new(2026, 10, 13);

    private static readonly DateOnly Monday = new(2026, 10, 12);

    [Fact]
    public void Since_yesterday_names_what_was_done_opened_for_review_started_and_added_since_the_previous_working_day()
    {
        var daily = SprintDailyMapper.ToModel(Sprint(Tuesday), Series(daily: true))!;

        Assert.Equal("Daily · Tue 13 Oct · Sprint 42, day 7 of 10", daily.Label);
        Assert.Equal(
            [
                "Done: Reserveringsdefinities and Categorieën (#4685)",
                "Opened for review: Klantrekening (#4707)",
                "Started: Signalering uitwerken",
                "Added to the sprint: Klantrekening"
            ],
            daily.SinceYesterday.Select(SprintDailyCopy.LineText));
        Assert.Equal(
            [SprintDailyLineKind.Done, SprintDailyLineKind.OpenedForReview, SprintDailyLineKind.Started, SprintDailyLineKind.Added],
            daily.SinceYesterday.Select(line => line.Kind));
    }

    [Fact]
    public void On_today_lists_the_work_in_progress_then_what_waits_on_review()
    {
        var daily = SprintDailyMapper.ToModel(Sprint(Tuesday), Series(daily: true))!;

        Assert.Equal(
            [
                "Signalering uitwerken",
                "Fix specs (#4710)",
                "Review waiting: Klantrekening (#4707)",
                "Review waiting: Local dev platform (#4572)"
            ],
            daily.OnToday.Select(SprintDailyCopy.LineText));
    }

    [Fact]
    public void Blocked_and_open_questions_name_the_day_a_member_was_blocked_and_the_question()
    {
        var daily = SprintDailyMapper.ToModel(Sprint(Tuesday), Series(daily: true))!;

        Assert.Equal(
            ["Openspec · blocked since Fri 9 Oct", "Categorieën · Import?", "Fix specs · Align with demo?"],
            daily.Blocked.Select(SprintDailyCopy.LineText));
    }

    [Fact]
    public void The_headline_summary_and_pace_are_templates_over_the_reading()
    {
        var daily = SprintDailyMapper.ToModel(Sprint(Tuesday), Series(daily: true))!;

        Assert.Equal("2 items landed since yesterday; Openspec has been blocked for 2 working days.", daily.Headline);
        Assert.Equal("Fincent · goal 1 of 5 · all work 3 of 9 · 3 working days left", daily.Summary);
        Assert.Equal(
            "Goal: 4 tasks open, Openspec blocked among them, so the goal is at risk. All work: 3 behind the even pace; about 4 of 9 land by Friday.",
            daily.Pace);
        Assert.True(daily.GoalAtRisk);
    }

    [Fact]
    public void On_a_monday_the_previous_working_day_is_friday_and_a_quiet_day_says_so()
    {
        var quiet = Sprint(Monday) with
        {
            Members = [.. Sprint(Monday).Members.Where(member => member.State is not SprintWorkState.Blocked).Select(member => member with
            {
                DoneOn = member.DoneOn is { } on && on >= Monday.AddDays(-3) ? Start : member.DoneOn,
                StartedOn = Start,
                JoinedOn = Start,
                AddedAfterStart = false,
                PullRequests = [.. member.PullRequests.Select(pull => pull with { OpenedOn = Start })]
            })]
        };

        var daily = SprintDailyMapper.ToModel(quiet, Series(daily: true))!;

        var line = Assert.Single(daily.SinceYesterday);
        Assert.Equal(SprintDailyLineKind.Quiet, line.Kind);
        Assert.Equal("Nothing moved since Friday.", line.Text);
        Assert.StartsWith("Nothing landed since Friday; ", daily.Headline, StringComparison.Ordinal);
    }

    [Fact]
    public void Work_done_on_the_previous_working_day_counts_and_work_done_before_it_does_not()
    {
        var daily = SprintDailyMapper.ToModel(Sprint(Monday), Series(daily: true))!;

        // Friday 9 Oct is Monday's previous working day: Monday's merge is in, Old done
        // (Thu 8) stays out, and Categorieën, done on Tuesday, is not done yet.
        Assert.Equal("Done: Reserveringsdefinities (#4685)", SprintDailyCopy.LineText(daily.SinceYesterday[0]));
        Assert.DoesNotContain(daily.SinceYesterday, line => line.Text.Contains("Old done", StringComparison.Ordinal));
    }

    [Fact]
    public void A_goal_with_nothing_blocked_and_the_pace_to_land_it_is_on_track()
    {
        SprintMemberDto[] members =
        [
            Member("A", SprintWorkState.Done, goal: true, doneOn: Start),
            Member("B", SprintWorkState.Done, goal: true, doneOn: Start.AddDays(1)),
            Member("C", SprintWorkState.InProgress, goal: true)
        ];
        var progress = Progress(Tuesday, members) with { DonePerWorkingDay = 1 };

        var daily = SprintDailyMapper.ToModel(progress, Series(daily: true))!;

        Assert.False(daily.GoalAtRisk);
        Assert.StartsWith("Goal: 1 task open, on track. All work: ", daily.Pace, StringComparison.Ordinal);
        Assert.Equal("Nothing landed since yesterday; the goal is on track.", daily.Headline);
        Assert.Empty(daily.Blocked);
    }

    [Fact]
    public void A_pull_request_two_done_members_share_is_named_once()
    {
        var shared = Pull("4685", LinkedPullRequestState.Merged, Monday);
        SprintMemberDto[] members =
        [
            Member("A", SprintWorkState.Done, doneOn: Monday, pulls: [shared]),
            Member("B", SprintWorkState.Done, doneOn: Monday, pulls: [shared with { Repository = "INNOVADIS-DEV/Fincent" }])
        ];

        var daily = SprintDailyMapper.ToModel(Progress(Tuesday, members), Series(daily: true))!;

        Assert.Equal("Done: A and B (#4685)", SprintDailyCopy.LineText(daily.SinceYesterday[0]));
    }

    [Fact]
    public void A_blocked_member_with_an_open_pull_request_is_under_blocked_and_not_waiting_on_review()
    {
        SprintMemberDto[] members =
        [
            Member("Stuck", SprintWorkState.Blocked, pulls: [Pull("9", LinkedPullRequestState.Open, Start)])
        ];

        var daily = SprintDailyMapper.ToModel(Progress(Tuesday, members), Series(daily: true))!;

        Assert.Equal(["Nothing is in progress."], daily.OnToday.Select(line => line.Text));
        Assert.Equal(["Stuck · blocked"], daily.Blocked.Select(line => line.Text));
    }

    [Fact]
    public void A_goal_whose_own_tasks_stall_is_at_risk_however_fast_the_other_work_goes()
    {
        SprintMemberDto[] members =
        [
            Member("Goal one", SprintWorkState.InProgress, goal: true),
            Member("Goal two", SprintWorkState.NotStarted, goal: true),
            Member("Side", SprintWorkState.Done, doneOn: Start)
        ];
        var progress = Progress(Tuesday, members) with { DonePerWorkingDay = 5 };

        var daily = SprintDailyMapper.ToModel(progress, Series(daily: true))!;

        Assert.True(daily.GoalAtRisk);
        Assert.StartsWith("Goal: 2 tasks open, so the goal is at risk. ", daily.Pace, StringComparison.Ordinal);
    }

    [Fact]
    public void A_met_goal_says_so()
    {
        SprintMemberDto[] members = [Member("A", SprintWorkState.Done, goal: true, doneOn: Start)];

        var daily = SprintDailyMapper.ToModel(Progress(Tuesday, members), Series(daily: true))!;

        Assert.StartsWith("Goal: met, 1 of 1 done. ", daily.Pace, StringComparison.Ordinal);
        Assert.EndsWith("; the goal is met.", daily.Headline, StringComparison.Ordinal);
    }

    [Fact]
    public void The_strip_is_drawn_only_for_the_open_sprint_of_a_series_that_shows_in_the_daily()
    {
        Assert.Null(SprintDailyMapper.ToModel(Sprint(Tuesday), Series(daily: false)));
        Assert.Null(SprintDailyMapper.ToModel(Sprint(Tuesday) with { IsOpen = false }, Series(daily: true)));
        Assert.Null(SprintDailyMapper.ToModel(Sprint(Tuesday) with { ReadOn = null }, Series(daily: true)));
    }

    [Fact]
    public void The_stand_up_copy_is_the_strip_as_plain_text()
    {
        var daily = new SprintDailyModel(
            "Daily · Tue 13 Oct · Sprint 42, day 7 of 10",
            "2 items landed yesterday.",
            "Fincent · goal 1 of 5",
            [new SprintDailyLine(SprintDailyLineKind.Done, "Done: Categorieën", [new SprintBoardPullRequest("o/r", "4685", SprintPullRequestState.Merged)])],
            [SprintDailyLine.Of(SprintDailyLineKind.InProgress, "Fix specs")],
            [],
            "Goal: met.",
            false);

        Assert.Equal(
            "Daily · Tue 13 Oct · Sprint 42, day 7 of 10\n2 items landed yesterday.\nFincent · goal 1 of 5\n\n"
            + "Since yesterday\n- Done: Categorieën (#4685)\n\nOn today\n- Fix specs\n\nPace\nGoal: met.\n",
            SprintDailyCopy.PlainText(daily));
    }

    [Fact]
    public void The_stand_up_copy_carries_the_strip_then_the_sprint_table_as_html_and_as_text()
    {
        var daily = new SprintDailyModel(
            "Daily · Tue 13 Oct",
            "Fix <specs> & more.",
            "Fincent",
            [new SprintDailyLine(SprintDailyLineKind.Done, "Done: Categorieën", [new SprintBoardPullRequest("o/r", "4685", SprintPullRequestState.Merged)])],
            [SprintDailyLine.Of(SprintDailyLineKind.Quiet, "Nothing is in progress.")],
            [],
            "Goal: 1 task open, so the goal is at risk.",
            true);
        var table = new SprintTable("<table>rows</table>", "Plan\tItem");

        var copy = SprintDailyCopy.StandUp(daily, table);

        Assert.Equal(
            "Daily · Tue 13 Oct\r\nFix <specs> & more.\r\nFincent\r\n\r\nSince yesterday\r\n- Done: Categorieën (#4685)\r\n\r\n"
            + "On today\r\n- Nothing is in progress.\r\n\r\nPace\r\nGoal: 1 task open, so the goal is at risk.\r\n\r\nPlan\tItem\r\n",
            copy.Text);
        Assert.Contains("Fix &lt;specs&gt; &amp; more.", copy.Html, StringComparison.Ordinal);
        Assert.Contains("<a href=\"https://github.com/o/r/pull/4685\"", copy.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("Blocked and open questions", copy.Html, StringComparison.Ordinal);
        Assert.EndsWith("<br><table>rows</table></div>", copy.Html, StringComparison.Ordinal);
        Assert.Equal(SprintDailyCopy.StandUp(daily, null).Text + "\r\nPlan\tItem\r\n", copy.Text);
    }

    // ---- Fixtures ----------------------------------------------------------------------------

    private static SprintSeriesDto Series(bool daily) =>
        new(Guid.NewGuid(), "Fincent", ["fincent"], SprintLength.TwoWeeks, DayOfWeek.Monday, "Sprint {n}", daily, false,
            [GoalRuleDto.MarkedAsGoal()], 100, [new SprintDto(42, "Sprint 42", Start, Start.AddDays(13), "Ship it", true, [], [])]);

    /// <summary>The artboard's sprint, read on <paramref name="today"/>.</summary>
    private static SprintProgressDto Sprint(DateOnly today)
    {
        SprintMemberDto[] members =
        [
            Member("Reserveringsdefinities", SprintWorkState.Done, doneOn: Monday, pulls: [Pull("4685", LinkedPullRequestState.Merged, Monday)]),
            Member("Categorieën", SprintWorkState.Done, goal: true, doneOn: Tuesday) with { Note = "Import?", OpenQuestion = true },
            Member("Old done", SprintWorkState.Done, doneOn: new DateOnly(2026, 10, 8)),
            Member("Klantrekening", SprintWorkState.InProgress, goal: true, joinedOn: Monday, pulls: [Pull("4707", LinkedPullRequestState.Open, Monday)]),
            Member("Signalering uitwerken", SprintWorkState.InProgress, goal: true, startedOn: Tuesday),
            Member("Fix specs", SprintWorkState.InProgress, goal: true, startedOn: Start.AddDays(1), pulls: [Pull("4710", LinkedPullRequestState.Draft, Start)])
                with { Note = "Align with demo?", OpenQuestion = true },
            Member("Local dev platform", SprintWorkState.InProgress, startedOn: Start, pulls: [Pull("4572", LinkedPullRequestState.Open, new DateOnly(2026, 10, 7))]),
            Member("Openspec", SprintWorkState.Blocked, goal: true) with { BlockedSince = new DateOnly(2026, 10, 9) },
            Member("Grootboek", SprintWorkState.NotStarted)
        ];

        return Progress(today, members) with { DonePerWorkingDay = 3.0 / 7 };
    }

    private static SprintProgressDto Progress(DateOnly today, SprintMemberDto[] members)
    {
        var goal = members.Where(member => member.IsGoal).ToList();
        var elapsed = Enumerable.Range(0, today.DayNumber - Start.DayNumber + 1)
            .Select(Start.AddDays)
            .Count(day => day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday));

        return new SprintProgressDto(
            Guid.NewGuid(), "Fincent", 42, "Sprint 42", Start, Start.AddDays(13), "Ship it", true, members,
            [GoalRuleDto.MarkedAsGoal()], [.. goal.Select(member => member.TaskId)], Counts(goal), Counts(members),
            100, goal.Count > 0 && goal.All(member => member.State is SprintWorkState.Done), 10, elapsed, 0, today);
    }

    private static SprintCountsDto Counts(IReadOnlyCollection<SprintMemberDto> members) =>
        new(
            members.Count(member => member.State is SprintWorkState.Done),
            members.Count(member => member.State is SprintWorkState.InProgress),
            members.Count(member => member.State is SprintWorkState.Blocked),
            members.Count(member => member.State is SprintWorkState.NotStarted),
            members.Count(member => member.AddedAfterStart));

    private static SprintMemberDto Member(
        string title,
        SprintWorkState state,
        bool goal = false,
        DateOnly? doneOn = null,
        DateOnly? startedOn = null,
        DateOnly? joinedOn = null,
        IReadOnlyList<LinkedPullRequestDto>? pulls = null) =>
        new(Guid.NewGuid(), title, SprintTaskStatus.InProgress, state, null, "fincent", doneOn, pulls ?? [], [], goal, true,
            joinedOn is { } joined && joined > Start, null, false, null, startedOn, null, joinedOn ?? Start);

    private static LinkedPullRequestDto Pull(string number, LinkedPullRequestState state, DateOnly openedOn) =>
        new("innovadis-dev/Fincent", number, state, state is LinkedPullRequestState.Merged ? openedOn : null, "jsdotnet", openedOn);
}
