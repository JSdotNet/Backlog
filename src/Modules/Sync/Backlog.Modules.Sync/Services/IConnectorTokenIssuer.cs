using Backlog.Modules.Sync.DomainModels;

namespace Backlog.Modules.Sync.Services;

/// <summary>What a connector access token says: whose it is, which client holds it, what it
/// may do, which resource it is for, and the client generation it was issued under.</summary>
public readonly record struct ConnectorTokenClaims(
    OwnerId Owner,
    string ClientId,
    string Scope,
    string Resource,
    int ClientGeneration);

/// <summary>A minted connector access token and when it stops being accepted.</summary>
public readonly record struct ConnectorAccessToken(string AccessToken, DateTimeOffset ExpiresAt);

/// <summary>
/// Mints the remote connector's access token (local ADR 0052 §8). A port, for the reason
/// <see cref="IDeviceTokenIssuer"/> is one: the host holds the signing key, and the
/// resource server that later validates the token sits beside it.
/// </summary>
public interface IConnectorTokenIssuer
{
    ConnectorAccessToken Issue(ConnectorTokenClaims claims);
}
