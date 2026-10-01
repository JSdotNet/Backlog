using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Infrastructure.Claude;

/// <summary>
/// Claude usage reporting — the settings, the Admin API transport, and the probe,
/// usage client and spend cache over them — registered in one place both desktop
/// heads reach through their shared composition.
/// </summary>
public static class ClaudeRegistration
{
    /// <summary>
    /// Registers everything Claude usage reporting needs, each once. Safe to call
    /// unconditionally: the clients report themselves unavailable until an Admin API
    /// key is configured, and the <c>usage-metrics</c> feature decides whether
    /// anything asks them.
    /// <para>
    /// The settings file and the cache folder are the host's to say: the installed
    /// app keeps its settings per user, and a development host keeps its own copy so
    /// a session there never rewrites the real one. The cache folder arrives as a
    /// delegate over the provider because the workspace that names it is not this
    /// project's to see; it is read per call, so settled days land wherever the
    /// workspace says at the time.
    /// </para>
    /// </summary>
    public static IServiceCollection AddClaude(
        this IServiceCollection services,
        Func<IServiceProvider, ClaudeSettingsStore> settings,
        Func<IServiceProvider, string> spendCacheDirectory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(spendCacheDirectory);

        services.AddSingleton(settings);
        services.AddHttpClient<IClaudeTransport, ClaudeAdminTransport>();
        services.AddSingleton<IClaudeAccountProbe>(sp => new ClaudeAccountProbe(sp.GetRequiredService<IClaudeTransport>()));
        services.AddSingleton<IClaudeUsageClient>(sp => new ClaudeUsageClient(
            sp.GetRequiredService<IClaudeTransport>(),
            sp.GetRequiredService<ClaudeSettingsStore>()));
        services.AddSingleton<IClaudeCodeUsageCache>(sp => new ClaudeCodeUsageCache(
            () => spendCacheDirectory(sp)));

        return services;
    }
}
