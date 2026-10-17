using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Devbook.Abstractions;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.SharedKernel;
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

        // Scoped, unlike the two above: the backlog port it reads (ITaskItems) is scoped,
        // and a singleton holding it would be the captive dependency the roadmap's
        // adapters are scoped to avoid.
        services.AddScoped<ICompletedTaskSource, TaskItemsCompletedTaskSource>();

        // Scoped for the same reason: the roadmap's planning, rollup and pace ports are
        // scoped, so this needs AddRoadmapModule() and AddRoadmapCrossContextAdapters()
        // registered first.
        services.AddScoped<IPlanProgressSource, RoadmapPlanProgressSource>();

        // The hours worked, split by office hours (local ADR 0019, §7): a singleton over
        // singletons — the activity source, the working week, the clock and the feature
        // switches — as Roadmap's actual hours over the same stretches is. Each is
        // optional, so a host that composed no Sessions activity still resolves the port
        // and it answers that it cannot state the hours.
        services.AddSingleton<IHoursWorkedSource>(sp =>
            new AgentActivityHoursWorkedSource(
                sp.GetService<IAgentActivitySource>(),
                sp.GetService<IWorkingHoursSettings>(),
                sp.GetService<TimeProvider>(),
                sp.GetService<IAppFeatureSettings>()));

        // The devbook sync verdicts, by unit, for the Drift at a glance part. A singleton
        // over the verdict store, itself a singleton; optional, so a host that composed no
        // Sessions surface still resolves the port and it answers that it keeps none.
        services.AddSingleton<IDriftUnitSource>(sp =>
            new SyncVerdictDriftUnitSource(sp.GetService<IDevbookSyncVerdicts>()));

        return services;
    }
}
