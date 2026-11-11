using System.Globalization;
using System.Text.RegularExpressions;

namespace Backlog.Modules.Sessions.Abstractions;

/// <summary>Which of the six causes of local ADR 0051 §5 an alert is raised for.</summary>
public enum SessionAlertCause
{
    /// <summary>A run reported Needs-you lines, and its session is not archived.</summary>
    NeedsYou,

    /// <summary>A schedule's latest runs skip themselves, because an earlier run is unarchived.</summary>
    Held,

    /// <summary>A run reported a failure, and its session is not archived.</summary>
    Failed,

    /// <summary>A schedule passed its due time by the grace with no run started.</summary>
    Missed,

    /// <summary>A loop's process is alive, and it stopped checking in.</summary>
    Stalled,

    /// <summary>A loop has no process, or its session is archived, while work waits for it.</summary>
    NotRunning,
}

/// <summary>Where an alert's action opens, inside the Sessions takeover.</summary>
public enum SessionAlertTarget
{
    /// <summary>The run's session, picked in the Sessions list.</summary>
    Run,

    /// <summary>The Schedules page.</summary>
    Schedules,

    /// <summary>The loop session, picked in the Sessions list.</summary>
    Sessions,
}

/// <summary>One action an alert offers beside Acknowledge.</summary>
/// <param name="Label">What the button reads — "Open run".</param>
/// <param name="Target">Where it opens.</param>
/// <param name="SessionId">The session it opens on, for <see cref="SessionAlertTarget.Run"/> and
/// <see cref="SessionAlertTarget.Sessions"/>; null for the Schedules page.</param>
/// <param name="ScheduleId">The schedule the Schedules page opens on, for
/// <see cref="SessionAlertTarget.Schedules"/>; null otherwise.</param>
public sealed record SessionAlertAction(string Label, SessionAlertTarget Target, string? SessionId = null, string? ScheduleId = null);

/// <summary>
/// One alert the Sessions context wants open now: its cause, the key that names this one
/// occurrence of it, and the words and actions the notification carries. A reading, never
/// stored: the publisher raises it once by <see cref="Key"/> and closes it once a later
/// reading no longer holds that key.
/// </summary>
/// <param name="Key">Names this occurrence of the cause, so one cause is one alert: the same key
/// on every reading while the cause holds. A run's, a held streak's and a missed due time's key
/// is new once that run, streak or due time is another; a loop's is new with every check-in, so a
/// loop that stops again after checking in alerts again, and one whose waiting work drains and
/// returns before it checks in does not.</param>
/// <param name="Cause">Which cause.</param>
/// <param name="Place">The line under Backlog it is raised from: a schedule's
/// <see cref="SessionAlerts.SchedulePlace"/> — its repository, then "Schedules" — or a loop's
/// "Sessions".</param>
/// <param name="Title">One line, under 80 characters.</param>
/// <param name="Summary">One line, under 180 characters.</param>
/// <param name="Body">Markdown that says what happened and what clears it.</param>
/// <param name="Actions">What it offers to open, in order.</param>
public sealed record SessionAlert(
    string Key,
    SessionAlertCause Cause,
    string Place,
    string Title,
    string Summary,
    string Body,
    IReadOnlyList<SessionAlertAction> Actions)
{
    /// <summary>Whether it is the failure kind of alert rather than a nudge: a failed run, or a
    /// loop not running while work waits.</summary>
    public bool IsCritical => Cause is SessionAlertCause.Failed or SessionAlertCause.NotRunning;
}

/// <summary>The work waiting for a loop of one kind when Backlog looks: what makes a loop that
/// is not running worth an alert.</summary>
/// <param name="SessionRequests">Session requests reading Requested.</param>
/// <param name="Deliveries">Answer deliveries reading Pending.</param>
public sealed record LoopWaitingWork(int SessionRequests, int Deliveries)
{
    public static LoopWaitingWork None { get; } = new(0, 0);

    public bool Any => SessionRequests > 0 || Deliveries > 0;
}

