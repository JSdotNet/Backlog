using System.ComponentModel;

using Backlog.Modules.Sessions.Abstractions;
using Backlog.SharedKernel.Results;

using ModelContextProtocol.Server;

namespace Backlog.Infrastructure.Mcp;

/// <summary>
/// <c>check_in</c>, the two-way check-in of local ADR 0051 §3: a loop session (the Manager
/// under <c>dispatch-sessions</c>, the Brain tender under <c>tend-brain</c>) says on every
/// tick what it is doing, and the answer carries the pause, resume or stop the person last
/// set on it and the work waiting for it.
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
[McpServerToolType]
public sealed class LoopTools(
    ILoopRegister register,
    TimeProvider? clock = null,
    ISessionRequestStore? requests = null,
    IWaitingQuestionStore? questions = null)
{
    internal const string CheckIn = "check_in";

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    [McpServerTool(Name = CheckIn, Title = "Check in a loop session", ReadOnly = false, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description(
        "Call at the start of every tick of a /loop session - dispatch-sessions as kind 'dispatcher', tend-brain as "
        + "'brain-tender' - with this session's id, what it is doing, how often it checks in and what it holds. Records "
        + "the check-in, so Backlog reads the loop as Listening, Holding, Stalled or Not running. The answer carries "
        + "command: 'pause' (do no work, keep checking in at the heartbeat), 'resume' (carry on), 'stop' (end the loop "
        + "and say so) or null when the person set none, and for a dispatcher the session requests and answer "
        + "deliveries waiting. Repeating a check-in records the same thing again.")]
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

        var checkIn = LoopSessions.CheckIn(sessionId, loopKind, activity, heartbeatMinutes, held, now);
        var loop = await register.CheckInAsync(checkIn, cancellationToken).ConfigureAwait(false);
        var waiting = loopKind == LoopKind.Dispatcher
            ? await WaitingAsync(now, cancellationToken).ConfigureAwait(false)
            : null;

        return LoopCheckInPayload.From(loop, waiting);
    }

    private async Task<LoopWaitingPayload> WaitingAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var waitingRequests = requests is null
            ? 0
            : SessionRequests.Waiting(await requests.ListAsync(null, cancellationToken).ConfigureAwait(false), now).Count;
        var waitingDeliveries = questions is null
            ? 0
            : (await questions.ListUndeliveredAsync(cancellationToken).ConfigureAwait(false))
                .Count(question => question.DeliveryAt(now) == AnswerDeliveryState.Pending);

        return new LoopWaitingPayload(waitingRequests, waitingDeliveries);
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
public sealed record LoopCheckInPayload(
    string SessionId,
    string Kind,
    string? Command,
    DateTimeOffset? CommandSetAt,
    DateTimeOffset CheckedInAt,
    DateTimeOffset StalledAfter,
    LoopWaitingPayload? Waiting)
{
    /// <summary>The answer for <paramref name="loop"/> as it now stands.</summary>
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
            waiting);
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
