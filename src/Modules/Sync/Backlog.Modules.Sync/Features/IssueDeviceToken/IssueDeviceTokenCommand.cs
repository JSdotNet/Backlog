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
/// §2), or the <paramref name="Credential"/> of a device that is not. A credential
/// device migrates by sending all of them: its credential, the
/// <paramref name="PublicKey"/> to bind, and an assertion that key signed.
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
/// on its credential alone. A public key offered beside a correct credential binds
/// only with an assertion that key signed — the same checks and the same replay
/// rule as a bound device's — so the caller proves it holds the key and not just
/// the credential; the binding drops the credential hash, happens once, and the
/// same credential is refused from then on.
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

        var proven = device.PublicKey is { } bound
            ? await ByAssertion(command, device.Id, bound, cancellationToken)
            : command.PublicKey is not null
                ? await ByMigration(command, device, cancellationToken)
                : ByCredential(command, device);

        if (proven.IsFailure)
        {
            return proven.Error;
        }

        var token = tokens.Issue(new OwnerScope(device.OwnerId, device.Id));

        return new DeviceTokenResponse(token.AccessToken, token.ExpiresAt);
    }

    /// <summary>An assertion <paramref name="publicKey"/> signed for this device,
    /// spent here so it cannot be taken again.</summary>
    private async Task<Result> ByAssertion(
        IssueDeviceTokenCommand command,
        DeviceId deviceId,
        string publicKey,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(command.ClientAssertionType, SyncAssertion.ClientAssertionType, StringComparison.Ordinal)
            || assertions.Verify(command.ClientAssertion ?? string.Empty, deviceId, publicKey) is not { } verified)
        {
            return Result.Failure(CredentialInvalid);
        }

        if (!await replays.TryAdd(deviceId, verified.Id, verified.AcceptableUntil, cancellationToken))
        {
            LogReplayed(log, deviceId.Value);
            return Result.Failure(CredentialInvalid);
        }

        return Result.Success();
    }

    /// <summary>The credential, and nothing else: an assertion beside it is a
    /// request this path does not understand, and is refused rather than ignored.</summary>
    private Result ByCredential(IssueDeviceTokenCommand command, Device device) =>
        command.ClientAssertion is null && command.ClientAssertionType is null && CredentialMatches(command, device)
            ? Result.Success()
            : Result.Failure(CredentialInvalid);

    /// <summary>The credential, then the offered key, then an assertion that key
    /// signed; only then the binding.</summary>
    private async Task<Result> ByMigration(IssueDeviceTokenCommand command, Device device, CancellationToken cancellationToken)
    {
        if (!CredentialMatches(command, device))
        {
            return Result.Failure(CredentialInvalid);
        }

        var key = DevicePublicKey.Validate(command.PublicKey!);
        if (key.IsFailure)
        {
            return Result.Failure(key.Error);
        }

        var possession = await ByAssertion(command, device.Id, key.Value, cancellationToken);
        if (possession.IsFailure)
        {
            return possession;
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

    private bool CredentialMatches(IssueDeviceTokenCommand command, Device device) =>
        device.CredentialHash is { } hash && hasher.Matches(command.Credential ?? string.Empty, hash);

    [LoggerMessage(Level = LogLevel.Information, Message = "Device {DeviceId} bound its key and retired its registration credential.")]
    private static partial void LogKeyBound(ILogger logger, Guid deviceId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A spent assertion was replayed for device {DeviceId}.")]
    private static partial void LogReplayed(ILogger logger, Guid deviceId);
}
