using Backlog.Modules.Sync.Abstractions;
using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Sync.UI;

/// <summary>
/// Puts the Devices page on the settings screen.
/// </summary>
/// <remarks>
/// <para>
/// On the precedent of <c>InboxSettingsRegistration</c>: whether a host's settings
/// screen carries the page is the host's decision, and the shell knows it only as
/// the <see cref="SettingsSection"/> this adds.
/// </para>
/// <para>
/// Offered behind <see cref="SyncFeatures.Sync"/>, the switch the shell used to
/// check for itself, so the page is not on the strip while sync is off.
/// </para>
/// </remarks>
public static class SyncSettingsRegistration
{
    /// <summary>The Devices page's place among the sections modules register:
    /// first, straight after the shell's own pages, where the tab sat while the
    /// shell drew it.</summary>
    public const int Order = 10;

    public static IServiceCollection AddSyncSettings(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(new SettingsSection("devices", "Devices", Order, typeof(DevicesSettings), SyncFeatures.Sync));

        return services;
    }
}
