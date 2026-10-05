using Backlog.Infrastructure.SpecManager.Api;
using Backlog.Infrastructure.SpecManager.OAuth;
using Backlog.Modules.Tasks.Abstractions.Connectors;
using Backlog.Modules.Tasks.Extensions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backlog.Infrastructure.SpecManager;

/// <summary>
/// The one call a host makes to ship the spec-manager connector: its options, its
/// token store, its HTTP client and pipeline, the sign-in, and the connector —
/// registered as an <see cref="ITaskConnector"/> for the sync and, the same
/// instance, as an <see cref="ITaskConnectorSignIn"/> for the settings screen.
/// </summary>
public static class SpecManagerRegistration
{
    /// <param name="services">The host's services. The sync's
    /// <see cref="IConnectedTargets"/> is read from here when registered.</param>
    /// <param name="tokenStore">Where the sign-in is kept;
    /// <see cref="SpecManagerTokenStoreFactory.Create"/> at its default path when
    /// null.</param>
    /// <param name="configure">Changes to <see cref="SpecManagerOptions"/>, such as
    /// another installation's base URL.</param>
    public static IServiceCollection AddSpecManager(
        this IServiceCollection services,
        Func<IServiceProvider, ISpecManagerTokenStore>? tokenStore = null,
        Action<SpecManagerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = services.AddOptions<SpecManagerOptions>();
        if (configure is not null) options.Configure(configure);
        options.Validate(value => value.IsValid(), "SpecManager:BaseUrl must be an absolute http or https URL.");

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(tokenStore ?? (_ => SpecManagerTokenStoreFactory.Create()));
        services.AddSpecManagerHttpClient();

        services.TryAddSingleton(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            return new SpecManagerSignIn(
                () => factory.CreateClient(SpecManagerClient.HttpClientName),
                sp.GetRequiredService<ISpecManagerTokenStore>(),
                sp.GetRequiredService<IOptions<SpecManagerOptions>>().Value,
                sp.GetRequiredService<TimeProvider>(),
                SystemBrowser.OpenAsync,
                sp.GetService<ILogger<SpecManagerSignIn>>());
        });
        services.TryAddSingleton(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            return new SpecManagerClient(
                () => factory.CreateClient(SpecManagerClient.HttpClientName),
                sp.GetRequiredService<SpecManagerSignIn>(),
                sp.GetRequiredService<IOptions<SpecManagerOptions>>().Value);
        });

        services.AddTaskConnector<SpecManagerConnector>();

        // The connector the sync holds, not a second one: AddTaskConnector registers
        // it by type, so the container builds it there and this resolves that one.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ITaskConnectorSignIn, SpecManagerConnector>(
            sp => sp.GetServices<ITaskConnector>().OfType<SpecManagerConnector>().Single()));

        return services;
    }
}
