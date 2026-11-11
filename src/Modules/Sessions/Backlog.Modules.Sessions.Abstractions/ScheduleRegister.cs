namespace Backlog.Modules.Sessions.Abstractions;

/// <summary>What a scheduled run reported (local ADR 0051 §1).</summary>
public enum ScheduledRunEvent
{
    /// <summary>The run began: its SessionStart hook reported it.</summary>
    Started,

    /// <summary>The run skipped itself, because an earlier run of its schedule is still unarchived.</summary>
    Skipped,

    /// <summary>The run ended and summarised itself: its verdict and any Needs-you lines.</summary>
    Finished,

    /// <summary>The run failed, with its error as the verdict.</summary>
    Failed,
}

/// <summary>
/// What a run, and the schedule whose latest run it is, needs from the person — the seven
/// states of local ADR 0051 §2. A reading, never stored.
/// </summary>
public enum ScheduledRunState
{
    /// <summary>The run reported Needs-you lines and its session is not archived.</summary>
    NeedsYou,

    /// <summary>The run reported that it skipped itself because an earlier run is unarchived.</summary>
    Held,

    /// <summary>The run reported a failure.</summary>
    Failed,

    /// <summary>The schedule's alone: 30 minutes past its cron's due time and no start report since.</summary>
    Missed,

    /// <summary>The run reported work ready for review, and its session is not archived.</summary>
    ReviewReady,

    /// <summary>The run's session is archived, whatever it reported.</summary>
    Handled,

    /// <summary>A start report and no finish report yet.</summary>
    NoSummaryYet,
}

/// <summary>
/// One report a scheduled run sends, through <c>report_schedule_run</c> or the
/// <c>POST /schedule-runs</c> route the hook posts to.
/// </summary>
/// <param name="ScheduleId">The Claude desktop app's id for the scheduled task.</param>
/// <param name="Title">The schedule's title, or null where this report leaves it as it was.</param>
/// <param name="Repository">The repository it runs in, as <c>owner/name</c>, or null as for <paramref name="Title"/>.</param>
/// <param name="Cron">The schedule's five-field cron, or null as for <paramref name="Title"/>.</param>
/// <param name="SessionId">The run's own session id — the id its transcript is named after, or the desktop app's <c>local_</c> id.</param>
/// <param name="Event">What happened.</param>
/// <param name="Verdict">The run's verdict as it sent it, or for <see cref="ScheduledRunEvent.Failed"/> its error.</param>
/// <param name="NeedsYou">The Needs-you lines its summary listed, in order.</param>
/// <param name="HeldBy">For <see cref="ScheduledRunEvent.Skipped"/>, the session id of the earlier run that held it, where it named one.</param>
public sealed record ScheduledRunReport(
    string ScheduleId,
    string? Title,
    string? Repository,
    string? Cron,
    string SessionId,
    ScheduledRunEvent Event,
    string? Verdict = null,
    IReadOnlyList<string>? NeedsYou = null,
    string? HeldBy = null);

/// <summary>
/// One schedule in the <see cref="IScheduleRegister"/>, keyed by its schedule id. It enters
/// with its first report, and a later report's title, repository and cron replace these.
/// </summary>
public sealed record Schedule(
    string Id,
    string? Title,
    string? Repository,
    string? Cron,
    DateTimeOffset FirstReportedAt,
    DateTimeOffset LastReportedAt);

/// <summary>
/// One run of a schedule, keyed by its session id (the domain's Scheduled Run). A finish
/// without a start is kept, because a start report can be lost while the run goes on.
/// </summary>
public sealed record ScheduledRun(
    string SessionId,
    string ScheduleId,
    DateTimeOffset FirstReportedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? Verdict,
    IReadOnlyList<string> NeedsYou,
    bool Failed,
    bool Skipped,
    string? HeldBy)
{
    /// <summary>Structural equality over <see cref="NeedsYou"/>, so a run read back from a store equals the one written.</summary>
    public bool Equals(ScheduledRun? other) =>
        other is not null
        && SessionId == other.SessionId
        && ScheduleId == other.ScheduleId
        && FirstReportedAt == other.FirstReportedAt
        && StartedAt == other.StartedAt
        && FinishedAt == other.FinishedAt
        && Verdict == other.Verdict
        && NeedsYou.SequenceEqual(other.NeedsYou)
        && Failed == other.Failed
        && Skipped == other.Skipped
        && HeldBy == other.HeldBy;

    public override int GetHashCode() => HashCode.Combine(SessionId, ScheduleId, FirstReportedAt, StartedAt, FinishedAt, Verdict, Failed, Skipped);
}

