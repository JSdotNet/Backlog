using System.Collections.Concurrent;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Ports;

namespace Backlog.Modules.Sync.Adapters;

/// <summary>
/// Spent assertion ids, in memory. Enough for a service that runs one replica: a
/// restart forgets them, and what that reopens is bounded by the five minutes an
/// assertion may live (<see cref="Abstractions.SyncAssertion.MaxLifetime"/>). A
/// second replica needs a shared store behind the port.
/// <para>
/// Lapsed entries are swept on the way in, so the table holds no more than the
/// assertions of the last few minutes.
/// </para>
/// </summary>
public sealed class InMemoryAssertionReplayCache(TimeProvider clock) : IAssertionReplayCache
{
    private readonly ConcurrentDictionary<(DeviceId Device, string Id), DateTimeOffset> _spent = new();

    public Task<bool> TryAdd(DeviceId deviceId, string assertionId, DateTimeOffset acceptableUntil, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(assertionId);

        var now = clock.GetUtcNow();
        Sweep(now);

        var key = (deviceId, assertionId);

        if (_spent.TryAdd(key, acceptableUntil))
        {
            return Task.FromResult(true);
        }

        // Spent before, and lapsed since — the sweep and this call raced. Taking it
        // again is only right if nobody else just did.
        return Task.FromResult(
            _spent.TryGetValue(key, out var until)
            && until <= now
            && _spent.TryUpdate(key, acceptableUntil, until));
    }

    private void Sweep(DateTimeOffset now)
    {
        foreach (var entry in _spent)
        {
            if (entry.Value <= now)
            {
                _spent.TryRemove(entry);
            }
        }
    }
}
