using Backlog.Modules.Sync.DomainModels;

namespace Backlog.Modules.Sync.Ports;

/// <summary>
/// Where the connector's refresh tokens are kept, by hash. They live thirty days, so they
/// must survive a restart. Rotation is a compare-and-swap: exactly one caller rotates a
/// token out.
/// </summary>
public interface IConnectorRefreshTokenStore
{
    /// <summary>Stores a fresh token — the first of a family.</summary>
    Task Add(ConnectorRefreshToken token, CancellationToken cancellationToken = default);

    /// <summary>The token with this hash, or null.</summary>
    Task<ConnectorRefreshToken?> FindByHash(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the live token <paramref name="presentedHash"/> as replaced by
    /// <paramref name="replacement"/> and stores the replacement, and reports whether this
    /// caller was the one that did. False when the token is unknown, already rotated or
    /// revoked, or another caller won the race; nothing is stored then.
    /// </summary>
    Task<bool> TryRotate(string presentedHash, ConnectorRefreshToken replacement, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes every token of a family, so none of them refreshes again. Once it returns, the
    /// family stays revoked against a concurrent writer too: a token of the family that
    /// <see cref="Add"/> or <see cref="TryRotate"/> stores at the same time is found revoked,
    /// and a rotation that races the revoke answers false or leaves its replacement revoked.
    /// </summary>
    Task RevokeFamily(Guid familyId, CancellationToken cancellationToken = default);
}
