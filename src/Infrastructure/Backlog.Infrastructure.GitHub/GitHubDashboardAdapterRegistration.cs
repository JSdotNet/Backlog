using Backlog.Infrastructure.GitHub.Dashboard;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Infrastructure.GitHub;

/// <summary>
/// Wires the adapters that answer the Dashboard module's ports from GitHub: the
/// repository directory, the activity and its baseline, and the Copilot spend.
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
/// <c>IGitHubBillingClient</c> and <c>GitHubSettingsStore</c>; the adapters only
/// hold those and do not construct them.
/// </para>
/// <para>
/// Singletons, unlike the scoped derivations above them. An adapter holds no
/// per-dashboard state — the session cache lives in the module — and the identity
/// client's one round trip is worth making once per app rather than once per
/// dashboard.
/// </para>
/// </remarks>
public static class GitHubDashboardAdapterRegistration
{
    public static IServiceCollection AddGitHubDashboardAdapters(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IRepositoryDirectory, SettingsRepositoryDirectory>();
        services.AddSingleton<IActivitySource, GitHubActivitySource>();
        services.AddSingleton<IActivityBaselineSource, GitHubActivityBaselineSource>();
        services.AddSingleton<ICopilotSpendSource, CopilotSpendSource>();

        return services;
    }
}
