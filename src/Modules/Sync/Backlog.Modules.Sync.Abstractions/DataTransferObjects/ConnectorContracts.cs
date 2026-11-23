using System.Text.Json.Serialization;

namespace Backlog.Modules.Sync.Abstractions.DataTransferObjects;

/// <summary>
/// The remote connector as the desktop's settings card shows it (local ADR 0058 §8):
/// whether a client secret exists, when it was made and last used, and the client id and
/// redirect URI the person enters in claude.ai. Never the secret itself.
/// </summary>
public sealed record ConnectorStatusResponse(
    bool HasSecret,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? LastUsedAt,
    string ClientId,
    Uri RedirectUri);

/// <summary>
/// A freshly minted client secret, answered once by the POST that made it. The service
/// keeps only its hash, so no later call can show it again.
/// </summary>
public sealed record ConnectorSecretResponse(string ClientId, string ClientSecret, DateTimeOffset CreatedAt);

/// <summary>
/// The token endpoint's success body (RFC 6749 §5.1), in the protocol's own snake-case
/// names: the access token for the <c>/mcp</c> resource, its lifetime in seconds, the
/// rotated refresh token and the granted scope.
/// </summary>
public sealed record ConnectorTokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("token_type")] string TokenType,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("scope")] string Scope);
