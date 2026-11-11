namespace Backlog.Modules.Sessions.Abstractions;

/// <summary>A run with the state it reads as now.</summary>
public sealed record ScheduledRunReading(ScheduledRun Run, ScheduledRunState State);

/// <summary>
/// A schedule with the state it reads as now, its runs newest first, and its due times.
/// </summary>
/// <param name="State">The latest run's state, or <see cref="ScheduledRunState.Missed"/>
/// when the schedule missed a due time since; null for a schedule with no run, which the
/// register never holds.</param>
/// <param name="DueAt">The first due time after the latest run started, or after the
/// schedule's first report with no run; null with no cron this reader understands.</param>
/// <param name="NextRunAt">The next due time after now, on the same terms.</param>
public sealed record ScheduleReadingResult(
    Schedule Schedule,
    ScheduledRunState? State,
    IReadOnlyList<ScheduledRunReading> Runs,
    DateTimeOffset? DueAt,
    DateTimeOffset? NextRunAt);

/// <summary>
/// The states of local ADR 0051 §2, read from the register, the session records and the
/// clock. Never stored, as the Liveness Assessment's are not.
/// <para>
/// <b>A run</b>, in this order:
/// <list type="number">
/// <item><b>Handled</b> when its session is archived, whatever it reported.</item>
/// <item><b>Failed</b> when it reported a failure.</item>
/// <item><b>Held</b> when it reported skipping itself — and Handled instead once the run
/// that held it, where it named one, is archived, because the cause has cleared.</item>
/// <item><b>Needs you</b> when it finished with Needs-you lines, or with a
/// <c>needs-you</c> or <c>blocked</c> verdict.</item>
/// <item><b>Review ready</b> when it finished otherwise: its summary waits for the person.</item>
/// <item><b>No summary yet</b> while it has started and not finished.</item>
/// </list>
/// </para>
/// <para>
/// <b>A schedule</b> reads its latest run's state — the run that began most recently, by
/// its start report, or by its first report where no start arrived — except <b>Missed</b>, which is the schedule's alone: 30 minutes
/// (<see cref="MissedGrace"/>) past the first due time after its latest run began, with no
/// run begun since. A schedule with no cron this reader understands
/// never reads Missed.
/// </para>
/// <para>
/// Whether a session is archived is the desktop record's flag the Sessions reader already
/// reads, <see cref="AgentSession.Archived"/>; a session with no desktop record, or one
/// outside the reading, is not archived.
/// </para>
/// </summary>
public static class ScheduleReading
{
    /// <summary>How long past a due time a schedule waits for a start before it reads Missed.</summary>
    public static readonly TimeSpan MissedGrace = TimeSpan.FromMinutes(30);

    private static readonly string[] NeedsYouVerdicts = ["needs-you", "needs_you", "needs you", "blocked"];

    /// <summary>Every schedule in <paramref name="records"/>, read at <paramref name="now"/>.</summary>
    /// <param name="isArchived">Whether a session id names an archived session.</param>
    /// <param name="zone">The time zone the crons are evaluated in: this machine's.</param>
    public static IReadOnlyList<ScheduleReadingResult> Read(
        IEnumerable<ScheduleRecord> records,
        Func<string, bool> isArchived,
        DateTimeOffset now,
        TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(records);

        return [.. records.Select(record => Read(record, isArchived, now, zone))];
    }

    /// <summary>One schedule, read at <paramref name="now"/>.</summary>
    public static ScheduleReadingResult Read(
        ScheduleRecord record,
        Func<string, bool> isArchived,
        DateTimeOffset now,
        TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(isArchived);
        ArgumentNullException.ThrowIfNull(zone);

        var runs = record.Runs
            .OrderByDescending(Began)
            .ThenByDescending(run => run.SessionId, StringComparer.Ordinal)
            .Select(run => new ScheduledRunReading(run, StateOf(run, isArchived)))
            .ToList();

        var cron = ScheduleCron.TryParse(record.Schedule.Cron);
        var since = runs.Count > 0 ? Began(runs[0].Run) : record.Schedule.FirstReportedAt;
        var due = cron?.NextAfter(since, zone);
        var next = cron?.NextAfter(now, zone);

        ScheduledRunState? state = due is { } dueAt && now >= dueAt + MissedGrace
            ? ScheduledRunState.Missed
            : runs.Count > 0 ? runs[0].State : null;

        return new ScheduleReadingResult(record.Schedule, state, runs, due, next);
    }

    /// <summary>The state <paramref name="run"/> reads as on its own.</summary>
    public static ScheduledRunState StateOf(ScheduledRun run, Func<string, bool> isArchived)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(isArchived);

        if (isArchived(run.SessionId)) return ScheduledRunState.Handled;
        if (run.Failed) return ScheduledRunState.Failed;

        if (run.Skipped)
        {
            return run.HeldBy is { } holder && isArchived(holder)
                ? ScheduledRunState.Handled
                : ScheduledRunState.Held;
        }

        if (run.FinishedAt is null) return ScheduledRunState.NoSummaryYet;

        var needsYou = run.NeedsYou.Count > 0
            || (run.Verdict is { } verdict && NeedsYouVerdicts.Contains(verdict.Trim(), StringComparer.OrdinalIgnoreCase));

        return needsYou ? ScheduledRunState.NeedsYou : ScheduledRunState.ReviewReady;
    }

    /// <summary>
    /// The session ids the reading of <paramref name="catalog"/> calls archived: each
    /// session's own id and its desktop id, for every session the desktop record marks
    /// archived.
    /// </summary>
    public static Func<string, bool> ArchivedIn(AgentSessionCatalog? catalog)
    {
        var archived = new HashSet<string>(StringComparer.Ordinal);

        foreach (var session in catalog?.Sessions ?? [])
        {
            if (session.Archived is not true) continue;

            archived.Add(session.Id);
            if (session.DesktopSessionId is { Length: > 0 } desktopId) archived.Add(desktopId);
        }

        return archived.Contains;
    }

    private static DateTimeOffset Began(ScheduledRun run) => run.StartedAt ?? run.FirstReportedAt;
}
