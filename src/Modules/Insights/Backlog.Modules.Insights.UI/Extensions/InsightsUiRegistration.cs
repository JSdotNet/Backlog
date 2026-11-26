using Backlog.Modules.Insights.Abstractions.Services;
using Backlog.Modules.Insights.UI.Adapters;
using Backlog.SharedKernel.Ai;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Insights.UI.Extensions;

/// <summary>
/// Wires what the pane itself answers: the machine directory, which wraps no
/// provider at all, the Ask AI pair, which reads what one reader's pane is
/// showing, and the Dashboard's page on the settings screen.
/// <para>
/// The provider adapters are not here. Each lives in the infrastructure project it
/// wraps and is registered there — <c>AddGitHubInsightsAdapters()</c>,
/// <c>AddClaudeInsightsAdapters()</c> and <c>AddAzureFoundryInsightsAdapters()</c>
/// — so this project hosts without any provider client. One port is not in any of
/// those either. <c>IAssistantSessionSource</c> is a cross-context join, and only
/// an infrastructure adapter may see both this context and the Sessions one, so a
/// host registers it with <c>AddInsightsCrossContextAdapters()</c> instead.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// Both hosts must call this after registering <c>IDeviceIdentitySource</c>, which
/// the machine directory holds and does not construct. It is a singleton, like the
/// provider adapters beside it: it holds no per-dashboard state.
/// </para>
/// <para>
/// The two Ask AI registrations are scoped, and that is the one lifetime they can
/// have. <see cref="InsightsScopeInView"/> is what one reader's pane is showing —
/// in the web harness two circuits are two readers — and the source reads it. On
/// the desktop the window is the scope, so the difference costs nothing there.
/// </para>
/// </remarks>
public static class InsightsUiRegistration
{
    public static IServiceCollection AddInsightsUi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IMachineDirectory, DeviceMachineDirectory>();

        AddInsightsAiContentSource(services);

        services.AddInsightsSettings();

        return services;
    }

    /// <summary>
    /// The scope mirror the pane writes and the Ask AI source that reads it. Its
    /// own call so a host — or a test — that composes the pane with doubles
    /// behind the ports can still compose these two, which have no provider
    /// behind them at all.
    /// <para>
    /// The source reads the module's seven insights, the same scoped instances
    /// the parts inject, so the figures it sends come out of the caches the parts
    /// on screen filled. They are resolved here rather than registered: they are
    /// <c>AddInsightsModule()</c>'s, or a test's doubles, and a host composing
    /// this call has composed one or the other already.
    /// </para>
    /// <para>
    /// The clock is taken from the container when the module registered one and
    /// is the system's otherwise, the way the module's own derivations take it:
    /// a host that composes the adapters without the module still gets a window
    /// that ends now.
    /// </para>
    /// </summary>
    public static IServiceCollection AddInsightsAiContentSource(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<InsightsScopeInView>();
        services.AddScoped<IAiContentSource>(sp => new InsightsAiContentSource(
            sp.GetRequiredService<IActivitySource>(),
            sp.GetRequiredService<IRepositoryDirectory>(),
            sp.GetRequiredService<InsightsScopeInView>(),
            new InsightsPorts(
                sp.GetRequiredService<IProductivityInsights>(),
                sp.GetRequiredService<ITaskInsights>(),
                sp.GetRequiredService<ISessionInsights>(),
                sp.GetRequiredService<IHoursWorkedInsights>(),
                sp.GetRequiredService<IDriftInsights>(),
                sp.GetRequiredService<ICostInsights>(),
                sp.GetRequiredService<IAttentionInsights>()),
            sp.GetService<TimeProvider>() ?? TimeProvider.System));

        return services;
    }
}
