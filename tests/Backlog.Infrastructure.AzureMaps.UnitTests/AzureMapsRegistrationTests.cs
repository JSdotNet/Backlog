using Backlog.Modules.Events;
using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace Backlog.Infrastructure.AzureMaps.UnitTests;

/// <summary>
/// What a host gets for calling <c>AddAzureMaps()</c> before <c>AddEventsModule()</c>: the adapter
/// behind both of the module's map ports, and the geocoder's client carrying the pipeline guideline
/// ADR 0015 and local ADR 0061 §5 ask for — a 10-second timeout and at most one retry —
/// as its only resilience handler.
/// </summary>
public sealed class AzureMapsRegistrationTests : IDisposable
{
    private readonly TemporaryDirectory _temp = new("backlog-azure-maps-registration");

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Registered_before_the_module_the_adapter_answers_both_map_ports()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAzureMaps(_ => Store());
        services.AddEventsModule();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        Assert.IsType<AzureMapsGeocoder>(provider.GetRequiredService<IAddressGeocoder>());
        Assert.IsType<AzureMapsTileSource>(provider.GetRequiredService<IEventMapTiles>());
        Assert.Same(provider.GetRequiredService<AzureMapsSettingsStore>(), provider.GetRequiredService<AzureMapsSettingsStore>());
    }

    [Fact]
    public void A_store_the_host_registered_first_is_kept()
    {
        var services = new ServiceCollection();
        var own = Store();
        services.AddSingleton(own);
        services.AddAzureMaps();

        using var provider = services.BuildServiceProvider();

        Assert.Same(own, provider.GetRequiredService<AzureMapsSettingsStore>());
    }

    [Fact]
    public void The_geocoder_waits_ten_seconds_and_retries_once()
    {
        var services = new ServiceCollection();
        services.AddAzureMaps(_ => Store());

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<HttpStandardResilienceOptions>>()
            .Get($"{AzureMapsRegistration.GeocoderClientName}-standard");

        Assert.Equal(TimeSpan.FromSeconds(10), AzureMapsRegistration.LookupBudget);
        Assert.Equal(AzureMapsRegistration.LookupBudget, options.AttemptTimeout.Timeout);
        Assert.Equal(AzureMapsRegistration.LookupBudget, options.TotalRequestTimeout.Timeout);
        Assert.Equal(1, options.Retry.MaxRetryAttempts);
        Assert.True(options.CircuitBreaker.SamplingDuration >= options.AttemptTimeout.Timeout * 2);
    }

    [Fact]
    public void The_geocoder_carries_one_resilience_handler_even_under_the_hosts_defaults()
    {
        var services = new ServiceCollection();
        services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());
        services.AddAzureMaps(_ => Store());

        using var provider = services.BuildServiceProvider();
        var chain = HandlerChain(provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(AzureMapsRegistration.GeocoderClientName));

        Assert.Single(chain.OfType<ResilienceHandler>());
    }

    /// <summary>The client factory's own logging writes the request URI, and the geocoder's URI
    /// carries the address in its query (ADR 0061 §5: no log carries the address). Its loggers
    /// are taken off.</summary>
    [Fact]
    public void The_geocoder_client_writes_no_request_log()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAzureMaps(_ => Store());

        using var provider = services.BuildServiceProvider();
        var chain = HandlerChain(provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(AzureMapsRegistration.GeocoderClientName));

        Assert.DoesNotContain(chain, handler => handler.GetType().Name.Contains("Logging", StringComparison.Ordinal));
    }

    private AzureMapsSettingsStore Store() =>
        new(_temp.Combine(Guid.NewGuid().ToString("n"), "azure-maps.json"));

    private static List<HttpMessageHandler> HandlerChain(HttpMessageHandler handler)
    {
        var chain = new List<HttpMessageHandler>();

        for (var current = handler; current is not null; current = (current as DelegatingHandler)?.InnerHandler)
        {
            chain.Add(current);
        }

        return chain;
    }
}
