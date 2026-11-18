using Backlog.Modules.Sync.DomainModels;

namespace Backlog.Modules.Sync.Ports;

/// <summary>
/// Where the short-lived steps of a connector authorization are kept, by hash — the
/// enrolment ticket store's shape. A record lives minutes, and an implementation may drop
/// one once it has expired.
/// </summary>
public interface IConnectorAuthorizationStore
{
    /// <summary>Stores a fresh record.</summary>
    Task Add(ConnectorAuthorization authorization, CancellationToken cancellationToken = default);

    /// <summary>The record with this hash, or null.</summary>
    Task<ConnectorAuthorization?> FindByHash(string keyHash, CancellationToken cancellationToken = default);

    /// <summary>Consumes a record and reports whether this caller was the one that did, so
    /// two callers racing on one code cannot both redeem it.</summary>
    Task<bool> TryBurn(string keyHash, CancellationToken cancellationToken = default);
}
