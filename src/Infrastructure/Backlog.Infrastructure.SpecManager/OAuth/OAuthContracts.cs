using System.Text.Json.Serialization;

namespace Backlog.Infrastructure.SpecManager.OAuth;

/// <summary>The fields of the authorization server metadata (RFC 8414) the sign-in
/// reads. Everything else the document carries is ignored.</summary>
internal sealed record AuthorizationServerMetadata(
    [property: JsonPropertyName("authorization_endpoint")] Uri? AuthorizationEndpoint,
    [property: JsonPropertyName("token_endpoint")] Uri? TokenEndpoint,
    [property: JsonPropertyName("registration_endpoint")] Uri? RegistrationEndpoint);

/// <summary>A dynamic client registration request (RFC 7591).</summary>
internal sealed record ClientRegistrationRequest(
    [property: JsonPropertyName("client_name")] string ClientName,
    [property: JsonPropertyName("redirect_uris")] IReadOnlyList<string> RedirectUris);

/// <summary>The one field of the registration answer the sign-in keeps.</summary>
internal sealed record ClientRegistrationResponse(
    [property: JsonPropertyName("client_id")] string? ClientId);

/// <summary>A token endpoint answer: a pair on success (RFC 6749 §5.1), an error
/// code on failure (§5.2). One shape so one read covers both.</summary>
internal sealed record TokenResponse(
    [property: JsonPropertyName("access_token")] string? AccessToken,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken,
    [property: JsonPropertyName("expires_in")] int? ExpiresIn,
    [property: JsonPropertyName("error")] string? Error,
    [property: JsonPropertyName("error_description")] string? ErrorDescription);
