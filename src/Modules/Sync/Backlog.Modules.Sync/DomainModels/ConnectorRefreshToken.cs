namespace Backlog.Modules.Sync.DomainModels;

/// <summary>
/// One refresh token of the remote connector (local ADR 0052 §8), stored by hash. It
/// rotates on use: the token that answers a refresh replaces the one presented, which then
/// stops working. Every token of one authorization shares a <see cref="FamilyId"/>, so a
/// rotated token presented again — a replay — revokes the whole family.
/// </summary>
/// <param name="TokenHash">SHA-256 of the token, hex. The lookup key.</param>
/// <param name="FamilyId">The authorization the token descends from.</param>
/// <param name="ClientGeneration">The client generation it was issued under; a later
/// create or revoke of the secret makes it dead.</param>
/// <param name="Owner">Whose token it is — the access token's <c>sub</c>.</param>
/// <param name="Scope">The scope granted, space-separated.</param>
/// <param name="Resource">The resource the access tokens are for.</param>
/// <param name="IssuedAt">When it was issued.</param>
/// <param name="ExpiresAt">When it stops working however it is used.</param>
/// <param name="ReplacedByHash">The token that rotated it out; null while it is live.</param>
/// <param name="Revoked">Set when its family was revoked.</param>
public sealed record ConnectorRefreshToken(
    string TokenHash,
    Guid FamilyId,
    int ClientGeneration,
    OwnerId Owner,
    string Scope,
    string Resource,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    string? ReplacedByHash = null,
    bool Revoked = false)
{
    /// <summary>Whether it has already been rotated or revoked, so presenting it is a replay.</summary>
    public bool IsSpent => ReplacedByHash is not null || Revoked;

    /// <summary>Whether the window has closed at <paramref name="now"/>.</summary>
    public bool HasExpired(DateTimeOffset now) => now >= ExpiresAt;
}
