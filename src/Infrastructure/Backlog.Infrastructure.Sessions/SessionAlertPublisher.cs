using System.Security.Cryptography;
using System.Text;

using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.SharedKernel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Infrastructure.Sessions;

/// <summary>
/// Raises the schedule and loop alerts of local ADR 0051 §5 in the notification centre and
/// closes them when their cause clears. Every <see cref="Interval"/> it reads the schedules and
/// the loop sessions as <see cref="SessionAlerts"/> reads them, then:
/// <list type="bullet">
/// <item>publishes each alert that holds under an id derived from its key
/// (<see cref="IdFor"/>), so the owner stores it once whatever number of readings hold it — a
/// later publish of a held id publishes nothing, the person's Acknowledge included;</item>
/// <item>closes every open <c>sessions</c> / <c>alert</c> notification whose key no reading holds
/// any more, with <see cref="SessionAlerts.ClosedReason"/>.</item>
/// </list>
/// It keeps nothing in memory: what is open is read back from the centre on every sweep, so a
/// restart neither raises an alert twice nor leaves one open whose cause cleared meanwhile.
/// <para>
/// A plain singleton the head resolves once at startup, as <see cref="QuestionRelayEscalation"/>
/// is. Nothing is raised while <paramref name="enabled"/> answers false — the <c>sessions</c> flag
/// off (§6) — and the alerts still open close, since they leave with the Sessions area. A sweep
/// that finds the previous one still running is skipped, one that fails is logged and never stops
/// the timer, and the one under way at <see cref="Dispose"/> is cancelled.
/// </para>
/// <para>
/// The session reading is dated back to the oldest run or check-in the registers hold, rather than
/// the inventory's newest hundred, so a run archived long ago still reads archived and an archived
/// loop still reads as having no process.
/// </para>
/// </summary>
/// <param name="schedules">The Schedule Register; null where the host composed none.</param>
/// <param name="loops">The Loop Register; null where the host composed none.</param>
/// <param name="sessions">The session list, read for archived runs and live loop processes.</param>
/// <param name="notifications">The port alerts are published and closed through, resolved per sweep.</param>
/// <param name="centre">The owner's centre, read for the alerts still open; resolved per sweep.</param>
/// <param name="enabled">Whether the Sessions area is on.</param>
/// <param name="requests">The session request queue, counted as work waiting for a dispatcher.</param>
/// <param name="questions">The waiting questions, whose pending deliveries are work waiting for a dispatcher.</param>
public sealed class SessionAlertPublisher(
    IScheduleRegister? schedules,
    ILoopRegister? loops,
    IAgentSessionSource? sessions,
    Func<INotifications?> notifications,
    Func<INotificationCentre?> centre,
    Func<bool> enabled,
    ISessionRequestStore? requests = null,
    IWaitingQuestionStore? questions = null,
    TimeProvider? clock = null,
    ILogger<SessionAlertPublisher>? logger = null) : IDisposable
{
    /// <summary>How often the causes are read: well under the shortest grace, Stalled's 10 minutes.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly Func<INotifications?> _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
    private readonly Func<INotificationCentre?> _centre = centre ?? throw new ArgumentNullException(nameof(centre));
    private readonly Func<bool> _enabled = enabled ?? throw new ArgumentNullException(nameof(enabled));
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly ILogger _logger = (ILogger?)logger ?? NullLogger.Instance;
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _stopping = new();
    private ITimer? _timer;
    private Task _running = Task.CompletedTask;

    /// <summary>The zone the times in an alert's words are written in: this machine's. Settable by
    /// a test.</summary>
    internal TimeZoneInfo Zone { get; init; } = TimeZoneInfo.Local;

    /// <summary>The sweep under way, or the last one — for a test to await.</summary>
    public Task Idle
    {
        get
        {
            lock (_gate) return _running;
        }
    }

    /// <summary>Starts the timer; the first sweep runs one <see cref="Interval"/> from now. Safe to
    /// call twice.</summary>
    public SessionAlertPublisher Start()
    {
        lock (_gate)
        {
            _timer ??= _clock.CreateTimer(_ => Tick(), null, Interval, Interval);
        }

        return this;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
        }

        _stopping.Cancel();
        _stopping.Dispose();
    }

    /// <summary>Why an open alert closes when the Sessions area is turned off.</summary>
    public const string TurnedOffReason = "Sessions turned off";

    /// <summary>The notification id an alert is stored under: the same for the same key on every
    /// desktop and every start, so publishing it again publishes nothing.</summary>
    public static Guid IdFor(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("backlog.sessions.alert\n" + key));
        var bytes = hash.AsSpan(0, 16).ToArray();

        // A name-based (version 5 shaped) id, so it reads as a well-formed UUID.
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        return new Guid(bytes, bigEndian: true);
    }

    /// <summary>The draft an alert is published with.</summary>
    /// <param name="alert">The alert.</param>
    /// <param name="sessionIdOf">The id the Sessions list knows a run's or a loop's session by,
    /// for the id a report named it by — the desktop app's <c>local_</c> id read back to its own;
    /// the id as named when null.</param>
    public static NotificationDraft Draft(SessionAlert alert, Func<string, string>? sessionIdOf = null)
    {
        ArgumentNullException.ThrowIfNull(alert);

        return new NotificationDraft(
            new NotificationSource(SessionNotificationSubjects.Context, alert.Key),
            Subject(alert.Key),
            NotificationKind.Alert,
            alert.IsCritical ? NotificationSeverity.Critical : NotificationSeverity.Warning,
            alert.Title,
            alert.Summary,
            alert.Body,
            Origin: NotificationOrigin.Fit(NotificationOrigins.Backlog, alert.Place),
            Id: IdFor(alert.Key),
            Actions: [.. alert.Actions.Select(action => Action(action, sessionIdOf))]);
    }

    /// <summary>The <c>backlog://window/sessions…</c> link an action opens.</summary>
    public static NotificationAction Action(SessionAlertAction action, Func<string, string>? sessionIdOf = null)
    {
        ArgumentNullException.ThrowIfNull(action);

        var query = new List<KeyValuePair<string, string>>();

        switch (action.Target)
        {
            case SessionAlertTarget.Schedules:
                query.Add(new(PageKey, SchedulesPage));
                break;
            case SessionAlertTarget.Run or SessionAlertTarget.Sessions when action.SessionId is { Length: > 0 } session:
                query.Add(new(SessionKey, sessionIdOf?.Invoke(session) ?? session));
                break;
        }

        return new NotificationAction(action.Label, SurfaceLink.Protocol(SurfaceId, query));
    }

    /// <summary>The Sessions takeover's surface id, as <c>SessionsSurfaceRegistration</c> names
    /// it — its screen project, which this one may not reference.</summary>
    internal const string SurfaceId = "sessions";

    /// <summary>The query key naming the session the Sessions list opens on.</summary>
    internal const string SessionKey = "session";

    /// <summary>The query key naming the page inside the Sessions takeover.</summary>
    internal const string PageKey = "page";

    /// <summary>The Schedules page's value of <see cref="PageKey"/>.</summary>
    internal const string SchedulesPage = "schedules";

    /// <summary>One sweep: publish what holds, close what cleared. Answers how many it published
    /// and closed; what the timer runs, and what a test calls.</summary>
    public async Task<(int Published, int Closed)> SweepAsync(CancellationToken cancellationToken = default)
    {
        if (_notifications() is not { } port || _centre() is not { } owner) return (0, 0);

        var enabled = _enabled();
        (IReadOnlyList<SessionAlert> Alerts, Func<string, string>? SessionIdOf) reading = enabled
            ? await ReadAsync(cancellationToken).ConfigureAwait(false)
            : ([], null);
        var (alerts, sessionIdOf) = reading;
        var holding = alerts.Select(alert => alert.Key).ToHashSet(StringComparer.Ordinal);

        var closed = 0;
        var listed = await owner.ListAsync(cancellationToken).ConfigureAwait(false);
        var open = listed
            .Where(notification => notification.IsOpen && IsAlert(notification.Subject))
            .ToList();

        foreach (var stale in open.Where(notification => !holding.Contains(notification.Subject!.Id)))
        {
            var subject = stale.Subject!;
            var reason = enabled ? SessionAlerts.ClosedReason(subject.Id) : TurnedOffReason;
            var result = await port.CloseForSubjectAsync(subject, reason, cancellationToken).ConfigureAwait(false);

            if (result.IsSuccess) closed += result.Value;
            else _logger.LogDebug("The alert {Key} was not closed: {Error}", subject.Id, result.Error.Message);
        }

        // Open, acknowledged or closed, an alert the centre still lists is not raised again; one
        // settled longer ago than the centre lists is refused by its id all the same.
        var published = 0;
        var known = listed.Select(notification => notification.Id).ToHashSet();

        foreach (var alert in alerts)
        {
            var id = IdFor(alert.Key);
            if (known.Contains(id)) continue;

            var result = await port.PublishAsync(Draft(alert, sessionIdOf), cancellationToken).ConfigureAwait(false);

            if (result.IsFailure) _logger.LogDebug("No alert was raised for {Key}: {Error}", alert.Key, result.Error.Message);
            else published++;
        }

        return (published, closed);
    }

    /// <summary>The alerts that hold now, and the id the Sessions list knows each session by.</summary>
    internal async Task<(IReadOnlyList<SessionAlert> Alerts, Func<string, string>? SessionIdOf)> ReadAsync(CancellationToken cancellationToken)
    {
        var scheduleRecords = schedules is null ? [] : await schedules.ListAsync(cancellationToken).ConfigureAwait(false);
        var loopRecords = loops is null ? [] : await loops.ListAsync(cancellationToken).ConfigureAwait(false);

        // Nothing has reported: no session reading, which is the costly part.
        if (scheduleRecords.Count == 0 && loopRecords.Count == 0) return ([], null);

        var now = _clock.GetUtcNow();
        var catalog = sessions is null
            ? null
            : await sessions.GetSessionsAsync(Since(scheduleRecords, loopRecords, now), cancellationToken).ConfigureAwait(false);

        // A session is found by its own id or the desktop app's, as a report may name either.
        var byId = new Dictionary<string, AgentSession>(StringComparer.Ordinal);
        foreach (var session in catalog?.Sessions ?? []) byId.TryAdd(session.Id, session);
        foreach (var session in catalog?.Sessions ?? [])
        {
            if (session.DesktopSessionId is { Length: > 0 } desktopId) byId.TryAdd(desktopId, session);
        }

        var scheduleReadings = ScheduleReading.Read(scheduleRecords, ScheduleReading.ArchivedIn(catalog), now, Zone);
        var loopReadings = loopRecords
            .Select(loop =>
            {
                var process = LoopSessions.ProcessOf(byId.GetValueOrDefault(loop.SessionId));
                return new LoopSessionReading(loop, LoopSessions.StateAt(loop.CheckIn, process, now), process);
            })
            .ToList();

        var dispatcherWork = loopReadings.Any(loop => loop.Loop.CheckIn.Kind == LoopKind.Dispatcher && loop.State == LoopState.NotRunning)
            ? await WaitingAsync(now, cancellationToken).ConfigureAwait(false)
            : LoopWaitingWork.None;

        var alerts = SessionAlerts.Read(
            scheduleReadings,
            loopReadings,
            kind => kind == LoopKind.Dispatcher ? dispatcherWork : LoopWaitingWork.None,
            Zone);

        return (alerts, id => byId.TryGetValue(id, out var session) ? session.Id : id);
    }

    /// <summary>The session reading dated back to the oldest run or check-in held, less a day for
    /// a session that began before it reported; the timestamps alone, as archived flags and
    /// liveness need nothing more.</summary>
    private static AgentSessionQuery Since(IReadOnlyList<ScheduleRecord> schedules, IReadOnlyList<LoopSession> loops, DateTimeOffset now)
    {
        var oldest = schedules
            .SelectMany(schedule => schedule.Runs.Select(run => run.FirstReportedAt))
            .Concat(loops.Select(loop => loop.CheckIn.At))
            .DefaultIfEmpty(now)
            .Min();

        return AgentSessionQuery.Since(oldest - TimeSpan.FromDays(1)).TimestampsOnly();
    }

    private async Task<LoopWaitingWork> WaitingAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var waitingRequests = requests is null
            ? 0
            : SessionRequests.Waiting(await requests.ListAsync(null, cancellationToken).ConfigureAwait(false), now).Count;
        var waitingDeliveries = questions is null
            ? 0
            : (await questions.ListUndeliveredAsync(cancellationToken).ConfigureAwait(false))
                .Count(question => question.DeliveryAt(now) == AnswerDeliveryState.Pending);

        return new LoopWaitingWork(waitingRequests, waitingDeliveries);
    }

    private static NotificationSubject Subject(string key) =>
        new(SessionNotificationSubjects.Context, SessionNotificationSubjects.Alert, key);

    private static bool IsAlert(NotificationSubject? subject) =>
        subject is { Context: SessionNotificationSubjects.Context, Type: SessionNotificationSubjects.Alert };

    private void Tick()
    {
        lock (_gate)
        {
            if (!_running.IsCompleted) return;

            _running = Task.Run(RunAsync);
        }
    }

    private async Task RunAsync()
    {
        try
        {
            var (published, closed) = await SweepAsync(_stopping.Token).ConfigureAwait(false);

            if (published + closed > 0) _logger.LogDebug("Raised {Published} schedule or loop alert(s) and closed {Closed}.", published, closed);
        }
        catch (OperationCanceledException)
        {
            // Stopped at Dispose.
        }
        catch (ObjectDisposedException)
        {
            // Stopped at Dispose, before the sweep took its token.
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Reading the schedule and loop alerts failed; the next sweep tries again.");
        }
    }
}
