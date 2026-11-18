using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Tests;

/// <summary>A Loop Register held in memory, applying the <see cref="LoopSessions"/>
/// transitions against <paramref name="clock"/> the way the SQLite register applies them
/// in one write.</summary>
internal sealed class InMemoryLoopRegister(TimeProvider? clock = null) : ILoopRegister
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, LoopSession> _held = new(StringComparer.Ordinal);

    public Task<LoopSession> CheckInAsync(LoopCheckIn checkIn, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var recorded = LoopSessions.Record(_held.GetValueOrDefault(checkIn.SessionId), checkIn);
            _held[checkIn.SessionId] = recorded;

            return Task.FromResult(recorded);
        }
    }

    public Task<LoopSession?> RecordWaitAsync(string sessionId, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return Task.FromResult<LoopSession?>(null);

        lock (_gate)
        {
            if (!_held.TryGetValue(sessionId.Trim(), out var loop)) return Task.FromResult<LoopSession?>(null);

            var waited = LoopSessions.RecordWait(loop, at);
            _held[loop.SessionId] = waited;

            return Task.FromResult<LoopSession?>(waited);
        }
    }

    public Task<LoopSession?> GetAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        lock (_gate) return Task.FromResult(_held.GetValueOrDefault(sessionId.Trim()));
    }

    public Task<IReadOnlyList<LoopSession>> ListAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<LoopSession> listed =
                [.. _held.Values.OrderByDescending(loop => loop.CheckIn.At).ThenBy(loop => loop.SessionId, StringComparer.Ordinal)];

            return Task.FromResult(listed);
        }
    }

    public Task<LoopSession?> SetCommandAsync(string sessionId, LoopCommand command, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_held.TryGetValue(sessionId.Trim(), out var loop)) return Task.FromResult<LoopSession?>(null);

            var changed = LoopSessions.Command(loop, command, _clock.GetUtcNow());
            _held[loop.SessionId] = changed;

            return Task.FromResult<LoopSession?>(changed);
        }
    }
}
