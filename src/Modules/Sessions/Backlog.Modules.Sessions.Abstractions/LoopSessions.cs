namespace Backlog.Modules.Sessions.Abstractions;

/// <summary>Which loop a loop session runs (local ADR 0051 §3).</summary>
public enum LoopKind
{
    /// <summary>The Manager of local ADR 0038: <c>dispatch-sessions</c> under <c>/loop</c>.</summary>
    Dispatcher,

    /// <summary>The Brain tender: <c>tend-brain</c> under <c>/loop</c>. It has no queue.</summary>
    BrainTender
}

/// <summary>What a loop says it is doing when it checks in.</summary>
public enum LoopActivity
{
    /// <summary>Waiting for work.</summary>
    Listening,

    /// <summary>Work waits, and the loop is holding it back.</summary>
    Holding,

    /// <summary>Doing a tick's work.</summary>
    Working
}

/// <summary>How a loop session is doing, as Backlog reads it (the Loop State of
/// <c>.devbook/domain/sessions/domain.md#loop-state</c>). A reading, never stored.</summary>
public enum LoopState
{
    /// <summary>It checks in.</summary>
    Listening,

    /// <summary>It checks in, work waits, and it is holding it back.</summary>
    Holding,

    /// <summary>Its process is alive, and nothing checked in past its heartbeat plus 10 minutes.</summary>
    Stalled,

    /// <summary>It has no process, or its session is archived.</summary>
    NotRunning
}

/// <summary>What the person last asked of a loop. The loop obeys it; Backlog never stops
/// a session itself.</summary>
public enum LoopCommand
{
    /// <summary>Nothing asked: the default.</summary>
    None,

    /// <summary>Do no work, and keep checking in.</summary>
    Pause,

    /// <summary>Carry on.</summary>
    Resume,

    /// <summary>End the loop.</summary>
    Stop
}

/// <summary>Whether a loop session's process is alive, as the Liveness Assessment reads
/// its session.</summary>
public enum LoopProcess
{
    /// <summary>The session list holds no session with the loop's id.</summary>
    Unknown,

    /// <summary>The session reads Running, Waiting or Stalled.</summary>
    Alive,

    /// <summary>The session reads Finished: no process.</summary>
    Gone,

    /// <summary>The Claude desktop app archived the session.</summary>
    Archived
}

/// <summary>The work a loop says it holds when it checks in.</summary>
/// <param name="Requests">Session requests it claimed and has not marked yet.</param>
/// <param name="Deliveries">Answer deliveries it claimed and has not marked yet.</param>
/// <param name="Sessions">Sessions it started that are still working.</param>
/// <param name="Notes">Brain notes it has in hand this tick.</param>
public sealed record LoopHeldWork(int Requests, int Deliveries, int Sessions, int Notes)
{
    public static LoopHeldWork None { get; } = new(0, 0, 0, 0);
}

/// <summary>One check-in: the Loop Check-in of
/// <c>.devbook/domain/sessions/domain.md#loop-check-in</c>.</summary>
/// <param name="SessionId">The loop session's own id, the one its transcript is named after.</param>
/// <param name="Kind">Which loop it runs.</param>
/// <param name="Activity">What it says it is doing.</param>
/// <param name="HeartbeatMinutes">How often it says it checks in, in minutes.</param>
/// <param name="Held">The work it says it holds.</param>
/// <param name="At">When the check-in arrived.</param>
public sealed record LoopCheckIn(
    string SessionId,
    LoopKind Kind,
    LoopActivity Activity,
    int HeartbeatMinutes,
    LoopHeldWork Held,
    DateTimeOffset At);

/// <summary>
/// One loop session in the Loop Register: its latest check-in and the command the person
/// last set on it. Kept per session id, in the app's local database.
/// </summary>
/// <param name="CheckIn">The latest check-in.</param>
/// <param name="Command">The command the person last set; <see cref="LoopCommand.None"/> until they set one.</param>
/// <param name="CommandSetAt">When they set it; null while <see cref="Command"/> is None.</param>
public sealed record LoopSession(LoopCheckIn CheckIn, LoopCommand Command, DateTimeOffset? CommandSetAt)
{
    public string SessionId => CheckIn.SessionId;
}

/// <summary>A loop session as the UI reads it: the register's record, its state now, and
/// whether its process is alive.</summary>
public sealed record LoopSessionReading(LoopSession Loop, LoopState State, LoopProcess Process);

/// <summary>
/// The Loop State's thresholds and reading (local ADR 0051 §3), as pure functions of the
/// latest check-in, the session's process and the clock.
/// <code>
/// archived or no process            → Not running
/// checked in within heartbeat + 10  → Listening, or Holding when it said so
/// past heartbeat + 10               → Stalled
/// </code>
/// Within <see cref="ListeningWithin"/> a loop reads Listening (or Holding); between that and
/// its heartbeat plus <see cref="StalledGrace"/> it keeps the state it last read, which is
/// the same reading of the same check-in. The Brain tender has no queue, so it never holds.
/// </summary>
public static class LoopSessions
{
    /// <summary>A loop that checked in this recently reads Listening, or Holding.</summary>
    public static readonly TimeSpan ListeningWithin = TimeSpan.FromMinutes(2);

