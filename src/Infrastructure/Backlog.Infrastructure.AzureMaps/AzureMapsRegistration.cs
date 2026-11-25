using Backlog.Modules.Events;
using Backlog.Modules.Events.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backlog.Infrastructure.AzureMaps;

/// <summary>
/// The Azure Maps adapter behind the Events module's two map ports (local ADR 0061 §4). A host
/// calls <see cref="AddAzureMaps"/> before <c>AddEventsModule()</c>, whose own registrations are
/// TryAdd, so the adapter is what the pane and the map get.
/// <para>
/// The geocoder's client carries its own pipeline (guideline ADR 0015, ADR 0061 §5) in place of
/// the host's default: one lookup is given <see cref="LookupBudget"/> in all, retried at most once
/// on a <c>408</c>, <c>429</c>, <c>5xx</c> or transport failure inside that budget. The person
/// may be waiting on a save, so the whole call is bounded rather than each attempt. The standard
/// handler's circuit breaker is on the client too, but it needs a hundred calls in its window
/// before it may open, which one person's lookups never reach: it is not protection this client
/// relies on.
/// </para>
/// <para>
/// The client factory's request logging is taken off that client: it writes the request URI, and
/// the URI's query carries the address, which no log may (ADR 0061 §5, guideline 0010).
/// </para>
/// </summary>
public static class AzureMapsRegistration
{
    /// <summary>The name the geocoder's <see cref="HttpClient"/> is registered under.</summary>
    public const string GeocoderClientName = nameof(IAddressGeocoder);

    /// <summary>How long one lookup may take, retry included.</summary>
    public static readonly TimeSpan LookupBudget = TimeSpan.FromSeconds(10);

    /// <param name="services">The host's services.</param>
    /// <param name="settings">Where the key is kept; the per-user <c>azure-maps.json</c> when
    /// null. A store the host registered first is kept either way.</param>
    public static IServiceCollection AddAzureMaps(
        this IServiceCollection services,
        Func<IServiceProvider, AzureMapsSettingsStore>? settings = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(settings ?? (_ => new AzureMapsSettingsStore()));
        services.TryAddSingleton(TimeProvider.System);

        var geocoder = services.AddHttpClient<IAddressGeocoder, AzureMapsGeocoder>(GeocoderClientName);
        geocoder.RemoveAllLoggers();

        // The experimental attribute on RemoveAllResilienceHandlers is a warning about the API's
        // shape, not its behaviour; it is the one published way to take the host's default
        // pipeline off a client (as AzureFoundryRegistration).
#pragma warning disable EXTEXP0001
        geocoder.RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001

        geocoder.AddStandardResilienceHandler(options =>
        {
            options.AttemptTimeout.Timeout = LookupBudget;
            options.TotalRequestTimeout.Timeout = LookupBudget;
            options.Retry.MaxRetryAttempts = 1;
            // The circuit breaker must sample over at least twice the attempt or the options
            // fail validation. It will not open for one person saving one address at a time -
            // its minimum throughput is a hundred calls - so the window is set for the
            // validator, not the breaker (as AzureFoundryRegistration).
            options.CircuitBreaker.SamplingDuration = LookupBudget * 2;
        });

        services.TryAddSingleton<IEventMapTiles, AzureMapsTileSource>();

        return services;
    }
}
