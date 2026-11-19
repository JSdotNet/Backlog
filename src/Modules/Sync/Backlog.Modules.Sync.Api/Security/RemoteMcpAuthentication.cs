using System.Diagnostics;
using System.Security.Claims;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Api.Options;
using Backlog.Modules.Sync.Observability;
using Backlog.Modules.Sync.Ports;
using Backlog.SharedKernel.Mcp;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;

namespace Backlog.Modules.Sync.Api.Security;

/// <summary>
/// The remote <c>/mcp</c> endpoint as an OAuth 2.1 resource server (local ADR 0058 §8): a
/// bearer scheme of its own, beside the device scheme rather than in it, that accepts only
/// the connector's access tokens.
/// <list type="bullet">
/// <item><b>Audience-bound (RFC 8707).</b> The token's <c>aud</c> must be the configured
/// <c>/mcp</c> resource and its <c>iss</c> the configured issuer; a token minted for another
/// resource is <c>invalid_token</c>, however valid its signature.</item>
/// <item><b>Never a device token.</b> The connector key is derived from the device signing key
/// under its own label (<see cref="ConnectorTokenIssuer"/>), so a device token's signature
/// fails here, and a connector token's fails on the sync API.</item>
/// <item><b>Revocable.</b> Each token carries the client generation it was issued under; one
/// from a generation the client store no longer holds — the person revoked the secret in
/// Settings, or minted a new one — is refused at once, not after its 15 minutes.</item>
/// <item><b>Challenged per the MCP authorization spec.</b> A 401 names the protected-resource
/// metadata (RFC 9728 §5.1); a token without the scope is a 403 <c>insufficient_scope</c>
/// naming the scope it lacks (RFC 6750 §3.1).</item>
/// <item><b>No passthrough.</b> The token authenticates this hop and nothing else: the tools
/// call the module's handlers in process, and nothing here forwards it anywhere.</item>
/// </list>
/// </summary>
internal static class RemoteMcpAuthentication
{
    /// <summary>The scheme the <c>/mcp</c> endpoint authenticates with. Never the default
    /// scheme, so no sync route can accept a connector token by falling back to it.</summary>
    internal const string Scheme = "RemoteMcp";

    /// <summary>The policy of the <c>inbox:capture</c> tool group: a connector token for this
    /// resource, from the current client generation, granting <c>inbox:capture</c>.</summary>
    internal const string InboxCapturePolicy = "mcp:inbox-capture";

    /// <summary>The scopes the remote endpoint serves. <c>tasks:admin</c> and
    /// <c>devbook:decide</c> never join it (local ADR 0058 §8).</summary>
    internal static IReadOnlyList<string> ServedScopes { get; } = [McpScopes.InboxCapture];

    internal static void AddRemoteMcpAuthentication(this IHostApplicationBuilder builder)
    {
        builder.Services.AddAuthentication().AddJwtBearer(Scheme);

        builder.Services
            .AddOptions<JwtBearerOptions>(Scheme)
            .Configure<IOptions<ConnectorOptions>, SigningKeyRing>(Configure);

        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(InboxCapturePolicy, policy => policy
                .AddAuthenticationSchemes(Scheme)
                .RequireAuthenticatedUser()
                .RequireAssertion(context => Grants(context.User, McpScopes.InboxCapture)));
    }

    /// <summary>Whether the token's space-separated <c>scope</c> claim names
    /// <paramref name="scope"/> (RFC 9068 §2.2.3).</summary>
    internal static bool Grants(ClaimsPrincipal user, string scope) =>
        user.FindFirstValue(ConnectorTokenIssuer.ScopeClaim)?
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Contains(scope, StringComparer.Ordinal) == true;

    /// <summary>The URL of the protected-resource metadata for <paramref name="resource"/>
    /// (RFC 9728 §3.1): the well-known segment between the origin and the resource's path.</summary>
    internal static string MetadataAddress(string resource)
    {
        var uri = new Uri(resource);
        return uri.GetLeftPart(UriPartial.Authority)
            + SyncRoutes.ProtectedResourceMetadata
            + uri.AbsolutePath.TrimEnd('/');
    }

