using Backlog.Modules.Sync.DomainModels;

namespace Backlog.Modules.Sync.Ports;

/// <summary>
/// The assertion ids a device has already spent (local ADR 0058 §2). A signed
/// assertion is good for minutes, so without this anybody who saw one on the wire
/// could trade it for a token of their own until it ran out.
/// </summary>
public interface IAssertionReplayCache
{
    /// <summary>
    /// Records <paramref name="assertionId"/> as spent by <paramref name="deviceId"/>
    /// until <paramref name="acceptableUntil"/>, the last moment the assertion could
    /// still be taken. Answers false when the device already spent that id and it has
    /// not lapsed: a replay.
    /// </summary>
    Task<bool> TryAdd(DeviceId deviceId, string assertionId, DateTimeOffset acceptableUntil, CancellationToken cancellationToken = default);
}
