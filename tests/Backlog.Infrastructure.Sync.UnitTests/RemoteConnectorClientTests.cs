using System.Net;

using Backlog.Infrastructure.Sync.Extensions;
using Backlog.Modules.Sync.Abstractions;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// The three calls the desktop's Remote connector card makes about the claude.ai
/// connector's client secret (local ADR 0058 §8): read where it stands, mint a new one,
/// revoke it. Only the wire is faked, so what is asserted is the request that would
/// have gone out and what a screen gets back.
/// </summary>
public sealed class RemoteConnectorClientTests
{
    private static readonly Guid Owner = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Device = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task The_status_is_read_from_the_connector_route()
    {
        using var fixture = Fixture.Create((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, """
            {"hasSecret":true,"createdAt":"2026-11-01T09:00:00+00:00","lastUsedAt":"2026-11-17T18:30:00+00:00","clientId":"claude-connector","redirectUri":"https://claude.ai/api/mcp/auth_callback"}
            """));

        var result = await fixture.Client.GetStatusAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var request = Assert.Single(fixture.Handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/api/sync/connector", request.Path);
        Assert.True(result.Unwrap().HasSecret);
        Assert.Equal("claude-connector", result.Unwrap().ClientId);
        Assert.Equal(new Uri("https://claude.ai/api/mcp/auth_callback"), result.Unwrap().RedirectUri);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero), result.Unwrap().CreatedAt);
        Assert.Equal(new DateTimeOffset(2026, 11, 17, 18, 30, 0, TimeSpan.Zero), result.Unwrap().LastUsedAt);
    }

    [Fact]
    public async Task Creating_a_secret_posts_and_hands_back_the_plaintext()
    {
        using var fixture = Fixture.Create((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, """
            {"clientId":"claude-connector","clientSecret":"s3cr3t-shown-once","createdAt":"2026-11-18T10:00:00+00:00"}
            """));

        var result = await fixture.Client.CreateSecretAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var request = Assert.Single(fixture.Handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/sync/connector/secret", request.Path);
        Assert.Equal("s3cr3t-shown-once", result.Unwrap().ClientSecret);
        Assert.Equal("claude-connector", result.Unwrap().ClientId);
    }

    [Fact]
    public async Task Revoking_deletes_the_secret_and_reads_no_body()
    {
        using var fixture = Fixture.Create((_, _) => new HttpResponseMessage(HttpStatusCode.NoContent));

        var result = await fixture.Client.RevokeSecretAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var request = Assert.Single(fixture.Handler.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal("/api/sync/connector/secret", request.Path);
    }

    /// <summary>A device of another owner is an ordinary answer the card has to say,
    /// so it comes back as the service's code, not as an exception.</summary>
    [Fact]
    public async Task A_device_that_is_not_the_owner_comes_back_as_its_problem_code()
    {
        using var fixture = Fixture.Create((_, _) => StubHttpMessageHandler.Problem(
            HttpStatusCode.Forbidden, SyncErrorCodes.ConnectorNotOwner, "Only the owner's devices manage the connector."));

        var status = await fixture.Client.GetStatusAsync(TestContext.Current.CancellationToken);
        var created = await fixture.Client.CreateSecretAsync(TestContext.Current.CancellationToken);
        var revoked = await fixture.Client.RevokeSecretAsync(TestContext.Current.CancellationToken);

        Assert.Equal(SyncErrorCodes.ConnectorNotOwner, status.Error.Code);
        Assert.Equal(SyncErrorCodes.ConnectorNotOwner, created.Error.Code);
        Assert.Equal(SyncErrorCodes.ConnectorNotOwner, revoked.Error.Code);
        Assert.Equal("Only the owner's devices manage the connector.", revoked.Error.Message);
    }

    [Fact]
    public async Task A_service_out_of_reach_is_filed_as_unreachable()
    {
        using var fixture = Fixture.Create((_, _) => throw new HttpRequestException("No such host is known."));

        var result = await fixture.Client.CreateSecretAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(DevicePairingClient.UnreachableCode, result.Error.Code);
    }

    /// <summary>The card offers nothing on a device that never paired: there is no
    /// bearer to send, so every call would be refused.</summary>
    [Fact]
    public async Task It_is_paired_only_while_this_device_holds_a_credential()
    {
        var store = new InMemoryDeviceCredentialStore();
        using var fixture = Fixture.Create((_, _) => new HttpResponseMessage(HttpStatusCode.NoContent), store);

        Assert.False(fixture.Client.IsPaired);

        await store.SaveAsync(new DeviceCredential(Owner, Device, "Workshop PC", "a-credential"), TestContext.Current.CancellationToken);

        Assert.True(fixture.Client.IsPaired);
    }

    [Fact]
    public void The_sync_client_composes_the_connector_port_over_the_token_pipeline()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDeviceCredentialStore>(new InMemoryDeviceCredentialStore());
        services.AddSyncClient(new Uri("https://sync.example"));

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        Assert.IsType<RemoteConnectorClient>(provider.GetRequiredService<IRemoteConnector>());
    }

    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient _http;

        private Fixture(HttpClient http, StubHttpMessageHandler handler, RemoteConnectorClient client)
        {
            _http = http;
            Handler = handler;
            Client = client;
        }

        public StubHttpMessageHandler Handler { get; }

        public RemoteConnectorClient Client { get; }

        public static Fixture Create(
            Func<HttpRequestMessage, int, HttpResponseMessage> respond,
            IDeviceCredentialStore? credentials = null)
        {
            var handler = new StubHttpMessageHandler(respond);
            var http = new HttpClient(handler) { BaseAddress = new Uri("https://sync.example") };
            return new Fixture(http, handler, new RemoteConnectorClient(http, credentials ?? new InMemoryDeviceCredentialStore()));
        }

        public void Dispose() => _http.Dispose();
    }
}
