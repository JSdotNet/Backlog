using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Ports;
using Backlog.Modules.Sync.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Backlog.Modules.Sync.Api.UnitTests;

/// <summary>
/// Local ADR 0052 §8 over HTTP: the remote <c>/mcp</c> endpoint as an OAuth 2.1 resource
/// server. It answers RFC 9728 metadata naming the authorization server, challenges with the
/// metadata address, accepts only a connector token for its own audience (RFC 8707) from the
/// current client generation, refuses one without <c>inbox:capture</c> as
/// <c>insufficient_scope</c>, and serves the one capture tool, whose capture the owner's
/// desktop then reads off the replica.
/// <para>
/// Tokens are minted by the service's own <see cref="IConnectorTokenIssuer"/> — the one the
/// token endpoint uses, which <see cref="ConnectorAuthorizationEndpointTests"/> covers — so a
/// test can name the audience and scope the authorization server would never put together.
/// </para>
/// </summary>
public sealed class RemoteMcpEndpointTests : IDisposable
{
    private const string Issuer = "https://sync.test";
    private const string Resource = "https://sync.test/mcp";
    private const string MetadataAddress = "https://sync.test/.well-known/oauth-protected-resource/mcp";
    private const string ClientId = "claude-connector";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private readonly SyncServiceFactory _service = new()
    {
        MoreConfiguration = SyncServiceFactory.ConfiguredConnector,
    };

    public void Dispose() => _service.Dispose();

