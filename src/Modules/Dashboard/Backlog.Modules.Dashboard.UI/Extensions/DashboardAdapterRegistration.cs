using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Dashboard.UI.Adapters;
using Backlog.SharedKernel.Ai;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Dashboard.UI.Extensions;

/// <summary>
/// Wires the adapters that answer the Dashboard module's ports from this project.
/// <para>
/// One port is deliberately not here. <c>IAssistantSessionSource</c> is a
/// cross-context join, and only an infrastructure adapter may see both this context
/// and the Sessions one, so a host registers it with
/// <c>AddDashboardCrossContextAdapters()</c> instead.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// Separate from <c>AddDashboardModule()</c> on purpose. That call brings the
/// derivations; this one decides which providers are behind them, which is the
/// host's choice — and a test replaces this call rather than having to unpick it.
/// </para>
/// <para>
/// Both hosts must call this after registering <c>IGitHubActivityClient</c>,
/// <c>IGitHubActivityBaselineClient</c>, <c>IGitHubIdentityClient</c>,
/// <c>IGitHubBillingClient</c>,
/// <c>IClaudeUsageClient</c>, <c>IAzureFoundryCostClient</c>, <c>GitHubSettingsStore</c>,
/// <c>ClaudeSettingsStore</c> and <c>IDeviceIdentitySource</c>; the adapters only hold
/// those interfaces and do not construct them.
/// </para>
/// <para>
/// Singletons, unlike the scoped derivations above them. An adapter holds no
/// per-dashboard state — the session cache lives in the module — and the identity
/// client's one round trip is worth making once per app rather than once per
/// dashboard.
/// </para>
/// <para>
/// The two Ask AI registrations at the end are scoped, and that is the one
/// lifetime they can have. <see cref="DashboardScopeInView"/> is what one
/// reader's pane is showing — in the web harness two circuits are two readers —
/// and the source reads it. On the desktop the window is the scope, so the
/// difference costs nothing there.
/// </para>
/// </remarks>
public static class DashboardAdapterRegistration
{
    public static IServiceCollection AddDashboardAdapters(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IRepositoryDirectory, SettingsRepositoryDirectory>();
        services.AddSingleton<IMachineDirectory, DeviceMachineDirectory>();
        services.AddSingleton<IActivitySource, GitHubActivitySource>();
        services.AddSingleton<IActivityBaselineSource, GitHubActivityBaselineSource>();
        services.AddSingleton<IClaudeSpendSource, ClaudeSpendSource>();
        services.AddSingleton<ICopilotSpendSource, CopilotSpendSource>();
        services.AddSingleton<IAzureFoundrySpendSource, AzureFoundrySpendSource>();

        AddDashboardAiContentSource(services);

        return services;
    }

    /// <summary>
    /// The scope mirror the pane writes and the Ask AI source that reads it. Its
    /// own call so a host — or a test — that composes the pane with doubles
    /// behind the ports can still compose these two, which have no provider
    /// behind them at all.
    /// <para>
    /// The clock is taken from the container when the module registered one and
    /// is the system's otherwise, the way the module's own derivations take it:
    /// a host that composes the adapters without the module still gets a window
    /// that ends now.
    /// </para>
    /// </summary>
    public static IServiceCollection AddDashboardAiContentSource(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<DashboardScopeInView>();
        services.AddScoped<IAiContentSource>(sp => new DashboardAiContentSource(
            sp.GetRequiredService<IActivitySource>(),
            sp.GetRequiredService<IRepositoryDirectory>(),
            sp.GetRequiredService<DashboardScopeInView>(),
            sp.GetService<TimeProvider>() ?? TimeProvider.System));

        return services;
    }
}
