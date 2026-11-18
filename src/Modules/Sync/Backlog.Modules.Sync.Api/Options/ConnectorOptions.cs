using System.ComponentModel.DataAnnotations;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Features.Connector;

namespace Backlog.Modules.Sync.Api.Options;

/// <summary>
/// The remote connector's authorization server (local ADR 0052 §8): who it says it is, the
/// resource its tokens are for, its one client and redirect, and the token lifetimes
/// (inherited ADR 0018: bind, validate, fail fast). Nothing here is a secret — the client
/// secret is minted by the service and kept as a hash.
/// </summary>
public sealed class ConnectorOptions
{
    /// <summary>The section this binds to, named for the module per inherited ADR 0018.</summary>
    public const string SectionName = "Modules:Sync:Connector";

    /// <summary>What a Development run with no issuer configured says it is.</summary>
    internal const string DevelopmentIssuer = "http://localhost";

    /// <summary>The authorization server's issuer (RFC 8414 §2): the service's public origin,
    /// with no path, so the metadata sits at <c>/.well-known/oauth-authorization-server</c>.
    /// Required anywhere but Development.</summary>
    public string? Issuer { get; set; }

    /// <summary>The <c>/mcp</c> resource URL every access token is for (RFC 8707). Required
    /// anywhere but Development.</summary>
    public string? Resource { get; set; }

    /// <summary>The one statically registered client.</summary>
    [Required]
    public string ClientId { get; set; } = ConnectorSettings.DefaultClientId;

    /// <summary>The one redirect URI, compared exactly. A setting so a move of Anthropic's
    /// callback is a configuration change.</summary>
    [Required]
    public string RedirectUri { get; set; } = ConnectorSettings.DefaultRedirectUri;

    /// <summary>How long an access token lives. Inherited ADR 0012's band is 15 to 60 minutes.</summary>
    [Range(15, 60)]
    public int AccessTokenMinutes { get; set; } = 15;

    /// <summary>How long one refresh token lives; each refresh rotates it.</summary>
    [Range(1, ConnectorTokenLimits.MaxRefreshTokenDays)]
    public int RefreshTokenDays { get; set; } = 30;

    /// <summary>Whether <see cref="Issuer"/> is an absolute http(s) origin with no path, query
    /// or fragment, as RFC 8414 needs to derive the metadata path from it.</summary>
    internal static bool IsOrigin(string? issuer) =>
        Uri.TryCreate(issuer, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
        && uri.AbsolutePath == "/"
        && string.IsNullOrEmpty(uri.Query)
        && string.IsNullOrEmpty(uri.Fragment)
        && !issuer!.EndsWith('/');

    /// <summary>Whether a URL is absolute http(s) with no fragment.</summary>
    internal static bool IsAbsoluteHttp(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
        && string.IsNullOrEmpty(uri.Fragment);
}
