using Backlog.SharedKernel.Mcp;

namespace Backlog.Desktop.UI.Mcp;

/// <summary>
/// The desktop listener's limit on failed bearer attempts (local ADR 0052, Phase 0):
/// after <see cref="MaxFailures"/> within <see cref="Window"/>, every bearer request is
/// refused for <see cref="Lockout"/>, the right token included, and the trip is one
/// audit record.
/// <para>
/// One limiter for the whole listener rather than one per client: every caller is on
/// loopback, so there is no address to tell a guesser from the agent beside it, and a
/// guesser on this machine is the case the limit is for. A lockout therefore also
/// refuses the person's own sessions for its minute, which is the price of the limit
/// and why it is short.
/// </para>
/// <para>
/// Here and not in the worker, for <see cref="McpLoopbackGuard"/>'s reason: no test
/// project can reference the MAUI head. Thread-safe: requests arrive concurrently.
/// </para>
/// </summary>
public sealed class McpBearerLimiter(TimeProvider? time = null)
{
    /// <summary>How many failed attempts within <see cref="Window"/> trip the lockout.</summary>
    public const int MaxFailures = 10;

    /// <summary>How far back failed attempts are counted.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(60);

    /// <summary>How long every bearer request is refused once tripped.</summary>
    public static readonly TimeSpan Lockout = TimeSpan.FromSeconds(60);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Lock _gate = new();

    /// <summary>When each counted failure happened, oldest first.</summary>
    private readonly Queue<DateTimeOffset> _failures = new();

    private DateTimeOffset? _lockedUntil;

    /// <summary>How long the lockout still has to run, or null when a bearer request
    /// may be checked. A lockout that has run out starts the count afresh.</summary>
    public TimeSpan? RetryAfter()
    {
        var now = _time.GetUtcNow();

        lock (_gate)
        {
            if (_lockedUntil is not { } until) return null;
            if (now < until) return until - now;

            _lockedUntil = null;
            _failures.Clear();
            return null;
        }
    }

    /// <summary>The token a Rotate replaced, and until when presenting it is not
    /// counted as a failure.</summary>
    private string? _retired;

    private DateTimeOffset _retiredUntil;

    /// <summary>
    /// Says the token was rotated away from <paramref name="previous"/>. For one
    /// <see cref="Window"/> a request presenting it is refused but not counted: every
    /// running registration sends it once more before its headers helper re-reads the
    /// new one, and a person running ten sessions must not lock themselves out by
    /// pressing Rotate.
    /// </summary>
    public void Retire(string? previous)
    {
        if (string.IsNullOrEmpty(previous)) return;

        var now = _time.GetUtcNow();

        lock (_gate)
        {
            _retired = previous;
            _retiredUntil = now + Window;
        }
    }

    /// <summary>
    /// Counts one failed attempt, whose <c>Authorization</c> header was
    /// <paramref name="authorization"/>; a missing header counts too, since a guesser
    /// need send none. Answers the audit record to write when this failure is the one
    /// that trips the lockout, else null. Not counted: a failure that arrives while
    /// locked out — one checked before the trip and answered after it — and one
    /// presenting the token <see cref="Retire"/> named, within its grace.
    /// </summary>
    public McpAuditRecord? Fail(string? authorization = null)
    {
        var now = _time.GetUtcNow();

        lock (_gate)
        {
            if (_lockedUntil is { } until && now < until) return null;

            if (_retired is not null)
            {
                if (now >= _retiredUntil) _retired = null;
                else if (McpLoopbackGuard.IsAuthorized(authorization, _retired)) return null;
            }

            _lockedUntil = null;
            while (_failures.Count > 0 && now - _failures.Peek() >= Window) _failures.Dequeue();

            _failures.Enqueue(now);
            if (_failures.Count < MaxFailures) return null;

            _failures.Clear();
            _lockedUntil = now + Lockout;
        }

        return new McpAuditRecord(
            now,
            McpAudit.NoValidTokenCaller,
            McpAudit.BearerLockout,
            TargetId: null,
            McpCallOutcome.Refused,
            McpCallAudit.CorrelationId());
    }
}