    /// <summary>How far past its own heartbeat a loop may go quiet before it reads Stalled.</summary>
    public static readonly TimeSpan StalledGrace = TimeSpan.FromMinutes(10);

    /// <summary>The longest heartbeat a check-in may state: a day.</summary>
    public const int MaxHeartbeatMinutes = 24 * 60;

    /// <summary>The title a loop gives its own session on its first turn
    /// (<c>plugins/backlog-tools/resources/standing-session.md</c>): its icon and its
    /// name in capitals.</summary>
    public static string Title(LoopKind kind) => kind switch
    {
        LoopKind.Dispatcher => "📡 SESSION DISPATCHER",
        LoopKind.BrainTender => "🧠 BRAIN TENDER",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "A loop kind with no title.")
    };

    /// <summary>The <c>/loop</c> command a person starts the loop with.</summary>
    public static string LoopCommandLine(LoopKind kind) => kind switch
    {
        LoopKind.Dispatcher => "/loop /backlog-tools:dispatch-sessions",
        LoopKind.BrainTender => "/loop /backlog-tools:tend-brain",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "A loop kind with no command.")
    };

    /// <summary>The loop a session title names, or null for any other title. The whole
    /// title, as the loop writes it; its name alone, without the icon, reads the same, so
    /// a host that drops the emoji still files the session as a loop.</summary>
    public static LoopKind? KindOfTitle(string? title)
    {
        var text = title?.Trim();

        if (string.IsNullOrEmpty(text)) return null;

        foreach (var kind in Enum.GetValues<LoopKind>())
        {
            var whole = Title(kind);

            if (string.Equals(text, whole, StringComparison.Ordinal)
                || string.Equals(text, whole[(whole.IndexOf(' ', StringComparison.Ordinal) + 1)..], StringComparison.Ordinal))
            {
                return kind;
            }
        }

        return null;
    }

    /// <summary>When a loop that checked in as <paramref name="checkIn"/> reads Stalled,
    /// if its process is still alive and it checks in no more.</summary>
    public static DateTimeOffset StalledAfter(LoopCheckIn checkIn)
    {
        ArgumentNullException.ThrowIfNull(checkIn);

        return checkIn.At + TimeSpan.FromMinutes(checkIn.HeartbeatMinutes) + StalledGrace;
    }

    /// <summary>The state a loop session reads as at <paramref name="now"/>.</summary>
    /// <remarks>A session the list does not hold (<see cref="LoopProcess.Unknown"/>) cannot be
    /// read as having no process, so it reads by its check-ins alone: Stalled once they stop.</remarks>
    public static LoopState StateAt(LoopCheckIn checkIn, LoopProcess process, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(checkIn);

        if (process is LoopProcess.Archived or LoopProcess.Gone) return LoopState.NotRunning;

        if (now > StalledAfter(checkIn)) return LoopState.Stalled;

        return checkIn.Activity == LoopActivity.Holding && checkIn.Kind != LoopKind.BrainTender
            ? LoopState.Holding
            : LoopState.Listening;
    }

    /// <summary>How a session's state and archived flag read as a loop's process.</summary>
    public static LoopProcess ProcessOf(AgentSession? session)
    {
        if (session is null) return LoopProcess.Unknown;
        if (session.Archived is true) return LoopProcess.Archived;

        return session.State == AgentSessionState.Finished ? LoopProcess.Gone : LoopProcess.Alive;
    }

    /// <summary>Why a check-in with these parts is refused — no session id, a heartbeat
    /// outside one minute to a day, a negative count — or null when it is not.</summary>
    public static string? Refusal(string? sessionId, int heartbeatMinutes, LoopHeldWork held)
    {
        ArgumentNullException.ThrowIfNull(held);

        if (string.IsNullOrWhiteSpace(sessionId)) return "sessionId is required.";

        if (heartbeatMinutes is < 1 or > MaxHeartbeatMinutes)
        {
            return $"heartbeatMinutes must be between 1 and {MaxHeartbeatMinutes}, not {heartbeatMinutes}.";
        }

        return held.Requests < 0 || held.Deliveries < 0 || held.Sessions < 0 || held.Notes < 0
            ? "A held count cannot be negative."
            : null;
    }

    /// <summary>A check-in as it arrives, its session id trimmed.</summary>
    /// <exception cref="ArgumentException">When <see cref="Refusal"/> refuses it.</exception>
    public static LoopCheckIn CheckIn(
        string sessionId,
        LoopKind kind,
        LoopActivity activity,
        int heartbeatMinutes,
        LoopHeldWork held,
        DateTimeOffset at)
    {
        if (Refusal(sessionId, heartbeatMinutes, held) is { } refusal) throw new ArgumentException(refusal);

        return new LoopCheckIn(sessionId.Trim(), kind, activity, heartbeatMinutes, held, at);
    }

    /// <summary><paramref name="checkIn"/> recorded over <paramref name="earlier"/>: the new
    /// check-in, the person's command kept.</summary>
    public static LoopSession Record(LoopSession? earlier, LoopCheckIn checkIn)
    {
        ArgumentNullException.ThrowIfNull(checkIn);

        return earlier is null
            ? new LoopSession(checkIn, LoopCommand.None, CommandSetAt: null)
            : earlier with { CheckIn = checkIn };
    }

    /// <summary>A dispatch wait at <paramref name="at"/> recorded over <paramref name="loop"/>
    /// (local ADR 0051 §3): a check-in with only the session and the time, so only the
    /// check-in time moves — and never backwards, so a wait that arrives late changes nothing.</summary>
    public static LoopSession RecordWait(LoopSession loop, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(loop);

        return at > loop.CheckIn.At
            ? loop with { CheckIn = loop.CheckIn with { At = at } }
            : loop;
    }

    /// <summary><paramref name="command"/> set on <paramref name="loop"/> at <paramref name="now"/>.
    /// Setting None clears what the person set.</summary>
    public static LoopSession Command(LoopSession loop, LoopCommand command, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(loop);

        return command == LoopCommand.None
            ? loop with { Command = LoopCommand.None, CommandSetAt = null }
            : loop with { Command = command, CommandSetAt = now };
    }
}

