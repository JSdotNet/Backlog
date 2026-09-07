using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Ports;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Sync.Features.DescribeOwner;

/// <summary>
/// What the caller's own token says it is, plus how many devices share that
/// owner. A settings screen shows this beside "paired devices"; there is no way
/// to ask it about anybody else, because the scope is the answer.
/// </summary>
public sealed record DescribeOwnerQuery(OwnerScope Scope);

/// <summary>
/// Reads the calling device back out of the registry so the response can carry
/// its name, and counts the owner's devices.
/// </summary>
public sealed class DescribeOwnerQueryHandler(IDeviceRegistry devices)
    : IQueryHandler<DescribeOwnerQuery, Result<DeviceStatusResponse>>
{
    public async Task<Result<DeviceStatusResponse>> Handle(
        DescribeOwnerQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var device = await devices.FindById(query.Scope.DeviceId, cancellationToken);

        // A validly signed token naming a device the registry has never heard
        // of is what a restart of the in-memory store looks like from outside.
        // Reported as a failed authentication rather than a missing resource,
        // because what the client has to do about it is register again.
        if (device is null || device.OwnerId != query.Scope.OwnerId)
        {
            return new Error(
                SyncErrorCodes.DeviceCredentialInvalid,
                "That device is no longer registered. Register or pair this device again.");
        }

        var count = await devices.CountByOwner(query.Scope.OwnerId, cancellationToken);

        return new DeviceStatusResponse(
            query.Scope.OwnerId.Value,
            query.Scope.DeviceId.Value,
            device.Name,
            count);
    }
}
