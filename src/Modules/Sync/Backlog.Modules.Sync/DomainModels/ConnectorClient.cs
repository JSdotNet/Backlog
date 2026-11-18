namespace Backlog.Modules.Sync.DomainModels;

/// <summary>
/// The one statically registered OAuth client of the remote connector (local ADR 0052 §8):
/// claude.ai's custom connector. Its secret is minted by the service and kept only as a
/// hash. <see cref="Generation"/> moves on every create and every revoke, and every code and
/// refresh token carries the generation it was issued under, so a revoke kills all of them
/// without a query.
/// </summary>
/// <param name="ClientId">The client id the person enters in claude.ai.</param>
/// <param name="SecretHash">SHA-256 of the client secret, hex; null when there is none.</param>
/// <param name="CreatedAt">When the current secret was made.</param>
/// <param name="LastUsedAt">When the current secret last authenticated a token call.</param>
/// <param name="Generation">Bumped on create and revoke.</param>
public sealed record ConnectorClient(
    string ClientId,
    string? SecretHash,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? LastUsedAt,
    int Generation)
{
    /// <summary>A client nobody has made a secret for yet.</summary>
    public static ConnectorClient Unregistered(string clientId) => new(clientId, null, null, null, 0);

    /// <summary>Whether a secret exists, so a token call can authenticate at all.</summary>
    public bool HasSecret => !string.IsNullOrEmpty(SecretHash);
}
