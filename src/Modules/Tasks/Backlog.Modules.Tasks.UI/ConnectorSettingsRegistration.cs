using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.Tasks;

/// <summary>
/// Puts the Connectors page on the settings screen: the outside tools tasks can
/// follow, signing in to them, and what is connected through each.
/// </summary>
/// <remarks>
/// <para>
/// On the precedent of <c>InboxSettingsRegistration</c>: the shell knows the page
/// only as the <see cref="SettingsSection"/> this adds. Called from
/// <see cref="TasksAdapterRegistration.AddTasksAdapters"/>, the way the Dashboard's
/// page is reached through <c>AddDashboardUi</c>, so every host that composes
/// Tasks' adapters carries the page without a line of its own.
/// </para>
/// <para>
/// Offered behind no switch: linked tasks have none, and a host with no connector
/// shows the page with an empty state that says so rather than hiding it.
/// </para>
/// </remarks>
public static class ConnectorSettingsRegistration
{
    /// <summary>The Connectors page's place among the sections modules register:
    /// first, straight after the shell's own Repositories and Devices pages, ahead
    /// of the Inbox's.</summary>
    public const int Order = 50;

    public static IServiceCollection AddConnectorSettings(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(new SettingsSection("connectors", "Connectors", Order, typeof(ConnectorSettings)));

        return services;
    }
}
