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

    /// <summary>
    /// Whether any device is on record under an owner other than this one. Asked
    /// only by a signed-in enrolment on a service with no pinned owner (local ADR
    /// 0058 §1), so it reads across every owner on purpose: the question is whether
    /// joining <paramref name="ownerId"/> would leave someone else's devices behind.
    /// </summary>
    Task<bool> AnyOutside(OwnerId ownerId, CancellationToken cancellationToken = default);

    /// <summary>Every device this owner has, in no particular order.</summary>
    Task<IReadOnlyList<Device>> ListByOwner(OwnerId ownerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Binds <paramref name="publicKey"/> to a device that has no key yet and drops
    /// its credential hash, so the credential mints no further token (local ADR 0058
    /// §2). Answers false when there is no such device or it is already bound — a
    /// concurrent binding included — so a device migrates once and only once.
    /// </summary>
    Task<bool> BindKey(DeviceId id, string publicKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Forgets one device of this owner, so its credential mints no further token.
    /// Answers false when the owner has no such device — already removed, or never
    /// theirs — which the caller reports the same way.
    /// </summary>
    Task<bool> Remove(OwnerId ownerId, DeviceId id, CancellationToken cancellationToken = default);
}
