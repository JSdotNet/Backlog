using Backlog.Modules.Sync.DomainModels;

namespace Backlog.Modules.Sync.Ports;

/// <summary>
/// Where registered devices are kept. Declared here and implemented outside, so
/// the module says what it needs of storage without naming the store.
/// </summary>
public interface IDeviceRegistry
{
    /// <summary>Records a newly registered or newly paired device.</summary>
    Task Add(Device device, CancellationToken cancellationToken = default);

    /// <summary>
    /// The device with this id, or null. Deliberately not scoped to an owner:
    /// this is the one lookup that runs before there is a scope, when a device
    /// presents its credential to get a token. Every other read takes the owner.
    /// </summary>
    Task<Device?> FindById(DeviceId id, CancellationToken cancellationToken = default);

    /// <summary>How many devices this owner has.</summary>
    Task<int> CountByOwner(OwnerId ownerId, CancellationToken cancellationToken = default);

    /// <summary>Whether this owner exists at all — that is, has at least one
    /// device. There is no owner record separate from its devices.</summary>
    Task<bool> OwnerExists(OwnerId ownerId, CancellationToken cancellationToken = default);
}