/// <summary>
/// The alerts of local ADR 0051 §5, read from the schedule and loop readings, as pure functions:
/// <list type="bullet">
/// <item><b>Needs you</b> and <b>Failed</b>: one per run that reads so, keyed on the run, so a
/// run's alert stays until its session is archived and the next run's is its own.</item>
/// <item><b>Held</b>: one per schedule's streak of skipping runs, keyed on the run that held it —
/// the one a skip named, else the oldest skip in the streak — so the later skips of the same
/// streak raise nothing more.</item>
/// <item><b>Missed</b>: one per due time missed, cleared once a run starts.</item>
/// <item><b>Stalled</b> and <b>Not running</b>: only for the loop of each kind that checked in
/// last, since an older one was replaced; keyed on its latest check-in, so checking in again
/// clears it. Not running alerts only while work waits — the Brain tender has no queue, so it
/// never does.</item>
/// </list>
/// Review ready, Handled and No summary yet raise nothing.
/// </summary>
public static partial class SessionAlerts
{
    /// <summary>The place a schedule's alert is raised from, after its repository.</summary>
    public const string SchedulesPlace = "Schedules";

    /// <summary>
    /// The place a schedule's alert names: the schedule's repository, then
    /// <see cref="SchedulesPlace"/> — "JSdotNet/Backlog · Schedules". The repository leads, as
    /// a run's does, so the centre files the alert under it rather than under the Brain; a
    /// schedule that reported no repository is <see cref="SchedulesPlace"/> alone, the Brain's.
    /// A repository name long enough to cut the place at its 120 characters loses the
    /// separator, and the alert then reads as the Brain's too.
    /// </summary>
    public static string SchedulePlace(string? repository) =>
        string.IsNullOrWhiteSpace(repository) ? SchedulesPlace : $"{OneLine(repository)} · {SchedulesPlace}";

    /// <summary>The place a loop's alert is raised from.</summary>
    public const string SessionsPlace = "Sessions";

    private const int MaxTitle = 80;
    private const int MaxSummary = 180;

    /// <summary>The most Needs-you lines an alert's body lists, and the most characters of
    /// each, so a run's 50 lines of 4000 characters stay well inside a notification's 32 KB.</summary>
    private const int MaxBodyLines = 20;
    private const int MaxBodyLine = 300;

    /// <summary>
    /// Why the alert <paramref name="key"/> names closes once no reading holds it any more, in
    /// words its notification closes with. A key opens with its cause, so the cause is read back
    /// from it after a restart, when nothing in memory remembers it.
    /// </summary>
    public static string ClosedReason(string key) =>
        (key ?? string.Empty).Split(':', 2)[0] switch
        {
            "needs-you" or "failed" => "Run archived",
            "held" => "No longer held",
            "missed" => "A run started",
            "stalled" => "Checked in again, or its process ended",
            "not-running" => "A dispatcher checked in again, or nothing waits any more",
            _ => "Cleared",
        };

    /// <summary>Every alert that holds now.</summary>
    /// <param name="schedules">The schedules as <see cref="ScheduleReading"/> reads them.</param>
    /// <param name="loops">The loop sessions as the Loop State reads them.</param>
    /// <param name="waiting">The work waiting for a loop of a kind.</param>
    /// <param name="zone">The zone the times in the words are written in: this machine's.</param>
    public static IReadOnlyList<SessionAlert> Read(
        IEnumerable<ScheduleReadingResult> schedules,
        IEnumerable<LoopSessionReading> loops,
        Func<LoopKind, LoopWaitingWork> waiting,
        TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(schedules);
        ArgumentNullException.ThrowIfNull(loops);
        ArgumentNullException.ThrowIfNull(waiting);
        ArgumentNullException.ThrowIfNull(zone);

        var alerts = new List<SessionAlert>();

        foreach (var schedule in schedules) alerts.AddRange(ForSchedule(schedule, zone));

        foreach (var latest in loops
                     .GroupBy(loop => loop.Loop.CheckIn.Kind)
                     .Select(kind => kind.OrderByDescending(loop => loop.Loop.CheckIn.At).First()))
        {
            if (ForLoop(latest, waiting(latest.Loop.CheckIn.Kind), zone) is { } alert) alerts.Add(alert);
        }

        return alerts;
    }

