using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Dashboard.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Dashboard.Extensions;

/// <summary>
/// The module's composition root. A host calls this once and gets both halves of
/// the dashboard; it never registers a derivation itself.
/// </summary>
/// <remarks>
/// <para>
/// The four ports are deliberately not registered here. They are the module's
/// contract with the outside, and which adapter answers each one is the host's
/// decision — today the GitHub and Anthropic clients, wired by
/// <c>AddDashboardAdapters()</c> in the UI project, and a fake in a test.
/// Registering them here would make the module choose its own providers, which is
/// the coupling the ports exist to prevent.
/// </para>
/// <para>
/// Both services are scoped rather than singletons, and that is what makes the
/// session cache a session cache: a scope is one dashboard being open, so closing
/// it drops what was fetched and opening it again asks afresh. As singletons they
/// would hold a quarter's figures for the lifetime of the app with no way to get
/// current ones but a restart.
/// </para>
/// <para>
/// <see cref="TimeProvider"/> is registered only if the host has not already, so a
/// test that installs a fake clock keeps it.
/// </para>
/// </remarks>
public static class DashboardModuleRegistration
{
    public static IServiceCollection AddDashboardModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IProductivityInsights, ProductivityInsights>();
        services.AddScoped<ICostInsights, CostInsights>();

        services.TryAddTimeProvider();

        return services;
    }

    private static void TryAddTimeProvider(this IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(TimeProvider))) return;

        services.AddSingleton(TimeProvider.System);
    }
}
