using Backlog.Modules.Insights.Abstractions.Services;
using Backlog.Modules.Insights.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Insights.Extensions;

/// <summary>
/// The module's composition root. A host calls this once and gets both halves of
/// the dashboard; it never registers a derivation itself.
/// </summary>
/// <remarks>
/// <para>
/// The ports are deliberately not registered here. They are the module's contract
/// with the outside, and which adapter answers each one is the host's decision —
/// today the GitHub, Anthropic and Azure Foundry clients and the device identity,
/// wired by each provider's own infrastructure call — <c>AddGitHubInsightsAdapters()</c>,
/// <c>AddClaudeInsightsAdapters()</c>, <c>AddAzureFoundryInsightsAdapters()</c> — and
/// <c>AddInsightsUi()</c> in the UI project, and by
/// <c>AddInsightsCrossContextAdapters()</c> in infrastructure for the join that has
/// to see two contexts, and a fake in a test. Registering them here would make the
/// module choose its own providers, which is the coupling the ports exist to prevent.
/// </para>
/// <para>
/// Every service here is scoped rather than singletons, and that is what makes the
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
public static class InsightsModuleRegistration
{
    public static IServiceCollection AddInsightsModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IProductivityInsights, ProductivityInsights>();
        services.AddScoped<ISessionInsights, SessionInsights>();
        services.AddScoped<ICostInsights, CostInsights>();
        services.AddScoped<ITaskInsights, TaskInsights>();
        services.AddScoped<IHoursWorkedInsights, HoursWorkedInsights>();
        services.AddScoped<IDriftInsights, DriftInsights>();
        services.AddScoped<IAttentionInsights, AttentionInsights>();

        services.TryAddTimeProvider();

        return services;
    }

    private static void TryAddTimeProvider(this IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(TimeProvider))) return;

        services.AddSingleton(TimeProvider.System);
    }
}