    /// <summary>The alerts one schedule holds now.</summary>
    public static IReadOnlyList<SessionAlert> ForSchedule(ScheduleReadingResult schedule, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(zone);

        var name = NameOf(schedule.Schedule);
        var id = schedule.Schedule.Id;
        var place = SchedulePlace(schedule.Schedule.Repository);
        var alerts = new List<SessionAlert>();

        foreach (var reading in schedule.Runs)
        {
            var run = reading.Run;

            if (reading.State == ScheduledRunState.NeedsYou)
            {
                var lines = run.NeedsYou.Count > 0 ? run.NeedsYou : [run.Verdict ?? "The run asks for you."];

                alerts.Add(new SessionAlert(
                    $"needs-you:schedule:{id}:{run.SessionId}",
                    SessionAlertCause.NeedsYou,
                    place,
                    Cut($"{name} needs you", MaxTitle),
                    Cut(string.Join("; ", lines.Select(PlainText)), MaxSummary),
                    $"""
                    A run of **{name}** finished and asks for you:

                    {Listed(lines)}

                    The alert closes once the run's session is archived.
                    """,
                    RunActions(run.SessionId, id)));
            }
            else if (reading.State == ScheduledRunState.Failed)
            {
                var error = run.Verdict ?? "It reported no error.";

                alerts.Add(new SessionAlert(
                    $"failed:schedule:{id}:{run.SessionId}",
                    SessionAlertCause.Failed,
                    place,
                    Cut($"{name} failed", MaxTitle),
                    Cut(error, MaxSummary),
                    $"""
                    A run of **{name}** failed:

                    > {Cut(error, 2000)}

                    The alert closes once the run's session is archived.
                    """,
                    RunActions(run.SessionId, id)));
            }
        }

        if (HeldStreak(schedule) is { } held)
        {
            alerts.Add(new SessionAlert(
                $"held:schedule:{id}:{held}",
                SessionAlertCause.Held,
                place,
                Cut($"{name} is held", MaxTitle),
                "An earlier run is not archived, so each run skips itself.",
                $"""
                Each run of **{name}** skips itself while an earlier run's session is not archived.
                Archive that run in Claude once it is handled, and the next run goes ahead.
                """,
                RunActions(held, id)));
        }

        if (schedule.State == ScheduledRunState.Missed && schedule.DueAt is { } due)
        {
            var at = Clock(due, zone);

            alerts.Add(new SessionAlert(
                $"missed:schedule:{id}:{due.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)}",
                SessionAlertCause.Missed,
                place,
                Cut($"{name} missed its run", MaxTitle),
                Cut($"It was due at {at} and no run has started.", MaxSummary),
                $"""
                **{name}** was due at {at}, and no run reported its start within {(int)ScheduleReading.MissedGrace.TotalMinutes} minutes.
                The machine may have been asleep, or the schedule turned off. The alert closes once a run starts.
                """,
                [new SessionAlertAction("Open Schedules", SessionAlertTarget.Schedules, ScheduleId: id)]));
        }

        return alerts;
    }

    /// <summary>The alert one loop holds now, or null when it holds none.</summary>
    public static SessionAlert? ForLoop(LoopSessionReading loop, LoopWaitingWork waiting, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(waiting);
        ArgumentNullException.ThrowIfNull(zone);

        var checkIn = loop.Loop.CheckIn;
        var name = checkIn.Kind == LoopKind.Dispatcher ? "Session dispatcher" : "Brain tender";
        var last = Clock(checkIn.At, zone);
        var stamp = checkIn.At.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        var actions = new[] { new SessionAlertAction("Open Sessions", SessionAlertTarget.Sessions, checkIn.SessionId) };

        switch (loop.State)
        {
            // Stalled means the process is seen alive: a session the reading does not hold reads
            // Stalled by its check-ins alone, and nothing says it is still running.
            case LoopState.Stalled when loop.Process == LoopProcess.Alive:
                return new SessionAlert(
                    $"stalled:loop:{checkIn.SessionId}:{stamp}",
                    SessionAlertCause.Stalled,
                    SessionsPlace,
                    $"{name} stalled",
                    Cut($"No check-in since {last}, past its {checkIn.HeartbeatMinutes}-minute heartbeat.", MaxSummary),
                    $"""
                    The **{name.ToLowerInvariant()}** is still running, but it has not checked in since {last}: its heartbeat is {Count(checkIn.HeartbeatMinutes, "minute")}.
                    Look at its session. The alert closes once it checks in again.
                    """,
                    actions);

            case LoopState.NotRunning when checkIn.Kind == LoopKind.Dispatcher && waiting.Any:
                var work = Waiting(waiting);

                return new SessionAlert(
                    $"not-running:loop:{checkIn.SessionId}:{stamp}",
                    SessionAlertCause.NotRunning,
                    SessionsPlace,
                    $"{name} stopped",
                    Cut($"{Capitalised(work)} {(waiting.SessionRequests + waiting.Deliveries == 1 ? "waits" : "wait")} for it. It last checked in at {last}.", MaxSummary),
                    $"""
                    The **{name.ToLowerInvariant()}** is not running, and {work} {(waiting.SessionRequests + waiting.Deliveries == 1 ? "waits" : "wait")} for it.
                    Start the loop again in a pinned session. The alert closes once a dispatcher checks in again.
                    """,
                    actions);

            default:
                return null;
        }
    }

