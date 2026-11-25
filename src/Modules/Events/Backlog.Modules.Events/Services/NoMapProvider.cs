using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.DomainModels;

namespace Backlog.Modules.Events.Services;

/// <summary>The geocoder of a host with no map provider: it sends nothing and gets no answer
/// back, so a saved address stays Not looked up yet, as on a desktop with no map key (local
/// ADR 0061 §4).</summary>
internal sealed class NoAddressGeocoder : IAddressGeocoder
{
    public Task<GeocodeAnswer> GeocodeAsync(Address address, CancellationToken cancellationToken = default) =>
        Task.FromResult(GeocodeAnswer.Failed);
}

/// <summary>The tile source of a host with no map provider: none, so the map shows its
/// missing-key note and requests no tile (local ADR 0061 §4).</summary>
internal sealed class NoEventMapTiles : IEventMapTiles
{
    public Task<EventMapTilesDto?> GetAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<EventMapTilesDto?>(null);
}
