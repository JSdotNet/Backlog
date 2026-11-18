using System.Threading.RateLimiting;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Api.Options;
using Backlog.Modules.Sync.Features.Connector;
using Backlog.Modules.Sync.Services;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Backlog.Modules.Sync.Api.Security;

/// <summary>
/// The remote connector's authorization server (local ADR 0052 §8): its options, the module's
/// view of them, the token issuer and the rate limit of its anonymous routes. Called before
/// <c>AddSyncModule</c>, so the configured settings win over the module's stand-in.
/// </summary>
internal static class ConnectorAuthorizationRegistration
{
    private const string IssuerPath = $"{ConnectorOptions.SectionName}:Issuer";
    private const string ResourcePath = $"{ConnectorOptions.SectionName}:Resource";

    /// <summary>The rate limit the anonymous authorization server routes share. A sibling of
    /// the enrolment limit rather than the same one, so the connector's refreshes cannot
    /// starve an owner's sign-in.</summary>
    internal const string RateLimitPolicy = "connector";

    internal static void AddConnectorAuthorization(this IHostApplicationBuilder builder)
    {
        var options = builder.Services.AddOptions<ConnectorOptions>()
            .BindConfiguration(ConnectorOptions.SectionName)
            .ValidateDataAnnotations();

        if (builder.Environment.IsDevelopment())
        {
            // A Development run with nothing configured answers as localhost, so the
            // metadata and the tokens are still well formed.
            options.PostConfigure(o =>
            {
                o.Issuer ??= ConnectorOptions.DevelopmentIssuer;
                o.Resource ??= $"{o.Issuer}/mcp";
            });
        }

        options
            .Validate(o => ConnectorOptions.IsOrigin(o.Issuer), $"{IssuerPath} must be the service's public origin, with no path; only a Development run defaults it.")
            .Validate(o => ConnectorOptions.IsAbsoluteHttp(o.Resource), $"{ResourcePath} must be the absolute /mcp resource URL; only a Development run defaults it.")
            // The audience, the challenge's metadata address and the mapped route must name
            // one URL, so a resource anywhere but <issuer>/mcp is refused at start.
            .Validate(o => string.Equals(o.Resource, o.Issuer + SyncRoutes.RemoteMcp, StringComparison.Ordinal), $"{ResourcePath} must be {IssuerPath} followed by {SyncRoutes.RemoteMcp}, the route the resource server is served at.")
            .Validate(o => Uri.TryCreate(o.RedirectUri, UriKind.Absolute, out var redirect) && redirect.Scheme == Uri.UriSchemeHttps, $"{ConnectorOptions.SectionName}:RedirectUri must be an absolute https URL.")
            .ValidateOnStart();

        builder.Services.AddSingleton(sp =>
        {
            var configured = sp.GetRequiredService<IOptions<ConnectorOptions>>().Value;
            return new ConnectorSettings(
                configured.ClientId,
                configured.RedirectUri,
                configured.Resource,
                TimeSpan.FromDays(configured.RefreshTokenDays));
        });

        builder.Services.AddSingleton<IConnectorTokenIssuer, ConnectorTokenIssuer>();

        builder.Services.AddRateLimiter(limiter => limiter.AddFixedWindowLimiter(RateLimitPolicy, window =>
        {
            window.PermitLimit = 120;
            window.Window = TimeSpan.FromMinutes(1);
            window.QueueLimit = 0;
            window.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        }));
    }
}
