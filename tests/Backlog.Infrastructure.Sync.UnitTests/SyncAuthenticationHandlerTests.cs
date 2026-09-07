using System.Globalization;
using System.Net;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// The one line the handler adds, and the retry it deliberately does not do.
/// </summary>
public sealed class SyncAuthenticationHandlerTests
{
    private static readonly DeviceCredential Paired =
        new(Guid.NewGuid(), Guid.NewGuid(), "Workshop PC", "a-registration-credential");

    [Fact]
    public async Task A_paired_device_leaves_carrying_a_bearer_token()
    {
        using var host = Host.Create(new InMemoryDeviceCredentialStore(Paired));

        using var response = await host.Client.GetAsync("/api/sync/inbox", TestContext.Current.CancellationToken);

        var sent = Assert.Single(host.Data.Requests);
        Assert.Equal("Bearer", sent.AuthorizationScheme);
        Assert.Equal("token-0", sent.AuthorizationParameter);
    }

    /// <summary>
    /// An unpaired device sends the request anyway, without a header. Blocking
    /// it here would be a second opinion about what this device may do; the
    /// service's own 401 is the one a screen reports.
    /// </summary>
    [Fact]
    public async Task An_unpaired_device_sends_the_request_with_no_header_at_all()
    {
        using var host = Host.Create(new InMemoryDeviceCredentialStore());

        using var response = await host.Client.GetAsync("/api/sync/inbox", TestContext.Current.CancellationToken);

        var sent = Assert.Single(host.Data.Requests);
        Assert.Null(sent.AuthorizationScheme);
        Assert.Empty(host.Token.Requests);
    }

    /// <summary>
    /// Inherited ADR 0015: never retry an authentication failure. One request
    /// out, one 401 back, and no second attempt with a token minted from the
    /// same credential that was just refused.
    /// </summary>
    [Fact]
    public async Task A_401_is_surfaced_rather_than_retried()
    {
        using var host = Host.Create(
            new InMemoryDeviceCredentialStore(Paired),
            data: (_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        using var response = await host.Client.GetAsync("/api/sync/inbox", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Single(host.Data.Requests);
        Assert.Single(host.Token.Requests);
    }

    /// <summary>
    /// The case that made this necessary: a token that has not expired but is no
    /// longer valid, because the service was restarted and signs with a new key.
    /// The provider renews before expiry, so nothing here would ever have gone
    /// back to the credential on its own - it would hand the same dead token to
    /// every call for the rest of the half hour.
    /// <para>
    /// Dropping it is not a retry. This request still ends in the 401 it earned;
    /// what changes is that the <em>next</em> call starts from the credential
    /// again, which is what turns a silent loop into an answer.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_401_drops_the_token_that_earned_it()
    {
        using var host = Host.Create(
            new InMemoryDeviceCredentialStore(Paired),
            data: (_, index) => index == 0
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : StubHttpMessageHandler.Json(HttpStatusCode.OK, "[]"));

        using (await host.Client.GetAsync("/api/sync/inbox", TestContext.Current.CancellationToken))
        {
        }

        using (await host.Client.GetAsync("/api/sync/inbox", TestContext.Current.CancellationToken))
        {
        }

        // The second call did not resend the token the first one was refused
        // for - it went back to the credential and got a new one.
        Assert.Equal(2, host.Token.Requests.Count);
        Assert.Equal("token-0", host.Data.Requests[0].AuthorizationParameter);
        Assert.Equal("token-1", host.Data.Requests[1].AuthorizationParameter);
    }

    /// <summary>
    /// A 401 for a request that carried no token says nothing about a token.
    /// An unpaired device earns one on every bearer endpoint, and treating that
    /// as a rejection would have it dropping a cache it does not have.
    /// </summary>
    [Fact]
    public async Task A_401_for_a_request_that_carried_no_token_drops_nothing()
    {
        using var host = Host.Create(
            new InMemoryDeviceCredentialStore(),
            data: (_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        using var response = await host.Client.GetAsync("/api/sync/inbox", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(host.Token.Requests);
    }

    /// <summary>The pipeline a host composes: a data client with the handler
    /// chained onto it, and the provider's own client behind it.</summary>
    private sealed class Host : IDisposable
    {
        private readonly ServiceProvider _services;

        private Host(ServiceProvider services, StubHttpMessageHandler token, StubHttpMessageHandler data)
        {
            _services = services;
            Token = token;
            Data = data;
            Client = services.GetRequiredService<IHttpClientFactory>().CreateClient("data");
        }

        public StubHttpMessageHandler Token { get; }

        public StubHttpMessageHandler Data { get; }

        public HttpClient Client { get; }

        public static Host Create(
            IDeviceCredentialStore credentials,
            Func<HttpRequestMessage, int, HttpResponseMessage>? data = null)
        {
            var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-09-07T09:00:00Z", CultureInfo.InvariantCulture));

            var tokenHandler = new StubHttpMessageHandler((_, index) => StubHttpMessageHandler.Json(
                HttpStatusCode.OK,
                $$"""
                {"accessToken":"token-{{index}}","expiresAt":"{{clock.GetUtcNow().AddMinutes(30).ToString("O", CultureInfo.InvariantCulture)}}","tokenType":"Bearer"}
                """));

            var dataHandler = new StubHttpMessageHandler(data ?? ((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, "[]")));

            var services = new ServiceCollection();
            services.AddSingleton<TimeProvider>(clock);
            services.AddSingleton(credentials);
            services.AddSingleton<SyncTokenProvider>();
            services.AddTransient<SyncAuthenticationHandler>();
            services.AddHttpClient(SyncTokenProvider.HttpClientName, client => client.BaseAddress = new Uri("https://sync.test"))
                .ConfigurePrimaryHttpMessageHandler(() => tokenHandler);
            services.AddHttpClient("data", client => client.BaseAddress = new Uri("https://sync.test"))
                .AddHttpMessageHandler<SyncAuthenticationHandler>()
                .ConfigurePrimaryHttpMessageHandler(() => dataHandler);

            return new Host(services.BuildServiceProvider(), tokenHandler, dataHandler);
        }

        public void Dispose()
        {
            Client.Dispose();
            _services.Dispose();
        }
    }
}
