using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Dashboard.UI.Adapters;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Dashboard.UI.Extensions;

/// <summary>
/// Wires the four adapters that answer the Dashboard module's ports.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <c>AddDashboardModule()</c> on purpose. That call brings the
/// derivations; this one decides which providers are behind them, which is the
/// host's choice — and a test replaces this call rather than having to unpick it.
/// </para>
/// <para>
/// Both hosts must call this after registering <c>IGitHubActivityClient</c>,
/// <c>IGitHubIdentityClient</c>, <c>IGitHubBillingClient</c>,
/// <c>IClaudeUsageClient</c>, <c>GitHubSettingsStore</c> and
/// <c>ClaudeSettingsStore</c>; the adapters only hold those interfaces and do not
/// construct them.
/// </para>
/// <para>
/// Singletons, unlike the scoped derivations above them. An adapter holds no
/// per-dashboard state — the session cache lives in the module — and the identity
/// client's one round trip is worth making once per app rather than once per
/// dashboard.
/// </para>
/// </remarks>
public static class DashboardAdapterRegistration
{
    public static IServiceCollection AddDashboardAdapters(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IRepositoryDirectory, SettingsRepositoryDirectory>();
        services.AddSingleton<IActivitySource, GitHubActivitySource>();
        services.AddSingleton<IClaudeSpendSource, ClaudeSpendSource>();
        services.AddSingleton<ICopilotSpendSource, CopilotSpendSource>();

        return services;
    }
}
