using System.ComponentModel;

using Backlog.Modules.Sessions.Abstractions;
using Backlog.SharedKernel.Results;

using ModelContextProtocol.Server;

namespace Backlog.Infrastructure.Mcp;

/// <summary>
/// <c>check_in</c>, the two-way check-in of local ADR 0051 §3: a loop session (the Manager
/// under <c>dispatch-sessions</c>, the Brain tender under <c>tend-brain</c>) says on every
/// tick what it is doing and its limits, and the answer carries the pause, resume or stop
/// the person last set on it and the work waiting for it.
/// <para>
/// A dispatcher's answer also carries the room the app measured against its limits (ADR
/// 0051 §4 and its Amends of ADR 0038 §4): the Manager reads its room from the answer rather
/// than measuring it. The reading and the requests waiting are kept with the check-in, so
/// the loop's row reads Holding from what the app measured.
/// </para>
/// <para>
/// Its own group (<see cref="BacklogMcpTools.Loops"/>) behind <c>sessions</c>, as the ADR's
/// §6 puts every part of it. The tool never stops a session: it says what the person asked,
/// and the loop obeys.
/// </para>
/// </summary>
/// <param name="register">This machine's Loop Register.</param>
/// <param name="clock">What "now" is; optional so a test can pin it.</param>
/// <param name="requests">The session request queue, counted for a dispatcher's waiting
/// work; null where the host composed none, and then none is counted.</param>
/// <param name="questions">The waiting questions, whose pending answer deliveries are a
/// dispatcher's waiting work; null where the host composed no relay.</param>
/// <param name="capacity">Measures a dispatcher's room; null where the host measures none,
/// and then the answer carries no capacity and the loop's own word decides Holding.</param>
[McpServerToolType]
public sealed class LoopTools(
    ILoopRegister register,
    TimeProvider? clock = null,
    ISessionRequestStore? requests = null,
    IWaitingQuestionStore? questions = null,
    ICapacityReader? capacity = null)
{
    internal const string CheckIn = "check_in";

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    [McpServerTool(Name = CheckIn, Title = "Check in a loop session", ReadOnly = false, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description(
        "Call at the start of every tick of a /loop session - dispatch-sessions as kind 'dispatcher', tend-brain as "
        + "'brain-tender' - with this session's id, what it is doing, how often it checks in, what it holds and its "
        + "limits (maxSessions, maxUsagePercent, maxCpuPercent, minFreeMemoryGb; each one left out is Backlog's default: "
        + "3 sessions, 80% usage, 80% CPU, 4 GB free). Records the check-in, so Backlog reads the loop as Listening, "
        + "Holding, Stalled or Not running. The answer carries command: 'pause' (do no work, keep checking in at the "
        + "heartbeat), 'resume' (carry on), 'stop' (end the loop and say so) or null when the person set none; and for "
        + "a dispatcher the session requests and answer deliveries waiting, and capacity: the room Backlog measured "
        + "against the limits - hasRoom, heldBy (the limits reached: sessions, usage, cpu, memory), workingSessions, "
        + "planUsagePercent, cpuPercent, freeMemoryGb (null where Backlog could not measure it) and the limits - or "
        + "null where this host measures none. Repeating a check-in records the same thing again.")]
    public async Task<LoopCheckInPayload> CheckInAsync(
        [Description("'dispatcher' for dispatch-sessions, 'brain-tender' for tend-brain.")]
        string kind,
        [Description("This session's own id, the one its transcript is named after.")]
        string sessionId,
        [Description("What the loop is doing: 'listening' (waiting for work), 'holding' (work waits, a limit holds it back) or 'working' (doing this tick's work).")]
        string state,
        [Description("How often this loop checks in, in minutes, from 1 to 1440. A loop quiet for this plus 10 minutes reads Stalled.")]
        int heartbeatMinutes,
        [Description("Optional. Session requests this loop claimed and has not marked yet.")]
        int requests = 0,
        [Description("Optional. Answer deliveries this loop claimed and has not marked yet.")]
        int deliveries = 0,
        [Description("Optional. Sessions this loop started that are still working.")]
        int sessions = 0,
        [Description("Optional. Brain notes this loop has in hand this tick.")]
        int notes = 0,
        [Description("Optional. The most dispatched sessions this loop runs at once, at least 1; 3 when left out.")]
        int? maxSessions = null,
        [Description("Optional. The plan usage, in percent of the weekly limit, it starts no more sessions at; above 0, at most 100; 80 when left out.")]
        double? maxUsagePercent = null,
        [Description("Optional. The CPU busy, in percent, it starts no more sessions at; above 0, at most 100; 80 when left out.")]
        double? maxCpuPercent = null,
        [Description("Optional. The free memory, in GB, the machine keeps; it starts no more sessions below it; 0 or more; 4 when left out.")]
        double? minFreeMemoryGb = null,
        CancellationToken cancellationToken = default)
    {
        var now = _clock.GetUtcNow();
        var loopKind = KindOf(kind);
        var activity = ActivityOf(state);
        var held = new LoopHeldWork(requests, deliveries, sessions, notes);

        if (LoopSessions.Refusal(sessionId, heartbeatMinutes, held) is { } refusal)
        {
            throw RepositoryScope.Failure(Error.Validation("loop.check_in", refusal));
        }

        var limits = CapacityLimits.OrDefault(maxSessions, maxUsagePercent, maxCpuPercent, minFreeMemoryGb);

        if (LoopSessions.Refusal(limits) is { } limitRefusal)
        {
            throw RepositoryScope.Failure(Error.Validation("loop.limits", limitRefusal));
        }

        // A dispatcher's room is measured before the check-in is recorded, so the register
        // keeps what the app read with it; the Brain tender has no queue to hold back. None
        // of these reads may cost the loop its check-in: one that fails reads unknown, and
        // the check-in is recorded all the same, so the loop never drifts to Stalled over it.
        LoopWaitingPayload? waiting = null;
        int? workWaiting = null;
        CapacityReading? reading = null;

        if (loopKind == LoopKind.Dispatcher)
        {
            workWaiting = await WaitingRequestsAsync(now, cancellationToken).ConfigureAwait(false);
            waiting = new LoopWaitingPayload(
                workWaiting ?? 0,
                await PendingDeliveriesAsync(now, cancellationToken).ConfigureAwait(false) ?? 0);
            reading = await ReadingAsync(limits, cancellationToken).ConfigureAwait(false);
        }

        var checkIn = LoopSessions.CheckIn(sessionId, loopKind, activity, heartbeatMinutes, held, now) with
        {
            Limits = limits,
            Reading = reading,
            WorkWaiting = workWaiting
        };
        var loop = await register.CheckInAsync(checkIn, cancellationToken).ConfigureAwait(false);

        return LoopCheckInPayload.From(loop, waiting);
    }

    /// <summary>The session requests waiting for a dispatcher — those reading Requested, none
    /// while the queue is paused (<see cref="SessionRequests.WaitingWork"/>) — or null where no
    /// queue is composed or it cannot be read.</summary>
    private async Task<int?> WaitingRequestsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (requests is null) return null;

        try
        {
            var held = await requests.ListAsync(null, cancellationToken).ConfigureAwait(false);
            var paused = await requests.IsPausedAsync(cancellationToken).ConfigureAwait(false);

            return SessionRequests.WaitingWork(held, paused, now);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>The answer deliveries reading Pending, or null where no relay is composed or
    /// it cannot be read.</summary>
    private async Task<int?> PendingDeliveriesAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (questions is null) return null;

        try
        {
            return (await questions.ListUndeliveredAsync(cancellationToken).ConfigureAwait(false))
                .Count(question => question.DeliveryAt(now) == AnswerDeliveryState.Pending);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>The room against <paramref name="limits"/>, or null where no reader is
    /// composed or it fails. One Manager per machine, and <c>check_in</c> names no
    /// dispatcher: every dispatched session counts against the limit.</summary>
    private async Task<CapacityReading?> ReadingAsync(CapacityLimits limits, CancellationToken cancellationToken)
    {
        if (capacity is null) return null;

        try
        {
            return await capacity.ReadAsync(limits, dispatcher: null, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
    }

    internal static LoopKind KindOf(string? kind) =>
        (kind ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "dispatcher" => LoopKind.Dispatcher,
            "brain-tender" => LoopKind.BrainTender,
            _ => throw RepositoryScope.Failure(Error.Validation(
                "loop.kind",
                $"kind must be 'dispatcher' or 'brain-tender', not '{kind}'."))
        };

    private static LoopActivity ActivityOf(string? state) =>
        (state ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "listening" => LoopActivity.Listening,
            "holding" => LoopActivity.Holding,
            "working" => LoopActivity.Working,
            _ => throw RepositoryScope.Failure(Error.Validation(
                "loop.state",
                $"state must be 'listening', 'holding' or 'working', not '{state}'."))
        };
}

/// <summary>What <c>check_in</c> answered.</summary>
/// <param name="SessionId">The loop session, as it checked in.</param>
/// <param name="Kind"><c>dispatcher</c> or <c>brain-tender</c>.</param>
/// <param name="Command"><c>pause</c>, <c>resume</c> or <c>stop</c> — what the person last
/// set on this loop — or null when they set none.</param>
/// <param name="CommandSetAt">When they set it, or null.</param>
/// <param name="CheckedInAt">When this check-in was recorded.</param>
/// <param name="StalledAfter">When the loop reads Stalled if it checks in no more: this
/// check-in plus its heartbeat plus 10 minutes.</param>
/// <param name="Waiting">The work waiting for a dispatcher; null for the Brain tender, which
/// has no queue.</param>
/// <param name="Capacity">The room the app measured for a dispatcher against its limits
/// (local ADR 0051 §4); null for the Brain tender and where the host measures none.</param>
public sealed record LoopCheckInPayload(
    string SessionId,
    string Kind,
    string? Command,
    DateTimeOffset? CommandSetAt,
    DateTimeOffset CheckedInAt,
    DateTimeOffset StalledAfter,
    LoopWaitingPayload? Waiting,
    LoopCapacityPayload? Capacity = null)
{
    /// <summary>The answer for <paramref name="loop"/> as it now stands: its capacity is the
    /// reading kept with its latest check-in.</summary>
    public static LoopCheckInPayload From(LoopSession loop, LoopWaitingPayload? waiting)
    {
        ArgumentNullException.ThrowIfNull(loop);

        return new LoopCheckInPayload(
            loop.SessionId,
            WireName(loop.CheckIn.Kind),
            loop.Command == LoopCommand.None ? null : SessionRequestPayload.WireName(loop.Command),
            loop.CommandSetAt,
            loop.CheckIn.At,
            LoopSessions.StalledAfter(loop.CheckIn),
            waiting,
            loop.CheckIn.Reading is { } reading ? LoopCapacityPayload.From(reading) : null);
    }

    /// <summary>A loop kind as the wire spells it.</summary>
    public static string WireName(LoopKind kind) => kind switch
    {
        LoopKind.Dispatcher => "dispatcher",
        LoopKind.BrainTender => "brain-tender",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "A loop kind with no wire name.")
    };
}

/// <summary>The work waiting for a dispatcher at its check-in.</summary>
/// <param name="SessionRequests">Session requests reading Requested, a lapsed claim included.</param>
/// <param name="Deliveries">Answer deliveries reading Pending, a lapsed claim included.</param>
public sealed record LoopWaitingPayload(int SessionRequests, int Deliveries);

/// <summary>A dispatcher's room at its check-in (local ADR 0051 §4,
/// <c>.devbook/domain/sessions/domain.md#capacity-reading</c>): four figures beside the
/// loop's limits. A null figure is one the app could not measure, and holds nothing back.</summary>
/// <param name="HasRoom">Whether no limit is reached.</param>
/// <param name="HeldBy">Every limit reached, in the order <c>sessions</c>, <c>usage</c>,
/// <c>cpu</c>, <c>memory</c>; empty when there is room.</param>
/// <param name="WorkingSessions">The dispatched sessions Running or Stalled.</param>
/// <param name="PlanUsagePercent">The week's estimated share of the weekly limit, in percent.</param>
/// <param name="CpuPercent">CPU busy, in percent.</param>
/// <param name="FreeMemoryGb">Free memory, in GB.</param>
/// <param name="Limits">The limits the figures were held against.</param>
public sealed record LoopCapacityPayload(
    bool HasRoom,
    IReadOnlyList<string> HeldBy,
    int? WorkingSessions,
    double? PlanUsagePercent,
    double? CpuPercent,
    double? FreeMemoryGb,
    LoopLimitsPayload Limits)
{
    /// <summary>The payload for <paramref name="reading"/>.</summary>
    public static LoopCapacityPayload From(CapacityReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);

        return new LoopCapacityPayload(
            reading.HasRoom,
            [.. reading.HeldBy().Select(WireName)],
            reading.WorkingSessions,
            reading.PlanUsagePercent,
            reading.CpuPercent,
            reading.FreeMemoryGb,
            new LoopLimitsPayload(reading.Limits.Sessions, reading.Limits.UsagePercent, reading.Limits.CpuPercent, reading.Limits.MinFreeGb));
    }

    /// <summary>A capacity limit as the wire spells it.</summary>
    public static string WireName(CapacityLimit limit) => limit switch
    {
        CapacityLimit.Sessions => "sessions",
        CapacityLimit.Usage => "usage",
        CapacityLimit.Cpu => "cpu",
        CapacityLimit.Memory => "memory",
        _ => throw new ArgumentOutOfRangeException(nameof(limit), limit, "A capacity limit with no wire name.")
    };
}

/// <summary>The limits a dispatcher starts no more sessions at (local ADR 0051 §4).</summary>
/// <param name="Sessions">Dispatched sessions working at once.</param>
/// <param name="UsagePercent">Plan usage, in percent of the weekly limit.</param>
/// <param name="CpuPercent">CPU busy, in percent.</param>
/// <param name="MinFreeGb">Free memory the machine keeps, in GB.</param>
public sealed record LoopLimitsPayload(int Sessions, double UsagePercent, double CpuPercent, double MinFreeGb);
