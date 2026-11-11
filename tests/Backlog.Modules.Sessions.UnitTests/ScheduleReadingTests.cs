using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Modules.Sessions.UnitTests;

/// <summary>
/// The seven states of local ADR 0051 §2, read from a schedule's run reports, the desktop
/// record's archived flag and the clock — every state, the order they are decided in, and
/// Missed's 30-minute grace on the schedule's cron.
/// </summary>
public sealed class ScheduleReadingTests
{
    /// <summary>Wednesday 11 November 2026, 05:30 UTC: the weekly update's due time.</summary>
    private static readonly DateTimeOffset Due = new(2026, 11, 11, 5, 30, 0, TimeSpan.Zero);

    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private static readonly Func<string, bool> NothingArchived = _ => false;

    private static ScheduledRunReport Report(
        ScheduledRunEvent @event,
        string session = "run-1",
        string? verdict = null,
        IReadOnlyList<string>? needsYou = null,
        string? heldBy = null,
        string? cron = "30 5 * * 3") =>
        new("weekly-update", "Weekly update", "JSdotNet/Backlog", cron, session, @event, verdict, needsYou, heldBy);

    /// <summary>The schedule after <paramref name="reports"/>, each applied a minute after the one before, from <see cref="Due"/>.</summary>
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

    private static ScheduleReadingResult Read(ScheduleRecord record, DateTimeOffset? now = null, Func<string, bool>? archived = null) =>
        ScheduleReading.Read(record, archived ?? NothingArchived, now ?? Due.AddHours(1), Utc);

    [Fact]
    public void A_run_that_started_and_has_not_finished_reads_no_summary_yet()
    {
        var reading = Read(Recorded(Report(ScheduledRunEvent.Started)));

        Assert.Equal(ScheduledRunState.NoSummaryYet, reading.State);
        Assert.Equal(ScheduledRunState.NoSummaryYet, Assert.Single(reading.Runs).State);
    }

    [Fact]
    public void A_run_that_finished_with_needs_you_lines_reads_needs_you()
    {
        var reading = Read(Recorded(
            Report(ScheduledRunEvent.Started),
            Report(ScheduledRunEvent.Finished, verdict: "needs-you", needsYou: ["Review two pull requests", "Look at a flaky test run"])));

        Assert.Equal(ScheduledRunState.NeedsYou, reading.State);
        Assert.Equal(["Review two pull requests", "Look at a flaky test run"], Assert.Single(reading.Runs).Run.NeedsYou);
    }

    [Theory]
    [InlineData("needs-you")]
    [InlineData("blocked")]
    [InlineData("Needs_You")]
    public void A_run_whose_verdict_says_it_needs_the_person_reads_needs_you_with_no_lines(string verdict)
    {
        var reading = Read(Recorded(Report(ScheduledRunEvent.Finished, verdict: verdict)));

        Assert.Equal(ScheduledRunState.NeedsYou, reading.State);
    }

    [Theory]
    [InlineData("review-ready")]
    [InlineData(null)]
    public void A_run_that_finished_with_nothing_asked_of_the_person_reads_review_ready(string? verdict)
    {
        var reading = Read(Recorded(Report(ScheduledRunEvent.Started), Report(ScheduledRunEvent.Finished, verdict: verdict)));

        Assert.Equal(ScheduledRunState.ReviewReady, reading.State);
    }

    [Fact]
    public void A_run_that_failed_reads_failed_even_with_needs_you_lines()
    {
        var reading = Read(Recorded(
            Report(ScheduledRunEvent.Started),
            Report(ScheduledRunEvent.Failed, verdict: "The worktree could not be created.", needsYou: ["Free some disk"])));

        Assert.Equal(ScheduledRunState.Failed, reading.State);
        Assert.Equal("The worktree could not be created.", Assert.Single(reading.Runs).Run.Verdict);
    }

