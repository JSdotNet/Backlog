using System.Collections.Concurrent;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Ports;

namespace Backlog.Modules.Sync.Adapters;

/// <summary>
/// In-memory stand-in that a Cosmos-backed registry replaces — the same status
/// as <see cref="InMemoryTaskReplica"/> beside it, and no more permanent than
/// that. Everything is lost on restart, which for the device table means every
/// paired device has to register again.
/// <para>
/// It scopes by owner where the port asks it to, so swapping the adapter is a
/// change of storage and not a change of who can read what: the boundary is in
/// the handlers and in this signature, not in whatever partitioning the real
/// store happens to use.
/// </para>
/// </summary>
public sealed class InMemoryDeviceRegistry : IDeviceRegistry
{
    private readonly ConcurrentDictionary<DeviceId, Device> _devices = new();

    public Task Add(Device device, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);

        _devices[device.Id] = device;
        return Task.CompletedTask;
    }

    public Task<Device?> FindById(DeviceId id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_devices.TryGetValue(id, out var device) ? device : null);

    public Task<int> CountByOwner(OwnerId ownerId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_devices.Values.Count(device => device.OwnerId == ownerId));

    public Task<bool> OwnerExists(OwnerId ownerId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_devices.Values.Any(device => device.OwnerId == ownerId));
}
