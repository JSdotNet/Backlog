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

    /// <summary>Revokes another device of this owner. Where the host composed a step-up, the
    /// person confirms with Windows Hello first (local ADR 0058 §6).</summary>
    Task<Result> RevokeDeviceAsync(Guid deviceId, CancellationToken cancellationToken = default);

    /// <summary>Revokes the device that just redeemed this device's pairing code, because the
    /// person answered Refuse to "Pair it?". Where the host composed a step-up, the person
    /// confirms with Windows Hello first, as for a revocation (local ADR 0058 §6). The answer may
    /// arrive from the phone while nobody is at this desktop, so the prompt expires after
    /// <see cref="DevicePairingClient.RefuseConfirmationWindow"/>; a refusal cancelled,
    /// unavailable or expired is never sent, and the failure says which
    /// (<see cref="DeviceStepUp.DeclinedCode"/>, <see cref="DeviceStepUp.UnavailableCode"/>,
    /// <see cref="DeviceStepUp.ExpiredCode"/>).</summary>
    Task<Result> RefuseDeviceAsync(Guid deviceId, CancellationToken cancellationToken = default);
}
