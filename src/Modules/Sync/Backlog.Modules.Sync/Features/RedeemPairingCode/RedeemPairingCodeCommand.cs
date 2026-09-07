using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Ports;
using Backlog.Modules.Sync.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Sync.Features.RedeemPairingCode;

/// <summary>
/// The second device in. It has no credential and no token yet — the code it
/// was handed out of band is the whole of its claim, which is why this slice
/// runs anonymously and why every failure it can produce says as little as
/// possible.
/// </summary>
public sealed record RedeemPairingCodeCommand(string Code, string DeviceName);

/// <summary>
/// Trades a live code for a registration credential under the code's owner.
/// <para>
/// There is no per-code attempt counter, and that is a decision rather than an
/// omission. Codes are stored by hash, so a wrong guess hashes to nothing we
/// hold: there is no code to attribute the failure to and none to burn, and a
/// counter on the code somebody was actually aiming at could only ever be
/// incremented by somebody who already had it right. What limits guessing is
/// the search itself — thirty-one symbols over eight characters is about
/// 8.5 x 10^11 codes, against a window of ten minutes and a single-use code —
/// so a cap would add a field and a branch without adding a bound.
/// </para>
/// <para>
/// A per-IP rate limiter is the control that would help against a caller trying
/// this in bulk, and it is deliberately not in this slice: it belongs at the
/// edge, in front of every anonymous endpoint rather than inside one handler,
/// and it is not built yet.
/// </para>
/// </summary>
public sealed class RedeemPairingCodeCommandHandler(
    IPairingCodeStore codes,
    IDeviceRegistry devices,
    ICredentialHasher hasher,
    IRegistrationCredentialGenerator credentials,
    TimeProvider clock)
    : ICommandHandler<RedeemPairingCodeCommand, Result<DeviceRegistrationResponse>>
{
    public async Task<Result<DeviceRegistrationResponse>> Handle(
        RedeemPairingCodeCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var name = DeviceName.Validate(command.DeviceName);
        if (name.IsFailure)
        {
            return name.Error;
        }

        var normalized = PairingCodeFormat.Normalize(command.Code ?? string.Empty);
        if (!PairingCodeFormat.IsWellFormed(normalized))
        {
            return Error.Validation(
                SyncErrorCodes.PairingCodeMalformed,
                $"A pairing code is {PairingCodeFormat.Length} characters from the pairing alphabet.");
        }

        var codeHash = hasher.Hash(normalized);
        var code = await codes.FindByHash(codeHash, cancellationToken);

        if (code is null)
        {
            return Error.NotFound(
                SyncErrorCodes.PairingCodeNotFound,
                "That pairing code is not one this service issued. Ask the paired device for a new one.");
        }

        if (code.Redeemed)
        {
            return Error.Conflict(
                SyncErrorCodes.PairingCodeUsed,
                "That pairing code has already paired a device. Codes work once.");
        }

        if (code.HasExpired(clock.GetUtcNow()))
        {
            return Error.Conflict(
                SyncErrorCodes.PairingCodeExpired,
                "That pairing code has expired. Ask the paired device for a new one.");
        }

        // Burn before registering: two devices racing on one code both pass the
        // checks above, and only the one that wins the burn may have a device.
        if (!await codes.TryBurn(codeHash, cancellationToken))
        {
            return Error.Conflict(
                SyncErrorCodes.PairingCodeUsed,
                "That pairing code has already paired a device. Codes work once.");
        }

        var deviceId = DeviceId.New();
        var credential = credentials.Next();

        await devices.Add(
            new Device(deviceId, code.OwnerId, name.Value, hasher.Hash(credential), clock.GetUtcNow()),
            cancellationToken);

        return new DeviceRegistrationResponse(code.OwnerId.Value, deviceId.Value, credential);
    }
}
