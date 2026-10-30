using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Ports;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Sync.Features.Devices;

/// <summary>
/// Every device of the caller's owner. The scope is the whole question: there is
/// no way to ask about anybody else's devices.
/// </summary>
public sealed record ListDevicesQuery(OwnerScope Scope);

/// <summary>
/// Lists the owner's devices, oldest first, each with the device that paired it.
/// A desktop that issued a pairing code reads this while the code is open, to see
/// the device that redeemed it and ask its person whether to keep it
/// (<c>.devbook/domain/sync/context.md</c>).
/// </summary>
public sealed class ListDevicesQueryHandler(IDeviceRegistry devices)
    : IQueryHandler<ListDevicesQuery, Result<DeviceListResponse>>
{
    public async Task<Result<DeviceListResponse>> Handle(ListDevicesQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var owned = await devices.ListByOwner(query.Scope.OwnerId, cancellationToken);

        return new DeviceListResponse(
            query.Scope.DeviceId.Value,
            [
                .. owned
                    .OrderBy(device => device.RegisteredAt)
                    .Select(device => new DeviceSummary(device.Id.Value, device.Name, device.RegisteredAt, device.PairedBy?.Value))
            ]);
    }
}