/// <summary>
/// PORT — this machine's Loop Register (local ADR 0051 §3): the latest check-in of every
/// loop session and the command the person last set on it. One row per session in the
/// app's local database; it does not replicate.
/// </summary>
public interface ILoopRegister
{
    /// <summary>Records <paramref name="checkIn"/> as its session's latest, keeping the
    /// command set on it, and answers the session as it now stands.</summary>
    Task<LoopSession> CheckInAsync(LoopCheckIn checkIn, CancellationToken cancellationToken = default);

    /// <summary>Records a dispatch wait at <paramref name="at"/> — the Manager's call to
    /// <c>GET /session-requests/wait</c>, which local ADR 0051 §3 counts as a check-in with
    /// only the session and the time — and answers the session as it now stands.</summary>
    /// <remarks>Only the check-in time moves, and never backwards (<see cref="LoopSessions.RecordWait"/>):
    /// what the last full check-in said and the person's command are kept. Null, and nothing
    /// written, when <paramref name="sessionId"/> is blank or no loop session has it.</remarks>
    Task<LoopSession?> RecordWaitAsync(string sessionId, DateTimeOffset at, CancellationToken cancellationToken = default);

    /// <summary>The loop session with <paramref name="sessionId"/>, or null when it never checked in.</summary>
    Task<LoopSession?> GetAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>Every loop session that has checked in, most recent check-in first.</summary>
    Task<IReadOnlyList<LoopSession>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Sets the person's <paramref name="command"/> on a loop session; the next
    /// check-in's answer carries it. Null when no loop session has that id.</summary>
    Task<LoopSession?> SetCommandAsync(string sessionId, LoopCommand command, CancellationToken cancellationToken = default);
}

/// <summary>
/// What a screen reads the loop sessions through, and how it sets a command on one: the
/// register's records read against the session list's liveness and the clock.
/// </summary>
/// <param name="register">This machine's Loop Register.</param>
/// <param name="sessions">The session list, read for each loop's process; null where the
/// host composed none, and then every loop reads by its check-ins alone.</param>
/// <param name="clock">What "now" is; the system clock when null.</param>
public sealed class LoopSessionMonitor(ILoopRegister register, IAgentSessionSource? sessions = null, TimeProvider? clock = null)
{
    private readonly ILoopRegister _register = register ?? throw new ArgumentNullException(nameof(register));
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    /// <summary>Every loop session that has checked in, with its state now.</summary>
    public async Task<IReadOnlyList<LoopSessionReading>> ReadAsync(CancellationToken cancellationToken = default)
    {
        var loops = await _register.ListAsync(cancellationToken).ConfigureAwait(false);

        if (loops.Count == 0) return [];

        var listed = sessions is null
            ? []
            : (await sessions.GetSessionsAsync(cancellationToken).ConfigureAwait(false)).Sessions;
        var byId = new Dictionary<string, AgentSession>(StringComparer.Ordinal);

        foreach (var session in listed) byId.TryAdd(session.Id, session);

        var now = _clock.GetUtcNow();

        return
        [
            .. loops.Select(loop =>
            {
                var process = LoopSessions.ProcessOf(byId.GetValueOrDefault(loop.SessionId));

                return new LoopSessionReading(loop, LoopSessions.StateAt(loop.CheckIn, process, now), process);
            })
        ];
    }

    /// <summary>Sets <paramref name="command"/> on the loop session with
    /// <paramref name="sessionId"/>; null when none has checked in with that id.</summary>
    public Task<LoopSession?> SetCommandAsync(string sessionId, LoopCommand command, CancellationToken cancellationToken = default) =>
        _register.SetCommandAsync(sessionId, command, cancellationToken);
}
