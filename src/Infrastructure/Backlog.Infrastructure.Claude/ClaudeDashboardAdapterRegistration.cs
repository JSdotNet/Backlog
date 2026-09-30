using Backlog.Infrastructure.Claude.Dashboard;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Infrastructure.Claude;

/// <summary>
/// Wires the adapter that answers the Dashboard module's Claude spend port.
/// </summary>
/// <remarks>
/// <para>
/// Both hosts must call this after registering <c>IClaudeUsageClient</c> and
/// <c>ClaudeSettingsStore</c>, and <c>IClaudeCodeUsageCache</c> when they compose
/// one; the adapter only holds those and does not construct them.
/// </para>
/// <para>
/// A singleton, like every provider adapter behind the dashboard: it holds no
/// per-dashboard state — the session cache lives in the module.
/// </para>
/// </remarks>
public static class ClaudeDashboardAdapterRegistration
{
    public static IServiceCollection AddClaudeDashboardAdapters(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IClaudeSpendSource, ClaudeSpendSource>();

        return services;
    }
}