/// <summary>A schedule with every run it has reported.</summary>
public sealed record ScheduleRecord(Schedule Schedule, IReadOnlyList<ScheduledRun> Runs);

/// <summary>
/// This machine's Schedule Register (local ADR 0051 §1): the schedules and their runs as the
/// runs reported them, kept in the workspace's local database and never replicated.
/// </summary>
public interface IScheduleRegister
{
    /// <summary>Applies <paramref name="report"/> through <see cref="ScheduleReports.Apply"/>,
    /// at <paramref name="at"/>, and answers the schedule as it now stands.</summary>
    Task<ScheduleRecord> RecordAsync(ScheduledRunReport report, DateTimeOffset at, CancellationToken cancellationToken = default);

    /// <summary>Every schedule with its runs.</summary>
    Task<IReadOnlyList<ScheduleRecord>> ListAsync(CancellationToken cancellationToken = default);
}

/// <summary>The pure rules for taking a report into the register.</summary>
public static class ScheduleReports
{
    /// <summary>The longest a text field is kept, so a hook posting a whole transcript cannot fill the database.</summary>
    public const int MaxText = 4000;

    /// <summary>The most Needs-you lines a run keeps.</summary>
    public const int MaxNeedsYouLines = 50;

    /// <summary>
    /// The schedule and run after <paramref name="report"/>, from what was held.
    /// <list type="bullet">
    /// <item><b>Started</b> sets the start once; a repeat keeps the first.</item>
    /// <item><b>Skipped</b> marks the run as having skipped itself, sticky, with what held it.</item>
    /// <item><b>Finished</b> sets the finish, the verdict and the Needs-you lines.</item>
    /// <item><b>Failed</b> sets the finish and the failure, with the error as the verdict.</item>
    /// </list>
    /// A report's title, repository and cron replace the schedule's where they are set.
    /// </summary>
    public static (Schedule Schedule, ScheduledRun Run) Apply(
        Schedule? schedule,
        ScheduledRun? run,
        ScheduledRunReport report,
        DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(report);

        var next = schedule is null
            ? new Schedule(report.ScheduleId, report.Title, report.Repository, report.Cron, at, at)
            : schedule with
            {
                Title = report.Title ?? schedule.Title,
                Repository = report.Repository ?? schedule.Repository,
                Cron = report.Cron ?? schedule.Cron,
                LastReportedAt = at > schedule.LastReportedAt ? at : schedule.LastReportedAt,
            };

        var held = run ?? new ScheduledRun(report.SessionId, report.ScheduleId, at, null, null, null, [], false, false, null);

        var changed = report.Event switch
        {
            ScheduledRunEvent.Started => held with { StartedAt = held.StartedAt ?? at },
            ScheduledRunEvent.Skipped => held with { Skipped = true, HeldBy = report.HeldBy ?? held.HeldBy },
            ScheduledRunEvent.Finished => held with
            {
                FinishedAt = at,
                Verdict = report.Verdict ?? held.Verdict,
                NeedsYou = report.NeedsYou is { } lines ? lines : held.NeedsYou,
            },
            ScheduledRunEvent.Failed => held with
            {
                FinishedAt = at,
                Failed = true,
                Verdict = report.Verdict ?? held.Verdict,
                NeedsYou = report.NeedsYou is { } lines ? lines : held.NeedsYou,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(report), report.Event, "An event this build does not know."),
        };

        return (next, changed);
    }

    /// <summary>
    /// <paramref name="report"/> trimmed and bounded, or the reason it is refused: a schedule
    /// id and a session id are required, blank fields read as not sent, and Needs-you lines
    /// are kept non-blank, trimmed, at most <see cref="MaxNeedsYouLines"/>.
    /// </summary>
    public static (ScheduledRunReport? Report, string? Refusal) Normalise(ScheduledRunReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var scheduleId = Text(report.ScheduleId);
        var sessionId = Text(report.SessionId);

        if (scheduleId is null) return (null, "scheduleId is required.");
        if (sessionId is null) return (null, "sessionId is required.");
        if (!Enum.IsDefined(report.Event)) return (null, "event must be started, skipped, finished or failed.");

        var lines = report.NeedsYou?
            .Select(Text)
            .OfType<string>()
            .Take(MaxNeedsYouLines)
            .ToList();

        return (new ScheduledRunReport(
            scheduleId,
            Text(report.Title),
            Text(report.Repository),
            Text(report.Cron),
            sessionId,
            report.Event,
            Text(report.Verdict),
            lines,
            Text(report.HeldBy)), null);
    }

    private static string? Text(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var trimmed = value.Trim();

        return trimmed.Length > MaxText ? trimmed[..MaxText] : trimmed;
    }
}
