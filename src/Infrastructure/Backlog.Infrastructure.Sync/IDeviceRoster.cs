using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.SharedKernel.Results;

namespace Backlog.Infrastructure.Sync;

/// <summary>
/// The owner's devices as a desktop sees them on the sync service: which there are, and
/// revoking one. <see cref="DevicePairingClient"/> answers it; <see cref="PairingSubjects"/>
/// reads it while a pairing code is open, and <see cref="PairingAnswers"/> revokes through it.
/// </summary>
public interface IDeviceRoster
{
    /// <summary>Every device of this device's owner, and which one is this device.</summary>
    Task<Result<DeviceListResponse>> ListDevicesAsync(CancellationToken cancellationToken = default);

    /// <summary>Revokes another device of this owner.</summary>
    Task<Result> RevokeDeviceAsync(Guid deviceId, CancellationToken cancellationToken = default);
}
