namespace Backlog.SharedKernel;

/// <summary>Where one device stands with its owner right now (local ADR 0058, Phase 2).</summary>
public enum DeviceStanding
{
    /// <summary>The device is one of the owner's paired devices now, or this installation.</summary>
    Paired,

    /// <summary>The device was one of the owner's and has since been revoked.</summary>
    Revoked,

    /// <summary>The device was never seen among the owner's devices.</summary>
    Unknown
}

/// <summary>
/// PORT — whether a device is one of the owner's paired devices right now.
/// <para>
/// In the kernel for the reason <see cref="IDeviceIdentitySource"/> is: Sync knows the
/// owner's devices and Sessions asks, before it hands an answer to a waiting agent, whether
/// the device it came from is still paired (local ADR 0041, ADR 0058 Phase 2). Neither may
/// see the other.
/// </para>
/// </summary>
public interface IPairedDevices
{
    /// <summary>
    /// Where <paramref name="deviceId"/> stands now. This installation's own identity is
    /// always <see cref="DeviceStanding.Paired"/>. Throws when the owner's devices cannot be
    /// read, so a caller never takes a device it could not check as paired.
    /// </summary>
    Task<DeviceStanding> StandingOfAsync(Guid deviceId, CancellationToken cancellationToken = default);
}
