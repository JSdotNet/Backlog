using System.Net;

using Backlog.Infrastructure.Sync.Extensions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// What a harness gets for composing the development auto-pair, under the
/// pipeline <c>AddServiceDefaults()</c> puts on every client. The auto-pair's
/// own loop is the readiness poll; a pipeline underneath it retrying as well
/// would send a register twice (guideline ADR 0015: no nested retries, never
/// retry a non-idempotent command).
/// </summary>
public sealed class LocalDevelopmentPairingRegistrationTests
{
    private static readonly Uri SyncAddress = new("https://sync.test");

    [Fact]
    public async Task A_call_the_service_refuses_is_sent_once_whatever_the_host_default_pipeline()
    {
        var wire = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var provider = Host(wire);

        using var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient(LocalDevelopmentPairing.HttpClientName);
        using var response = await http.PostAsync(new Uri("/api/sync/devices/register", UriKind.Relative), content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Single(wire.Requests);
    }

    /// <summary>With no pipeline, the client's own timeout is the only bound on
    /// a call, so it has to be a finite one (guideline ADR 0015).</summary>
    [Fact]
    public void Each_call_is_bounded_and_the_pairing_composes()
    {
        using var provider = Host(new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)));

        using var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient(LocalDevelopmentPairing.HttpClientName);

        Assert.Equal(SyncAddress, http.BaseAddress);
        Assert.NotEqual(Timeout.InfiniteTimeSpan, http.Timeout);
        Assert.True(http.Timeout <= TimeSpan.FromMinutes(1), $"HttpClient.Timeout was {http.Timeout}.");
        Assert.NotNull(provider.GetRequiredService<LocalDevelopmentPairing>());
    }

    private static ServiceProvider Host(HttpMessageHandler wire)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());
        services.AddLocalDevelopmentPairing(_ => SyncAddress);
        services.AddHttpClient(LocalDevelopmentPairing.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => wire);
        services.PostConfigure<HttpStandardResilienceOptions>(options => options.Retry.Delay = TimeSpan.Zero);

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
}
