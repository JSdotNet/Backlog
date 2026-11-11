using System.ComponentModel;

using Backlog.Modules.Sessions.Abstractions;
using Backlog.SharedKernel.Results;

using ModelContextProtocol.Server;

namespace Backlog.Infrastructure.Mcp;

/// <summary>
/// The report tool of local ADR 0051 §1: a scheduled run tells Backlog that it started,
/// skipped itself, finished or failed. The <c>backlog-tools</c> hook pair posts the same
/// report to <c>POST /schedule-runs</c> on the listener; both go through
/// <see cref="ScheduleRunReporting"/>, so they record and answer alike.
/// <para>
/// Its own group (<see cref="BacklogMcpTools.Schedules"/>) behind <c>sessions</c>, so with
/// that flag off the tool leaves <c>tools/list</c> and nothing is recorded (§6).
/// </para>
/// </summary>
/// <param name="register">This machine's Schedule Register.</param>
/// <param name="sessions">The session list, read for whether a run's session is archived;
/// null where the host composed none, and then no run reads Handled.</param>
/// <param name="clock">What "now" is; optional so a test can pin it.</param>
[McpServerToolType]
public sealed class ScheduleTools(
    IScheduleRegister register,
    IAgentSessionSource? sessions = null,
    TimeProvider? clock = null)
{
    internal const string ReportScheduleRun = "report_schedule_run";

    /// <summary>The zone the crons are evaluated in: this machine's. Settable by a test
    /// rather than a constructor parameter, because the desktop listener forwards every
    /// constructor parameter's type from its container, and none composes a zone.</summary>
    internal TimeZoneInfo Zone { get; init; } = TimeZoneInfo.Local;

    [McpServerTool(Name = ReportScheduleRun, Title = "Report a scheduled run", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = false)]
    [Description(
        "Reports one event of a scheduled run to Backlog's Schedules page: 'started' when the run begins, 'skipped' "
        + "when it skips itself because an earlier run of its schedule is still unarchived, 'finished' with its "
        + "verdict and any Needs-you lines from its summary, or 'failed' with its error as the verdict. The schedule "
        + "appears with its first report; title, repository and cron replace what was held where they are sent. "
        + "Answers the schedule's state and the run's: needs-you, held, failed, missed, review-ready, handled or "
        + "no-summary-yet.")]
    public async Task<ScheduleRunReportPayload> ReportScheduleRunAsync(
        [Description("The scheduled task's id, as the session's scheduler tool names it.")]
        string scheduleId,
        [Description("The run's own session id.")]
        string sessionId,
        [Description("'started', 'skipped', 'finished' or 'failed'.")]
        string @event,
        [Description("The schedule's title. Send it with 'started'.")]
        string? title = null,
        [Description("The repository the schedule runs in, as owner/name. Send it with 'started'.")]
        string? repository = null,
        [Description("The schedule's five-field cron, in this machine's local time. Send it with 'started'.")]
        string? cron = null,
        [Description("With 'finished', the run's verdict as its summary gave it, e.g. review-ready or needs-you; with 'failed', its error.")]
        string? verdict = null,
        [Description("With 'finished', the Needs-you lines of the run's summary, in order; omit or send none when it lists none.")]
        string[]? needsYou = null,
        [Description("With 'skipped', the session id of the earlier, unarchived run that held this one, where known: the id it reported under, or the Claude desktop app's local_ id the scheduler's run list gives it.")]
        string? heldBy = null,
        CancellationToken cancellationToken = default)
    {
        if (!ScheduleRunReporting.TryEvent(@event, out var kind))
        {
            throw RepositoryScope.Failure(Error.Validation(
                "schedule_run.event",
                $"event must be started, skipped, finished or failed, not '{@event}'."));
        }

        var report = new ScheduledRunReport(scheduleId, title, repository, cron, sessionId, kind, verdict, needsYou, heldBy);
        var outcome = await ScheduleRunReporting.RecordAsync(report, register, sessions, clock ?? TimeProvider.System, Zone, cancellationToken).ConfigureAwait(false);

        return outcome.Payload ?? throw RepositoryScope.Failure(Error.Validation("schedule_run.report", outcome.Refusal!));
    }
}

