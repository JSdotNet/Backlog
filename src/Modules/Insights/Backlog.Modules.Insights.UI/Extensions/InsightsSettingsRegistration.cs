using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Insights.UI.Extensions;

/// <summary>
/// Puts the Dashboard's page on the settings screen: the weekly usage reset and
/// the spend budget. The working week is Work's, and its page is the plan UI's
/// (<c>RoadmapSettingsRegistration</c>, local ADR 0073 §1).
/// </summary>
/// <remarks>
/// <para>
/// On the precedent of <c>InboxSettingsRegistration</c>: the shell knows the page
/// only as the <see cref="SettingsSection"/> this adds. Called from
/// <see cref="InsightsUiRegistration.AddInsightsUi"/>, so every host that
/// composes the pane carries its page without a line of its own.
/// </para>
/// <para>
/// The page is offered with no switch: the dashboard that reads the usage reset is
/// always on.
/// </para>
/// </remarks>
public static class InsightsSettingsRegistration
{
    /// <summary>The Dashboard page's place among the Product areas pages: first.</summary>
    public const int Order = 10;

    public static IServiceCollection AddInsightsSettings(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(new SettingsSection(
            "insights",
            "Insights",
            Order,
            typeof(InsightsSettings),
            null,
            SettingsGroup.ProductAreas,
            CardTitles: [InsightsSettings.UsageResetCardTitle, InsightsSettings.SpendBudgetCardTitle]));

        return services;
    }
}