    [Fact]
    public async Task The_protected_resource_metadata_names_the_resource_and_its_authorization_server()
    {
        using var response = await _service.CreateClient().GetAsync(SyncRoutes.RemoteMcpResourceMetadata, Cancellation);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation));
        var metadata = document.RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Resource, metadata.GetProperty("resource").GetString());
        Assert.Equal([Issuer], Strings(metadata, "authorization_servers"));
        Assert.Equal(["inbox:capture"], Strings(metadata, "scopes_supported"));
        Assert.Equal(["header"], Strings(metadata, "bearer_methods_supported"));
    }

    [Fact]
    public async Task A_call_without_a_token_is_challenged_with_the_metadata_address()
    {
        using var response = await PostInitialize(token: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var challenge = Assert.Single(response.Headers.WwwAuthenticate);
        Assert.Equal("Bearer", challenge.Scheme);
        Assert.Contains($"resource_metadata=\"{MetadataAddress}\"", challenge.Parameter, StringComparison.Ordinal);
        Assert.DoesNotContain("error=", challenge.Parameter, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_capture_lands_on_the_replica_where_the_owners_desktop_reads_it()
    {
        var (desktop, owner) = await OwnerDesktop();
        var generation = await ConnectorClient();
        await using var mcp = await Connect(Mint(owner, "inbox:capture", Resource, generation));

        var tools = await mcp.ListToolsAsync(cancellationToken: Cancellation);
        var result = await mcp.CallToolAsync(
            "capture_to_inbox",
            new Dictionary<string, object?>
            {
                ["title"] = "Call the plumber",
                ["notes"] = "About the **boiler**.",
                ["tags"] = new[] { "home" },
            },
            cancellationToken: Cancellation);

        Assert.Equal(["capture_to_inbox"], tools.Select(tool => tool.Name));
        Assert.NotEqual(true, result.IsError);
        var payload = result.StructuredContent!.Value;
        Assert.True(payload.GetProperty("created").GetBoolean());

        var inbox = await desktop.GetFromJsonAsync<List<InboxItem>>(SyncRoutes.Absolute(SyncRoutes.Inbox), Cancellation);
        var item = Assert.Single(inbox!);
        Assert.Equal(payload.GetProperty("id").GetGuid(), item.Id);
        Assert.Equal("Call the plumber", item.Title);
        Assert.Equal("claude", item.Source);
        Assert.Equal("About the **boiler**.", item.BodyMd);
        Assert.Equal(["home"], item.Tags);
    }

    [Fact]
    public async Task A_capture_the_bounds_refuse_is_a_tool_error_and_writes_nothing()
    {
        var (desktop, owner) = await OwnerDesktop();
        var generation = await ConnectorClient();
        await using var mcp = await Connect(Mint(owner, "inbox:capture", Resource, generation));

        var result = await mcp.CallToolAsync(
            "capture_to_inbox",
            new Dictionary<string, object?> { ["title"] = "Mention", ["tags"] = new[] { "@ana" } },
            cancellationToken: Cancellation);

        Assert.True(result.IsError);
        Assert.Contains("capture", Text(result), StringComparison.Ordinal);
        Assert.Empty((await desktop.GetFromJsonAsync<List<InboxItem>>(SyncRoutes.Absolute(SyncRoutes.Inbox), Cancellation))!);
    }

    [Fact]
    public async Task A_token_for_another_audience_is_refused_as_an_invalid_token()
    {
        var (_, owner) = await OwnerDesktop();
        var generation = await ConnectorClient();

        using var response = await PostInitialize(Mint(owner, "inbox:capture", "https://elsewhere.test/mcp", generation));

        AssertInvalidToken(response);
    }

    [Fact]
    public async Task A_token_without_inbox_capture_is_refused_as_insufficient_scope()
    {
        var (_, owner) = await OwnerDesktop();
        var generation = await ConnectorClient();

        using var response = await PostInitialize(Mint(owner, "tasks:read", Resource, generation));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var challenge = Assert.Single(response.Headers.WwwAuthenticate);
        Assert.Contains("error=\"insufficient_scope\"", challenge.Parameter, StringComparison.Ordinal);
        Assert.Contains("scope=\"inbox:capture\"", challenge.Parameter, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_device_token_is_not_a_connector_token()
    {
        var (desktop, _) = await OwnerDesktop();

        using var response = await PostInitialize(desktop.DefaultRequestHeaders.Authorization!.Parameter);

        AssertInvalidToken(response);
    }

    [Fact]
    public async Task A_connector_token_is_not_a_device_token()
    {
        var (_, owner) = await OwnerDesktop();
        var generation = await ConnectorClient();
        var connector = _service.CreateClient().Bearing(Mint(owner, "inbox:capture", Resource, generation));

        using var response = await connector.GetAsync(SyncRoutes.Absolute(SyncRoutes.Inbox), Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_token_for_another_client_is_refused_as_an_invalid_token()
    {
        var (_, owner) = await OwnerDesktop();
        var generation = await ConnectorClient();
        var token = _service.Services.GetRequiredService<IConnectorTokenIssuer>()
            .Issue(new ConnectorTokenClaims(owner, "someone-else", "inbox:capture", Resource, generation))
            .AccessToken;

        using var response = await PostInitialize(token);

        AssertInvalidToken(response);
    }

    [Fact]
    public void A_resource_other_than_the_issuers_mcp_route_stops_the_start()
    {
        using var misconfigured = new SyncServiceFactory
        {
            MoreConfiguration =
            [
                ("Modules:Sync:Connector:Issuer", Issuer),
                ("Modules:Sync:Connector:Resource", "https://sync.test/mcp/"),
            ],
        };

        var failure = Assert.ThrowsAny<Exception>(() => misconfigured.CreateClient());
        Assert.Contains("Modules:Sync:Connector:Resource", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_token_from_before_the_secret_was_revoked_is_refused()
    {
        var (_, owner) = await OwnerDesktop();
        var generation = await ConnectorClient();
        var token = Mint(owner, "inbox:capture", Resource, generation);

        // What DELETE /connector/secret leaves: no secret, the generation moved on.
        await Store().Save(new ConnectorClient(ClientId, null, null, null, generation + 1), Cancellation);
        using var response = await PostInitialize(token);

        AssertInvalidToken(response);
    }

    private static void AssertInvalidToken(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var challenge = Assert.Single(response.Headers.WwwAuthenticate);
        Assert.Contains("error=\"invalid_token\"", challenge.Parameter, StringComparison.Ordinal);
        Assert.Contains($"resource_metadata=\"{MetadataAddress}\"", challenge.Parameter, StringComparison.Ordinal);
    }

    /// <summary>A desktop holding its device token, and the owner that token names.</summary>
    private async Task<(HttpClient Desktop, OwnerId Owner)> OwnerDesktop()
    {
        var desktop = await _service.CreateClient().RegisteredDevice("Study desktop");
        var token = new JsonWebTokenHandler().ReadJsonWebToken(desktop.DefaultRequestHeaders.Authorization!.Parameter);

        return (desktop, new OwnerId(Guid.Parse(token.GetClaim(SyncClaims.OwnerId).Value)));
    }

    /// <summary>The connector client with a secret, as the settings card's Create leaves it.</summary>
    private async Task<int> ConnectorClient()
    {
        const int generation = 1;
        await Store().Save(new ConnectorClient(ClientId, "ab12", DateTimeOffset.UtcNow, null, generation), Cancellation);
        return generation;
    }

    private IConnectorClientStore Store() => _service.Services.GetRequiredService<IConnectorClientStore>();

    private string Mint(OwnerId owner, string scope, string resource, int generation) =>
        _service.Services.GetRequiredService<IConnectorTokenIssuer>()
            .Issue(new ConnectorTokenClaims(owner, ClientId, scope, resource, generation))
            .AccessToken;

    private async Task<McpClient> Connect(string token)
    {
        var http = _service.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(http.BaseAddress!, SyncRoutes.RemoteMcp),
                TransportMode = HttpTransportMode.StreamableHttp,
            },
            http,
            ownsHttpClient: true);

        return await McpClient.CreateAsync(transport, cancellationToken: Cancellation);
    }

    /// <summary>An MCP initialize posted by hand, so the status and challenge can be read.</summary>
    private async Task<HttpResponseMessage> PostInitialize(string? token)
    {
        const string initialize =
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}""";

        using var request = new HttpRequestMessage(HttpMethod.Post, SyncRoutes.RemoteMcp)
        {
            Content = new StringContent(initialize, Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await _service.CreateClient().SendAsync(request, Cancellation);
    }

    private static string Text(CallToolResult result) =>
        string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));

    private static string?[] Strings(JsonElement element, string name) =>
        [.. element.GetProperty(name).EnumerateArray().Select(e => e.GetString())];
}
