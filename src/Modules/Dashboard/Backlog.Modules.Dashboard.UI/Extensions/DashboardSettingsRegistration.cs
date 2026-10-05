using Backlog.Modules.Dashboard.Abstractions;
using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Dashboard.UI.Extensions;

/// <summary>
/// Puts the Dashboard's pages on the settings screen: the weekly usage reset
/// and the working week.
/// </summary>
/// <remarks>
/// <para>
/// On the precedent of <c>InboxSettingsRegistration</c>: the shell knows the page
/// only as the <see cref="SettingsSection"/> this adds. Called from
/// <see cref="DashboardUiRegistration.AddDashboardUi"/>, so every host that
/// composes the pane carries its page without a line of its own.
/// </para>
/// <para>
/// The usage-reset page is offered behind <see cref="DashboardFeatures.Dashboard"/>,
/// the switch the pane that reads it is opened under, so the page is not on the
/// strip while nothing would read it. The working week is offered with no switch:
/// the roadmap counts it as well as the dashboard (ADR 0019), so it is read
/// whether or not the dashboard is on.
/// </para>
/// </remarks>
public static class DashboardSettingsRegistration
{
    /// <summary>The Dashboard page's place among the sections modules register:
    /// after the Inbox's.</summary>
    public const int Order = 200;

    /// <summary>The working week's page: straight after the Dashboard's.</summary>
    public const int WorkingWeekOrder = 210;

    public static IServiceCollection AddDashboardSettings(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(new SettingsSection("dashboard", "Dashboard", Order, typeof(DashboardSettings), DashboardFeatures.Dashboard));
        services.AddSingleton(new SettingsSection("working-week", "Working week", WorkingWeekOrder, typeof(WorkingWeekSettings)));

        return services;
    }
}
