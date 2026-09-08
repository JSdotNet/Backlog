using Backlog.Modules.Dashboard.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Infrastructure.FileSystem.Dashboard;

/// <summary>
/// The cross-context join the dashboard takes part in: the Sessions context supplies
/// the assistant-session facts the Dashboard's productivity half reports on.
/// <para>
/// Registered here — in one place both hosts call — so the lifetime cannot drift
/// between the desktop app and the web harness, exactly as
/// <see cref="Roadmap.RoadmapCrossContextAdapterRegistration"/> is. A singleton, unlike
/// the roadmap adapters: the port it captures (<c>IAgentSessionSource</c>) is itself a
/// singleton, so nothing scoped is being held.
/// </para>
/// </summary>
public static class DashboardCrossContextAdapterRegistration
{
    /// <summary>
    /// Registers the dashboard's cross-context adapters. Call after
    /// <c>AddAgentSessionSource()</c> and <c>AddAgentActivitySource()</c>, which supply
    /// the ports these are written over, and after <c>AddDashboardModule()</c>, whose
    /// derivation consumes them.
    /// </summary>
    /// <remarks>
    /// Two adapters over two ports, mirroring the split on the Sessions side rather than
    /// flattening it. One stats files and one parses their bodies; a single adapter
    /// answering both would put the expensive read behind a contract the cheap caller
    /// also holds, which is exactly what the two ports exist to prevent.
    /// </remarks>
    public static IServiceCollection AddDashboardCrossContextAdapters(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IAssistantSessionSource, AgentSessionAssistantSessionSource>();
        services.AddSingleton<IAssistantActivitySource, AgentActivityAssistantActivitySource>();

        return services;
    }
}
