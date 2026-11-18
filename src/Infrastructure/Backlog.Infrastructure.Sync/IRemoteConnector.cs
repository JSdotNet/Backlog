using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.SharedKernel.Results;

namespace Backlog.Infrastructure.Sync;

/// <summary>
/// The claude.ai remote connector as the owner's desktop manages it on the sync service
/// (local ADR 0052 §8): where its client secret stands, minting a new one, and revoking it.
/// <see cref="RemoteConnectorClient"/> answers it; the MCP server page's Remote connector
/// card reads it.
/// <para>
/// The desktop keeps nothing of the connector. The secret <see cref="CreateSecretAsync"/>
/// hands back is the only copy outside the service, which keeps a hash: the caller shows it
/// once and drops it.
/// </para>
/// </summary>
public interface IRemoteConnector
{
    /// <summary>Whether this device holds a credential, so the owner calls have a bearer to
    /// carry. A device that never paired has nothing to manage the connector with.</summary>
    bool IsPaired { get; }

    /// <summary>Whether a secret exists, when it was made and last used, and the client id
    /// and redirect URI to enter in claude.ai. Never the secret.</summary>
    Task<Result<ConnectorStatusResponse>> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>Mints a new secret, replacing any previous one, and answers its plaintext
    /// once.</summary>
    Task<Result<ConnectorSecretResponse>> CreateSecretAsync(CancellationToken cancellationToken = default);

    /// <summary>Revokes the secret, and with it every refresh token issued under it.</summary>
    Task<Result> RevokeSecretAsync(CancellationToken cancellationToken = default);
}