    [Fact]
    public void A_run_that_skipped_itself_reads_held_and_holds_the_schedule()
    {
        var reading = Read(Recorded(
            Report(ScheduledRunEvent.Started, session: "friday"),
            Report(ScheduledRunEvent.Finished, session: "friday", needsYou: ["Answer the proposals"]),
            Report(ScheduledRunEvent.Started, session: "monday"),
            Report(ScheduledRunEvent.Skipped, session: "monday", heldBy: "friday")));

        Assert.Equal(ScheduledRunState.Held, reading.State);
        Assert.Equal(["monday", "friday"], reading.Runs.Select(run => run.Run.SessionId));
        Assert.Equal([ScheduledRunState.Held, ScheduledRunState.NeedsYou], reading.Runs.Select(run => run.State));
    }

    [Fact]
    public void A_skipped_run_that_also_reports_its_finish_stays_held()
    {
        var reading = Read(Recorded(
            Report(ScheduledRunEvent.Started, session: "monday"),
            Report(ScheduledRunEvent.Skipped, session: "monday", heldBy: "friday"),
            Report(ScheduledRunEvent.Finished, session: "monday", verdict: "skipped")));

        Assert.Equal(ScheduledRunState.Held, reading.State);
    }

    [Fact]
    public void A_held_run_reads_handled_once_the_run_that_held_it_is_archived()
    {
        var record = Recorded(
            Report(ScheduledRunEvent.Finished, session: "friday", needsYou: ["Answer the proposals"]),
            Report(ScheduledRunEvent.Skipped, session: "monday", heldBy: "friday"));

        var reading = Read(record, archived: id => id == "friday");

        Assert.Equal(ScheduledRunState.Handled, reading.State);
        Assert.All(reading.Runs, run => Assert.Equal(ScheduledRunState.Handled, run.State));
    }

    [Theory]
    [InlineData(ScheduledRunEvent.Started)]
    [InlineData(ScheduledRunEvent.Finished)]
    [InlineData(ScheduledRunEvent.Failed)]
    [InlineData(ScheduledRunEvent.Skipped)]
    public void A_run_whose_session_is_archived_reads_handled_whatever_it_reported(ScheduledRunEvent last)
    {
        var record = Recorded(Report(ScheduledRunEvent.Started), Report(last, needsYou: ["Something"]));

        var reading = Read(record, archived: id => id == "run-1");

        Assert.Equal(ScheduledRunState.Handled, reading.State);
    }

    [Fact]
    public void A_finish_without_a_start_is_kept_and_read()
    {
        var reading = Read(Recorded(Report(ScheduledRunEvent.Finished, verdict: "review-ready")));

        var run = Assert.Single(reading.Runs);
        Assert.Null(run.Run.StartedAt);
        Assert.Equal(ScheduledRunState.ReviewReady, run.State);
    }

    [Fact]
    public void A_schedule_due_at_0530_reads_missed_at_0601_with_no_start_since()
    {
        // Its last run started a week before, at the previous Wednesday's 05:30.
        var lastWeek = Due.AddDays(-7);
        var (schedule, run) = ScheduleReports.Apply(null, null, Report(ScheduledRunEvent.Started), lastWeek);
        (schedule, run) = ScheduleReports.Apply(schedule, run, Report(ScheduledRunEvent.Finished), lastWeek.AddMinutes(20));
        var record = new ScheduleRecord(schedule, [run]);

        var inGrace = Read(record, now: Due.AddMinutes(29));
        var atGraceEnd = Read(record, now: Due.AddMinutes(30));
        var past = Read(record, now: Due.AddMinutes(31));

        Assert.Equal(ScheduledRunState.ReviewReady, inGrace.State);
        Assert.Equal(ScheduledRunState.Missed, atGraceEnd.State);
        Assert.Equal(ScheduledRunState.Missed, past.State);
        Assert.Equal(Due, past.DueAt);
        Assert.Equal(Due.AddDays(7), past.NextRunAt);
    }

    [Fact]
    public void Missed_is_the_schedules_alone_and_its_runs_keep_their_own_states()
    {
        var lastWeek = Due.AddDays(-7);
        var (schedule, run) = ScheduleReports.Apply(null, null, Report(ScheduledRunEvent.Finished, needsYou: ["Review"]), lastWeek);

        var reading = Read(new ScheduleRecord(schedule, [run]), now: Due.AddHours(2), archived: id => id == "run-1");

        Assert.Equal(ScheduledRunState.Missed, reading.State);
        Assert.Equal(ScheduledRunState.Handled, Assert.Single(reading.Runs).State);
    }

