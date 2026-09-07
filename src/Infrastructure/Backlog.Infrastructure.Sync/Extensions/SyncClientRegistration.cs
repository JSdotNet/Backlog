using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backlog.Infrastructure.Sync.Extensions;

/// <summary>
/// What a host registers to be able to talk to the sync service as a device.
/// </summary>
public static class SyncClientRegistration
{
    /// <summary>
    /// Registers the token pipeline and the pairing client against
    /// <paramref name="baseAddress"/>.
    /// <para>
    /// It deliberately does not register an <see cref="IDeviceCredentialStore"/>.
    /// Which store is right is the one thing only the host knows — DPAPI on the
    /// Windows desktop, its own file under the content root in each browser
    /// harness so the two are two distinct devices, and in memory on the Android
    /// head until its secure-storage adapter lands. Registering a default here
    /// would make the wrong one silently work.
    /// </para>
    /// <para>
    /// A host with data clients of its own — mobile's <c>CloudSyncClient</c> —
    /// chains <c>.AddHttpMessageHandler&lt;SyncAuthenticationHandler&gt;()</c>
    /// onto them so they leave carrying the same token. The handler is transient
    /// because <c>IHttpClientFactory</c> owns handler lifetimes; the provider
    /// behind it is the singleton that actually holds the cache.
    /// </para>
    /// </summary>
    public static IServiceCollection AddSyncClient(this IServiceCollection services, Uri baseAddress)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(baseAddress);

        // The token endpoint's own client, with no authentication handler on it.
        // See SyncTokenProvider for why it cannot be the same one.
        services.AddHttpClient(SyncTokenProvider.HttpClientName, client => client.BaseAddress = baseAddress);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<SyncTokenProvider>();
        services.TryAddTransient<SyncAuthenticationHandler>();

        // devices/register, devices/pair and devices/token are anonymous, and
        // devices/codes and devices/me are bearer — one client covers both,
        // because sending no Authorization header when this device has no token
        // is exactly what an unpaired device should do.
        services.AddHttpClient<DevicePairingClient>(client => client.BaseAddress = baseAddress)
            .AddHttpMessageHandler<SyncAuthenticationHandler>();

        return services;
    }
}