    /// <summary>The session id that names a schedule's current Held streak — what the newest
    /// skip named as holding it, else the oldest skip in the run of skips the newest one
    /// ends — or null when its latest run is not held.</summary>
    private static string? HeldStreak(ScheduleReadingResult schedule)
    {
        if (schedule.Runs.Count == 0 || schedule.Runs[0].State != ScheduledRunState.Held) return null;

        if (schedule.Runs[0].Run.HeldBy is { Length: > 0 } holder) return holder;

        var oldest = schedule.Runs[0].Run.SessionId;

        foreach (var reading in schedule.Runs.Skip(1))
        {
            if (reading.State != ScheduledRunState.Held) break;

            oldest = reading.Run.SessionId;
        }

        return oldest;
    }

    private static string Listed(IReadOnlyList<string> lines)
    {
        var listed = string.Join('\n', lines.Take(MaxBodyLines).Select(line => "- " + Cut(line, MaxBodyLine)));

        return lines.Count > MaxBodyLines
            ? listed + $"\n- …and {lines.Count - MaxBodyLines} more"
            : listed;
    }

    private static SessionAlertAction[] RunActions(string sessionId, string scheduleId) =>
    [
        new("Open run", SessionAlertTarget.Run, sessionId),
        new("Open Schedules", SessionAlertTarget.Schedules, ScheduleId: scheduleId),
    ];

    private static string NameOf(Schedule schedule) =>
        OneLine(string.IsNullOrWhiteSpace(schedule.Title) ? schedule.Id : schedule.Title);

    private static string Waiting(LoopWaitingWork waiting)
    {
        var parts = new List<string>();

        if (waiting.SessionRequests > 0) parts.Add(Count(waiting.SessionRequests, "session request"));
        if (waiting.Deliveries > 0) parts.Add(Count(waiting.Deliveries, "answer delivery", "answer deliveries"));

        return string.Join(" and ", parts);
    }

    private static string Count(int count, string one, string? many = null) =>
        count.ToString(CultureInfo.InvariantCulture) + " " + (count == 1 ? one : many ?? one + "s");

    private static string Capitalised(string text) =>
        text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private static string Clock(DateTimeOffset at, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(at, zone).ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>
    /// A Needs-you line as plain text, for the alert's summary, which is plain text
    /// wherever it is drawn — a Windows notification, the phone, the centre's card: a link
    /// keeps its words, and emphasis and code marks go. The body keeps the Markdown, where it
    /// renders.
    /// </summary>
    internal static string PlainText(string line) =>
        InlineMarks().Replace(MarkdownLink().Replace(line, "${text}"), string.Empty);

    [GeneratedRegex(@"!?\[(?<text>[^\]]*)\]\([^)\s]*(?:\s+""[^""]*"")?\)")]
    private static partial Regex MarkdownLink();

    [GeneratedRegex(@"\*\*|`+")]
    private static partial Regex InlineMarks();

    private static string OneLine(string text) =>
        string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(part => part.Trim())).Trim();

    private static string Cut(string text, int length)
    {
        var line = OneLine(text);

        return line.Length <= length ? line : line[..(length - 1)] + "…";
    }
}
