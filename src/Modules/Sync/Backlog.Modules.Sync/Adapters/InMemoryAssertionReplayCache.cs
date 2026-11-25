using System.Collections.Concurrent;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Ports;

namespace Backlog.Modules.Sync.Adapters;

/// <summary>
/// Spent assertion ids, in memory: the Development and test stand-in. A restart
/// forgets them and a second instance never sees them, so the deployed service
/// registers the Cosmos store over this one.
/// <para>
/// Lapsed entries are swept on the way in, at most once every
/// <see cref="SweepInterval"/>, so the table holds no more than the assertions of
/// the last few minutes and a burst of calls does not walk it on every one. An id
/// that lapsed since the last sweep still reads as spent until the next: a refusal
/// in the safe direction, for an id no honest device reuses.
/// </para>
/// </summary>
public sealed class InMemoryAssertionReplayCache(TimeProvider clock) : IAssertionReplayCache
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<(DeviceId Device, string Id), DateTimeOffset> _spent = new();

    private long _nextSweepTicks;

    public Task<bool> TryAdd(DeviceId deviceId, string assertionId, DateTimeOffset acceptableUntil, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(assertionId);

        SweepIfDue(clock.GetUtcNow());

        return Task.FromResult(_spent.TryAdd((deviceId, assertionId), acceptableUntil));
    }

    private void SweepIfDue(DateTimeOffset now)
    {
        var due = Interlocked.Read(ref _nextSweepTicks);

        // One caller per interval wins the exchange and sweeps; the rest go on.
        if (now.UtcTicks < due
            || Interlocked.CompareExchange(ref _nextSweepTicks, (now + SweepInterval).UtcTicks, due) != due)
        {
            return;
        }

        foreach (var entry in _spent)
        {
            if (entry.Value <= now)
            {
                _spent.TryRemove(entry);
            }
        }
    }
}