    private static void Configure(JwtBearerOptions jwt, IOptions<ConnectorOptions> connector, SigningKeyRing keys)
    {
        var settings = connector.Value;

        jwt.MapInboundClaims = false;

        jwt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = settings.Issuer,
            ValidateAudience = true,
            ValidAudience = settings.Resource,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeyResolver = (_, _, _, _) => ConnectorTokenIssuer.AcceptedKeys(keys),
            ClockSkew = TimeSpan.FromSeconds(30),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            NameClaimType = JwtRegisteredClaimNames.Sub,
        };

        jwt.Events = new JwtBearerEvents
        {
            OnTokenValidated = RefuseRevokedAsync,
            OnAuthenticationFailed = context =>
            {
                Logger(context.HttpContext).LogWarning(
                    "Remote MCP token rejected on {Method} {Path}: {Reason}.",
                    LogSafe(context.Request.Method),
                    LogSafe(context.Request.Path.Value),
                    context.Exception.GetType().Name);
                return Task.CompletedTask;
            },
            OnChallenge = context =>
            {
                // Answered here rather than by the handler, so the header carries the
                // metadata address RFC 9728 §5.1 asks for and the error RFC 6750 §3.1 names.
                context.HandleResponse();

                var invalid = context.AuthenticateFailure is not null || context.Request.Headers.Authorization.Count > 0;
                var parameters = $"resource_metadata=\"{MetadataAddress(settings.Resource!)}\"";
                if (invalid)
                {
                    parameters += ", error=\"invalid_token\"";
                }

                SyncTelemetry.RemoteMcpRefused.Add(1, new TagList { { "error", invalid ? "invalid_token" : "no_token" } });
                Logger(context.HttpContext).LogWarning(
                    "Remote MCP call to {Method} {Path} challenged: {Error}.",
                    LogSafe(context.Request.Method),
                    LogSafe(context.Request.Path.Value),
                    invalid ? "invalid_token" : "no bearer token");

                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.Headers.Append(HeaderNames.WWWAuthenticate, "Bearer " + parameters);
                context.Response.Headers.CacheControl = "no-store";
                return Task.CompletedTask;
            },
            OnForbidden = context =>
            {
                var scope = string.Join(' ', ServedScopes);

                SyncTelemetry.RemoteMcpRefused.Add(1, new TagList { { "error", "insufficient_scope" } });
                Logger(context.HttpContext).LogWarning(
                    "Remote MCP call to {Method} {Path} refused: the token does not grant {Scope}.",
                    LogSafe(context.Request.Method),
                    LogSafe(context.Request.Path.Value),
                    scope);

                context.Response.Headers.Append(
                    HeaderNames.WWWAuthenticate,
                    $"Bearer error=\"insufficient_scope\", scope=\"{scope}\", resource_metadata=\"{MetadataAddress(settings.Resource!)}\"");
                context.Response.Headers.CacheControl = "no-store";
                return Task.CompletedTask;
            },
        };
    }

    /// <summary>
    /// Fails a token whose client no longer stands as it was when the token was minted: no
    /// client, no secret, another client id, or another generation. One store read per call,
    /// the same one the token endpoint makes; a store fault propagates rather than reading
    /// as a revoke, so a throttled replica cannot sign the connector out.
    /// </summary>
    private static async Task RefuseRevokedAsync(TokenValidatedContext context)
    {
        var principal = context.Principal!;
        var settings = context.HttpContext.RequestServices.GetRequiredService<IOptions<ConnectorOptions>>().Value;
        var clientId = principal.FindFirstValue(ConnectorTokenIssuer.ClientIdClaim);

        if (!Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out _)
            || !string.Equals(clientId, settings.ClientId, StringComparison.Ordinal)
            || !int.TryParse(principal.FindFirstValue(ConnectorTokenIssuer.GenerationClaim), out var generation))
        {
            context.Fail("The token does not name an owner, the connector client and its generation.");
            return;
        }

        var store = context.HttpContext.RequestServices.GetRequiredService<IConnectorClientStore>();
        var client = await store.Find(settings.ClientId, context.HttpContext.RequestAborted);

        if (client is not { HasSecret: true } || client.Generation != generation)
        {
            context.Fail("The connector's access was revoked after this token was issued.");
        }
    }

    // The method and path are the caller's own text: drop line breaks so a request cannot forge a log line.
    private static string LogSafe(string? value) =>
        (value ?? string.Empty).Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", string.Empty, StringComparison.Ordinal);

    private static ILogger Logger(HttpContext context) =>
        context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(RemoteMcpAuthentication).FullName!);
}
