using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Observability;
using Backlog.Modules.Sync.Ports;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;
using Microsoft.Extensions.Logging;

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
/// <para>
/// A revoked phone's push installation is deleted too (local ADR 0038 §4), so it is pushed
/// nothing more. Best effort: the device is already revoked when the hub is asked, and a hub
/// that fails is logged and counted rather than turning the revoke into a failure.
/// </para>
/// </summary>
public sealed partial class RevokeDeviceCommandHandler(
    IDeviceRegistry devices,
    INotificationPushChannel pushes,
    ILogger<RevokeDeviceCommandHandler> log)
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

        if (!await devices.Remove(command.Scope.OwnerId, target.Id, cancellationToken))
        {
            return NotFound();
        }

        try
        {
            await pushes.RemoveAsync(target.Id, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            SyncTelemetry.PushFailed.Add(1, new KeyValuePair<string, object?>(
                SyncTelemetry.PushOperationTag, SyncTelemetry.PushOperations.Remove));
            LogRemoveFailed(log, target.Id.Value, ex);
        }

        return Result.Success();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The push hub did not delete the installation of revoked device {DeviceId}; it may still be pushed until its installation is replaced.")]
    private static partial void LogRemoveFailed(ILogger logger, Guid deviceId, Exception exception);

    private static Result NotFound() =>
        Result.Failure(Error.NotFound(SyncErrorCodes.DeviceNotFound, "There is no such device for this owner."));
}
