using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Features.Enrolment;
using Backlog.Modules.Sync.Observability;
using Backlog.Modules.Sync.Ports;
using Backlog.Modules.Sync.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace Backlog.Modules.Sync.Features.RegisterFirstDevice;

/// <summary>
/// A device enrols as the owner's: the first device, or — when the owner already has
/// devices — a recovery. What authorizes it is the one-time code an owner sign-in
/// handed back to its loopback and the PKCE verifier behind it (local ADR 0058 §1).
/// A device that sends its <paramref name="PublicKey"/> is bound to that key and gets
/// no credential (§2).
/// </summary>
public sealed record RegisterFirstDeviceCommand(string DeviceName, string? EnrolmentCode = null, string? CodeVerifier = null, string? PublicKey = null);

/// <summary>
/// Redeems the enrolment code, then mints the device and its credential, storing only
/// the credential's hash. The plaintext goes back in the response and is never
/// recoverable from here afterwards. A device that brought a public key gets no
/// credential: the key is stored instead, checked before the code is burned so a
/// malformed key does not cost the owner a sign-in.
/// <para>
/// The verifier is checked before the code is burned, so somebody who saw a code but
/// not the verifier cannot spend it and lock the owner out of that sign-in.
/// </para>
/// <para>
/// With an owner configured, the device joins that owner. When the owner already has
/// devices this is recovery: every other device is revoked, so a lost or stolen one
/// mints no further token. A token one of them already holds runs out within its
/// lifetime — minutes, per inherited ADR 0012. With no owner configured, a
/// Development service only, every enrolment mints an owner of its own.
/// </para>
/// <para>
/// A deployed service with no pinned owner derives one from the GitHub user, which is
/// a new owner wherever devices were enrolled before. While any device of another owner
/// is on record, such an enrolment is refused before anything is added or revoked, so
/// the existing owner is never silently orphaned.
/// </para>
/// </summary>
public sealed partial class RegisterFirstDeviceCommandHandler(
    IDeviceRegistry devices,
    IEnrolmentTicketStore tickets,
    OwnerEnrolment owner,
    ICredentialHasher hasher,
    IRegistrationCredentialGenerator credentials,
    INotificationPushChannel pushes,
    TimeProvider clock,
    ILogger<RegisterFirstDeviceCommandHandler> log)
    : ICommandHandler<RegisterFirstDeviceCommand, Result<DeviceRegistrationResponse>>
{
    public async Task<Result<DeviceRegistrationResponse>> Handle(
        RegisterFirstDeviceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validName = DeviceName.Validate(command.DeviceName);
        if (validName is not Ok<string>(var name))
        {
            return (Error)validName.Value!;
        }

        string? publicKey = null;
        if (command.PublicKey is not null)
        {
            var validated = DevicePublicKey.Validate(command.PublicKey);
            if (validated is not Ok<string>(var validKey))
            {
                return (Error)validated.Value!;
            }

            publicKey = validKey;
        }

        if (string.IsNullOrEmpty(command.EnrolmentCode) || !EnrolmentPkce.IsWellFormedVerifier(command.CodeVerifier))
        {
            return Error.Validation(
                SyncErrorCodes.EnrolmentCodeRequired,
                "Registering a device needs the owner's sign-in. Start it from Backlog.");
        }

        var key = hasher.Hash(command.EnrolmentCode);
        var ticket = await tickets.FindByHash(key, cancellationToken);

        if (ticket is not { Kind: EnrolmentTicketKind.Code })
        {
            return Error.NotFound(SyncErrorCodes.EnrolmentCodeNotFound, "That enrolment code is not one this service issued.");
        }

        if (ticket.Redeemed)
        {
            return Error.Conflict(SyncErrorCodes.EnrolmentCodeUsed, "That enrolment code has already registered a device. Sign in again.");
        }

        if (ticket.HasExpired(clock.GetUtcNow()))
        {
            return Error.Conflict(SyncErrorCodes.EnrolmentCodeExpired, "That enrolment code has expired. Sign in again.");
        }

        if (!EnrolmentPkce.Matches(command.CodeVerifier!, ticket.CodeChallenge))
        {
            return Error.Validation(
                SyncErrorCodes.EnrolmentVerifierMismatch,
                "This device did not start the sign-in that enrolment code came from.");
        }

        if (owner.MustCheckForOtherOwners && owner.Owner is { } derived && await devices.AnyOutside(derived, cancellationToken))
        {
            // Before the burn, so the code is not spent on a refusal the person cannot
            // act on; they sign in again once the owner is pinned.
            LogOwnerUnpinned(log, derived.Value);
            return Error.Conflict(
                SyncErrorCodes.EnrolmentOwnerUnpinned,
                "This sync service holds devices of an owner it is not pinned to. Its operator must set Owner__OwnerId to that owner's id; until then no device enrols or recovers.");
        }

        if (!await tickets.TryBurn(key, cancellationToken))
        {
            return Error.Conflict(SyncErrorCodes.EnrolmentCodeUsed, "That enrolment code has already registered a device. Sign in again.");
        }

        var ownerId = owner.Owner ?? OwnerId.New();
        var deviceId = DeviceId.New();
        var credential = publicKey is null ? credentials.Next() : null;

        // The new device first, so a recovery that fails halfway never leaves the
        // owner with no device at all.
        await devices.Add(
            new Device(
                deviceId,
                ownerId,
                name,
                credential is null ? null : hasher.Hash(credential),
                clock.GetUtcNow(),
                PublicKey: publicKey),
            cancellationToken);

        var revoked = owner.RequiresSignIn
            ? await RevokeOthers(ownerId, deviceId, cancellationToken)
            : 0;

        return new DeviceRegistrationResponse(ownerId.Value, deviceId.Value, credential, revoked);
    }

    private async Task<int> RevokeOthers(OwnerId ownerId, DeviceId keep, CancellationToken cancellationToken)
    {
        var revoked = 0;

        foreach (var other in await devices.ListByOwner(ownerId, cancellationToken))
        {
            if (other.Id == keep || !await devices.Remove(ownerId, other.Id, cancellationToken))
            {
                continue;
            }

            revoked++;

            try
            {
                await pushes.RemoveAsync(other.Id, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                SyncTelemetry.PushFailed.Add(1, new KeyValuePair<string, object?>(
                    SyncTelemetry.PushOperationTag, SyncTelemetry.PushOperations.Remove));
                LogRemoveFailed(log, other.Id.Value, ex);
            }
        }

        if (revoked > 0)
        {
            LogRecovered(log, keep.Value, revoked);
        }

        return revoked;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Refused a signed-in enrolment: Owner__OwnerId is unset and devices of an owner other than the derived {DerivedOwnerId} are on record. Pin the existing owner with Owner__OwnerId (azd: SYNC_OWNER_ID) and redeploy.")]
    private static partial void LogOwnerUnpinned(ILogger logger, Guid derivedOwnerId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Owner recovery enrolled device {DeviceId} and revoked {RevokedCount} other device(s).")]
    private static partial void LogRecovered(ILogger logger, Guid deviceId, int revokedCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The push hub did not delete the installation of device {DeviceId}, revoked by an owner recovery.")]
    private static partial void LogRemoveFailed(ILogger logger, Guid deviceId, Exception exception);
}
