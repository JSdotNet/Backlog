using System.Text.Json.Serialization;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Api.Mcp;
using Backlog.Modules.Sync.Api.Options;
using Backlog.Modules.Sync.Api.Security;
using Microsoft.Extensions.Options;

namespace Backlog.Modules.Sync.Api.Endpoints;

/// <summary>
/// The remote MCP endpoint (local ADR 0058 §8): <c>/mcp</c> over the streamable HTTP transport,
/// stateless so any instance answers any call, and its RFC 9728 protected-resource metadata.
/// <para>
/// One tool group is served, <c>inbox:capture</c>, and the whole endpoint stands behind its
/// policy: a token without the scope is refused before any MCP message is read. The tool
/// list is <see cref="RemoteInboxTools"/> and nothing else — the desktop's tool classes are
/// never registered here, so <c>tasks:admin</c> and <c>devbook:decide</c> cannot arrive by a
/// registration that read a shared table.
/// </para>
/// </summary>
internal static class RemoteMcpEndpoints
{
    internal static void AddRemoteMcpServer(this IHostApplicationBuilder builder)
    {
        builder.AddRemoteMcpAuthentication();

        builder.Services
            .AddMcpServer(server => server.ServerInfo = new() { Name = "Backlog", Version = BuildInformation.Version })
            .WithHttpTransport(transport => transport.Stateless = true)
            .WithTools<RemoteInboxTools>();
    }

    internal static IEndpointRouteBuilder MapRemoteMcp(this IEndpointRouteBuilder app)
    {
        var metadata = app.MapGroup(string.Empty)
            .AllowAnonymous()
            .RequireRateLimiting(ConnectorAuthorizationRegistration.RateLimitPolicy)
            .WithTags("Remote MCP");

        metadata.MapGet(SyncRoutes.RemoteMcpResourceMetadata, Metadata)
            .WithSummary("The /mcp resource's RFC 9728 protected-resource metadata.");

        app.MapMcp(SyncRoutes.RemoteMcp)
            .RequireAuthorization(RemoteMcpAuthentication.InboxCapturePolicy)
            .WithTags("Remote MCP");

        return app;
    }

    private static IResult Metadata(IOptions<ConnectorOptions> options)
    {
        var settings = options.Value;

        return Results.Json(new ProtectedResourceMetadataDocument(
            settings.Resource!,
            [settings.Issuer!],
            [.. RemoteMcpAuthentication.ServedScopes],
            ["header"],
            "Backlog"));
    }

    private sealed record ProtectedResourceMetadataDocument(
        [property: JsonPropertyName("resource")] string Resource,
        [property: JsonPropertyName("authorization_servers")] string[] AuthorizationServers,
        [property: JsonPropertyName("scopes_supported")] string[] ScopesSupported,
        [property: JsonPropertyName("bearer_methods_supported")] string[] BearerMethodsSupported,
        [property: JsonPropertyName("resource_name")] string ResourceName);
}
