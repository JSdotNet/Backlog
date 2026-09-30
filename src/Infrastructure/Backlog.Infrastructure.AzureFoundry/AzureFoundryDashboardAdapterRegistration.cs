using Backlog.Infrastructure.AzureFoundry.Dashboard;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Infrastructure.AzureFoundry;

/// <summary>
/// Wires the adapter that answers the Dashboard module's Azure Foundry spend port.
/// </summary>
/// <remarks>
/// <para>
/// Both hosts must call this after registering <c>IAzureFoundryCostClient</c>
/// (<c>AddAzureFoundryCostClient()</c>); the adapter only holds it and does not
/// construct it.
/// </para>
/// <para>
/// A singleton, like every provider adapter behind the dashboard: it holds no
/// per-dashboard state — the session cache lives in the module.
/// </para>
/// </remarks>
public static class AzureFoundryDashboardAdapterRegistration
{
    public static IServiceCollection AddAzureFoundryDashboardAdapters(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IAzureFoundrySpendSource, AzureFoundrySpendSource>();

        return services;
    }
}
