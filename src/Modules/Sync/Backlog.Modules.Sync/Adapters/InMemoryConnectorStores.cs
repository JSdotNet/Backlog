using System.Collections.Concurrent;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Ports;

namespace Backlog.Modules.Sync.Adapters;

/// <summary>
/// The connector's short-lived authorization steps, in memory — the store a deployed service
/// uses too, on the enrolment tickets' terms: a step lives minutes, the service runs one
/// replica, and a restart mid-authorization costs one more attempt from claude.ai. Expired
/// steps are dropped as new ones arrive.
/// </summary>
public sealed class InMemoryConnectorAuthorizationStore(TimeProvider clock) : IConnectorAuthorizationStore
{
    private readonly ConcurrentDictionary<string, ConnectorAuthorization> _records = new(StringComparer.Ordinal);

    public Task Add(ConnectorAuthorization authorization, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authorization);

        var now = clock.GetUtcNow();
        foreach (var (key, stale) in _records)
        {
            if (stale.HasExpired(now)) _records.TryRemove(key, out _);
        }

        _records[authorization.KeyHash] = authorization;
        return Task.CompletedTask;
    }

    public Task<ConnectorAuthorization?> FindByHash(string keyHash, CancellationToken cancellationToken = default) =>
        Task.FromResult(_records.TryGetValue(keyHash, out var record) ? record : null);

    /// <summary>Compare-and-swap, so exactly one caller burns a step.</summary>
    public Task<bool> TryBurn(string keyHash, CancellationToken cancellationToken = default)
    {
        if (!_records.TryGetValue(keyHash, out var existing) || existing.Redeemed)
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(_records.TryUpdate(keyHash, existing with { Redeemed = true }, existing));
    }
}

/// <summary>
/// The connector client, in memory: the Development and test stand-in. A deployed service
/// registers a durable store before <c>AddSyncModule</c>, because a restart here forgets the
/// secret and disconnects the connector.
/// </summary>
public sealed class InMemoryConnectorClientStore : IConnectorClientStore
{
    private readonly ConcurrentDictionary<string, ConnectorClient> _clients = new(StringComparer.Ordinal);

    public Task<ConnectorClient?> Find(string clientId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_clients.TryGetValue(clientId, out var client) ? client : null);

    public Task Save(ConnectorClient client, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);

        _clients[client.ClientId] = client;
        return Task.CompletedTask;
    }

    public Task StampLastUsed(string clientId, int generation, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        if (_clients.TryGetValue(clientId, out var current) && current.Generation == generation)
        {
            _clients.TryUpdate(clientId, current with { LastUsedAt = at }, current);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// The connector's refresh tokens, in memory: the Development and test stand-in, on the
/// client store's terms. Rotation is a compare-and-swap on the presented token.
/// <para>
/// Revocation is a family-level marker rather than a sweep over the tokens: a sweep can pass
/// before a concurrent rotation or code exchange stores its token, which would then outlive
/// its family. <see cref="TryRotate"/> and <see cref="Add"/> check the marker, the rotation
/// again after storing its replacement, and <see cref="FindByHash"/> answers every token of a
/// marked family as revoked — so a token stored in the same instant is dead all the same.
/// </para>
/// </summary>
public sealed class InMemoryConnectorRefreshTokenStore : IConnectorRefreshTokenStore
{
    private readonly ConcurrentDictionary<string, ConnectorRefreshToken> _tokens = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, byte> _revokedFamilies = new();

    public Task Add(ConnectorRefreshToken token, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);

        _tokens[token.TokenHash] = IsRevoked(token.FamilyId) ? token with { Revoked = true } : token;
        return Task.CompletedTask;
    }

    public Task<ConnectorRefreshToken?> FindByHash(string tokenHash, CancellationToken cancellationToken = default) =>
        Task.FromResult(_tokens.TryGetValue(tokenHash, out var token) ? AsStored(token) : null);

    public Task<bool> TryRotate(string presentedHash, ConnectorRefreshToken replacement, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(replacement);

        if (!_tokens.TryGetValue(presentedHash, out var existing)
            || AsStored(existing).IsSpent
            || !_tokens.TryUpdate(presentedHash, existing with { ReplacedByHash = replacement.TokenHash }, existing))
        {
            return Task.FromResult(false);
        }

        _tokens[replacement.TokenHash] = replacement;

        // A revoke that landed between the swap and the store must still win.
        return Task.FromResult(!IsRevoked(existing.FamilyId));
    }

    public Task RevokeFamily(Guid familyId, CancellationToken cancellationToken = default)
    {
        _revokedFamilies.TryAdd(familyId, 0);
        return Task.CompletedTask;
    }

    private bool IsRevoked(Guid familyId) => _revokedFamilies.ContainsKey(familyId);

    private ConnectorRefreshToken AsStored(ConnectorRefreshToken token) =>
        !token.Revoked && IsRevoked(token.FamilyId) ? token with { Revoked = true } : token;
}
