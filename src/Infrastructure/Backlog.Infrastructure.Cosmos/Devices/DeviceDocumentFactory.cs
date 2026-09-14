using Backlog.Modules.Sync.DomainModels;

namespace Backlog.Infrastructure.Cosmos.Devices;

/// <summary>
/// Between the domain record and the stored document, in both directions.
/// <para>
/// Separate from <see cref="CosmosDeviceRegistry"/> so the mapping can be
/// tested without a store: the thing that is easy to get silently wrong here is
/// the spelling of the two ids the container and its queries depend on, and
/// that does not need an emulator to pin.
/// </para>
/// </summary>
internal static class DeviceDocumentFactory
{
    /// <summary>The document to write for one registration.</summary>
    public static DeviceDocument From(Device device)
    {
        ArgumentNullException.ThrowIfNull(device);

        return new DeviceDocument
        {
            Id = ReplicaDocumentSerialization.Key(device.Id.Value),
            OwnerId = ReplicaDocumentSerialization.Key(device.OwnerId.Value),
            Name = device.Name,
            CredentialHash = device.CredentialHash,
            RegisteredAt = device.RegisteredAt,
        };
    }

    /// <summary>
    /// The record to hand back for one stored document.
    /// <para>
    /// A document whose id or owner is not a GUID is not one this service
    /// wrote. It comes back as null and the registry answers "no such device":
    /// on the token path that is the same 401 an unknown id gets, which is the
    /// right answer for a document nobody can be authenticated against.
    /// </para>
    /// </summary>
    public static Device? ToDevice(DeviceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!Guid.TryParse(document.Id, out var id) || !Guid.TryParse(document.OwnerId, out var ownerId))
        {
            return null;
        }

        return new Device(
            new DeviceId(id),
            new OwnerId(ownerId),
            document.Name,
            document.CredentialHash,
            document.RegisteredAt);
    }
}
