using Backlog.SharedKernel.Mcp;

namespace Backlog.Modules.Sync.Features.Connector;

/// <summary>
/// The remote connector's one client as the host configured it (local ADR 0052 §8). The
/// host builds it from <c>Modules:Sync:Connector</c>; the module only reads it.
/// </summary>
/// <param name="ClientId">The one statically registered client id.</param>
/// <param name="RedirectUri">The one redirect URI, compared by exact string equality.</param>
/// <param name="Resource">The <c>/mcp</c> resource URL — every access token's audience.
/// Null when the host configured none, and then nothing is authorized.</param>
/// <param name="RefreshTokenLifetime">How long one refresh token lives.</param>
public sealed record ConnectorSettings(
    string ClientId,
    string RedirectUri,
    string? Resource,
    TimeSpan RefreshTokenLifetime)
{
    /// <summary>The client id claude.ai's custom connector is registered under.</summary>
    public const string DefaultClientId = "claude-connector";

    /// <summary>The callback Anthropic documents for every hosted Claude app.</summary>
    public const string DefaultRedirectUri = "https://claude.ai/api/mcp/auth_callback";

    /// <summary>The name the consent page shows for the client.</summary>
    public const string ClientName = "Claude";

    /// <summary>The stand-in when the host configured no connector: no resource, so every
    /// authorize is refused.</summary>
    public static ConnectorSettings Unconfigured { get; } =
        new(DefaultClientId, DefaultRedirectUri, Resource: null, TimeSpan.FromDays(30));
}

/// <summary>
/// The scopes the connector may be granted — <c>inbox:capture</c> and nothing else (local ADR
/// 0052 §8) — each with the sentence the consent page shows for it.
/// </summary>
public static class ConnectorScopes
{
    /// <summary>Every scope a connector may ask for.</summary>
    public static IReadOnlyList<string> Supported { get; } = [McpScopes.InboxCapture];

    /// <summary>The scope granted when the request names none.</summary>
    public const string Default = McpScopes.InboxCapture;

    /// <summary>What a scope lets the client do, in plain words.</summary>
    public static string Describe(string scope) => scope switch
    {
        McpScopes.InboxCapture => "Add items to your Backlog Inbox.",
        _ => scope,
    };

    /// <summary>The requested scope as a list, or null when it names a scope outside
    /// <see cref="Supported"/>. An empty request is the default scope.</summary>
    internal static IReadOnlyList<string>? Parse(string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested))
        {
            return [Default];
        }

        var scopes = requested.Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).ToList();
        return scopes.All(s => Supported.Contains(s, StringComparer.Ordinal)) ? scopes : null;
    }
}

/// <summary>The RFC 6749 error codes the authorization server answers with (§4.1.2.1 and
/// §5.2), plus RFC 8707's <c>invalid_target</c>.</summary>
public static class OAuthErrors
{
    public const string InvalidRequest = "invalid_request";
    public const string InvalidClient = "invalid_client";
    public const string InvalidGrant = "invalid_grant";
    public const string UnauthorizedClient = "unauthorized_client";
    public const string UnsupportedGrantType = "unsupported_grant_type";
    public const string UnsupportedResponseType = "unsupported_response_type";
    public const string InvalidScope = "invalid_scope";
    public const string InvalidTarget = "invalid_target";
    public const string AccessDenied = "access_denied";
    public const string TemporarilyUnavailable = "temporarily_unavailable";
}