    [Fact]
    public void A_start_after_the_due_time_clears_missed()
    {
        var lastWeek = Due.AddDays(-7);
        var (schedule, old) = ScheduleReports.Apply(null, null, Report(ScheduledRunEvent.Finished), lastWeek);
        var (_, late) = ScheduleReports.Apply(schedule, null, Report(ScheduledRunEvent.Started, session: "run-2"), Due.AddMinutes(45));

        var reading = Read(new ScheduleRecord(schedule, [old, late]), now: Due.AddMinutes(50));

        Assert.Equal(ScheduledRunState.NoSummaryYet, reading.State);
        Assert.Equal("run-2", reading.Runs[0].Run.SessionId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("every monday")]
    [InlineData("61 5 * * *")]
    public void A_schedule_with_no_cron_this_reader_understands_never_reads_missed(string? cron)
    {
        var (schedule, run) = ScheduleReports.Apply(null, null, Report(ScheduledRunEvent.Finished, cron: cron), Due.AddDays(-30));

        var reading = Read(new ScheduleRecord(schedule, [run]), now: Due);

        Assert.Equal(ScheduledRunState.ReviewReady, reading.State);
        Assert.Null(reading.DueAt);
        Assert.Null(reading.NextRunAt);
    }

    [Fact]
    public void A_session_is_archived_by_its_own_id_or_its_desktop_id_and_only_when_the_record_says_so()
    {
        AgentSession Session(string id, bool? archived, string? desktopId = null) =>
            new(id, AgentSessionKind.Claude, "dev", "DEV", id, @"D:\Repos\Backlog", null, null, null, Due, AgentSessionState.Finished, null, AgentSessionOrigin.Local)
            {
                Archived = archived,
                DesktopSessionId = desktopId,
            };

        var archived = ScheduleReading.ArchivedIn(new AgentSessionCatalog(
            [Session("a", true, "local_a"), Session("b", false, "local_b"), Session("c", null)], [], 3));

        Assert.True(archived("a"));
        Assert.True(archived("local_a"));
        Assert.False(archived("b"));
        Assert.False(archived("local_b"));
        Assert.False(archived("c"));
        Assert.False(archived("unknown"));
        Assert.False(ScheduleReading.ArchivedIn(null)("a"));
    }

    [Fact]
    public void A_repeated_start_keeps_the_first_and_a_later_report_updates_the_schedule()
    {
        var (schedule, run) = ScheduleReports.Apply(null, null, Report(ScheduledRunEvent.Started), Due);
        (schedule, run) = ScheduleReports.Apply(
            schedule,
            run,
            new ScheduledRunReport("weekly-update", "Weekly update (Wed)", null, null, "run-1", ScheduledRunEvent.Started),
            Due.AddMinutes(3));

        Assert.Equal(Due, run.StartedAt);
        Assert.Equal("Weekly update (Wed)", schedule.Title);
        Assert.Equal("JSdotNet/Backlog", schedule.Repository);
        Assert.Equal("30 5 * * 3", schedule.Cron);
        Assert.Equal(Due, schedule.FirstReportedAt);
        Assert.Equal(Due.AddMinutes(3), schedule.LastReportedAt);
    }

    [Fact]
    public void A_report_is_trimmed_and_refused_without_its_ids()
    {
        var (report, refusal) = ScheduleReports.Normalise(new ScheduledRunReport(
            " weekly-update ", "  ", null, " 30 5 * * 3 ", " run-1 ", ScheduledRunEvent.Finished, " review-ready ", ["  one ", " ", "two"]));

        Assert.Null(refusal);
        Assert.Equal("weekly-update", report!.ScheduleId);
        Assert.Null(report.Title);
        Assert.Equal("30 5 * * 3", report.Cron);
        Assert.Equal("run-1", report.SessionId);
        Assert.Equal("review-ready", report.Verdict);
        Assert.Equal(["one", "two"], report.NeedsYou);

        Assert.Equal("scheduleId is required.", ScheduleReports.Normalise(Report(ScheduledRunEvent.Started) with { ScheduleId = " " }).Refusal);
        Assert.Equal("sessionId is required.", ScheduleReports.Normalise(Report(ScheduledRunEvent.Started) with { SessionId = "" }).Refusal);
    }
}
