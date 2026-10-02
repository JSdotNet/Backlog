using Backlog.Modules.Dashboard.Abstractions;
using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Dashboard.UI.Extensions;

/// <summary>
/// Puts the Dashboard's page on the settings screen.
/// </summary>
/// <remarks>
/// <para>
/// On the precedent of <c>InboxSettingsRegistration</c>: the shell knows the page
/// only as the <see cref="SettingsSection"/> this adds. Called from
/// <see cref="DashboardUiRegistration.AddDashboardUi"/>, so every host that
/// composes the pane carries its page without a line of its own.
/// </para>
/// <para>
/// Offered behind <see cref="DashboardFeatures.Dashboard"/>, the switch the pane
/// that reads the weekly usage reset is opened under, so the page is not on the
/// strip while nothing would read it.
/// </para>
/// </remarks>
public static class DashboardSettingsRegistration
{
    /// <summary>The Dashboard page's place among the sections modules register:
    /// after the Inbox's.</summary>
    public const int Order = 200;

    public static IServiceCollection AddDashboardSettings(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(new SettingsSection("dashboard", "Dashboard", Order, typeof(DashboardSettings), DashboardFeatures.Dashboard));

        return services;
    }
}
