using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Ports;
using Backlog.Modules.Sync.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Sync.Features.IssueDeviceToken;

/// <summary>
/// The exchange that happens on every sync: the long-lived credential the
/// device keeps in its OS credential store, in return for a token that is good
/// for half an hour.
/// </summary>
public sealed record IssueDeviceTokenCommand(Guid DeviceId, string Credential);

/// <summary>
/// Checks the presented credential against the stored hash and mints a token
/// naming the device's owner.
/// <para>
/// An unknown device and a wrong credential come back as the same error on
/// purpose. They are the same event — a caller failed to authenticate — and
/// answering them differently would let somebody enumerate which device ids
/// exist without holding a credential for any of them. The two are not
/// indistinguishable in every respect: the lookup short-circuits before the
/// hash comparison, so an unknown id is answered slightly sooner. Closing that
/// timing gap would mean hashing against a decoy, which is not worth its own
/// code path for a device id that is a v7 GUID nobody can guess anyway.
/// </para>
/// </summary>
public sealed class IssueDeviceTokenCommandHandler(
    IDeviceRegistry devices,
    ICredentialHasher hasher,
    IDeviceTokenIssuer tokens)
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

        if (device is null || !hasher.Matches(command.Credential ?? string.Empty, device.CredentialHash))
        {
            return CredentialInvalid;
        }

        var token = tokens.Issue(new OwnerScope(device.OwnerId, device.Id));

        return new DeviceTokenResponse(token.AccessToken, token.ExpiresAt);
    }
}
