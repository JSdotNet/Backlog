using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Modules.Sessions.UnitTests;

/// <summary>
/// The six causes of local ADR 0051 §5 read from the schedule and loop readings: which state
/// raises an alert, the key that makes one cause one alert, and what the alert offers to open.
/// </summary>
public sealed class SessionAlertsTests
{
    private static readonly DateTimeOffset Due = new(2026, 11, 11, 5, 30, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private static ScheduledRunReport Report(ScheduledRunEvent @event, string session, string? verdict = null, IReadOnlyList<string>? needsYou = null, string? heldBy = null) =>
        new("issue-sweep", "Issue sweep", "JSdotNet/Backlog", "30 5 * * *", session, @event, verdict, needsYou, heldBy);

    private static ScheduleRecord Recorded(params ScheduledRunReport[] reports)
    {
        Schedule? schedule = null;
        var runs = new Dictionary<string, ScheduledRun>(StringComparer.Ordinal);
        var at = Due;

        foreach (var report in reports)
        {
            var (next, run) = ScheduleReports.Apply(schedule, runs.GetValueOrDefault(report.SessionId), report, at);
            schedule = next;
            runs[run.SessionId] = run;
            at = at.AddMinutes(1);
        }

        return new ScheduleRecord(schedule!, [.. runs.Values]);
    }

    private static IReadOnlyList<SessionAlert> Alerts(ScheduleRecord record, DateTimeOffset? now = null, Func<string, bool>? archived = null) =>
        SessionAlerts.ForSchedule(ScheduleReading.Read(record, archived ?? (_ => false), now ?? Due.AddHours(1), Utc), Utc);

    private static LoopSessionReading Loop(LoopKind kind, LoopState state, string session = "loop-1", DateTimeOffset? at = null, int heartbeat = 30) =>
        new(
            new LoopSession(new LoopCheckIn(session, kind, LoopActivity.Listening, heartbeat, LoopHeldWork.None, at ?? Due), LoopCommand.None, null),
            state,
            state == LoopState.NotRunning ? LoopProcess.Archived : LoopProcess.Alive);

    // --- Schedules ---------------------------------------------------------------------------

    [Fact]
    public void A_run_that_needs_you_raises_one_alert_with_its_lines_and_open_run_and_open_schedules()
    {
        var alert = Assert.Single(Alerts(Recorded(
            Report(ScheduledRunEvent.Started, "run-1"),
            Report(ScheduledRunEvent.Finished, "run-1", "needs-you", ["Review two pull requests", "Follow up on two issues"]))));

        Assert.Equal(SessionAlertCause.NeedsYou, alert.Cause);
        Assert.Equal("Issue sweep needs you", alert.Title);
        Assert.Equal("Review two pull requests; Follow up on two issues", alert.Summary);
        Assert.Equal("JSdotNet/Backlog · Schedules", alert.Place);
        Assert.Equal(
            [new SessionAlertAction("Open run", SessionAlertTarget.Run, "run-1"), new SessionAlertAction("Open Schedules", SessionAlertTarget.Schedules, ScheduleId: "issue-sweep")],
            alert.Actions);
        Assert.False(alert.IsCritical);
    }

    [Fact]
    public void A_needs_you_alerts_summary_is_plain_text_and_its_body_keeps_the_markdown()
    {
        var alert = Assert.Single(Alerts(Recorded(
            Report(ScheduledRunEvent.Started, "run-1"),
            Report(ScheduledRunEvent.Finished, "run-1", "needs-you", ["Review [#1751](https://github.com/JSdotNet/Backlog/pull/1751) first", "Then merge **plan/x** with `gh`"]))));

        Assert.Equal("Review #1751 first; Then merge plan/x with gh", alert.Summary);
        Assert.Contains("[#1751](https://github.com/JSdotNet/Backlog/pull/1751)", alert.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_schedule_alert_leads_its_place_with_the_schedules_repository()
    {
        var held = Alerts(Recorded(
            Report(ScheduledRunEvent.Started, "run-1"),
            Report(ScheduledRunEvent.Finished, "run-1", "needs-you", ["Review it"]),
            Report(ScheduledRunEvent.Skipped, "run-2", heldBy: "run-1")));
        var failed = Alerts(Recorded(Report(ScheduledRunEvent.Started, "run-1"), Report(ScheduledRunEvent.Failed, "run-1", "broke")));

        Assert.NotEmpty(held);
        Assert.All(held.Concat(failed), alert => Assert.Equal("JSdotNet/Backlog · Schedules", alert.Place));
    }

    [Theory]
    [InlineData("JSdotNet/Backlog", "JSdotNet/Backlog · Schedules")]
    [InlineData("  ", "Schedules")]
    [InlineData(null, "Schedules")]
    public void A_schedules_place_is_its_repository_then_Schedules_or_Schedules_alone(string? repository, string place) =>
        Assert.Equal(place, SessionAlerts.SchedulePlace(repository));

    [Fact]
    public void A_loops_alert_names_no_repository()
    {
        var alert = SessionAlerts.ForLoop(Loop(LoopKind.Dispatcher, LoopState.Stalled), LoopWaitingWork.None, Utc);

        Assert.Equal(SessionAlerts.SessionsPlace, alert!.Place);
    }

    [Fact]
    public void A_failed_run_raises_a_critical_alert_with_its_error()
    {
        var alert = Assert.Single(Alerts(Recorded(
            Report(ScheduledRunEvent.Started, "run-1"),
            Report(ScheduledRunEvent.Failed, "run-1", "gh: authentication required"))));

        Assert.Equal(SessionAlertCause.Failed, alert.Cause);
        Assert.Equal("gh: authentication required", alert.Summary);
        Assert.True(alert.IsCritical);
    }

    [Fact]
    public void A_run_ready_for_review_or_still_running_raises_nothing()
    {
        Assert.Empty(Alerts(Recorded(Report(ScheduledRunEvent.Started, "run-1"), Report(ScheduledRunEvent.Finished, "run-1", "review-ready"))));
        Assert.Empty(Alerts(Recorded(Report(ScheduledRunEvent.Started, "run-1"))));
    }

    [Fact]
    public void Archiving_the_run_clears_its_alert()
    {
        var record = Recorded(Report(ScheduledRunEvent.Started, "run-1"), Report(ScheduledRunEvent.Finished, "run-1", "needs-you", ["Review"]));

        Assert.Empty(Alerts(record, archived: session => session == "run-1"));
    }

    [Fact]
    public void A_held_schedules_later_skips_keep_the_one_alert_of_the_streak()
    {
        var first = Recorded(
            Report(ScheduledRunEvent.Started, "run-1"),
            Report(ScheduledRunEvent.Finished, "run-1", "needs-you", ["Review"]),
            Report(ScheduledRunEvent.Skipped, "run-2"));
        var again = Recorded(
            Report(ScheduledRunEvent.Started, "run-1"),
            Report(ScheduledRunEvent.Finished, "run-1", "needs-you", ["Review"]),
            Report(ScheduledRunEvent.Skipped, "run-2"),
            Report(ScheduledRunEvent.Skipped, "run-3"));

        var held = Assert.Single(Alerts(first), alert => alert.Cause == SessionAlertCause.Held);
        var heldAgain = Assert.Single(Alerts(again), alert => alert.Cause == SessionAlertCause.Held);

        Assert.Equal(held.Key, heldAgain.Key);
        Assert.Equal("Issue sweep is held", held.Title);
        Assert.Equal("run-2", held.Actions[0].SessionId);
    }

    [Fact]
    public void A_skip_that_names_what_held_it_is_keyed_on_that_run()
    {
        var alert = Assert.Single(Alerts(Recorded(Report(ScheduledRunEvent.Skipped, "run-2", heldBy: "run-1"))));

        Assert.Equal(SessionAlertCause.Held, alert.Cause);
        Assert.Equal("run-1", alert.Actions[0].SessionId);
    }

    [Fact]
    public void A_missed_due_time_raises_one_alert_that_clears_once_a_run_starts()
    {
        var record = Recorded(Report(ScheduledRunEvent.Started, "run-1"), Report(ScheduledRunEvent.Finished, "run-1", "review-ready"));
        var nextDay = Due.AddDays(1);

        var missed = Assert.Single(Alerts(record, nextDay.AddMinutes(31)));
        var stillMissed = Assert.Single(Alerts(record, nextDay.AddMinutes(50)));

        Assert.Equal(SessionAlertCause.Missed, missed.Cause);
        Assert.Equal(missed.Key, stillMissed.Key);
        Assert.Equal("It was due at 05:30 and no run has started.", missed.Summary);
        Assert.Equal([new SessionAlertAction("Open Schedules", SessionAlertTarget.Schedules, ScheduleId: "issue-sweep")], missed.Actions);

        var started = Recorded(
            Report(ScheduledRunEvent.Started, "run-1"),
            Report(ScheduledRunEvent.Finished, "run-1", "review-ready"),
            Report(ScheduledRunEvent.Started, "run-2"));
        Assert.Empty(Alerts(started, started.Runs.Max(run => run.FirstReportedAt).AddMinutes(1)));
    }

    // --- Loops -------------------------------------------------------------------------------

    [Fact]
    public void A_stalled_loop_raises_one_alert_keyed_on_its_last_check_in()
    {
        var alert = SessionAlerts.ForLoop(Loop(LoopKind.Dispatcher, LoopState.Stalled), LoopWaitingWork.None, Utc);

        Assert.NotNull(alert);
        Assert.Equal(SessionAlertCause.Stalled, alert.Cause);
        Assert.Equal("Session dispatcher stalled", alert.Title);
        Assert.Equal("No check-in since 05:30, past its 30-minute heartbeat.", alert.Summary);
        Assert.Equal([new SessionAlertAction("Open Sessions", SessionAlertTarget.Sessions, "loop-1")], alert.Actions);

        var checkedInAgain = SessionAlerts.ForLoop(Loop(LoopKind.Dispatcher, LoopState.Stalled, at: Due.AddMinutes(5)), LoopWaitingWork.None, Utc);
        Assert.NotEqual(alert.Key, checkedInAgain!.Key);
    }

    [Theory]
    [InlineData(LoopKind.Dispatcher, 1, "The **session dispatcher** is still running, but it has not checked in since 05:30: its heartbeat is 1 minute.")]
    [InlineData(LoopKind.BrainTender, 30, "The **brain tender** is still running, but it has not checked in since 05:30: its heartbeat is 30 minutes.")]
    public void A_stalled_loops_body_names_the_loop_once_and_counts_its_heartbeat(LoopKind kind, int heartbeat, string expected)
    {
        var alert = SessionAlerts.ForLoop(Loop(kind, LoopState.Stalled, heartbeat: heartbeat), LoopWaitingWork.None, Utc);

        Assert.StartsWith(expected, alert!.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void A_dispatcher_not_running_while_requests_wait_raises_a_critical_alert_that_counts_them()
    {
        var alert = SessionAlerts.ForLoop(Loop(LoopKind.Dispatcher, LoopState.NotRunning), new LoopWaitingWork(3, 0), Utc);

        Assert.NotNull(alert);
        Assert.Equal(SessionAlertCause.NotRunning, alert.Cause);
        Assert.Equal("Session dispatcher stopped", alert.Title);
        Assert.Equal("3 session requests wait for it. It last checked in at 05:30.", alert.Summary);
        Assert.True(alert.IsCritical);
    }

    [Fact]
    public void A_loop_not_running_with_nothing_waiting_raises_nothing()
    {
        Assert.Null(SessionAlerts.ForLoop(Loop(LoopKind.Dispatcher, LoopState.NotRunning), LoopWaitingWork.None, Utc));
        Assert.Null(SessionAlerts.ForLoop(Loop(LoopKind.BrainTender, LoopState.NotRunning), new LoopWaitingWork(3, 1), Utc));
    }

    [Fact]
    public void A_loop_whose_session_the_reading_does_not_hold_raises_no_stalled_alert()
    {
        var unseen = Loop(LoopKind.Dispatcher, LoopState.Stalled) with { Process = LoopProcess.Unknown };

        Assert.Null(SessionAlerts.ForLoop(unseen, LoopWaitingWork.None, Utc));
    }

    [Fact]
    public void A_needs_you_alert_lists_twenty_lines_cut_short_and_counts_the_rest()
    {
        var lines = Enumerable.Range(1, ScheduleReports.MaxNeedsYouLines).Select(i => i + new string('x', ScheduleReports.MaxText - 3)).ToList();

        var alert = Assert.Single(Alerts(Recorded(Report(ScheduledRunEvent.Finished, "run-1", "needs-you", lines))));

        Assert.Contains("- …and 30 more", alert.Body, StringComparison.Ordinal);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(alert.Body) < 32 * 1024);
        Assert.True(alert.Summary.Length <= 180);
    }

    [Fact]
    public void A_listening_or_holding_loop_raises_nothing()
    {
        Assert.Null(SessionAlerts.ForLoop(Loop(LoopKind.Dispatcher, LoopState.Listening), new LoopWaitingWork(3, 0), Utc));
        Assert.Null(SessionAlerts.ForLoop(Loop(LoopKind.Dispatcher, LoopState.Holding), new LoopWaitingWork(3, 0), Utc));
    }

    [Fact]
    public void Only_the_dispatcher_that_checked_in_last_is_read_for_an_alert()
    {
        var replaced = Loop(LoopKind.Dispatcher, LoopState.NotRunning, "old", Due);
        var current = Loop(LoopKind.Dispatcher, LoopState.Listening, "new", Due.AddMinutes(10));

        var alerts = SessionAlerts.Read([], [replaced, current], _ => new LoopWaitingWork(2, 0), Utc);

        Assert.Empty(alerts);
    }

    [Fact]
    public void A_clean_run_after_a_failure_clears_the_failed_alert_and_names_itself_as_its_reason()
    {
        var record = Recorded(
            Report(ScheduledRunEvent.Started, "run-1"),
            Report(ScheduledRunEvent.Failed, "run-1", "gh: authentication required"),
            Report(ScheduledRunEvent.Started, "run-2"),
            Report(ScheduledRunEvent.Finished, "run-2", "review-ready"));
        var reading = ScheduleReading.Read(record, _ => false, Due.AddHours(1), Utc);

        Assert.Empty(SessionAlerts.ForSchedule(reading, Utc));
        Assert.Equal(
            new Dictionary<string, string> { ["failed:schedule:issue-sweep:run-1"] = "Handled: the Wed 11 Nov 05:32 run finished Review ready" },
            SessionAlerts.Recoveries([reading], Utc));
    }

    [Fact]
    public void A_failed_run_cleared_by_archiving_has_no_recovery_reason()
    {
        var record = Recorded(
            Report(ScheduledRunEvent.Failed, "run-1", "gh: authentication required"),
            Report(ScheduledRunEvent.Finished, "run-2", "review-ready"));

        Assert.Empty(SessionAlerts.Recoveries([ScheduleReading.Read(record, id => id == "run-1", Due.AddHours(1), Utc)], Utc));
    }

    [Fact]
    public void A_failed_alert_says_a_recovering_run_closes_it_too()
    {
        var alert = Assert.Single(Alerts(Recorded(Report(ScheduledRunEvent.Failed, "run-1", "Out of disk"))));

        Assert.EndsWith("The alert closes once the run's session is archived, or once a later run of the schedule finishes Review ready.", alert.Body, StringComparison.Ordinal);
    }

    // --- How far back the session reading reaches (local ADR 0051 §5) ---------------------

    private static readonly DateTimeOffset Now = Due.AddDays(100);

    private static ScheduledRun Run(
        string session,
        DateTimeOffset at,
        string? verdict = "review-ready",
        IReadOnlyList<string>? needsYou = null,
        bool failed = false,
        bool skipped = false,
        string? heldBy = null) =>
        new(session, "issue-sweep", at, at, skipped ? null : at.AddMinutes(10), verdict, needsYou ?? [], failed, skipped, heldBy);

    private static ScheduleRecord Scheduled(params ScheduledRun[] runs) =>
        new(new Schedule("issue-sweep", "Issue sweep", "JSdotNet/Backlog", "30 5 * * *", runs.Min(run => run.FirstReportedAt), runs.Max(run => run.FirstReportedAt)), runs);

    private static LoopSession CheckedIn(string session, LoopKind kind, DateTimeOffset at) =>
        new(new LoopCheckIn(session, kind, LoopActivity.Listening, 30, LoopHeldWork.None, at), LoopCommand.None, null);

    private static DateTimeOffset Horizon(IEnumerable<ScheduleRecord> schedules, IEnumerable<LoopSession>? loops = null, Func<string, bool>? archived = null) =>
        SessionAlerts.Horizon(schedules, loops ?? [], archived ?? (_ => false), Now, Utc);

    [Fact]
    public void With_nothing_reported_the_reading_reaches_back_a_day()
    {
        Assert.Equal(Now.AddDays(-1), Horizon([]));
    }

    [Fact]
    public void A_schedules_latest_run_is_read_and_its_finished_runs_before_it_are_not()
    {
        var record = Scheduled(Run("run-old", Now.AddDays(-90)), Run("run-new", Now.AddDays(-1)));

        Assert.Equal(Now.AddDays(-2), Horizon([record]));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_needs_you_or_failed_run_not_known_archived_is_read_however_old(bool failed)
    {
        var old = failed
            ? Run("run-old", Now.AddDays(-90), "gh: authentication required", failed: true)
            : Run("run-old", Now.AddDays(-90), "needs-you", ["Review it"]);
        var record = Scheduled(old, Run("run-new", Now.AddDays(-1), "needs-you", ["And this"]));

        Assert.Equal(Now.AddDays(-91), Horizon([record]));
        Assert.Equal(Now.AddDays(-2), Horizon([record], archived: id => id == "run-old"));
    }

    [Fact]
    public void A_failed_run_a_later_run_recovered_is_not_read()
    {
        var record = Scheduled(
            Run("run-failed", Now.AddDays(-90), "gh: authentication required", failed: true),
            Run("run-clean", Now.AddDays(-89)),
            Run("run-new", Now.AddDays(-1)));

        Assert.Equal(Now.AddDays(-2), Horizon([record]));
    }

    [Fact]
    public void The_run_holding_the_latest_skip_is_read_until_it_is_known_archived()
    {
        var record = Scheduled(Run("run-holder", Now.AddDays(-50)), Run("run-skip", Now.AddDays(-1), verdict: null, skipped: true, heldBy: "run-holder"));

        Assert.Equal(Now.AddDays(-51), Horizon([record]));
        Assert.Equal(Now.AddDays(-2), Horizon([record], archived: id => id == "run-holder"));
    }

    [Fact]
    public void Only_the_latest_check_in_of_each_loop_kind_is_read()
    {
        IReadOnlyList<LoopSession> loops =
        [
            CheckedIn("manager-old", LoopKind.Dispatcher, Now.AddDays(-60)),
            CheckedIn("manager-new", LoopKind.Dispatcher, Now.AddHours(-1)),
            CheckedIn("brain", LoopKind.BrainTender, Now.AddDays(-10))
        ];

        Assert.Equal(Now.AddDays(-11), Horizon([], loops));
    }

    [Theory]
    [InlineData("needs-you:schedule:sweep:run-1", "Run archived")]
    [InlineData("failed:schedule:sweep:run-1", "Run archived")]
    [InlineData("missed:schedule:sweep:2026-11-11T05:30:00.0000000Z", "A run started")]
    [InlineData("stalled:loop:loop-1:2026-11-11T05:30:00.0000000Z", "Checked in again, or its process ended")]
    [InlineData("something-else", "Cleared")]
    public void A_cleared_alert_closes_with_the_reason_its_cause_reads(string key, string reason)
    {
        Assert.Equal(reason, SessionAlerts.ClosedReason(key));
    }
}