/// <summary>
/// What a schedule run report answers: the schedule as it now reads, and the run reported.
/// </summary>
/// <param name="State">The schedule's state; the wire spells it kebab-case.</param>
/// <param name="RunState">The reported run's own state.</param>
/// <param name="DueAt">The due time a start is now awaited for, where the cron is understood.</param>
/// <param name="NextRunAt">The schedule's next due time after now.</param>
public sealed record ScheduleRunReportPayload(
    string ScheduleId,
    string? Title,
    string? Repository,
    string? Cron,
    string? State,
    string SessionId,
    string RunState,
    DateTimeOffset? DueAt,
    DateTimeOffset? NextRunAt,
    int Runs);

/// <summary>
/// The one path a report takes, from the tool and the route alike: normalise it, record
/// it, and read the schedule back with its states.
/// </summary>
public static class ScheduleRunReporting
{
    /// <summary>What recording answered: the payload, or why the report was refused.</summary>
    public sealed record Outcome(ScheduleRunReportPayload? Payload, string? Refusal);

    /// <summary>The event <paramref name="text"/> names, matched by name without regard to case.</summary>
    public static bool TryEvent(string? text, out ScheduledRunEvent value)
    {
        var name = Enum.GetNames<ScheduledRunEvent>()
            .FirstOrDefault(candidate => string.Equals(candidate, text?.Trim(), StringComparison.OrdinalIgnoreCase));

        value = name is null ? default : Enum.Parse<ScheduledRunEvent>(name);

        return name is not null;
    }

    /// <summary>A state as the wire spells it: <c>needs-you</c>, <c>review-ready</c>, <c>no-summary-yet</c>, …</summary>
    public static string WireName(ScheduledRunState state) => state switch
    {
        ScheduledRunState.NeedsYou => "needs-you",
        ScheduledRunState.Held => "held",
        ScheduledRunState.Failed => "failed",
        ScheduledRunState.Missed => "missed",
        ScheduledRunState.ReviewReady => "review-ready",
        ScheduledRunState.Handled => "handled",
        ScheduledRunState.NoSummaryYet => "no-summary-yet",
        _ => state.ToString().ToLowerInvariant(),
    };

    /// <summary>Records <paramref name="report"/> at the clock's now and reads its schedule back.</summary>
    public static async Task<Outcome> RecordAsync(
        ScheduledRunReport report,
        IScheduleRegister register,
        IAgentSessionSource? sessions,
        TimeProvider clock,
        TimeZoneInfo zone,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(register);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(zone);

        var (normalised, refusal) = ScheduleReports.Normalise(report);

        if (normalised is null) return new Outcome(null, refusal);

        var catalog = await CatalogAsync(sessions, cancellationToken).ConfigureAwait(false);
        normalised = normalised with { HeldBy = RunSessionId(normalised.HeldBy, catalog) };

        var now = clock.GetUtcNow();
        var record = await register.RecordAsync(normalised, now, cancellationToken).ConfigureAwait(false);
        var reading = ScheduleReading.Read(record, ScheduleReading.ArchivedIn(catalog), now, zone);
        var run = reading.Runs.First(candidate => candidate.Run.SessionId == normalised.SessionId);

        return new Outcome(
            new ScheduleRunReportPayload(
                reading.Schedule.Id,
                reading.Schedule.Title,
                reading.Schedule.Repository,
                reading.Schedule.Cron,
                reading.State is { } state ? WireName(state) : null,
                run.Run.SessionId,
                WireName(run.State),
                reading.DueAt,
                reading.NextRunAt,
                reading.Runs.Count),
            null);
    }

    /// <summary>
    /// The id a run is keyed by for <paramref name="heldBy"/>: where a skip named the holding
    /// run by the Claude desktop app's <c>local_</c> id — the id the scheduler's run list gives
    /// it — the id of the session that record belongs to, which is the one that run reported
    /// under; otherwise <paramref name="heldBy"/> as sent.
    /// </summary>
    private static string? RunSessionId(string? heldBy, AgentSessionCatalog? catalog)
    {
        if (heldBy is null || catalog is null) return heldBy;

        var session = catalog.Sessions.FirstOrDefault(candidate =>
            string.Equals(candidate.DesktopSessionId, heldBy, StringComparison.Ordinal));

        return session?.Id ?? heldBy;
    }

    /// <summary>
    /// The session list, or null where it cannot be read. It is read before the report is
    /// recorded, and a list that fails to read records the report all the same and answers
    /// with no run Handled, rather than failing a report the hook would only send again.
    /// </summary>
    private static async Task<AgentSessionCatalog?> CatalogAsync(IAgentSessionSource? sessions, CancellationToken cancellationToken)
    {
        if (sessions is null) return null;

        try
        {
            return await sessions.GetSessionsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }
}
