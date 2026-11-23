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
/// Both pages are offered with no switch: the dashboard that reads the usage reset
/// is always on, and the roadmap counts the working week as well as the dashboard
/// (ADR 0019).
/// </para>
/// </remarks>
public static class DashboardSettingsRegistration
{
    /// <summary>The Dashboard page's place among the Product areas pages: first.</summary>
    public const int Order = 10;

    /// <summary>The working week's page: straight after the Dashboard's.</summary>
    public const int WorkingWeekOrder = 20;

    public static IServiceCollection AddDashboardSettings(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(new SettingsSection(
            "dashboard",
            "Dashboard",
            Order,
            typeof(DashboardSettings),
            null,
            SettingsGroup.ProductAreas,
            CardTitles: [DashboardSettings.UsageResetCardTitle, DashboardSettings.SpendBudgetCardTitle]));

        services.AddSingleton(new SettingsSection(
            "working-week",
            "Working week",
            WorkingWeekOrder,
            typeof(WorkingWeekSettings),
            Group: SettingsGroup.ProductAreas,
            CardTitles: [WorkingWeekSettings.CardTitle]));

        return services;
    }
}
