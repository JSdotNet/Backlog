using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Ports;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Sync.Features.Devices;

/// <summary>One device of the caller's owner revokes another.</summary>
public sealed record RevokeDeviceCommand(OwnerScope Scope, Guid DeviceId);

/// <summary>
/// Forgets a device the caller paired, so its registration credential mints no further
/// token. What a desktop does when its person refuses a device that just redeemed one of
/// its pairing codes.
/// <para>
/// Only the device whose code paired the target may revoke it
/// (<see cref="SyncErrorCodes.DeviceRevokeNotPairer"/> otherwise). That is the whole use,
/// and it is what stops a device that redeemed a code it should not have from revoking the
/// desktop that is about to refuse it. The caller itself is refused too
/// (<see cref="SyncErrorCodes.DeviceRevokeSelf"/>), and a device that is gone already, or
/// under another owner, answers <see cref="SyncErrorCodes.DeviceNotFound"/>, so a guessed id
/// confirms nothing.
/// </para>
/// <para>
/// A token the device already holds stays valid until it expires — minutes, per inherited
/// ADR 0012 — because tokens are checked by signature, not against the registry.
/// </para>
/// </summary>
public sealed class RevokeDeviceCommandHandler(IDeviceRegistry devices)
    : ICommandHandler<RevokeDeviceCommand, Result>
{
    public async Task<Result> Handle(RevokeDeviceCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.DeviceId == command.Scope.DeviceId.Value)
        {
            return Result.Failure(Error.Validation(
                SyncErrorCodes.DeviceRevokeSelf,
                "A device cannot revoke itself. Revoke it from the device that paired it."));
        }

        var target = await devices.FindById(new DeviceId(command.DeviceId), cancellationToken);

        if (target is null || target.OwnerId != command.Scope.OwnerId)
        {
            return NotFound();
        }

        if (target.PairedBy != command.Scope.DeviceId)
        {
            return Result.Failure(Error.Validation(
                SyncErrorCodes.DeviceRevokeNotPairer,
                "Only the device whose pairing code paired this device can revoke it."));
        }

        return await devices.Remove(command.Scope.OwnerId, target.Id, cancellationToken)
            ? Result.Success()
            : NotFound();
    }

    private static Result NotFound() =>
        Result.Failure(Error.NotFound(SyncErrorCodes.DeviceNotFound, "There is no such device for this owner."));
}
