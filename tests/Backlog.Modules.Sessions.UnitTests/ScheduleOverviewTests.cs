using Backlog.Modules.Sessions.Abstractions;
using static Backlog.Modules.Sessions.UnitTests.ScheduleFixtures;

namespace Backlog.Modules.Sessions.UnitTests;

/// <summary>
/// The rules the Schedules page reads its schedules by: which lead the page, what each
/// Show filter keeps, how the table groups them by repository, what holds a held schedule,
/// and the cadence a cron reads as.
/// </summary>
public sealed class ScheduleOverviewTests
{
    [Theory]
    [InlineData(ScheduledRunState.NeedsYou, true)]
    [InlineData(ScheduledRunState.Held, true)]
    [InlineData(ScheduledRunState.Failed, false)]
    [InlineData(ScheduledRunState.Missed, false)]
    [InlineData(ScheduledRunState.ReviewReady, false)]
    [InlineData(ScheduledRunState.Handled, false)]
    [InlineData(ScheduledRunState.NoSummaryYet, false)]
    public void Needs_you_and_held_lead_the_page(ScheduledRunState state, bool leads) =>
        Assert.Equal(leads, ScheduleOverview.NeedsYou(state));

    [Theory]
    [InlineData(ScheduleFilter.All, ScheduledRunState.Handled, true)]
    [InlineData(ScheduleFilter.NeedsYou, ScheduledRunState.Held, true)]
    [InlineData(ScheduleFilter.NeedsYou, ScheduledRunState.ReviewReady, false)]
    [InlineData(ScheduleFilter.ReviewReady, ScheduledRunState.ReviewReady, true)]
    [InlineData(ScheduleFilter.ReviewReady, ScheduledRunState.NeedsYou, false)]
    [InlineData(ScheduleFilter.FailedOrMissed, ScheduledRunState.Failed, true)]
    [InlineData(ScheduleFilter.FailedOrMissed, ScheduledRunState.Missed, true)]
    [InlineData(ScheduleFilter.FailedOrMissed, ScheduledRunState.NoSummaryYet, false)]
    public void Each_filter_keeps_its_states(ScheduleFilter filter, ScheduledRunState state, bool kept) =>
        Assert.Equal(kept, ScheduleOverview.Matches(filter, state));

    [Fact]
    public void Counts_follow_the_filters()
    {
        ScheduleReadingResult[] schedules =
        [
            Schedule("a", "A", "r", ScheduledRunState.NeedsYou),
            Schedule("b", "B", "r", ScheduledRunState.Held),
            Schedule("c", "C", "r", ScheduledRunState.ReviewReady),
            Schedule("d", "D", "r", ScheduledRunState.Missed),
            Schedule("e", "E", "r", ScheduledRunState.Handled),
        ];

        Assert.Equal(5, ScheduleOverview.Count(schedules, ScheduleFilter.All));
        Assert.Equal(2, ScheduleOverview.Count(schedules, ScheduleFilter.NeedsYou));
        Assert.Equal(1, ScheduleOverview.Count(schedules, ScheduleFilter.ReviewReady));
        Assert.Equal(1, ScheduleOverview.Count(schedules, ScheduleFilter.FailedOrMissed));
    }

    [Fact]
    public void Groups_are_by_repository_in_name_order_with_the_unnamed_last()
    {
        var groups = ScheduleOverview.Groups(
        [
            Schedule("1", "Weekly update", "JSdotNet/Backlog", ScheduledRunState.Handled),
            Schedule("2", null, null, ScheduledRunState.Handled),
            Schedule("3", "Issue sweep", "jsdotnet/backlog", ScheduledRunState.Held),
            Schedule("4", "Package update", "finance", ScheduledRunState.Handled),
        ]);

        Assert.Equal(["finance", "JSdotNet/Backlog", null], groups.Select(group => group.Repository));
        Assert.Equal(["Issue sweep", "Weekly update"], groups[1].Schedules.Select(schedule => ScheduleOverview.Name(schedule.Schedule)));
        Assert.Equal("2", ScheduleOverview.Name(Assert.Single(groups[2].Schedules).Schedule));
    }

