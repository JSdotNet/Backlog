using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Ports;
using Backlog.Modules.Sync.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace Backlog.Modules.Sync.Features.IssueDeviceToken;

/// <summary>
/// The exchange that happens on every sync: proof that the caller is the device,
/// in return for a token that is good for half an hour. The proof is a signed
/// <paramref name="ClientAssertion"/> from a device bound to a key (local ADR 0052
/// §2), or the <paramref name="Credential"/> of a device that is not — which may
/// bring a <paramref name="PublicKey"/> to bind in the credential's place.
/// </summary>
public sealed record IssueDeviceTokenCommand(
    Guid DeviceId,
    string? Credential = null,
    string? ClientAssertionType = null,
    string? ClientAssertion = null,
    string? PublicKey = null);

/// <summary>
/// Checks the presented proof against the device on file and mints a token naming
/// the device's owner.
/// <para>
/// A device bound to a key is taken only on an assertion its key signed, once per
/// assertion id; its credential path is closed. A device on a credential is taken
/// on its credential, and a public key offered beside a correct one binds that key
/// and drops the credential hash: the migration happens once, and the same
/// credential is refused from then on.
/// </para>
/// <para>
/// An unknown device and a wrong proof come back as the same error on purpose.
/// They are the same event — a caller failed to authenticate — and answering them
/// differently would let somebody enumerate which device ids exist without holding
/// a credential for any of them. The two are not indistinguishable in every
/// respect: the lookup short-circuits before the hash comparison, so an unknown id
/// is answered slightly sooner. Closing that timing gap would mean hashing against
/// a decoy, which is not worth its own code path for a device id that is a v7 GUID
/// nobody can guess anyway. The one answer that differs is a malformed key on a
/// correct credential, and only a caller holding the credential can see it.
/// </para>
/// </summary>
public sealed partial class IssueDeviceTokenCommandHandler(
    IDeviceRegistry devices,
    ICredentialHasher hasher,
    IDeviceTokenIssuer tokens,
    IDeviceAssertionVerifier assertions,
    IAssertionReplayCache replays,
    ILogger<IssueDeviceTokenCommandHandler> log)
    : ICommandHandler<IssueDeviceTokenCommand, Result<DeviceTokenResponse>>
{
    private static Error CredentialInvalid => new(
        SyncErrorCodes.DeviceCredentialInvalid,
        "That device id and credential do not match a registered device.");

    public async Task<Result<DeviceTokenResponse>> Handle(
        IssueDeviceTokenCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var device = await devices.FindById(new DeviceId(command.DeviceId), cancellationToken);

        if (device is null)
        {
            return CredentialInvalid;
        }

        var proven = command.ClientAssertion is not null || command.ClientAssertionType is not null
            ? await ByAssertion(command, device, cancellationToken)
            : await ByCredential(command, device, cancellationToken);

        if (proven.IsFailure)
        {
            return proven.Error;
        }

        var token = tokens.Issue(new OwnerScope(device.OwnerId, device.Id));

        return new DeviceTokenResponse(token.AccessToken, token.ExpiresAt);
    }

    private async Task<Result> ByAssertion(IssueDeviceTokenCommand command, Device device, CancellationToken cancellationToken)
    {
        if (device.PublicKey is null
            || !string.Equals(command.ClientAssertionType, SyncAssertion.ClientAssertionType, StringComparison.Ordinal)
            || assertions.Verify(command.ClientAssertion ?? string.Empty, device.Id, device.PublicKey) is not { } verified)
        {
            return Result.Failure(CredentialInvalid);
        }

        if (!await replays.TryAdd(device.Id, verified.Id, verified.AcceptableUntil, cancellationToken))
        {
            LogReplayed(log, device.Id.Value);
            return Result.Failure(CredentialInvalid);
        }

        return Result.Success();
    }

    private async Task<Result> ByCredential(IssueDeviceTokenCommand command, Device device, CancellationToken cancellationToken)
    {
        if (device.IsKeyBound || !hasher.Matches(command.Credential ?? string.Empty, device.CredentialHash ?? string.Empty))
        {
            return Result.Failure(CredentialInvalid);
        }

        if (command.PublicKey is null)
        {
            return Result.Success();
        }

        var key = DevicePublicKey.Validate(command.PublicKey);
        if (key.IsFailure)
        {
            return Result.Failure(key.Error);
        }

        // False only when another fetch bound a key first: that one migrated, and
        // this credential is no longer good.
        if (!await devices.BindKey(device.Id, key.Value, cancellationToken))
        {
            return Result.Failure(CredentialInvalid);
        }

        LogKeyBound(log, device.Id.Value);
        return Result.Success();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Device {DeviceId} bound its key and retired its registration credential.")]
    private static partial void LogKeyBound(ILogger logger, Guid deviceId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Device {DeviceId} was offered an assertion it had already spent.")]
    private static partial void LogReplayed(ILogger logger, Guid deviceId);
}
