using System.Globalization;
using System.Net;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// The token provider a host registers, over a scripted token endpoint that
/// mints <c>token-0</c>, <c>token-1</c> and so on, each good for half an hour.
/// <para>
/// Shared by the three loops' tests, because each of them asks the same
/// question of it - did a token get refused during the cycle - and a fixture
/// per loop would be three chances for the wire to differ from the one a host
/// composes.
/// </para>
/// </summary>
internal sealed class TokenPipeline : IDisposable
{
    private readonly ServiceProvider _services;

    private TokenPipeline(ServiceProvider services, StubHttpMessageHandler endpoint)
    {
        _services = services;
        Endpoint = endpoint;
        Provider = services.GetRequiredService<SyncTokenProvider>();
    }

    public SyncTokenProvider Provider { get; }

    /// <summary>The token endpoint, with every request it was sent.</summary>
    public StubHttpMessageHandler Endpoint { get; }

    /// <param name="respond">What the token endpoint answers; left out, it
    /// mints a fresh token for every request.</param>
    public static TokenPipeline Create(
        IDeviceCredentialStore credentials,
        FakeTimeProvider clock,
        Func<HttpRequestMessage, int, HttpResponseMessage>? respond = null)
    {
        var endpoint = new StubHttpMessageHandler(respond ?? ((_, index) => StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            $$"""
            {"accessToken":"token-{{index}}","expiresAt":"{{clock.GetUtcNow().AddMinutes(30).ToString("O", CultureInfo.InvariantCulture)}}","tokenType":"Bearer"}
            """)));

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton(credentials);
        services.AddSingleton<SyncTokenProvider>();
        services.AddHttpClient(SyncTokenProvider.HttpClientName, client => client.BaseAddress = new Uri("https://sync.test"))
            .ConfigurePrimaryHttpMessageHandler(() => endpoint);

        return new TokenPipeline(services.BuildServiceProvider(), endpoint);
    }

    /// <summary>The data wire a host composes: the real handler in front of
    /// <paramref name="service"/>, bearing this provider's tokens.</summary>
    public HttpMessageHandler InFrontOf(HttpMessageHandler service) =>
        new SyncAuthenticationHandler(Provider) { InnerHandler = service };

    public void Dispose() => _services.Dispose();
}