    [Fact]
    public void A_held_schedule_is_held_by_the_run_its_latest_skip_named()
    {
        var reading = Schedule(
            "sweep", "Issue sweep", "JSdotNet/Backlog", ScheduledRunState.Held, runs:
            [
                Run("tue", ScheduledRunState.Held, Now.AddDays(-1), skipped: true, heldBy: "fri"),
                Run("mon", ScheduledRunState.Held, Now.AddDays(-2), skipped: true, heldBy: "fri"),
                Run("fri", ScheduledRunState.NeedsYou, Now.AddDays(-5), needsYou: ["Answer the proposals"]),
                Run("thu", ScheduledRunState.Handled, Now.AddDays(-6)),
            ]);

        Assert.Equal("fri", ScheduleOverview.Holder(reading)?.Run.SessionId);
        Assert.Equal(2, ScheduleOverview.SkippedInARow(reading));
    }

    [Fact]
    public void A_held_schedule_whose_skip_named_no_run_is_held_by_the_newest_run_that_did_not_skip()
    {
        var reading = Schedule(
            "sweep", "Issue sweep", "JSdotNet/Backlog", ScheduledRunState.Held, runs:
            [
                Run("tue", ScheduledRunState.Held, Now.AddDays(-1), skipped: true),
                Run("fri", ScheduledRunState.NeedsYou, Now.AddDays(-5)),
            ]);

        Assert.Equal("fri", ScheduleOverview.Holder(reading)?.Run.SessionId);
    }

    [Fact]
    public void A_held_schedule_whose_runs_are_all_handled_or_skipped_names_no_holder()
    {
        var reading = Schedule(
            "sweep", "Issue sweep", "JSdotNet/Backlog", ScheduledRunState.Held, runs:
            [
                Run("tue", ScheduledRunState.Held, Now.AddDays(-1), skipped: true),
                Run("fri", ScheduledRunState.Handled, Now.AddDays(-5)),
            ]);

        Assert.Null(ScheduleOverview.Holder(reading));
    }

    [Fact]
    public void A_schedule_that_is_not_held_has_no_holder()
    {
        var reading = Schedule(
            "update", "Weekly update", "JSdotNet/Backlog", ScheduledRunState.NeedsYou, runs:
            [Run("sun", ScheduledRunState.NeedsYou, Now.AddDays(-3))]);

        Assert.Null(ScheduleOverview.Holder(reading));
        Assert.Equal(0, ScheduleOverview.SkippedInARow(reading));
    }

    [Theory]
    [InlineData("30 5 * * 1-5", "Mon–Fri 05:30")]
    [InlineData("0 18 * * 0", "Sun 18:00")]
    [InlineData("0 18 * * 7", "Sun 18:00")]
    [InlineData("0 4 * * *", "Daily 04:00")]
    [InlineData("0 4 * * 0-6", "Daily 04:00")]
    [InlineData("0 9 * * 0-7", "Daily 09:00")]
    [InlineData("0 9 * * 1-7", "Daily 09:00")]
    [InlineData("0 5 * * SAT", "Sat 05:00")]
    [InlineData("0 5 * * MON-FRI", "Mon–Fri 05:00")]
    [InlineData("15 9 * * 1,3,5", "Mon, Wed, Fri 09:15")]
    [InlineData("0 6 1 * *", "Monthly on day 1 06:00")]
    [InlineData("10 * * * *", "Hourly at :10")]
    [InlineData("0 */4 * * *", "Every 4 hours at :00")]
    [InlineData("*/15 * * * *", "Every 15 min")]
    [InlineData("0 8,12 * * *", "0 8,12 * * *")]
    [InlineData("0 5 * 1 *", "0 5 * 1 *")]
    [InlineData("not a cron", "not a cron")]
    [InlineData(null, ScheduleCadence.Unknown)]
    [InlineData("  ", ScheduleCadence.Unknown)]
    public void A_cron_reads_as_its_cadence(string? cron, string cadence) =>
        Assert.Equal(cadence, ScheduleCadence.Describe(cron));
}
