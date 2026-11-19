using Backlog.Modules.Sync.DomainModels;

namespace Backlog.Modules.Sync.Ports;

/// <summary>
/// Where the connector client — its secret hash, times and generation — is kept. It must
/// survive a restart: the service scales to zero, and a lost hash would disconnect the
/// connector.
/// </summary>
public interface IConnectorClientStore
{
    /// <summary>The client with this id, or null when no secret was ever made for it.</summary>
    Task<ConnectorClient?> Find(string clientId, CancellationToken cancellationToken = default);

    /// <summary>Stores the client, replacing what was kept for its id.</summary>
    Task Save(ConnectorClient client, CancellationToken cancellationToken = default);

    /// <summary>Records that the secret of <paramref name="generation"/> authenticated at
    /// <paramref name="at"/> — only while that generation is still current, so a stamp racing
    /// a revoke can never bring the revoked secret back.</summary>
    Task StampLastUsed(string clientId, int generation, DateTimeOffset at, CancellationToken cancellationToken = default);
}
