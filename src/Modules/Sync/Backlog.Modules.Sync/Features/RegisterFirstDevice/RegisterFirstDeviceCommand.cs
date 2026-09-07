using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Ports;
using Backlog.Modules.Sync.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Sync.Features.RegisterFirstDevice;

/// <summary>
/// A device with no owner asks for one. This is where an owner comes from —
/// there is no account, no sign-up, and nothing to look up, so the first
/// registration mints an owner id and the device that asked is its first
/// member.
/// </summary>
public sealed record RegisterFirstDeviceCommand(string DeviceName);

/// <summary>
/// Mints owner, device, and credential, and stores only the credential's hash.
/// The plaintext goes back in the response and is never recoverable from here
/// afterwards, which is the point: losing it means registering again, and that
/// is a better failure than a service that can impersonate every device it ever
/// registered.
/// </summary>
public sealed class RegisterFirstDeviceCommandHandler(
    IDeviceRegistry devices,
    ICredentialHasher hasher,
    IRegistrationCredentialGenerator credentials,
    TimeProvider clock)
    : ICommandHandler<RegisterFirstDeviceCommand, Result<DeviceRegistrationResponse>>
{
    public async Task<Result<DeviceRegistrationResponse>> Handle(
        RegisterFirstDeviceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var name = DeviceName.Validate(command.DeviceName);
        if (name.IsFailure)
        {
            return name.Error;
        }

        var ownerId = OwnerId.New();
        var deviceId = DeviceId.New();
        var credential = credentials.Next();

        await devices.Add(
            new Device(deviceId, ownerId, name.Value, hasher.Hash(credential), clock.GetUtcNow()),
            cancellationToken);

        return new DeviceRegistrationResponse(ownerId.Value, deviceId.Value, credential);
    }
}
